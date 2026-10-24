using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZestDrop.Platform.Windows;

public enum ModernContextMenuState
{
    Unsupported,
    Off,
    On,
    Outdated
}

public sealed record ModernContextMenuResult(bool Succeeded, string? Error = null, bool Cancelled = false);

[SupportedOSPlatform("windows")]
public static class ExplorerMenuRegistration
{
    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public sealed record RegisteredPackage(string FullName, Version Version, string? ExternalLocation);

    public static bool IsSupportedOs =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, ExplorerMenuPackage.MinimumBuild)
        && RuntimeInformation.OSArchitecture == Architecture.X64;

    public static string TitleFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZestDrop", ExplorerMenuPackage.TitleFileName);

    public static bool HasFiles(string installFolder) =>
        File.Exists(Path.Combine(installFolder, ExplorerMenuPackage.DllFileName))
        && File.Exists(Path.Combine(installFolder, ExplorerMenuPackage.PackageFileName));

    public static ModernContextMenuState GetState(string installFolder)
    {
        if (!IsSupportedOs)
            return ModernContextMenuState.Unsupported;
        if (!HasFiles(installFolder))
            return ModernContextMenuState.Off;

        var package = Find();
        if (package is null)
            return ModernContextMenuState.Off;

        var sameFolder = package.ExternalLocation is null || SamePath(package.ExternalLocation, installFolder);
        return sameFolder && package.Version >= ExplorerMenuPackage.ManifestVersion
            ? ModernContextMenuState.On
            : ModernContextMenuState.Outdated;
    }

    public static unsafe RegisteredPackage? Find()
    {
        uint count = 0, length = 0;
        var rc = GetPackagesByPackageFamily(ExplorerMenuPackage.FamilyName, ref count, null, ref length, null);
        if (rc != 122 || count == 0) // 122 = ERROR_INSUFFICIENT_BUFFER
            return null;

        var names = new nint[count];
        var buffer = new char[length];
        string fullName;
        fixed (nint* pNames = names)
        fixed (char* pBuffer = buffer)
        {
            if (GetPackagesByPackageFamily(ExplorerMenuPackage.FamilyName, ref count, (char**)pNames, ref length, pBuffer) != 0 || count == 0)
                return null;
            fullName = new string((char*)pNames[0]);
        }

        var parts = fullName.Split('_');
        var version = parts.Length > 1 && Version.TryParse(parts[1], out var v) ? v : new Version(0, 0);
        return new RegisteredPackage(fullName, version, ExternalLocationOf(fullName));
    }

    public static void SetTitle(string? title)
    {
        var path = TitleFilePath;
        if (title is null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        var content = title.ReplaceLineEndings(" ").Trim() + "\r\n";
        if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content)
            return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public static string? ReadTitle()
    {
        try
        {
            var path = TitleFilePath;
            return File.Exists(path) ? File.ReadLines(path, Encoding.UTF8).FirstOrDefault()?.Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static async Task<ModernContextMenuResult> AddAsync(string installFolder)
    {
        if (!IsSupportedOs)
            return new ModernContextMenuResult(false, Error: "The top-level Explorer menu requires Windows 11 (x64).");
        if (!HasFiles(installFolder))
            return new ModernContextMenuResult(false, Error: $"{ExplorerMenuPackage.PackageFileName} or {ExplorerMenuPackage.DllFileName} is missing in {installFolder}.");

        var package = Path.Combine(installFolder, ExplorerMenuPackage.PackageFileName);
        var result = await RunPowerShellAsync(errorFile => BuildAddScript(package, installFolder, errorFile), elevated: true);
        if (result.Succeeded && GetState(installFolder) != ModernContextMenuState.On)
        {
            return new ModernContextMenuResult(false, Error: "Windows accepted the package, but registration could not be verified for this user account.");
        }
        return result;
    }

    public static async Task<ModernContextMenuResult> RemoveAsync(bool allowElevation)
    {
        if (Find() is null)
            return new ModernContextMenuResult(true);

        var result = await RunPowerShellAsync(BuildRemoveScript, elevated: false);
        if (!result.Succeeded && allowElevation && Find() is not null)
            result = await RunPowerShellAsync(BuildRemoveScript, elevated: true);

        return result.Succeeded && Find() is not null
            ? new ModernContextMenuResult(false, Error: "The package is still registered.")
            : result;
    }

    private static string BuildAddScript(string packagePath, string externalLocation, string errorFile) =>
        WrapScript($"Add-AppxPackage -Path {Quote(packagePath)} -ExternalLocation {Quote(externalLocation)} -AllowUnsigned -ForceUpdateFromAnyVersion", errorFile);

    private static string BuildRemoveScript(string errorFile) =>
        WrapScript($"Get-AppxPackage -Name {Quote(ExplorerMenuPackage.PackageName)} | Remove-AppxPackage", errorFile);

    private static string WrapScript(string command, string errorFile) =>
        $"$ErrorActionPreference = 'Stop'; try {{ {command}; exit 0 }} " +
        $"catch {{ [IO.File]::WriteAllText({Quote(errorFile)}, $_.Exception.Message); exit 1 }}";

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            builder.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛')
                builder.Append(c);
        }
        return builder.Append('\'').ToString();
    }

    private static async Task<ModernContextMenuResult> RunPowerShellAsync(Func<string, string> buildScript, bool elevated)
    {
        var errorFile = Path.Combine(Path.GetTempPath(), $"ZestDrop-explorer-menu-{Guid.NewGuid():N}.txt");
        try
        {
            var script = buildScript(errorFile);
            var info = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                $"-NoLogo -NoProfile -NonInteractive -Command \"{script}\"");

            if (elevated)
            {
                info.UseShellExecute = true;
                info.Verb = "runas";
                info.WindowStyle = ProcessWindowStyle.Hidden;
            }
            else
            {
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
            }

            using var process = Process.Start(info);
            if (process is null)
                return new ModernContextMenuResult(false, Error: "PowerShell did not start.");

            using var timeout = new CancellationTokenSource(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!elevated)
                    process.Kill();
                return new ModernContextMenuResult(false, Error: "Windows did not finish within " + Timeout.TotalMinutes + " minutes.");
            }

            if (process.ExitCode == 0)
                return new ModernContextMenuResult(true);

            var message = File.Exists(errorFile) ? ShortenDeploymentError(File.ReadAllText(errorFile)) : "";
            return new ModernContextMenuResult(false, Error: message.Length > 0 ? message : $"PowerShell exit code {process.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ModernContextMenuResult(false, Cancelled: true);
        }
        catch (Win32Exception ex)
        {
            return new ModernContextMenuResult(false, Error: ex.Message);
        }
        finally
        {
            try { File.Delete(errorFile); } catch { }
        }
    }

    private static string ShortenDeploymentError(string message)
    {
        var note = message.IndexOf("NOTE: For additional information", StringComparison.Ordinal);
        if (note >= 0)
            message = message[..note];
        return string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static unsafe string? ExternalLocationOf(string fullName)
    {
        try
        {
            uint length = 0;
            if (GetPackagePathByFullName2(fullName, 5, ref length, null) != 122 || length == 0)
                return null;
            var buffer = new char[length];
            fixed (char* pBuffer = buffer)
            {
                return GetPackagePathByFullName2(fullName, 5, ref length, pBuffer) == 0 ? new string(pBuffer) : null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern unsafe int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, char** packageFullNames, ref uint bufferLength, char* buffer);

    [DllImport("api-ms-win-appmodel-runtime-l1-1-3.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern unsafe int GetPackagePathByFullName2(string packageFullName, int packagePathType, ref uint pathLength, char* path);
}

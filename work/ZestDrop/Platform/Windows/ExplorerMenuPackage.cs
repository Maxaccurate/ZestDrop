using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace ZestDrop.Platform.Windows;

/// <summary>
/// Identity, file names and manifest of the Explorer context-menu package.
/// </summary>
public static class ExplorerMenuPackage
{
    public const string PackageName = "ZestDrop.ExplorerMenu";
    public const string Publisher = "CN=ZestDrop, OID.2.25.311729368913984317654407730594956997722=1";
    public const string FamilyName = "ZestDrop.ExplorerMenu_c73vxh346rtay";
    public static readonly Version ManifestVersion = new(1, 0, 0, 0);

    // CLSID of the IExplorerCommand in ZestDropExplorerMenu.dll
    public static readonly Guid ClassId = new("E839185B-FDC2-4DE6-8E3A-95E052DE1CF4");

    public const string DllFileName = "ZestDropExplorerMenu.dll";
    public const string PackageFileName = "ZestDropExplorerMenu.msix";
    public const string TitleFileName = "explorer-menu.txt";
    public const string Architecture = "x64";
    public const int MinimumBuild = 22000;
    public const string LogoPath = @"Assets\Logo.png";

    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
    private static readonly XNamespace Desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    public static string CreateManifest()
    {
        var clsid = ClassId.ToString("D").ToUpperInvariant();
        var package = new XElement(Foundation + "Package",
            new XAttribute("xmlns", Foundation.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "uap", Uap),
            new XAttribute(XNamespace.Xmlns + "uap10", Uap10),
            new XAttribute(XNamespace.Xmlns + "desktop4", Desktop4),
            new XAttribute(XNamespace.Xmlns + "desktop5", Desktop5),
            new XAttribute(XNamespace.Xmlns + "com", Com),
            new XAttribute(XNamespace.Xmlns + "rescap", Rescap),
            new XAttribute("IgnorableNamespaces", "uap uap10 desktop4 desktop5 com rescap"),
            new XElement(Foundation + "Identity",
                new XAttribute("Name", PackageName),
                new XAttribute("Publisher", Publisher),
                new XAttribute("Version", ManifestVersion.ToString(4)),
                new XAttribute("ProcessorArchitecture", Architecture)),
            new XElement(Foundation + "Properties",
                new XElement(Foundation + "DisplayName", "ZestDrop"),
                new XElement(Foundation + "PublisherDisplayName", "ZestDrop contributors"),
                new XElement(Foundation + "Logo", LogoPath),
                new XElement(Uap10 + "AllowExternalContent", "true")),
            new XElement(Foundation + "Resources",
                new XElement(Foundation + "Resource", new XAttribute("Language", "en-us"))),
            new XElement(Foundation + "Dependencies",
                new XElement(Foundation + "TargetDeviceFamily",
                    new XAttribute("Name", "Windows.Desktop"),
                    new XAttribute("MinVersion", "10.0." + MinimumBuild.ToString(CultureInfo.InvariantCulture) + ".0"),
                    new XAttribute("MaxVersionTested", "10.0.26100.0"))),
            new XElement(Foundation + "Capabilities",
                new XElement(Rescap + "Capability", new XAttribute("Name", "runFullTrust")),
                new XElement(Rescap + "Capability", new XAttribute("Name", "unvirtualizedResources"))),
            new XElement(Foundation + "Applications",
                new XElement(Foundation + "Application",
                    new XAttribute("Id", "ZestDrop"),
                    new XAttribute("Executable", "ZestDrop.exe"),
                    new XAttribute(Uap10 + "TrustLevel", "mediumIL"),
                    new XAttribute(Uap10 + "RuntimeBehavior", "win32App"),
                    new XElement(Uap + "VisualElements",
                        new XAttribute("AppListEntry", "none"),
                        new XAttribute("DisplayName", "ZestDrop"),
                        new XAttribute("Description", "ZestDrop File Explorer integration"),
                        new XAttribute("BackgroundColor", "transparent"),
                        new XAttribute("Square150x150Logo", LogoPath),
                        new XAttribute("Square44x44Logo", LogoPath)),
                    new XElement(Foundation + "Extensions",
                        new XElement(Desktop4 + "Extension",
                            new XAttribute("Category", "windows.fileExplorerContextMenus"),
                            new XElement(Desktop4 + "FileExplorerContextMenus",
                                new XElement(Desktop5 + "ItemType",
                                    new XAttribute("Type", "*"),
                                    new XElement(Desktop5 + "Verb",
                                        new XAttribute("Id", "ConvertWithZestDrop"),
                                        new XAttribute("Clsid", clsid))))),
                        new XElement(Com + "Extension",
                            new XAttribute("Category", "windows.comServer"),
                            new XElement(Com + "ComServer",
                                new XElement(Com + "SurrogateServer",
                                    new XAttribute("DisplayName", "ZestDrop context menu"),
                                    new XElement(Com + "Class",
                                        new XAttribute("Id", clsid),
                                        new XAttribute("Path", DllFileName),
                                        new XAttribute("ThreadingModel", "STA")))))))));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), package);
        return document.Declaration + Environment.NewLine + document.Root;
    }

    /// <summary>Minimal raw-vtable client of IAppxFactory / IAppxPackageWriter (from AppxPackaging.dll in Windows 10/11).</summary>
    public static unsafe void PackAppx(string packagePath, string manifestPath, IEnumerable<(string Name, string Path, string Type)> payload)
    {
        var init = CoInitializeEx(0, 0); // COINIT_MULTITHREADED
        const int RpcEChangedMode = unchecked((int)0x80010106);
        if (init != RpcEChangedMode && init < 0)
            Marshal.ThrowExceptionForHR(init);

        var clsidFactory = new Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781");
        var iidFactory = new Guid("beb94909-e451-438b-b5a7-d79e767b75d8");
        nint factory = 0, output = 0, hash = 0, writer = 0;

        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(&clsidFactory, 0, 1, &iidFactory, out factory));
            const uint StgmCreate = 0x1000, StgmWrite = 1, StgmShareExclusive = 0x10;
            Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(packagePath, StgmCreate | StgmWrite | StgmShareExclusive, 0x80, 1, 0, out output));
            Marshal.ThrowExceptionForHR(CreateUri("http://www.w3.org/2001/04/xmlenc#sha256", 0, 0, out hash));

            var settings = new PackageSettings { ForceZip32 = 1, HashMethod = hash };
            var create = (delegate* unmanaged[Stdcall]<nint, nint, PackageSettings*, nint*, int>)Slot(factory, 3);
            nint created;
            Marshal.ThrowExceptionForHR(create(factory, output, &settings, &created));
            writer = created;

            foreach (var (name, path, type) in payload)
            {
                const uint StgmRead = 0, StgmShareDenyWrite = 0x20;
                Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(path, StgmRead | StgmShareDenyWrite, 0, 0, 0, out var input));
                try
                {
                    var add = (delegate* unmanaged[Stdcall]<nint, char*, char*, int, nint, int>)Slot(writer, 3);
                    fixed (char* n = name)
                    fixed (char* t = type)
                        Marshal.ThrowExceptionForHR(add(writer, n, t, 1, input)); // APPX_COMPRESSION_NORMAL = 1
                }
                finally
                {
                    Marshal.Release(input);
                }
            }

            const uint StgmReadManifest = 0, StgmShareDenyWriteManifest = 0x20;
            Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(manifestPath, StgmReadManifest | StgmShareDenyWriteManifest, 0, 0, 0, out var manifest));
            try
            {
                var close = (delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(writer, 4);
                Marshal.ThrowExceptionForHR(close(writer, manifest));
            }
            finally
            {
                Marshal.Release(manifest);
            }
        }
        finally
        {
            if (writer != 0) Marshal.Release(writer);
            if (hash != 0) Marshal.Release(hash);
            if (output != 0) Marshal.Release(output);
            if (factory != 0) Marshal.Release(factory);
        }
    }

    private static unsafe nint Slot(nint obj, int index) =>
        ((nint*)(*(nint*)obj))[index];

    [StructLayout(LayoutKind.Sequential)]
    private struct PackageSettings
    {
        public int ForceZip32;
        public nint HashMethod;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern unsafe int CoCreateInstance(Guid* rclsid, nint pUnkOuter, uint dwClsContext, Guid* riid, out nint ppv);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateStreamOnFileEx(string pszFile, uint grfMode, uint dwAttributes, int fCreate, nint pstmTemplate, out nint ppstm);

    [DllImport("urlmon.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CreateUri(string pwzURI, uint dwFlags, nuint dwReserved, out nint ppURI);
}

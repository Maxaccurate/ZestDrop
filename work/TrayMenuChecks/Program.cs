using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ZestDrop;
using Forms = System.Windows.Forms;

internal static class Checks
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "outputs/tray-menu-checks.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string settings = Path.Combine(Path.GetTempPath(), "zestdrop-tray-check-" + Guid.NewGuid().ToString("N") + ".json");
        string registry = @"Software\ZestDrop\Tests\Tray-" + Guid.NewGuid().ToString("N");
        var oldProvider = StartupManager.ProviderOverride;
        var oldSettings = Settings.PathOverride;
        var results = new List<object>();
        int failed = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); }
            catch (Exception error) { failed++; results.Add(new { name, passed = false, error = error.GetBaseException().Message }); }
        }
        try
        {
            Settings.PathOverride = settings;
            L.UseForSession("zh");
            Check("welcome is directly available even when hidden at launch", () =>
            {
                Settings.Set(WelcomeWindow.HideAtStartKey, true);
                StartupManager.ProviderOverride = new FakeProvider();
                int opened = 0;
                using var menu = new TrayMenu(new TrayMenuActions { Welcome = () => opened++ }, false);
                Require(menu.Menu.Items.IndexOf(menu.Welcome) == 2);
                Require(menu.Welcome.Text == "欢迎窗口…" && menu.Welcome.Enabled);
                menu.Welcome.PerformClick();
                Require(opened == 1 && Settings.GetBool(WelcomeWindow.HideAtStartKey, false));
            });
            Check("real menu clicks enable and disable startup and persist across menu recreation", () =>
            {
                var provider = new RegistryStartupProvider(registry, "ZestDrop", @"C:\Apps with spaces\ZestDrop.exe");
                StartupManager.ProviderOverride = provider;
                using (var menu = new TrayMenu(new TrayMenuActions(), false))
                {
                    Require(!menu.Startup.Checked && menu.Startup.CheckOnClick);
                    menu.Startup.PerformClick();
                    Until(() => menu.Startup.Enabled);
                    Require(menu.Startup.Checked && provider.GetAsync().Result.Enabled);
                    using var key = Registry.CurrentUser.OpenSubKey(registry);
                    Require((string?)key?.GetValue("ZestDrop") == "\"C:\\Apps with spaces\\ZestDrop.exe\" --startup");
                }
                using (var menu = new TrayMenu(new TrayMenuActions(), false))
                {
                    Require(menu.Startup.Checked);
                    menu.Startup.PerformClick();
                    Until(() => menu.Startup.Enabled);
                    Require(!menu.Startup.Checked && !provider.GetAsync().Result.Enabled);
                }
            });
            Check("a failed startup write restores the real check state and reports the problem", () =>
            {
                StartupManager.ProviderOverride = new FakeProvider { FailWrite = true };
                string? problem = null;
                using var menu = new TrayMenu(new TrayMenuActions { StartupProblem = text => problem = text }, false);
                menu.Startup.PerformClick();
                Until(() => menu.Startup.Enabled);
                Require(!menu.Startup.Checked && problem != null && menu.Startup.ToolTipText?.Contains("Test access denial") == true);
            });
            Check("a delayed refresh cannot undo a newer startup choice", () =>
            {
                var read = new TaskCompletionSource<StartupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                var provider = new FakeProvider { NextRead = read.Task };
                StartupManager.ProviderOverride = provider;
                using var item = new Forms.ToolStripMenuItem();
                using var menu = new Forms.ContextMenuStrip();
                menu.Items.Add(item);
                using var binding = new StartupMenuBinding(item, menu, _ => { });
                item.PerformClick();
                Until(() => item.Enabled && provider.Enabled);
                read.SetResult(new StartupStatus(false));
                Pump();
                Require(item.Checked && item.Enabled);
            });
            Check("pending changes disable repeated clicks and ignore refreshes until completion", () =>
            {
                var write = new TaskCompletionSource<StartupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                var provider = new FakeProvider { PendingWrite = write.Task };
                StartupManager.ProviderOverride = provider;
                using var item = new Forms.ToolStripMenuItem();
                using var menu = new Forms.ContextMenuStrip();
                menu.Items.Add(item);
                using var binding = new StartupMenuBinding(item, menu, _ => { });
                item.PerformClick();
                Require(item.Checked && !item.Enabled && provider.Writes == 1);
                item.PerformClick();
                Require(item.Checked && provider.Writes == 1);
                binding.RefreshAsync().GetAwaiter().GetResult();
                Require(item.Checked && !item.Enabled);
                write.SetResult(new StartupStatus(true));
                Until(() => item.Enabled);
                Require(item.Checked && provider.Writes == 1);
            });
            Check("system-disabled startup keeps the toggle disabled and settings remain accessible", () =>
            {
                StartupManager.ProviderOverride = new FakeProvider { Blocked = true };
                int settingsOpened = 0;
                using var menu = new TrayMenu(new TrayMenuActions { StartupSettings = () => settingsOpened++ }, false);
                Require(!menu.Startup.Checked && !menu.Startup.Enabled && !string.IsNullOrEmpty(menu.Startup.ToolTipText));
                var settingsItem = ((Forms.ToolStripDropDownItem)menu.Startup.OwnerItem!).DropDownItems
                    .OfType<Forms.ToolStripMenuItem>().Single(i => i.Text == "Windows 启动应用设置");
                settingsItem.PerformClick();
                Require(settingsOpened == 1);
            });
            Check("disposing a menu while a read is pending does not resurrect it", () =>
            {
                var read = new TaskCompletionSource<StartupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                StartupManager.ProviderOverride = new FakeProvider { NextRead = read.Task };
                var item = new Forms.ToolStripMenuItem();
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add(item);
                var binding = new StartupMenuBinding(item, menu, _ => { });
                binding.Dispose(); menu.Dispose();
                read.SetResult(new StartupStatus(true));
                Pump();
                Require(item.IsDisposed);
            });
            Check("welcome startup clicks persist in the registry across reopening", () =>
            {
                var provider = new RegistryStartupProvider(registry, "ZestDrop", @"C:\Apps with spaces\ZestDrop.exe");
                provider.SetAsync(false).GetAwaiter().GetResult();
                StartupManager.ProviderOverride = provider;
                var window = new WelcomeWindow();
                try
                {
                    Require(StartupBox(window).IsChecked == false && StartupBox(window).IsEnabled);
                    ClickStartup(window);
                    Until(() => StartupBox(window).IsEnabled);
                    Require(StartupBox(window).IsChecked == true && provider.GetAsync().Result.Enabled);
                }
                finally { window.Close(); }
                window = new WelcomeWindow();
                try
                {
                    Require(StartupBox(window).IsChecked == true);
                    ClickStartup(window);
                    Until(() => StartupBox(window).IsEnabled);
                    Require(StartupBox(window).IsChecked == false && !provider.GetAsync().Result.Enabled);
                }
                finally { window.Close(); }
            });
            Check("a stale welcome refresh cannot overwrite a newer startup click", () =>
            {
                var provider = new FakeProvider();
                StartupManager.ProviderOverride = provider;
                var window = new WelcomeWindow();
                try
                {
                    var read = new TaskCompletionSource<StartupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                    provider.NextRead = read.Task;
                    RefreshWelcome(window);
                    ClickStartup(window);
                    Until(() => provider.Enabled && StartupBox(window).IsEnabled);
                    read.SetResult(new StartupStatus(false)); Pump();
                    Require(StartupBox(window).IsChecked == true);
                }
                finally { window.Close(); }
            });
            Check("welcome refreshes and layout rebuilds keep a pending startup change disabled", () =>
            {
                var write = new TaskCompletionSource<StartupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                var provider = new FakeProvider { PendingWrite = write.Task };
                StartupManager.ProviderOverride = provider;
                var window = new WelcomeWindow();
                try
                {
                    ClickStartup(window);
                    Require(!StartupBox(window).IsEnabled && provider.Writes == 1);
                    RefreshWelcome(window);
                    Require(!StartupBox(window).IsEnabled && StartupBox(window).IsChecked == true);
                    window.GetType().GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Require(!StartupBox(window).IsEnabled);
                    write.SetResult(new StartupStatus(true));
                    Until(() => StartupBox(window).IsEnabled);
                    Require(StartupBox(window).IsChecked == true && provider.Writes == 1);
                }
                finally { window.Close(); }
            });
            Check("a failed welcome change restores its checkbox and explains the error", () =>
            {
                StartupManager.ProviderOverride = new FakeProvider { FailWrite = true };
                var window = new WelcomeWindow();
                try
                {
                    ClickStartup(window);
                    Until(() => StartupBox(window).IsEnabled);
                    Require(StartupBox(window).IsChecked == false);
                    var message = (System.Windows.Controls.TextBlock)window.GetType().GetField("startupMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    Require(message.Text.Contains("Test access denial"));
                    var button = (System.Windows.Controls.Button)window.GetType().GetField("startupSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    Require(button.Visibility == System.Windows.Visibility.Visible);
                }
                finally { window.Close(); }
            });
            Check("closed welcome windows ignore pending startup reads", () =>
            {
                var read = new TaskCompletionSource<StartupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                StartupManager.ProviderOverride = new FakeProvider { NextRead = read.Task };
                var window = new WelcomeWindow();
                var checkbox = StartupBox(window);
                window.Close();
                read.SetResult(new StartupStatus(true)); Pump();
                Require(checkbox.IsChecked != true && !checkbox.IsEnabled);
            });
            Check("an OS startup request that is not saved explains how to resolve it", () =>
            {
                StartupManager.ProviderOverride = new FakeProvider { RefuseWrite = true };
                var status = StartupManager.SetAsync(true).GetAwaiter().GetResult();
                Require(!status.Enabled && status.Message?.Contains("Windows") == true);
            });
        }
        finally
        {
            StartupManager.ProviderOverride = oldProvider;
            Settings.PathOverride = oldSettings;
            Registry.CurrentUser.DeleteSubKeyTree(registry, false);
            File.Delete(settings); File.Delete(settings + ".tmp");
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = results.Count - failed, failed, checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"passed={results.Count - failed}, failed={failed}");
        if (failed > 0) Console.WriteLine(File.ReadAllText(output));
        app.Shutdown();
        return failed == 0 ? 0 : 1;
    }
    private static void Require(bool okay) { if (!okay) throw new Exception("Unexpected tray menu state."); }
    private static System.Windows.Controls.CheckBox StartupBox(WelcomeWindow window) =>
        (System.Windows.Controls.CheckBox)window.GetType().GetField("startup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    // Invoke the real WPF toggle/click path, using an isolated window and startup provider.
    private static void ClickStartup(WelcomeWindow window) => typeof(System.Windows.Controls.CheckBox)
        .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(StartupBox(window), null);
    private static void RefreshWelcome(WelcomeWindow window) => window.GetType()
        .GetMethod("RefreshStartup", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    private static void Until(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.ElapsedMilliseconds < 3000) { Forms.Application.DoEvents(); Thread.Sleep(10); }
        Require(ready());
    }
    private static void Pump()
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 200) { Forms.Application.DoEvents(); Thread.Sleep(10); }
    }
    private sealed class FakeProvider : IStartupProvider
    {
        public bool Enabled, FailWrite, Blocked, RefuseWrite;
        public int Writes;
        public Task<StartupStatus>? NextRead, PendingWrite;
        public Task<StartupStatus> GetAsync()
        {
            if (NextRead is { } next) { NextRead = null; return next; }
            return Task.FromResult(new StartupStatus(Enabled, !Blocked, Blocked ? "Blocked by Windows" : null));
        }
        public async Task<StartupStatus> SetAsync(bool enabled)
        {
            Writes++;
            if (FailWrite) throw new UnauthorizedAccessException("Test access denial");
            var status = PendingWrite == null ? new StartupStatus(RefuseWrite ? Enabled : enabled) : await PendingWrite;
            Enabled = status.Enabled;
            return status;
        }
    }
}

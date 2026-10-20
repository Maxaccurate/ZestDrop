using System;
using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace ZestDrop;

// Development-only host: opens the actual tray menu without touching the running app or its watch rules.
internal static class TrayMenuPreview
{
    public static int Render(string[] args)
    {
        Settings.PathOverride = Path.Combine(Path.GetTempPath(), "ZestDrop-menu-render-" + Guid.NewGuid().ToString("N") + ".json");
        var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        L.UseForSession(args.Length > 1 ? args[1] : "zh");
        UiTheme.Choose(args.Length > 2 && args[2] == "dark" ? "dark" : "light");
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        string folder = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(folder);
        using var menu = new TrayMenu(new TrayMenuActions(), explorerAvailable: true);
        void Save(Forms.ToolStrip strip, string name)
        {
            strip.CreateControl();
            menu.ApplyStyle();
            strip.PerformLayout();
            strip.Size = strip.GetPreferredSize(Size.Empty);
            using var bitmap = new Bitmap(strip.Width, strip.Height);
            strip.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            // Export the same rounded clipping region used by the native popup window.
            using var clipped = new Bitmap(strip.Width, strip.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(clipped))
            using (var path = TrayMenuRenderer.Rounded(new RectangleF(0, 0, strip.Width, strip.Height), TrayMenuCorners.Radius * strip.DeviceDpi / 96f))
            {
                graphics.SetClip(path);
                graphics.DrawImageUnscaled(bitmap, 0, 0);
            }
            clipped.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        Save(menu.Menu, "main");
        File.WriteAllText(Path.Combine(folder, "layout.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            menu.Menu.Width, menu.Menu.DeviceDpi,
            Items = System.Linq.Enumerable.Select(System.Linq.Enumerable.Cast<Forms.ToolStripItem>(menu.Menu.Items), item => new { item.Text, item.Width, item.Height, Preferred = item.GetPreferredSize(Size.Empty) })
        }));
        foreach (Forms.ToolStripItem item in menu.Menu.Items)
        {
            if (item is not Forms.ToolStripMenuItem { HasDropDownItems: true } parent) continue;
            string name = item.Text == L.T("设置") ? "settings" : item.Text == L.T("任务与进度") ? "tasks" : item.Text == L.T("自动处理") ? "automation" : item.Text == L.T("最近输出") ? "recent" : "help";
            Save(parent.DropDown, name);
            if (name == "settings")
                foreach (Forms.ToolStripItem child in parent.DropDownItems)
                    if (child is Forms.ToolStripMenuItem { HasDropDownItems: true } childMenu && child.Text == L.T("轮盘"))
                        Save(childMenu.DropDown, "wheel");
        }
        application.Shutdown();
        try { if (File.Exists(Settings.PathOverride)) File.Delete(Settings.PathOverride); } catch (IOException) { }
        return 0;
    }

    public static int Run(string[] args)
    {
        Settings.PathOverride = Path.Combine(Path.GetTempPath(), "ZestDrop-menu-preview-" + Guid.NewGuid().ToString("N") + ".json");
        // Register WPF's pack URI parser before the shared theme initializes its bundled font.
        var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        L.UseForSession(args.Length > 0 ? args[0] : "zh");
        UiTheme.Choose(args.Length > 1 && args[1] == "dark" ? "dark" : "light");
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        using var host = new Forms.Form
        {
            Text = "ZestDrop — Tray menu preview",
            StartPosition = Forms.FormStartPosition.CenterScreen,
            ClientSize = new Size(380, 215),
            BackColor = Color.FromArgb(248, 249, 250),
            Font = TrayMenuRenderer.MenuFont,
            AutoScaleMode = Forms.AutoScaleMode.Dpi,
            FormBorderStyle = Forms.FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            Icon = AppIcon.CreateTrayIcon()
        };
        var heading = new Forms.Label { Text = "ZestDrop", Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(24, 22), AutoSize = true };
        var detail = new Forms.Label { Text = "托盘菜单预览 · 不启动文件处理", Location = new Point(26, 63), AutoSize = true, ForeColor = Color.DimGray };
        var open = new Forms.Button { Text = "打开菜单", Location = new Point(26, 102), Size = new Size(150, 38) };
        var progress = new Forms.Button { Text = "模拟进行中的任务", Location = new Point(186, 102), Size = new Size(166, 38) };
        var status = new Forms.Label { Text = "点击菜单项可核对入口反馈。", Location = new Point(26, 162), Size = new Size(328, 36) };
        void Selected(string name) => status.Text = name;
        using var menu = new TrayMenu(new TrayMenuActions
        {
            Clipboard = () => Selected("已选择：处理剪贴板"),
            Progress = () => Selected("已选择：显示处理进度"),
            Pause = () => Selected("已选择：暂停全部任务"),
            Cancel = () => Selected("已选择：取消全部任务"),
            Watch = () => Selected("已选择：监视文件夹"),
            Flows = () => Selected("已选择：流程"),
            Customize = () => Selected("已选择：自定义轮盘"),
            Welcome = () => Selected("已选择：使用指南"),
            Notices = () => Selected("已选择：第三方组件许可"),
            Quit = host.Close,
            Reveal = _ => Selected("已选择：打开输出位置")
        }, explorerAvailable: true);
        open.Click += (_, _) => menu.Menu.Show(open, new Point(0, open.Height + 4));
        progress.Click += (_, _) =>
        {
            menu.Update([Path.Combine(Path.GetTempPath(), "holiday-converted.mp4")], Path.Combine(Path.GetTempPath(), "holiday-converted.mp4"), 2, true, false);
            Selected("已切换为：2 个任务 + 最近输出");
        };
        host.Controls.AddRange([heading, detail, open, progress, status]);
        host.Shown += (_, _) => menu.Menu.Show(open, new Point(0, open.Height + 4));
        Forms.Application.Run(host);
        application.Shutdown();
        try { if (File.Exists(Settings.PathOverride)) File.Delete(Settings.PathOverride); } catch (IOException) { }
        return 0;
    }
}

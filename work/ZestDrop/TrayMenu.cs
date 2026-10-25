using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using Forms = System.Windows.Forms;

namespace ZestDrop;

internal sealed class TrayMenuActions
{
    public Action Welcome { get; init; } = () => { };
    public Action Clipboard { get; init; } = () => { };
    public Action Progress { get; init; } = () => { };
    public Action Pause { get; init; } = () => { };
    public Action Cancel { get; init; } = () => { };
    public Action Watch { get; init; } = () => { };
    public Action Flows { get; init; } = () => { };
    public Action Customize { get; init; } = () => { };
    public Action ChooseOutput { get; init; } = () => { };
    public Action Explorer { get; init; } = () => { };
    public Action StartupSettings { get; init; } = StartupManager.OpenWindowsSettings;
    public Action<string> StartupProblem { get; init; } = _ => { };
    public Action Notices { get; init; } = () => { };
    public Action Quit { get; init; } = () => { };
    public Action<string> Reveal { get; init; } = _ => { };
    public Action<string> Theme { get; init; } = UiTheme.Choose;
}

// The same menu definition is used by the tray and the standalone visual preview.
internal sealed class TrayMenu : IDisposable
{
    public Forms.ContextMenuStrip Menu { get; } = new();
    public Forms.ToolStripMenuItem Welcome { get; }
    public Forms.ToolStripMenuItem Startup { get; }
    public Forms.ToolStripMenuItem? Explorer { get; }
    private readonly Forms.ToolStripMenuItem progress, pause, cancel, recent, latestFolder, emptyRecent, tasks;
    private readonly Forms.ToolStripSeparator recentSeparator;
    private readonly Forms.ToolStripMenuItem sameFolder, otherFolder;
    private readonly TrayMenuActions actions;
    private readonly StartupMenuBinding startupBinding;

    private static TrayMenuItem Item(string text, Action? action = null) =>
        new(L.T(text), null, (_, _) => action?.Invoke());

    public TrayMenu(TrayMenuActions actions, bool explorerAvailable)
    {
        this.actions = actions;
        Menu.ShowImageMargin = false;
        Menu.ShowCheckMargin = true;
        Menu.ShowItemToolTips = true;
        Menu.Items.Add(new TrayMenuHeader());
        Menu.Items.Add(new Forms.ToolStripSeparator());
        Welcome = Item("欢迎窗口…", actions.Welcome);
        Menu.Items.Add(Welcome);
        Menu.Items.Add(Item("处理剪贴板…", actions.Clipboard));
        recent = Item("最近输出");
        latestFolder = Item("打开最近输出文件夹", () => { });
        emptyRecent = Item("暂无输出记录");
        emptyRecent.Enabled = false;
        recentSeparator = new Forms.ToolStripSeparator();
        Menu.Items.Add(recent);

        tasks = Item("任务与进度");
        progress = Item("显示处理进度", actions.Progress);
        pause = Item("暂停全部任务", actions.Pause);
        cancel = Item("取消全部任务", actions.Cancel);
        tasks.DropDownItems.AddRange([progress, pause, new Forms.ToolStripSeparator(), cancel]);
        Menu.Items.Add(tasks);
        Menu.Items.Add(new Forms.ToolStripSeparator());

        var automation = Item("自动处理");
        automation.DropDownItems.AddRange([Item("监视文件夹…", actions.Watch), Item("流程…", actions.Flows)]);
        Menu.Items.Add(automation);

        var settings = Item("设置");
        var wheel = Item("轮盘");
        wheel.DropDownItems.Add(Item("自定义轮盘…", actions.Customize));
        wheel.DropDownItems.Add(new Forms.ToolStripSeparator());
        var merged = Item("合并相关工具", () => Settings.Set(Catalog.LayoutKey, "merged"));
        var twoWheels = Item("两层轮盘", () => Settings.Set(Catalog.LayoutKey, "twoWheels"));
        var pinWheel = Item("固定菜单（松手不关闭）", () =>
        {
            bool next = !Settings.GetBool(DropWheel.PinnedKey, false);
            Settings.Set(DropWheel.PinnedKey, next);
        });
        wheel.DropDownItems.AddRange([merged, twoWheels, new Forms.ToolStripSeparator(), pinWheel]);
        wheel.DropDownOpening += (_, _) =>
        {
            merged.Checked = !Catalog.TwoWheels;
            twoWheels.Checked = Catalog.TwoWheels;
            pinWheel.Checked = Settings.GetBool(DropWheel.PinnedKey, false);
        };
        settings.DropDownItems.Add(wheel);

        var output = Item("保存位置");
        sameFolder = Item("与原文件相同", () => OutputFolder.Choose(null));
        otherFolder = Item("选择文件夹…", actions.ChooseOutput);
        output.DropDownItems.AddRange([sameFolder, otherFolder]);
        output.DropDownOpening += (_, _) =>
        {
            sameFolder.Checked = OutputFolder.Current == null;
            otherFolder.Checked = OutputFolder.Current != null;
            // Keep long paths in a tooltip instead of stretching the entire menu.
            otherFolder.ToolTipText = OutputFolder.Current ?? "";
        };
        settings.DropDownItems.Add(output);

        var appearance = Item("外观");
        foreach (var (choice, label) in new[] { ("system", "跟随系统"), ("light", "浅色"), ("dark", "深色") })
        {
            var item = Item(label, () => actions.Theme(choice));
            item.Tag = choice;
            appearance.DropDownItems.Add(item);
        }
        appearance.DropDownOpening += (_, _) =>
        {
            foreach (Forms.ToolStripMenuItem item in appearance.DropDownItems)
                item.Checked = (string)item.Tag! == UiTheme.Choice;
        };
        settings.DropDownItems.Add(appearance);

        // Language names remain recognizable regardless of the active translation.
        var language = new TrayMenuItem("Language / 语言 / اللغة");
        foreach (var (code, name) in new[] { ("zh", "简体中文"), ("en", "English"), ("ar", "العربية") })
            language.DropDownItems.Add(new TrayMenuItem(name, null, (_, _) => L.Language = code) { Tag = code });
        language.DropDownOpening += (_, _) =>
        {
            foreach (Forms.ToolStripMenuItem item in language.DropDownItems)
                item.Checked = (string)item.Tag! == L.Language;
        };
        settings.DropDownItems.Add(language);
        settings.DropDownItems.Add(new Forms.ToolStripSeparator());
        if (explorerAvailable)
        {
            Explorer = Item("资源管理器右键菜单", actions.Explorer);
            settings.DropDownItems.Add(Explorer);
        }
        Startup = Item("开机自启");
        settings.DropDownItems.Add(Startup);
        settings.DropDownItems.Add(Item("Windows 启动应用设置", actions.StartupSettings));
        settings.DropDownOpening += (_, _) =>
        {
            if (Explorer != null) Explorer.Checked = ExplorerIntegration.Enabled;
        };
        Menu.Items.Add(settings);

        var help = Item("帮助");
        help.DropDownItems.AddRange([Item("使用指南…", actions.Welcome), Item("第三方组件许可", actions.Notices)]);
        Menu.Items.Add(help);
        Menu.Items.Add(new Forms.ToolStripSeparator());
        Menu.Items.Add(Item("退出 ZestDrop", actions.Quit));
        Menu.Opening += (_, _) => ApplyStyle();
        ApplyStyle();
        Update([], null, 0, false, false);
        startupBinding = new StartupMenuBinding(Startup, Menu, actions.StartupProblem);
    }

    public void Update(IReadOnlyList<string> outputs, string? lastOutput, int jobCount, bool canPause, bool canResume)
    {
        // Dispose old rows so repeatedly opening the menu doesn't retain handlers and file paths.
        foreach (var item in recent.DropDownItems.Cast<Forms.ToolStripItem>().ToArray())
            if (item != latestFolder && item != emptyRecent && item != recentSeparator) item.Dispose();
        recent.DropDownItems.Clear();
        latestFolder.Enabled = lastOutput != null;
        latestFolder.Tag = lastOutput;
        recent.DropDownItems.Add(latestFolder);
        latestFolder.Click -= RevealLatest;
        latestFolder.Click += RevealLatest;
        if (outputs.Count > 0)
        {
            recent.DropDownItems.Add(recentSeparator);
            foreach (string path in outputs.Take(8))
            {
                string name = Path.GetFileName(path);
                if (name.Length > 36) name = name[..22] + "…" + name[^10..];
                recent.DropDownItems.Add(new TrayMenuItem(name.Length > 0 ? name : path, null, (_, _) => actions.Reveal(path)) { ToolTipText = path });
            }
        }
        else recent.DropDownItems.Add(emptyRecent);
        tasks.Text = jobCount > 0 ? L.F("任务与进度（{0}）", jobCount) : L.T("任务与进度");
        progress.Enabled = cancel.Enabled = jobCount > 0;
        pause.Enabled = canPause || canResume;
        pause.Text = !canPause && canResume ? L.T("继续全部任务") : L.T("暂停全部任务");
    }

    private void RevealLatest(object? sender, EventArgs e)
    {
        if (latestFolder.Tag is string path) actions.Reveal(path);
    }

    internal void ApplyStyle()
    {
        UiTheme.Refresh();
        var renderer = new TrayMenuRenderer(UiTheme.Dark);
        void Apply(Forms.ToolStrip strip)
        {
            strip.Renderer = renderer;
            strip.Font = TrayMenuRenderer.MenuFont;
            strip.BackColor = renderer.Surface;
            strip.ForeColor = renderer.Ink;
            float scale = strip.DeviceDpi / 96f;
            strip.Padding = new Forms.Padding((int)(7 * scale), (int)(7 * scale), (int)(7 * scale), (int)(7 * scale));
            strip.RightToLeft = L.Arabic ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
            if (strip is Forms.ToolStripDropDownMenu drop)
            {
                drop.ShowImageMargin = false;
                drop.ShowCheckMargin = true;
            }
            // AutoSize only measures the visible text and otherwise clips the two-line header.
            // Measure intrinsic text only. GetPreferredSize includes MinimumItemSize from the last
            // refresh, so adding padding to it would make the menu grow every time it opens.
            // Reserve 32px for checks, 26px for arrows, 14px for outer padding, and 4px breathing room.
            int width = Math.Max((int)((strip == Menu ? 240 : 200) * scale), strip.Items.Cast<Forms.ToolStripItem>()
                .Where(item => item is Forms.ToolStripMenuItem and not TrayMenuHeader)
                .Select(item => Forms.TextRenderer.MeasureText(item.Text, strip.Font,
                    new Size(int.MaxValue, int.MaxValue), Forms.TextFormatFlags.NoPrefix).Width + (int)(76 * scale))
                .DefaultIfEmpty(0).Max());
            strip.MinimumSize = new Size(width, 0);
            foreach (Forms.ToolStripItem item in strip.Items)
            {
                item.RightToLeft = strip.RightToLeft;
                item.ForeColor = renderer.Ink;
                if (item is TrayMenuHeader)
                {
                    ((TrayMenuItem)item).MinimumItemSize = new Size(width - (int)(14 * scale), (int)(59 * scale));
                }
                else if (item is Forms.ToolStripSeparator)
                {
                    item.Margin = new Forms.Padding(0, (int)(3 * scale), 0, (int)(3 * scale));
                }
                else
                {
                    if (item is TrayMenuItem row)
                        row.MinimumItemSize = new Size(width - (int)(14 * scale), strip.Font.Height + (int)(12 * scale));
                    item.Padding = new Forms.Padding((int)(3 * scale), (int)(6 * scale), (int)(14 * scale), (int)(6 * scale));
                }
                if (item is Forms.ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
                {
                    Apply(menuItem.DropDown);
                    menuItem.DropDown.Opening -= RefreshDropDownStyle;
                    menuItem.DropDown.Opening += RefreshDropDownStyle;
                }
            }
            strip.PerformLayout();
            if (strip is Forms.ToolStripDropDown popup) TrayMenuCorners.Attach(popup);
        }
        Apply(Menu);
    }

    private void RefreshDropDownStyle(object? sender, System.ComponentModel.CancelEventArgs e) => ApplyStyle();

    public void Dispose()
    {
        startupBinding.Dispose();
        Menu.Dispose();
    }
}

internal class TrayMenuItem : Forms.ToolStripMenuItem
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Size MinimumItemSize { get; set; }
    public TrayMenuItem(string text, Image? image = null, EventHandler? click = null) : base(text, image, click) { }
    public override Size GetPreferredSize(Size constrainingSize)
    {
        var size = base.GetPreferredSize(constrainingSize);
        return new Size(Math.Max(size.Width, MinimumItemSize.Width), Math.Max(size.Height, MinimumItemSize.Height));
    }
}

internal sealed class TrayMenuHeader : TrayMenuItem
{
    public TrayMenuHeader() : base("ZestDrop") { Enabled = false; AccessibleName = "ZestDrop"; }
}

// Keep native ToolStrip keyboard navigation and accessibility, with the app's existing palette.
internal sealed class TrayMenuRenderer(bool dark) : Forms.ToolStripProfessionalRenderer
{
    internal static readonly Font MenuFont = new("Segoe UI", 9.5f, FontStyle.Regular);
    private static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f, FontStyle.Regular);
    private static readonly Font HintFont = new("Segoe UI", 8.25f, FontStyle.Regular);
    private static readonly Lazy<Icon> HeaderIcon = new(() =>
    {
        using var stream = typeof(TrayMenuRenderer).Assembly.GetManifestResourceStream("ZestDrop.AppIcon")!;
        return new Icon(stream, new Size(64, 64));
    });
    public Color Surface => ColorTranslator.FromHtml(dark ? "#26292E" : "#FFFFFF");
    public Color Ink => ColorTranslator.FromHtml(dark ? "#E6E8EB" : "#24272B");
    private Color Muted => ColorTranslator.FromHtml(dark ? "#9EA4AC" : "#737983");
    private Color Line => ColorTranslator.FromHtml(dark ? "#3A3F46" : "#E8EAED");
    private Color Hover => ColorTranslator.FromHtml(dark ? "#373B42" : "#F2F3F5");

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(Surface);
    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }
    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Line);
        float scale = e.ToolStrip.DeviceDpi / 96f;
        using var path = Rounded(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), TrayMenuCorners.Radius * scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        float scale = e.Item.Owner?.DeviceDpi / 96f ?? 1;
        var bounds = new RectangleF(2 * scale, 0, e.Item.Width - 4 * scale, e.Item.Height);
        using var path = Rounded(bounds, 7 * scale);
        using var fill = new SolidBrush(Hover);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(fill, path);
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is TrayMenuHeader)
        {
            float scale = e.Item.Owner?.DeviceDpi / 96f ?? 1;
            bool rtl = e.Item.RightToLeft == Forms.RightToLeft.Yes;
            var flags = Forms.TextFormatFlags.NoPrefix | Forms.TextFormatFlags.VerticalCenter |
                (rtl ? Forms.TextFormatFlags.Right | Forms.TextFormatFlags.RightToLeft : Forms.TextFormatFlags.Left);
            float iconX = rtl ? e.Item.Width - 44 * scale : 8 * scale;
            var tile = new RectangleF(iconX, 10 * scale, 36 * scale, 36 * scale);
            using var tilePath = Rounded(tile, 10 * scale);
            using var tileFill = new SolidBrush(ColorTranslator.FromHtml(dark ? "#3D3029" : "#FFF1E7"));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(tileFill, tilePath);
            e.Graphics.DrawIcon(HeaderIcon.Value, Rectangle.Round(new RectangleF(tile.X + 5 * scale, tile.Y + 5 * scale, 26 * scale, 26 * scale)));
            int textX = (int)((rtl ? 8 : 55) * scale);
            int textWidth = e.Item.Width - (int)(63 * scale);
            Forms.TextRenderer.DrawText(e.Graphics, "ZestDrop", TitleFont, new Rectangle(textX, (int)(9 * scale), textWidth, (int)(23 * scale)), Ink, flags);
            Forms.TextRenderer.DrawText(e.Graphics, L.T("文件转换与处理"), HintFont, new Rectangle(textX, (int)(32 * scale), textWidth, (int)(17 * scale)), Muted, flags);
            return;
        }
        e.TextColor = e.Item.Enabled ? Ink : Muted;
        // ToolStrip caches its text/arrow rectangles from the measured text width. Use the full row instead.
        float itemScale = e.Item.Owner?.DeviceDpi / 96f ?? 1;
        bool rightToLeft = e.Item.RightToLeft == Forms.RightToLeft.Yes;
        int checkInset = (int)(32 * itemScale);
        int arrowInset = (int)(26 * itemScale);
        int textLeft = rightToLeft ? arrowInset : checkInset;
        int rowTextWidth = Math.Max(0, e.Item.Width - checkInset - arrowInset);
        e.TextRectangle = new Rectangle(textLeft, 0, rowTextWidth, e.Item.Height);
        e.TextFormat = Forms.TextFormatFlags.NoPrefix | Forms.TextFormatFlags.VerticalCenter |
            (rightToLeft ? Forms.TextFormatFlags.Right | Forms.TextFormatFlags.RightToLeft : Forms.TextFormatFlags.Left);
        base.OnRenderItemText(e);
        if (e.Item is Forms.ToolStripMenuItem { HasDropDownItems: true } parent)
            DrawChevron(e.Graphics, parent);
    }
    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        int inset = (int)(10 * (e.Item.Owner?.DeviceDpi / 96f ?? 1));
        using var pen = new Pen(Line);
        e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
    }
    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
    {
        // Draw alongside the text instead; native arrow callbacks can be clipped by cached submenu bounds.
    }
    private void DrawChevron(Graphics graphics, Forms.ToolStripMenuItem item)
    {
        float scale = item.Owner?.DeviceDpi / 96f ?? 1;
        bool left = item.RightToLeft == Forms.RightToLeft.Yes;
        int width = Math.Min(item.Width, item.Owner == null ? item.Width : item.Owner.Width - item.Bounds.Left - item.Owner.Padding.Right);
        float x = left ? 13 * scale : width - 14 * scale;
        float y = item.Height / 2f;
        using var pen = new Pen(item.Enabled ? Muted : Line, 1.3f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float direction = left ? -1 : 1;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawLines(pen, new PointF[] { new(x - direction * 2 * scale, y - 3 * scale), new(x + direction * scale, y), new(x - direction * 2 * scale, y + 3 * scale) });
    }
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        float scale = e.Item.Owner?.DeviceDpi / 96f ?? 1;
        var center = new PointF(e.Item.RightToLeft == Forms.RightToLeft.Yes ? e.Item.Width - 15 * scale : 15 * scale, e.Item.Height / 2f);
        using var pen = new Pen(ColorTranslator.FromHtml(dark ? "#F1A272" : "#D55418"), 1.7f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new PointF[] { new(center.X - 4 * scale, center.Y), new(center.X - scale, center.Y + 3 * scale), new(center.X + 5 * scale, center.Y - 4 * scale) });
    }
    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class TrayMenuCorners
{
    internal const float Radius = 12;

    public static void Attach(Forms.ToolStripDropDown menu)
    {
        menu.DropShadowEnabled = true;
        menu.SizeChanged -= Refresh;
        menu.SizeChanged += Refresh;
        menu.HandleCreated -= Refresh;
        menu.HandleCreated += Refresh;
        Refresh(menu, EventArgs.Empty);
    }

    private static void Refresh(object? sender, EventArgs e)
    {
        var menu = (Forms.ToolStripDropDown)sender!;
        if (menu.Width <= 0 || menu.Height <= 0) return;
        using var path = TrayMenuRenderer.Rounded(new RectangleF(0, 0, menu.Width, menu.Height), Radius * menu.DeviceDpi / 96f);
        var previous = menu.Region;
        menu.Region = new Region(path);
        previous?.Dispose();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZestDrop;

internal sealed class WheelOrderWindow : Window
{
    private readonly ComboBox kind = new(), menu = new(), sample = new();
    private sealed record SampleChoice(string Extension, bool Multiple);
    private readonly ListBox list = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly DropWheel wheel = new((_, _) => { });
    private readonly TextBlock summary = new(), selection = new(), previewNote = new();
    private readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox lastFirst = new();
    private readonly Button moveFirst, moveUp, moveDown, reset;
    private readonly string dragFormat = "ZestDrop.WheelOrder." + Guid.NewGuid().ToString("N");
    private Point dragStart;
    private string? dragId;
    private bool filling, changingSample, previousTwoWheels;

    public WheelOrderWindow()
    {
        UiTheme.Apply(this);
        FlowDirection = L.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = L.T("自定义轮盘");
        Width = Math.Min(1060, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(800, SystemParameters.WorkArea.Height - 48);
        MinWidth = Math.Min(860, Width); MinHeight = Math.Min(620, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid { Margin = new Thickness(28, 8, 28, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(new TextBlock { Text = L.T("自定义轮盘"), FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = L.T("拖动列表调整顺序，右侧轮盘实时更新。也可点击扇区选择对应选项。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        root.Children.Add(heading);

        foreach (var (id, label) in new[] {
            ("image", "图片"), ("video", "视频"), ("audio", "音频"), ("document", "PDF 与文本"), ("archive", "压缩包"),
            ("model3d", "3D 模型"), ("cad", "CAD 图纸"), ("font", "字体"), ("data", "结构化数据"),
            ("ebook", "电子书"), ("comic", "漫画"), ("email", "电子邮件"), ("subtitle", "字幕") })
            kind.Items.Add(new ComboBoxItem { Content = L.T(label), Tag = id });
        menu.Items.Add(new ComboBoxItem { Content = L.T("工具（Ctrl+Shift）"), Tag = true });
        menu.Items.Add(new ComboBoxItem { Content = L.T("格式（Shift）"), Tag = false });
        AutomationProperties.SetName(kind, L.T("文件类型"));
        AutomationProperties.SetName(menu, L.T("菜单"));
        AutomationProperties.SetName(sample, L.T("示例文件"));
        var pickers = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.Children.Add(Labelled(L.T("文件类型"), kind));
        var mode = Labelled(L.T("菜单"), menu); Grid.SetColumn(mode, 2); pickers.Children.Add(mode);
        var input = Labelled(L.T("示例文件"), sample); Grid.SetColumn(input, 4); pickers.Children.Add(input);
        Grid.SetRow(pickers, 1); root.Children.Add(pickers);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var editor = new Grid();
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition());
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        summary.FontSize = 14; summary.FontWeight = FontWeights.SemiBold;
        summary.Margin = new Thickness(0, 0, 0, 10); editor.Children.Add(summary);
        list.AllowDrop = true;
        AutomationProperties.SetName(list, L.T("选项顺序"));
        Grid.SetRow(list, 1); editor.Children.Add(list);
        var controls = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        moveFirst = MakeButton(L.T("移到最前"), () => MoveTo(0));
        moveUp = MakeButton(L.T("上移"), () => MoveTo(list.SelectedIndex - 1));
        moveDown = MakeButton(L.T("下移"), () => MoveTo(list.SelectedIndex + 1));
        controls.Children.Add(moveFirst); controls.Children.Add(moveUp); controls.Children.Add(moveDown);
        Grid.SetRow(controls, 2); editor.Children.Add(controls); body.Children.Add(editor);

        var preview = new Grid();
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.RowDefinitions.Add(new RowDefinition());
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var previewTitle = new DockPanel { Margin = new Thickness(16, 12, 16, 0) };
        previewTitle.Children.Add(new TextBlock { Text = L.T("实时预览"), FontSize = 14, FontWeight = FontWeights.SemiBold });
        selection.Foreground = UiTheme.Accent; selection.FontSize = 12;
        selection.Margin = new Thickness(12, 2, 0, 0); selection.TextTrimming = TextTrimming.CharacterEllipsis;
        previewTitle.Children.Add(selection); preview.Children.Add(previewTitle);
        PreviewVisual = wheel.AttachEditorPreview(SelectOperation);
        var wheelView = new Viewbox { Child = PreviewVisual, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(8) };
        var previewArea = new Grid(); previewArea.Children.Add(wheelView); previewArea.Children.Add(empty);
        empty.Text = L.T("这里没有可调整的选项。"); empty.Foreground = UiTheme.Muted;
        Grid.SetRow(previewArea, 1); preview.Children.Add(previewArea);
        previewNote.FontSize = 12; previewNote.Foreground = UiTheme.Muted; previewNote.TextWrapping = TextWrapping.Wrap;
        previewNote.Margin = new Thickness(16, 0, 16, 16); Grid.SetRow(previewNote, 2); preview.Children.Add(previewNote);
        var previewCard = UiTheme.Card(preview, new Thickness(0));
        previewCard.Background = UiTheme.PreviewBack; Grid.SetColumn(previewCard, 2); body.Children.Add(previewCard);
        Grid.SetRow(body, 2); root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var preferences = new StackPanel();
        lastFirst.Content = L.T("把上次使用的选项放在最前（带 ↻ 标记）");
        lastFirst.IsChecked = Catalog.LastFirst; lastFirst.Margin = new Thickness(0);
        lastFirst.Click += (_, _) => { Settings.Set(Catalog.LastFirstKey, lastFirst.IsChecked == true); RenderPreview(); };
        preferences.Children.Add(lastFirst);
        preferences.Children.Add(new TextBlock { Text = L.T("修改自动保存，下次打开轮盘即生效。"), FontSize = 12, Foreground = UiTheme.Muted, Margin = new Thickness(6, 2, 0, 0) });
        footer.Children.Add(preferences);
        reset = MakeButton(L.T("恢复默认顺序"), () => { Catalog.SaveOrder(Kind, Tools, null); Fill(); });
        reset.Margin = new Thickness(12, 0, 0, 0); reset.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(reset, 1); footer.Children.Add(reset); Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = UiTheme.Frame(this, root, "ZestDrop");

        kind.SelectionChanged += (_, _) => { FillSamples(); Fill(); };
        menu.SelectionChanged += (_, _) => Fill();
        sample.SelectionChanged += (_, _) => { if (!changingSample) Fill(); };
        list.SelectionChanged += (_, _) => { if (!filling) RenderPreview(); };
        list.PreviewMouseLeftButtonDown += (_, e) => { dragStart = e.GetPosition(list); dragId = RowAt(e.OriginalSource as DependencyObject)?.Tag as string; };
        list.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragId == null) return;
            var movement = e.GetPosition(list) - dragStart;
            if (Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            string id = dragId; dragId = null;
            DragDrop.DoDragDrop(list, new DataObject(dragFormat, id), DragDropEffects.Move);
        };
        list.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(dragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        list.Drop += DropRow;
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && key is Key.Up or Key.Down)
            { MoveTo(list.SelectedIndex + (key == Key.Up ? -1 : 1)); e.Handled = true; }
        };
        Closed += (_, _) => wheel.Close();
        Activated += (_, _) =>
        {
            if (previousTwoWheels != Catalog.TwoWheels) Fill(SelectedId);
            else { lastFirst.IsChecked = Catalog.LastFirst; RenderPreview(); }
        };
        previousTwoWheels = Catalog.TwoWheels;
        kind.SelectedIndex = 0; menu.SelectedIndex = 0;
    }

    internal FrameworkElement PreviewVisual { get; }
    internal string Kind => (kind.SelectedItem as ComboBoxItem)?.Tag as string ?? "image";
    internal bool Tools => (menu.SelectedItem as ComboBoxItem)?.Tag is true;
    internal string? SelectedId => (list.SelectedItem as ListBoxItem)?.Tag as string;
    internal string SampleExtension => (sample.SelectedItem as ComboBoxItem)?.Tag is SampleChoice choice ? choice.Extension : Catalog.SampleExtensions(Kind)[0];
    internal bool MultipleFiles => (sample.SelectedItem as ComboBoxItem)?.Tag is SampleChoice { Multiple: true };
    internal IReadOnlyList<string> OrderedIds => list.Items.Cast<ListBoxItem>().Select(i => (string)i.Tag).ToList();
    internal void Choose(string category, bool tools)
    {
        kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == category);
        menu.SelectedIndex = tools ? 0 : 1;
    }
    internal void SelectOperation(string id)
    {
        list.SelectedItem = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == id);
        if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
    }
    internal void ChooseSample(string extension, bool multiple) => sample.SelectedItem = sample.Items.Cast<ComboBoxItem>()
        .First(i => i.Tag is SampleChoice choice && choice.Extension == extension && choice.Multiple == multiple);
    internal void MoveTo(int target)
    {
        int index = list.SelectedIndex;
        if (index < 0 || target < 0 || target >= list.Items.Count || target == index) return;
        var ids = OrderedIds.ToList(); string moved = ids[index]; ids.RemoveAt(index); ids.Insert(target, moved);
        Catalog.SaveVisibleOrder(Kind, Tools, ids); Fill(moved);
    }
    private void FillSamples()
    {
        changingSample = true; sample.Items.Clear();
        foreach (string extension in Catalog.SampleExtensions(Kind))
            foreach (bool multiple in new[] { false, true })
                sample.Items.Add(new ComboBoxItem { Tag = new SampleChoice(extension, multiple),
                    Content = extension.ToUpperInvariant() + " · " + L.T(multiple ? "多个文件" : "单个文件") });
        sample.SelectedIndex = 0; changingSample = false;
    }
    private void Fill(string? selectedId = null)
    {
        if (kind.SelectedItem == null || menu.SelectedItem == null) return;
        previousTwoWheels = Catalog.TwoWheels;
        filling = true; list.Items.Clear();
        var operations = Catalog.EditorSample(Kind, Tools, SampleExtension, MultipleFiles);
        for (int i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = "≡", Foreground = UiTheme.Muted, VerticalAlignment = VerticalAlignment.Center });
            var number = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, Foreground = UiTheme.Muted, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(number, 1); row.Children.Add(number);
            var label = new TextBlock { Text = operation.Label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 2); row.Children.Add(label);
            var item = new ListBoxItem { Content = row, Tag = operation.Id, ToolTip = operation.Label, Cursor = Cursors.Hand };
            AutomationProperties.SetName(item, (i + 1) + ". " + operation.Label);
            list.Items.Add(item);
        }
        list.SelectedItem = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == selectedId) ?? list.Items.Cast<ListBoxItem>().FirstOrDefault();
        filling = false;
        if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
        RenderPreview();
    }
    private void RenderPreview()
    {
        if (filling || kind.SelectedItem == null || menu.SelectedItem == null) return;
        var choices = Catalog.EditorSample(Kind, Tools, SampleExtension, MultipleFiles, true);
        wheel.PreviewOptions(choices, Kind, Tools, SelectedId);
        summary.Text = L.F("选项顺序 · {0} 项", list.Items.Count);
        selection.Text = choices.FirstOrDefault(o => o.Id == SelectedId)?.Label ?? "";
        empty.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviewVisual.Visibility = choices.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        previewNote.Text = L.T("示例轮盘，实际选项随文件变化。第一项在顶部，之后顺时针排列。") +
            (Tools && Catalog.TwoWheels ? " " + L.T("两层轮盘：点击分类查看外圈选项。") : "") +
            (choices.Any(o => o.Recent) ? " " + L.T("↻ 最近使用优先已开启，该选项会显示在最前。") : "");
        moveFirst.IsEnabled = moveUp.IsEnabled = list.SelectedIndex > 0;
        moveDown.IsEnabled = list.SelectedIndex >= 0 && list.SelectedIndex < list.Items.Count - 1;
        reset.IsEnabled = list.Items.Count > 0;
    }
    private ListBoxItem? RowAt(DependencyObject? element) => element == null ? null : ItemsControl.ContainerFromElement(list, element) as ListBoxItem;
    private void DropRow(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(dragFormat) || e.Data.GetData(dragFormat) is not string id) return;
        int source = OrderedIds.ToList().IndexOf(id);
        if (source < 0) return;
        var targetRow = RowAt(e.OriginalSource as DependencyObject);
        int insertion = targetRow == null ? list.Items.Count : list.Items.IndexOf(targetRow) + (e.GetPosition(targetRow).Y >= targetRow.ActualHeight / 2 ? 1 : 0);
        if (source < insertion) insertion--;
        SelectOperation(id); MoveTo(insertion);
        e.Effects = DragDropEffects.Move; e.Handled = true;
    }
    private static StackPanel Labelled(string label, UIElement control)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = label, Foreground = UiTheme.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }); panel.Children.Add(control); return panel;
    }
    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 8, 12, 8) };
        button.Click += (_, _) => click(); return button;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace ZestDrop;

// Lists the watched folders and lets the user add or remove one with advanced options.
internal sealed class WatchWindow : Window
{
    private readonly List<WatchRule> rules = WatchFolders.Load();
    private readonly ListBox list = new() { MinHeight = 120, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBox folder = new() { Padding = new Thickness(8), IsReadOnly = true };
    private readonly TextBox outputFolder = new() { Padding = new Thickness(8), IsReadOnly = true };
    private readonly TextBox filter = new() { Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox kind = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox action = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox includeSubfolders = new() { Content = L.T("包含子文件夹（递归监视）"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox moveToOriginals = new() { Content = L.T("处理完成后移动原文件至 originals 文件夹（收件箱模式）"), Margin = new Thickness(0, 6, 0, 0) };
    private readonly Action changed;

    public WatchWindow(Action changed)
    {
        this.changed = changed;
        Title = L.T("监视文件夹");
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        UiTheme.Apply(this);

        var stack = new StackPanel { Margin = new Thickness(28, 12, 28, 24) };
        stack.Children.Add(new TextBlock
        {
            Text = L.F("放进这些文件夹的新文件会自动处理。默认保存在原文件夹的“{0}”目录中，或自定义指定的输出文件夹。", WatchFolders.OutputName),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(list);

        var remove = new Button
        {
            Content = L.T("移除选中的文件夹"),
            Padding = new Thickness(12, 7, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
            {
                rules.RemoveAt(list.SelectedIndex);
                Commit();
            }
        };
        stack.Children.Add(remove);

        stack.Children.Add(new TextBlock { Text = L.T("添加监视规则"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

        // Source folder
        stack.Children.Add(new TextBlock { Text = L.T("要监视的文件夹："), Margin = new Thickness(0, 4, 0, 2), Foreground = System.Windows.Media.Brushes.Gray });
        var pickSource = new DockPanel();
        var browseSource = new Button { Content = L.T("选择…"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browseSource, Dock.Right);
        browseSource.Click += (_, _) =>
        {
            using var dialog = new Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
                folder.Text = dialog.SelectedPath;
        };
        pickSource.Children.Add(browseSource);
        pickSource.Children.Add(folder);
        stack.Children.Add(pickSource);

        // Custom output folder
        stack.Children.Add(new TextBlock { Text = L.T("输出保存位置（留空默认存放在原文件夹中）："), Margin = new Thickness(0, 8, 0, 2), Foreground = System.Windows.Media.Brushes.Gray });
        var pickOutput = new DockPanel();
        var clearOutput = new Button { Content = L.T("清除"), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(clearOutput, Dock.Right);
        clearOutput.Click += (_, _) => outputFolder.Text = "";
        var browseOutput = new Button { Content = L.T("选择…"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browseOutput, Dock.Right);
        browseOutput.Click += (_, _) =>
        {
            using var dialog = new Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
                outputFolder.Text = dialog.SelectedPath;
        };
        pickOutput.Children.Add(clearOutput);
        pickOutput.Children.Add(browseOutput);
        pickOutput.Children.Add(outputFolder);
        stack.Children.Add(pickOutput);

        // Kind selection
        stack.Children.Add(new TextBlock { Text = L.T("文件类型与转换动作："), Margin = new Thickness(0, 8, 0, 2), Foreground = System.Windows.Media.Brushes.Gray });
        foreach (var (id, label) in new[]
        {
            ("image", L.T("图片 (JPG, PNG, WebP, HEIC...)")),
            ("model3d", L.T("3D 模型 (GLB, STL, OBJ, 3MF, PLY...)")),
            ("cad", L.T("CAD 图纸 (DWG, DXF)")),
            ("document", L.T("文档 (PDF, Word, TXT...)")),
            ("video", L.T("视频 (MP4, MKV, MOV...)")),
            ("audio", L.T("音频 (MP3, WAV, FLAC...)")),
            ("all", L.T("所有文件"))
        })
        {
            kind.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        }
        kind.SelectionChanged += (_, _) => FillActions();
        stack.Children.Add(kind);
        stack.Children.Add(action);

        // Filter
        stack.Children.Add(new TextBlock { Text = L.T("文件名匹配筛选（可选，如 *.stl, *.dwg, scan_*，逗号分隔，留空处理所有）："), Margin = new Thickness(0, 8, 0, 2), Foreground = System.Windows.Media.Brushes.Gray });
        stack.Children.Add(filter);

        // Options
        stack.Children.Add(includeSubfolders);
        stack.Children.Add(moveToOriginals);

        // Add button
        var add = new Button
        {
            Content = L.T("添加监视文件夹"),
            Padding = new Thickness(18, 8, 18, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(folder.Text) ||
                kind.SelectedItem is not ComboBoxItem { Tag: string type } ||
                action.SelectedItem is not ComboBoxItem { Tag: string chosen })
                return;

            rules.Add(new WatchRule(
                folder.Text,
                type,
                chosen,
                includeSubfolders.IsChecked == true,
                string.IsNullOrWhiteSpace(outputFolder.Text) ? null : outputFolder.Text,
                moveToOriginals.IsChecked == true,
                filter.Text.Trim()
            ));

            folder.Text = "";
            outputFolder.Text = "";
            filter.Text = "";
            includeSubfolders.IsChecked = false;
            moveToOriginals.IsChecked = false;
            Commit();
        };
        stack.Children.Add(add);

        Content = UiTheme.Frame(this, stack, "ZestDrop  /  " + L.T("监视文件夹"));
        kind.SelectedIndex = 0;
        Refresh();
    }

    private void FillActions()
    {
        action.Items.Clear();
        if (kind.SelectedItem is ComboBoxItem { Tag: string type })
        {
            foreach (var (id, label) in WatchFolders.Actions(type))
                action.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        }
        if (action.Items.Count > 0)
            action.SelectedIndex = 0;
    }

    private void Commit()
    {
        WatchFolders.Save(rules);
        Refresh();
        changed();
    }

    private void Refresh()
    {
        list.Items.Clear();
        foreach (var rule in rules)
        {
            string label = WatchFolders.Actions(rule.Kind).FirstOrDefault(a => a.Action == rule.Action).Label ?? rule.Action;
            string details = rule.Folder + "  →  " + label;
            var tags = new List<string>();
            if (rule.IncludeSubfolders) tags.Add(L.T("含子文件夹"));
            if (rule.MoveToOriginals) tags.Add(L.T("移至originals"));
            if (!string.IsNullOrWhiteSpace(rule.Filter)) tags.Add(L.F("筛选: {0}", rule.Filter));
            if (!string.IsNullOrWhiteSpace(rule.OutputFolder)) tags.Add(L.F("输出: {0}", System.IO.Path.GetFileName(rule.OutputFolder.TrimEnd('\\'))));

            if (tags.Count > 0)
                details += $"  [{string.Join(" · ", tags)}]";

            list.Items.Add(details);
        }
    }
}

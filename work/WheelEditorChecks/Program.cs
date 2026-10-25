using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ZestDrop;
using IOPath = System.IO.Path;

internal static class Checks
{
    [STAThread] public static int Main(string[] args)
    {
        string output = IOPath.GetFullPath(args.Length > 0 ? args[0] : "outputs/wheel-editor-checks");
        Directory.CreateDirectory(output);
        string settings = IOPath.Combine(output, "isolated-settings.json");
        if (File.Exists(settings)) throw new IOException("Use a fresh test output folder.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var previous = Settings.PathOverride;
        Settings.PathOverride = settings;
        L.UseForSession("zh");
        var results = new List<object>(); int failed = 0;
        void Check(string name, Action run)
        {
            try { run(); results.Add(new { name, passed = true }); }
            catch (Exception error) { failed++; results.Add(new { name, passed = false, error = error.GetBaseException().Message }); }
        }
        WheelOrderWindow? editor = null;
        try
        {
            Settings.Set(Catalog.LastFirstKey, false);
            Settings.Set(Catalog.LayoutKey, "merged");
            Settings.Set(UiTheme.ThemeKey, "dark");
            editor = new WheelOrderWindow();
            Check("the real wheel visual is embedded and matches the initial stored order", () =>
            {
                Require(editor.PreviewVisual.Parent is Viewbox);
                Require(PetalIds(editor).SequenceEqual(editor.OrderedIds));
                Require(editor.OrderedIds.Count > 1);
            });
            string[] original = editor.OrderedIds.ToArray();
            Check("moving an option persists and immediately changes visible petal order", () =>
            {
                editor.SelectOperation(original[1]); editor.MoveTo(0);
                Require(editor.SelectedId == original[1]);
                Require(Catalog.SavedOrder("image", true)[0] == original[1]);
                Require(PetalIds(editor).First() == original[1]);
            });
            Check("selecting a list option highlights the corresponding actual petal", () =>
            {
                editor.SelectOperation(original[^1]);
                var shapes = Descendants<System.Windows.Shapes.Path>(editor.PreviewVisual).Where(p => p.Tag is string).ToList();
                var chosen = shapes.Single(p => (string)p.Tag == original[^1]);
                Require(shapes.Where(p => p != chosen).All(p => p.Fill != chosen.Fill));
            });
            Check("file types and format/tool menus retain independent orders", () =>
            {
                var saved = Catalog.SavedOrder("image", true).ToArray();
                editor.Choose("font", false);
                Require(editor.OrderedIds.Contains("convert:woff"));
                Require(PetalIds(editor).SequenceEqual(editor.OrderedIds));
                editor.SelectOperation(editor.OrderedIds[1]); editor.MoveTo(0);
                Require(Catalog.SavedOrder("image", true).SequenceEqual(saved));
                editor.Choose("image", true);
                Require(editor.OrderedIds.SequenceEqual(Catalog.EditorSample("image", true, "jpg", false).Select(o => o.Id)));
            });
            Check("restore defaults clears only the current stored order and updates the preview", () =>
            {
                var button = Descendants<Button>((DependencyObject)editor.Content).Single(b => (string?)b.Content == L.T("恢复默认顺序"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(Catalog.SavedOrder("image", true).Count == 0);
                Require(editor.OrderedIds.SequenceEqual(original));
                Require(PetalIds(editor).SequenceEqual(original));
                Require(Catalog.SavedOrder("font", false).Count > 0);
            });
            Check("recent-first preview reflects the runtime rule without rewriting the custom order", () =>
            {
                var saved = Catalog.SavedOrder("image", true).ToArray();
                Settings.Set("wheelLast:image:tools", original[^1]);
                Settings.Set(Catalog.LastFirstKey, true);
                editor.SelectOperation(original[^1]);
                Require(PetalIds(editor).First() == original[^1]);
                Require(Catalog.SavedOrder("image", true).SequenceEqual(saved));
                Settings.Set(Catalog.LastFirstKey, false);
            });
            Check("editing a single-file example preserves options saved for batch examples", () =>
            {
                editor.Choose("image", true); editor.ChooseSample("jpg", true);
                Require(editor.OrderedIds.Contains("packArchive"));
                editor.SelectOperation("packArchive"); editor.MoveTo(0);
                editor.ChooseSample("jpg", false);
                Require(!editor.OrderedIds.Contains("packArchive"));
                editor.SelectOperation(editor.OrderedIds[1]); editor.MoveTo(0);
                Require(Catalog.SavedOrder("image", true).Contains("packArchive"));
                editor.ChooseSample("jpg", true);
                Require(editor.OrderedIds[0] == "packArchive");
                editor.ChooseSample("jpg", false);
            });
            Check("two-level live and preview wheels both respect custom category and child order", () =>
            {
                Settings.Set(Catalog.LayoutKey, "twoWheels");
                editor.Choose("video", true);
                var options = Catalog.EditorSample("video", true, "mp4", false);
                var before = Catalog.Grouped(options, "video")!;
                Require(before.Count > 1);
                string chosen = before[^1].Items[^1].Id;
                editor.SelectOperation(chosen); editor.MoveTo(0);
                var grouped = Catalog.Grouped(Catalog.EditorSample("video", true, "mp4", false), "video")!;
                Require(grouped[0].Items[0].Id == chosen);
                Require(editor.PreviewVisual.Width == 640);
                Require(PetalIds(editor).Contains(chosen));
            });
            Check("all supported editor categories populate without executing conversions", () =>
            {
                foreach (var category in new[] { "image", "video", "audio", "document", "archive", "model3d", "cad", "font", "data", "ebook", "comic", "email", "subtitle" })
                    foreach (bool tools in new[] { false, true })
                    {
                        editor.Choose(category, tools);
                        Require(editor.OrderedIds.SequenceEqual(Catalog.EditorSample(category, tools, editor.SampleExtension, editor.MultipleFiles).Select(o => o.Id)));
                        Require(editor.PreviewVisual.Visibility == (editor.OrderedIds.Count == 0 ? Visibility.Collapsed : Visibility.Visible));
                    }
            });
            editor.Close(); editor = null;
            Check("light and dark, English and Arabic layouts render at both wheel sizes", () =>
            {
                foreach (string lang in new[] { "zh", "en", "ar" })
                    foreach (string theme in new[] { "light", "dark" })
                        foreach (string layout in new[] { "merged", "twoWheels" })
                        {
                            L.UseForSession(lang); Settings.Set(UiTheme.ThemeKey, theme); Settings.Set(Catalog.LayoutKey, layout);
                            using var view = new EditorLifetime();
                            var window = view.Window;
                            window.Choose("image", true);
                            window.Width = 1060; window.Height = 800;
                            var root = (FrameworkElement)window.Content;
                            root.Measure(new Size(1060, 800)); root.Arrange(new Rect(0, 0, 1060, 800)); root.UpdateLayout();
                            var bitmap = new RenderTargetBitmap(1060, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(IOPath.Combine(output, $"{lang}-{theme}-{layout}.png")); png.Save(file);
                            Require(window.PreviewVisual.ActualWidth > 0);
                        }
            });
        }
        finally { editor?.Close(); Settings.PathOverride = previous; }
        File.WriteAllText(IOPath.Combine(output, "checks.json"), JsonSerializer.Serialize(new { passed = results.Count - failed, failed, checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"passed={results.Count - failed}, failed={failed}");
        if (failed > 0) Console.WriteLine(File.ReadAllText(IOPath.Combine(output, "checks.json")));
        app.Shutdown(); return failed == 0 ? 0 : 1;
    }
    private sealed class EditorLifetime : IDisposable
    {
        public WheelOrderWindow Window { get; } = new();
        public void Dispose() => Window.Close();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static IEnumerable<string> PetalIds(WheelOrderWindow window) => Descendants<System.Windows.Shapes.Path>(window.PreviewVisual).Where(p => p.Tag is string).Select(p => (string)p.Tag);
    private static void Require(bool condition) { if (!condition) throw new Exception("Unexpected wheel editor state."); }
}

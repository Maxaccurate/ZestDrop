using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace ZestDrop;

// Development aids for screenshots and checks (packaging/make-screenshots.py, work/test-office-extra.py). Not used by the running app.
internal static class DebugModes
{
    // Returns the exit code when args name a debug mode, or null to start the app normally.
    public static int? Run(string[] args)
    {
        // Optional 5th argument: a scene such as "crop=250,200,800,600;ratio=4:3" or "busy" (see ToolWindow.DebugScene).
        if (args.Length is 4 or 5 && args[0] == "--debug-tool-render")
        {
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new ToolWindow([args[2]], Catalog.Definition(args[1], Catalog.Category(args[2])), Backend.Execute);
            window.Loaded += async (_, _) =>
            {
                for (int attempt = 0; attempt < 150 && !window.DebugReady; attempt++)
                    await Task.Delay(100);
                if (args.Length == 5)
                {
                    window.DebugScene(args[4]);
                    await Task.Delay(700);
                }
                window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var background = new System.Windows.Media.DrawingVisual();
                using (var dc = background.RenderOpen())
                    dc.DrawRoundedRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight),
                        window.WindowState == WindowState.Maximized ? 0 : UiTheme.WindowCornerRadius,
                        window.WindowState == WindowState.Maximized ? 0 : UiTheme.WindowCornerRadius);
                bitmap.Render(background);
                bitmap.Render((System.Windows.Media.Visual)window.Content);
                Program.SavePng(bitmap, args[3]);
                window.Close();
            };
            testApp.Run(window);
            return 0;
        }
        if (args.Length == 3 && args[0] == "--capabilities")
        {
            string[] files = [args[1]];
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { Category = Catalog.Category(args[1]), Conversions = Catalog.Options(files, false), Tools = Catalog.Options(files, true) }));
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--debug-tool")
        {
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var definition = Catalog.Definition(args[1], Catalog.Category(args[2]));
            testApp.Run(definition.Parts != null ? new ComboWindow(args.Skip(2).ToArray(), definition, Backend.Execute) : new ToolWindow(args.Skip(2).ToArray(), definition, Backend.Execute));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--debug-window")
        {
            // Opens one of the small windows on its own, for checking its look (both languages and themes).
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Window window = args[1] switch { "wheel-order" => new WheelOrderWindow(), "watch" => new WatchWindow(() => { }), "flows" => new FlowWindow(() => { }), _ => new WelcomeWindow() };
            testApp.Run(window);
            return 0;
        }
        if (args.Length == 5 && args[0] == "--debug-wheel-render")
        {
            // --debug-wheel-render <formats|tools> <file> <operation id to highlight> <out.png>
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            testApp.Dispatcher.InvokeAsync(() =>
            {
                var wheel = new DropWheel((_, _) => { });
                wheel.Preview([args[2]], args[1] == "tools", args[3]);
                var root = (FrameworkElement)wheel.Content;
                double stage = wheel.StageSize;
                root.Measure(new Size(stage, stage));
                root.Arrange(new Rect(0, 0, stage, stage));
                root.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(stage * 3), (int)(stage * 3), 288, 288, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                Program.SavePng(bitmap, args[4]);
                testApp.Shutdown();
            });
            testApp.Run();
            return 0;
        }
        if (args.Length == 2 && args[0] == "--debug-indicator")
        {
            // Renders the progress stack in several states to <prefix>-<state>.png for visual checks.
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var stack = new TaskIndicatorWindow(headless: true);
            void Render(string state)
            {
                var root = (FrameworkElement)stack.Content;
                root.Width = stack.Width;
                root.Measure(new Size(stack.Width, double.PositiveInfinity));
                root.Arrange(new Rect(root.DesiredSize));
                root.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                var backdrop = new System.Windows.Media.DrawingVisual();
                using (var dc = backdrop.RenderOpen())
                    dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 217, 221)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bitmap.Render(backdrop);
                bitmap.Render(root);
                Program.SavePng(bitmap, $"{args[1]}-{state}.png");
            }
            testApp.Dispatcher.InvokeAsync(() =>
            {
                var first = stack.Add(new ConversionJob(["B&W"], "pack:zip"));
                first.Start().Report(new JobProgress("processing", 0, 1, "B&W"));
                Render("single");
                var second = stack.Add(new ConversionJob([L.T("假期视频.mp4")], "convert:webm"));
                second.Start().Report(new JobProgress("processing", 0, 1, L.T("假期视频.mp4")));
                second.SetPaused(true);
                var third = stack.Add(new ConversionJob([L.T("照片 1.heic"), L.T("照片 2.heic"), L.T("照片 3.heic")], "convert:jpg"));
                stack.Update();
                Render("expanded");
                stack.SetExpanded(false);
                Render("collapsed");
                stack.SetExpanded(true);
                stack.HideCard(first);
                Render("one-hidden");
                testApp.Shutdown();
            });
            testApp.Run();
            return 0;
        }
        return null;
    }
}

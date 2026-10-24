using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace ZestDrop.Engines.Vector.Tracing;

public static class VectorTracingEngine
{
    private static readonly HashSet<string> SupportedInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "webp", "bmp", "gif", "ico", "tiff", "tif", "avif"
    };

    public const int MaxPixels = 6_000_000;
    public const int MaxSide = 4096;

    public static bool CanHandle(string from, string to)
    {
        return SupportedInputs.Contains(from) && to.Equals("svg", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task ConvertAsync(
        string inputPath,
        string outputPath,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            progress?.Invoke(0.05);
            cancellationToken.ThrowIfCancellationRequested();

            using var original = SKBitmap.Decode(inputPath);
            if (original == null)
                throw new InvalidDataException($"Could not decode image at '{inputPath}'.");

            int width = original.Width;
            int height = original.Height;
            if (width <= 0 || height <= 0)
                throw new InvalidDataException("Invalid image dimensions.");

            var (tracedWidth, tracedHeight) = TracedSize(width, height);

            // Render to straight RGBA8888 with unpremultiplied alpha
            using var rgbaBitmap = new SKBitmap(new SKImageInfo(tracedWidth, tracedHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (var canvas = new SKCanvas(rgbaBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(original, new SKRect(0, 0, tracedWidth, tracedHeight), paint);
                canvas.Flush();
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(0.15);

            byte[] pixels = rgbaBitmap.GetPixelSpan().ToArray();
            var settings = TraceSettings.From(TracingOptions.Default, Math.Max(tracedWidth, tracedHeight));

            var traceProgress = progress != null
                ? new Progress<double>(p => progress.Invoke(0.15 + 0.8 * p))
                : null;

            var result = ImageTracer.Trace(pixels, tracedWidth, tracedHeight, settings, traceProgress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var comment = tracedWidth == width && tracedHeight == height
                ? null
                : string.Create(CultureInfo.InvariantCulture, $"Traced from {width}x{height} px image reduced to {tracedWidth}x{tracedHeight} px.");

            using (var stream = File.Create(outputPath))
            {
                ImageTracer.WriteSvg(result, stream, width, height, comment);
            }

            progress?.Invoke(1.0);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static (int Width, int Height) TracedSize(int width, int height)
    {
        var scale = Math.Min(1, Math.Min(MaxSide / (double)Math.Max(width, height), Math.Sqrt(MaxPixels / ((double)width * height))));
        if (scale >= 1)
            return (width, height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static void Convert(string inputPath, string outputPath)
    {
        ConvertAsync(inputPath, outputPath, null, CancellationToken.None).GetAwaiter().GetResult();
    }
}


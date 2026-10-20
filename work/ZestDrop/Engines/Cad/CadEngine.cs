using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace ZestDrop.Engines.Cad;

public static class CadEngine
{
    public static readonly string[] Formats = ["dwg", "dxf"];
    public static readonly string[] VectorTargets = ["pdf", "svg"];
    public static readonly string[] ImageTargets = ["png", "jpg", "jpeg", "webp", "bmp"];

    public static bool CanHandle(string fromExt, string toExt)
    {
        fromExt = fromExt.TrimStart('.').ToLowerInvariant();
        toExt = toExt.TrimStart('.').ToLowerInvariant();

        if (!Formats.Contains(fromExt)) return false;

        return Formats.Contains(toExt) || VectorTargets.Contains(toExt) || ImageTargets.Contains(toExt);
    }

    public static Task<IReadOnlyList<string>> ConvertAsync(
        string inputPath,
        string targetFormat,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
            targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();

            progress?.Report(0.05);
            var document = CadFile.Read(inputPath, ext);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.4);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (targetFormat is "dxf" or "dwg")
            {
                CadFile.Write(document, outputPath, targetFormat);
                progress?.Report(1.0);
                return [outputPath];
            }

            var scene = CadSceneBuilder.Build(document, cancellationToken);
            if (scene.Items.Count == 0)
            {
                throw new InvalidDataException(scene.Skipped.Count > 0
                    ? $"The drawing only contains objects that cannot be drawn ({string.Join(", ", scene.Skipped.Keys)})."
                    : "The drawing has nothing visible to draw: model space and the layout are empty, or every entity is on a layer that is off, frozen or not plotted.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.7);

            switch (targetFormat)
            {
                case "pdf":
                    using (var stream = File.Create(outputPath))
                    {
                        CadRenderer.WritePdf(scene, new PdfOptions(), Path.GetFileNameWithoutExtension(inputPath), stream);
                    }
                    break;

                case "svg":
                    using (var stream = File.Create(outputPath))
                    {
                        CadRenderer.WriteSvg(scene, stream);
                    }
                    break;

                default:
                    WriteRasterImage(scene, targetFormat, outputPath);
                    break;
            }

            progress?.Report(1.0);
            return [outputPath];
        }, cancellationToken);
    }

    public static void Convert(string inputPath, string targetFormat, string outputPath)
    {
        ConvertAsync(inputPath, targetFormat, outputPath, null, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void WriteRasterImage(CadScene scene, string format, string outputPath)

    {
        using var bitmap = CadRenderer.RenderBitmap(scene, 150);
        var encodeFormat = format switch
        {
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            "bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(encodeFormat, 90);
        using var fs = File.OpenWrite(outputPath);
        data.SaveTo(fs);
    }
}

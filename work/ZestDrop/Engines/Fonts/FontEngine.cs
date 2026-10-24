using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZestDrop.Engines.Fonts;

public static class FontEngine
{
    public static readonly string[] Formats = ["ttf", "otf", "woff", "woff2", "eot"];

    public static bool CanHandle(string fromExt, string toExt)
    {
        fromExt = fromExt.TrimStart('.').ToLowerInvariant();
        toExt = toExt.TrimStart('.').ToLowerInvariant();

        return Formats.Contains(fromExt) && Formats.Contains(toExt) && fromExt != toExt;
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
            targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();

            var fontBytes = File.ReadAllBytes(inputPath);
            var font = FontFile.Load(fontBytes, cancellationToken);
            progress?.Report(0.2);

            var outlineProgress = progress != null ? new SubProgress(progress, 0.2, 0.7) : null;
            var data = FontFile.Save(font, targetFormat, cancellationToken, outlineProgress);
            progress?.Report(0.9);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outputPath, data);
            progress?.Report(1.0);

            return (IReadOnlyList<string>)[outputPath];
        }, cancellationToken);
    }

    public static void Convert(string inputPath, string targetFormat, string outputPath)
    {
        ConvertAsync(inputPath, targetFormat, outputPath, null, CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed class SubProgress(IProgress<double> parent, double start, double length) : IProgress<double>

    {
        public void Report(double value) => parent.Report(start + length * value);
    }
}

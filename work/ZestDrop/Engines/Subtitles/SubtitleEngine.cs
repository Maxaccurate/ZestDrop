using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZestDrop.Engines.Subtitles;

public static class SubtitleEngine
{
    private static readonly HashSet<string> SourceFormats = new(SubtitleReader.Formats, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TargetFormats = new(SubtitleWriter.Formats, StringComparer.OrdinalIgnoreCase);

    public static bool CanHandle(string from, string to)
    {
        return SourceFormats.Contains(from) && TargetFormats.Contains(to);
    }

    public static async Task ConvertAsync(
        string inputPath,
        string outputPath,
        string from,
        string to,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Invoke(0.1);
        var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Invoke(0.3);
        var document = SubtitleReader.Read(from, bytes);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Invoke(0.7);
        var outputBytes = SubtitleWriter.Write(document, to.ToLowerInvariant());
        cancellationToken.ThrowIfCancellationRequested();

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await File.WriteAllBytesAsync(outputPath, outputBytes, cancellationToken).ConfigureAwait(false);
        progress?.Invoke(1.0);
    }

    public static IReadOnlyCollection<string> SupportedInputs => SourceFormats;
    public static IReadOnlyCollection<string> SupportedOutputs => TargetFormats;

    public static void Convert(string inputPath, string outputPath, string from, string to)
    {
        ConvertAsync(inputPath, outputPath, from, to, null, CancellationToken.None).GetAwaiter().GetResult();
    }
}

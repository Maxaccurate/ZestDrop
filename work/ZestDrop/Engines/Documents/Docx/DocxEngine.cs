using System.Text;
using ZestDrop.Engines.Documents;

namespace ZestDrop.Engines.Documents.Docx;

public static class DocxEngine
{
    private static readonly HashSet<string> InputFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "docm", "dotx", "dotm",
        "xlsx", "xlsm", "xltx", "xls", "xlt", "ods", "csv", "tsv",
        "pptx", "pptm", "potx", "ppsx",
        "md", "txt", "html"
    };

    private static readonly HashSet<string> OutputFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "md", "txt", "html"
    };

    public static bool CanConvert(string fromExt, string toExt)
    {
        fromExt = fromExt.TrimStart('.').ToLowerInvariant();
        toExt = toExt.TrimStart('.').ToLowerInvariant();

        if (string.Equals(fromExt, toExt, StringComparison.OrdinalIgnoreCase))
            return false;

        return InputFormats.Contains(fromExt) && OutputFormats.Contains(toExt);
    }

    public static string Convert(string inputPath, string targetFormat, string? outputPath = null)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input document not found: {inputPath}");

        var fromExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var toExt = targetFormat.TrimStart('.').ToLowerInvariant();

        if (!CanConvert(fromExt, toExt))
            throw new NotSupportedException($"DocxEngine cannot convert from .{fromExt} to .{toExt}");

        outputPath ??= Path.ChangeExtension(inputPath, "." + toExt);
        var workDir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Path.GetTempPath();

        var document = DocumentReaders.Read(inputPath, fromExt, workDir);

        switch (toExt)
        {
            case "docx":
                DocxWriter.Write(document, outputPath);
                break;

            case "md":
                var md = MarkdownWriter.Write(document);
                File.WriteAllText(outputPath, md, new UTF8Encoding(false));
                break;

            case "txt":
                var txt = PlainTextWriter.Write(document);
                File.WriteAllText(outputPath, txt, new UTF8Encoding(false));
                break;

            case "html":
                var chapters = XhtmlWriter.Write(document, new XhtmlOptions
                {
                    SplitChapters = false,
                    ImageSource = _ => null
                });
                var body = chapters.Count > 0 ? chapters[0].Body : "";
                var html = XhtmlWriter.Page(document.Title ?? Path.GetFileNameWithoutExtension(inputPath), body, "en", null, false);
                File.WriteAllText(outputPath, html, new UTF8Encoding(false));
                break;

            default:
                throw new NotSupportedException($"Output format .{toExt} is not supported by DocxEngine.");
        }

        return outputPath;
    }
}


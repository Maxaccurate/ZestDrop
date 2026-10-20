// Registry of document readers: format id → reader.

using System.Globalization;
using System.Text;
using ZestDrop.Engines.Documents.Docx;
using ZestDrop.Engines.Documents;
using ZestDrop.Engines.Documents.Pptx;
using ZestDrop.Engines.Office.Sheets;

namespace ZestDrop.Engines.Documents;

internal static class DocumentReaders
{
    private static readonly Dictionary<string, Func<string, string, HDocument>> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["docx"] = DocxReader.Read,
        ["docm"] = DocxReader.Read,
        ["dotx"] = DocxReader.Read,
        ["dotm"] = DocxReader.Read,
        ["txt"] = (path, _) => TextDocument(File.ReadAllBytes(path)),
        ["md"] = (path, _) => MarkdownReader.Read(DecodeText(File.ReadAllBytes(path)), Path.GetDirectoryName(Path.GetFullPath(path)) ?? ""),
        ["xlsx"] = (path, _) => SheetDocument.Build(XlsxReader.Read(path)),
        ["xlsm"] = (path, _) => SheetDocument.Build(XlsxReader.Read(path)),
        ["xltx"] = (path, _) => SheetDocument.Build(XlsxReader.Read(path)),
        ["csv"] = (path, _) => SheetDocument.Build(CsvFormat.Read(path)),
        ["tsv"] = (path, _) => SheetDocument.Build(SpreadsheetEngine.Read(path, "tsv")),
        ["xls"] = (path, _) => SheetDocument.Build(SpreadsheetEngine.Read(path, "xls")),
        ["xlt"] = (path, _) => SheetDocument.Build(SpreadsheetEngine.Read(path, "xlt")),
        ["ods"] = (path, _) => SheetDocument.Build(SpreadsheetEngine.Read(path, "ods")),
        ["pptx"] = PptxReader.Read,
        ["pptm"] = PptxReader.Read,
        ["potx"] = PptxReader.Read,
        ["ppsx"] = PptxReader.Read,
        ["html"] = HtmlReader.Read,
    };

    public static IEnumerable<string> SupportedFormats => BuiltIn.Keys;

    public static bool CanRead(string format) => BuiltIn.ContainsKey(format);

    public static HDocument Read(string path, string format, string workDirectory)
    {
        var media = Path.Combine(workDirectory, $"media-{Guid.NewGuid():N}");
        if (BuiltIn.TryGetValue(format, out var reader))
            return reader(path, media);
        throw new NotSupportedException($"There is no document reader for {format.ToUpperInvariant()} files.");
    }

    internal static HDocument TextDocument(byte[] bytes)
    {
        var section = new HSection();
        foreach (var line in DecodeText(bytes).Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = new HParagraph();
            if (line.Length > 0)
                paragraph.Inlines.Add(new HText(line, default));
            section.Blocks.Add(paragraph);
        }
        var document = new HDocument();
        document.Sections.Add(section);
        return document;
    }

    internal static string DecodeText(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
            return new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd();
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }
}


// Native E-Book engine: EPUB, KEPUB, MOBI, AZW, AZW3, AZW4, PRC, FB2, HTMLZ, TXTZ, CBZ, CBR, CB7, CBT, CBC.
// Converts completely in-process using managed code without external Calibre or Python CLI.

using System.Text;
using ZestDrop.Engines.Documents;

namespace ZestDrop.Engines.Ebooks;

public static class EbookEngine
{
    private static readonly HashSet<string> BookReaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "epub", "kepub", "mobi", "azw3", "azw", "prc", "fb2", "htmlz", "txtz"
    };

    private static readonly HashSet<string> BookWriters = new(StringComparer.OrdinalIgnoreCase)
    {
        "epub", "kepub", "txt", "html", "md", "fb2", "htmlz", "txtz"
    };

    private static readonly HashSet<string> DocumentReaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "md", "txt"
    };

    private static readonly HashSet<string> DocumentToEbookWriters = new(StringComparer.OrdinalIgnoreCase)
    {
        "epub", "kepub", "fb2", "htmlz", "txtz"
    };

    private static readonly HashSet<string> Comics = new(StringComparer.OrdinalIgnoreCase)
    {
        "cbz", "cbr", "cb7", "cbt", "cbc"
    };

    private static readonly HashSet<string> ComicWriters = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "epub", "cbz"
    };

    public static IReadOnlyList<string> SupportedInputs => BookReaders.Concat(Comics).Concat(DocumentReaders).Distinct().ToList();
    public static IReadOnlyList<string> SupportedOutputs => BookWriters.Concat(ComicWriters).Concat(DocumentToEbookWriters).Distinct().ToList();

    public static string Convert(string inputPath, string targetFormat, string? outputPath = null)
    {
        var to = targetFormat.TrimStart('.').ToLowerInvariant();
        var outputs = ConvertAsync(inputPath, to, outputPath, null, CancellationToken.None).GetAwaiter().GetResult();
        return outputs.Count > 0 ? outputs[0] : (outputPath ?? Path.ChangeExtension(inputPath, "." + to));
    }

    public static bool CanHandle(string fromExt, string toExt)
    {
        fromExt = fromExt.TrimStart('.').ToLowerInvariant();
        toExt = toExt.TrimStart('.').ToLowerInvariant();

        if (fromExt == toExt)
            return false;

        if (fromExt == "azw4" && toExt == "pdf")
            return true;

        if (Comics.Contains(fromExt) && ComicWriters.Contains(toExt))
            return true;

        if (BookReaders.Contains(fromExt) && BookWriters.Contains(toExt))
            return true;

        if (DocumentReaders.Contains(fromExt) && DocumentToEbookWriters.Contains(toExt))
            return true;

        if (fromExt == "html" && (toExt == "txt" || toExt == "md"))
            return true;

        return false;
    }

    public static Task<IReadOnlyList<string>> ConvertAsync(string inputPath, string targetExtension, string? outputPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            progress?.Report(0.05);
            var fromExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
            var toExt = targetExtension.TrimStart('.').ToLowerInvariant();

            var tempWorkRoot = Path.Combine(Path.GetTempPath(), "ZestDrop", "Ebooks");
            var workFolder = EbookFiles.NewFolder(tempWorkRoot, "ebook");

            try
            {
                var resolvedOutput = OutputPathHelper.Resolve(inputPath, toExt, outputPath);

                // Comic books
                if (Comics.Contains(fromExt))
                {
                    var pages = ComicBook.ExtractPages(inputPath, fromExt, workFolder, cancellationToken);
                    progress?.Report(0.4);

                    switch (toExt)
                    {
                        case "pdf":
                            ComicBook.ToPdf(pages, resolvedOutput, workFolder, progress, cancellationToken);
                            break;
                        case "epub":
                            var bookTitle = Path.GetFileNameWithoutExtension(inputPath);
                            EpubWriter.WriteFixedLayout(bookTitle, pages, resolvedOutput, workFolder, cancellationToken);
                            break;
                        case "cbz":
                            ComicBook.ToCbz(pages, resolvedOutput, cancellationToken);
                            break;
                        default:
                            throw new NotSupportedException($"{fromExt} → {toExt}");
                    }
                    progress?.Report(1.0);
                    return [resolvedOutput];
                }

                // AZW4 Print Replica -> PDF
                if (fromExt == "azw4" && toExt == "pdf")
                {
                    var pdf = MobiReader.PrintReplicaPdf(inputPath);
                    File.WriteAllBytes(resolvedOutput, pdf);
                    progress?.Report(1.0);
                    return [resolvedOutput];
                }

                // Direct KEPUB ↔ EPUB fast conversion
                if (fromExt == "kepub" && toExt == "epub")
                {
                    Kepub.ToEpub(inputPath, resolvedOutput, cancellationToken);
                    progress?.Report(1.0);
                    return [resolvedOutput];
                }
                if (fromExt == "epub" && toExt == "kepub")
                {
                    Kepub.FromEpub(inputPath, resolvedOutput, cancellationToken);
                    progress?.Report(1.0);
                    return [resolvedOutput];
                }

                // Document / Ebook parsing
                var book = ReadBook(inputPath, fromExt, workFolder, cancellationToken);
                progress?.Report(0.5);
                cancellationToken.ThrowIfCancellationRequested();

                WriteBook(book, inputPath, toExt, resolvedOutput, workFolder, cancellationToken);
                progress?.Report(1.0);
                return [resolvedOutput];
            }
            finally
            {
                try
                {
                    if (Directory.Exists(workFolder))
                        Directory.Delete(workFolder, recursive: true);
                }
                catch
                {
                    // Ignore temp folder cleanup errors
                }
            }
        }, cancellationToken);
    }

    private static Book ReadBook(string inputPath, string fromExt, string workFolder, CancellationToken cancellationToken)
    {
        var media = Path.Combine(workFolder, "media");
        switch (fromExt)
        {
            case "epub" or "kepub":
                return EpubReader.ReadBook(inputPath, workFolder, cancellationToken);
            case "mobi" or "azw3" or "azw" or "prc":
                return MobiReader.ReadBook(inputPath, workFolder, cancellationToken);
            case "fb2":
                return Fb2Reader.ReadBook(inputPath, workFolder);
            case "htmlz":
                return ZippedText.ReadHtmlzBook(inputPath, workFolder, cancellationToken);
            case "txtz":
                return ZippedText.ReadTxtzBook(inputPath, workFolder);
            case "html":
                {
                    var document = HtmlReader.Read(inputPath, media);
                    return new Book(document, new BookMetadata { Title = document.Title });
                }
            case "md":
                {
                    var document = MarkdownReader.Read(PlainText.DecodeText(File.ReadAllBytes(inputPath)), Path.GetDirectoryName(Path.GetFullPath(inputPath))!);
                    return new Book(document, new BookMetadata { Title = document.Title });
                }
            case "txt":
                return new Book(PlainText.ToDocument(PlainText.DecodeText(File.ReadAllBytes(inputPath))), new BookMetadata());
            default:
                throw new NotSupportedException($"Unsupported input format: {fromExt}");
        }
    }

    private static void WriteBook(Book book, string inputPath, string toExt, string resolvedOutput, string workFolder, CancellationToken cancellationToken)
    {
        switch (toExt)
        {
            case "epub":
                EpubWriter.Write(book, resolvedOutput, inputPath, workFolder);
                break;
            case "kepub":
                {
                    var tempEpub = Path.Combine(workFolder, $"{Guid.NewGuid():N}.epub");
                    EpubWriter.Write(book, tempEpub, inputPath, workFolder);
                    Kepub.FromEpub(tempEpub, resolvedOutput, cancellationToken);
                    break;
                }
            case "fb2":
                Fb2Writer.Write(book, resolvedOutput, inputPath, workFolder);
                break;
            case "htmlz":
                ZippedText.WriteHtmlz(book, resolvedOutput, inputPath, workFolder);
                break;
            case "txtz":
                ZippedText.WriteTxtz(book, resolvedOutput, inputPath, workFolder);
                break;
            case "txt":
                File.WriteAllText(resolvedOutput, PlainTextWriter.Write(book.Document), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                break;
            case "html":
                {
                    var images = EbookFiles.NewFolder(workFolder, "html-images");
                    var page = XhtmlWriter.Write(book.Document, new XhtmlOptions { ImageSource = path => DataUri(path, images) }).Single();
                    File.WriteAllText(resolvedOutput, XhtmlWriter.Page(book.DisplayTitle(inputPath), page.Body, book.DisplayLanguage(), null, epub: false), new UTF8Encoding(false));
                    break;
                }
            case "md":
                File.WriteAllText(resolvedOutput, MarkdownWriter.Write(book.Document, null), new UTF8Encoding(false));
                break;
            default:
                throw new NotSupportedException($"Unsupported output format: {toExt}");
        }
    }

    private static string? DataUri(string path, string convertFolder) =>
        EbookFiles.CommonImage(path, convertFolder, gifAndSvg: true) is ({ } file, { } mediaType)
            ? $"data:{mediaType};base64,{System.Convert.ToBase64String(File.ReadAllBytes(file))}"
            : null;
}


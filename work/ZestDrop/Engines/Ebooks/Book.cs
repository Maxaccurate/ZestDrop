// An e-book in memory: the document model (content) plus the metadata e-book formats carry (title, authors, language, cover).

using System.IO.Compression;
using SkiaSharp;
using ZestDrop.Engines.Documents;

namespace ZestDrop.Engines.Ebooks;

/// <summary>Title, authors, language and cover of a book.</summary>
internal sealed class BookMetadata
{
    public string? Title { get; set; }
    public List<string> Authors { get; } = [];
    public string? Language { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? CoverImage { get; set; }
}

/// <summary>An e-book: content and metadata.</summary>
internal sealed record Book(HDocument Document, BookMetadata Metadata)
{
    public string DisplayTitle(string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(Metadata.Title))
            return Metadata.Title.Trim();
        if (!string.IsNullOrWhiteSpace(Document.Title))
            return Document.Title.Trim();
        var heading = Document.Sections.SelectMany(s => s.Blocks).OfType<HParagraph>().FirstOrDefault(p => p.HeadingLevel > 0);
        if (heading is not null && HDocumentWalker.Text(heading.Inlines) is { Length: > 0 } text)
            return text.Length > 200 ? text[..200] : text;
        return Path.GetFileNameWithoutExtension(sourcePath);
    }

    public string DisplayLanguage()
    {
        if (!string.IsNullOrWhiteSpace(Metadata.Language))
            return Metadata.Language.Trim();
        int hangul = 0, han = 0, kana = 0, latin = 0;
        foreach (var text in Document.Sections.SelectMany(s => HDocumentWalker.Inlines(s.Blocks)).OfType<HText>().Take(2000))
        {
            foreach (var ch in text.Text)
            {
                if (ch is >= '가' and <= '힣')
                    hangul++;
                else if (ch is >= '぀' and <= 'ヿ')
                    kana++;
                else if (ch is >= '一' and <= '鿿')
                    han++;
                else if (char.IsAsciiLetter(ch))
                    latin++;
            }
        }
        return hangul > 0 && hangul * 3 >= latin ? "ko"
            : kana > 0 && kana * 3 >= latin ? "ja"
            : han > 0 && han * 3 >= latin ? "zh"
            : "en";
    }
}

/// <summary>Helpers shared by the e-book readers and writers.</summary>
internal static class EbookFiles
{
    public const string DrmMessage =
        "This book is protected with DRM (copy protection), so it cannot be converted. DRM removal is not supported.";

    public static void Extract(ZipArchive zip, string folder)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;
            if (SafePath(root, entry.FullName) is not { } target)
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    public static string? SafePath(string root, string entryName)
    {
        var relative = entryName.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || relative.Contains(':'))
            return null;
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static string NewFolder(string workDirectory, string prefix)
    {
        var folder = Path.Combine(workDirectory, $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static (string File, string MediaType)? CommonImage(string path, string folder, bool gifAndSvg)
    {
        var mediaType = ImageMediaType(path);
        if (mediaType is "image/jpeg" or "image/png" || gifAndSvg && mediaType is "image/gif" or "image/svg+xml")
            return (path, mediaType);
        try
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap == null)
                return null;
            bool hasAlpha = !bitmap.Info.IsOpaque;
            var jpeg = !hasAlpha && mediaType is "image/webp" or "image/avif" or "image/jxl" or "image/tiff";
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{Guid.NewGuid():N}.{(jpeg ? "jpg" : "png")}");
            using var data = bitmap.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 90);
            using var stream = File.Create(file);
            data.SaveTo(stream);
            return (file, jpeg ? "image/jpeg" : "image/png");
        }
        catch
        {
            return null;
        }
    }

    public static string? ImageMediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jpe" or ".jfif" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".avif" => "image/avif",
        ".jxl" => "image/jxl",
        _ => null,
    };
}

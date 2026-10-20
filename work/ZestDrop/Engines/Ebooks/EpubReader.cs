// EPUB 2 / 3 → Book: META-INF/container.xml → OPF package → spine → XHTML chapters through HtmlReader.

using System.IO.Compression;
using System.Xml.Linq;
using ZestDrop.Engines.Documents;

namespace ZestDrop.Engines.Ebooks;

internal static class EpubReader
{
    private static readonly HashSet<string> FontObfuscation =
    [
        "http://www.idpf.org/2008/embedding",
        "http://ns.adobe.com/pdf/enc#RC",
    ];

    public static HDocument Read(string path, string workFolder) => ReadBook(path, workFolder).Document;

    public static Book ReadBook(string path, string workFolder, CancellationToken cancellationToken = default)
    {
        var folder = EbookFiles.NewFolder(workFolder, "epub");
        using (var zip = OpenZip(path))
        {
            CheckDrm(zip);
            EbookFiles.Extract(zip, folder);
        }

        var package = OpfPackage.Load(OpfPackage.RootFile(folder));
        var chapters = new List<string>();
        foreach (var item in package.Spine)
        {
            if (item.MediaType is "application/xhtml+xml" or "text/html" or "application/xml" || item.MediaType.Length == 0 && item.Path.EndsWith("html", StringComparison.OrdinalIgnoreCase))
                chapters.Add(item.Path);
            else if (item.MediaType.StartsWith("image/", StringComparison.Ordinal))
                chapters.Add(ImagePage(item.Path));
        }
        if (chapters.Count == 0)
            throw new InvalidDataException("The EPUB has no readable chapters.");

        var document = ChapterReader.Read(chapters, Path.Combine(folder, "~media"), cancellationToken);
        document.Title = package.Metadata.Title ?? document.Title;
        return new Book(document, package.Metadata);
    }

    private static ZipArchive OpenZip(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("This is not a valid EPUB file (it is not a ZIP package).", ex);
        }
    }

    internal static void CheckDrm(ZipArchive zip)
    {
        if (zip.GetEntry("META-INF/sinf.xml") is not null)
            throw new InvalidOperationException(EbookFiles.DrmMessage);
        if (zip.GetEntry("META-INF/encryption.xml") is not { } entry)
            return;
        XDocument encryption;
        try
        {
            using var stream = entry.Open();
            encryption = XDocument.Load(stream);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }
        var algorithms = encryption.Descendants().Where(e => e.Name.LocalName == "EncryptionMethod")
            .Select(e => (string?)e.Attribute("Algorithm") ?? "");
        if (algorithms.Any(a => !FontObfuscation.Contains(a)))
            throw new InvalidOperationException(EbookFiles.DrmMessage);
    }

    private static string ImagePage(string image)
    {
        var page = Path.Combine(Path.GetDirectoryName(image)!, $"~{Path.GetFileNameWithoutExtension(image)}-{Guid.NewGuid():N}.xhtml");
        File.WriteAllText(page, $"<html><body><p style=\"text-align:center\"><img src=\"{Uri.EscapeDataString(Path.GetFileName(image))}\"/></p></body></html>");
        return page;
    }
}

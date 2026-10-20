using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ZestDrop.Engines.Office.Ooxml;

/// <summary>Converts between the variants of Word, Excel and PowerPoint files by rewriting the package.</summary>
public static class OoxmlEngine
{
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    internal static readonly Dictionary<string, (string Family, string ContentType, bool Macros)> Variants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["docx"] = ("word", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", false),
        ["docm"] = ("word", "application/vnd.ms-word.document.macroEnabled.main+xml", true),
        ["dotx"] = ("word", "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml", false),
        ["dotm"] = ("word", "application/vnd.ms-word.template.macroEnabledTemplate.main+xml", true),
        ["xlsx"] = ("excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", false),
        ["xlsm"] = ("excel", "application/vnd.ms-excel.sheet.macroEnabled.main+xml", true),
        ["xltx"] = ("excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml", false),
        ["pptx"] = ("powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml", false),
        ["pptm"] = ("powerpoint", "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml", true),
        ["potx"] = ("powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml", false),
        ["ppsx"] = ("powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml", false),
    };

    internal static readonly HashSet<string> MacroContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.ms-office.vbaProject",
        "application/vnd.ms-office.vbaProjectSignature",
        "application/vnd.ms-office.vbaProjectSignatureAgile",
        "application/vnd.ms-office.vbaProjectSignatureV3",
        "application/vnd.ms-word.vbaData+xml",
        "application/vnd.ms-word.keyMapCustomizations+xml",
        "application/vnd.ms-excel.macrosheet+xml",
        "application/vnd.ms-excel.intlmacrosheet+xml",
    };

    public static bool CanHandle(string from, string to)
    {
        return Variants.TryGetValue(from, out var source)
            && Variants.TryGetValue(to, out var target)
            && !from.Equals(to, StringComparison.OrdinalIgnoreCase)
            && source.Family.Equals(target.Family, StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanConvert(string from, string to) => CanHandle(from, to);

    public static string Convert(string inputPath, string targetFormat, string? outputPath = null)
    {
        var to = targetFormat.TrimStart('.').ToLowerInvariant();
        outputPath ??= Path.ChangeExtension(inputPath, "." + to);
        ConvertAsync(inputPath, outputPath, to).GetAwaiter().GetResult();
        return outputPath;
    }

    public static IEnumerable<(string From, string To)> SupportedPairs
    {
        get
        {
            foreach (var from in Variants.Keys)
                foreach (var to in Variants.Keys)
                    if (CanHandle(from, to))
                        yield return (from, to);
        }
    }

    public static async Task ConvertAsync(
        string inputPath,
        string outputPath,
        string to,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            progress?.Invoke(0.1);
            cancellationToken.ThrowIfCancellationRequested();

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var temp = outputPath + ".tmp";
            try
            {
                Rewrite(inputPath, temp, to);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(0.8);

                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                File.Move(temp, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    try { File.Delete(temp); } catch { }
            }

            progress?.Invoke(1.0);
        }, cancellationToken).ConfigureAwait(false);
    }

    public static void Rewrite(string input, string output, string to)
    {
        var target = Variants[to];
        using var source = ZipFile.OpenRead(input);
        var types = Load(source, "[Content_Types].xml") ?? throw new InvalidDataException("The file is not an Office Open XML package (no [Content_Types].xml).");
        var root = types.Root!;
        var main = Load(source, "_rels/.rels")?.Root?.Elements(RelationshipsNs + "Relationship")
            .FirstOrDefault(r => ((string?)r.Attribute("Type"))?.EndsWith("/officeDocument", StringComparison.Ordinal) == true)
            ?.Attribute("Target")?.Value.TrimStart('/')
            ?? throw new InvalidDataException("The package has no main document part.");

        var mainOverride = root.Elements(ContentTypesNs + "Override")
            .FirstOrDefault(o => string.Equals(((string?)o.Attribute("PartName"))?.TrimStart('/'), main, StringComparison.OrdinalIgnoreCase));
        if (mainOverride is null)
            root.Add(mainOverride = new XElement(ContentTypesNs + "Override", new XAttribute("PartName", "/" + main)));
        mainOverride.SetAttributeValue("ContentType", target.ContentType);

        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!target.Macros)
        {
            foreach (var entry in source.Entries)
            {
                if (ContentTypeOf(root, entry.FullName) is { } type && MacroContentTypes.Contains(type))
                    removed.Add(entry.FullName);
            }
            foreach (var part in removed.ToList())
                removed.Add(RelationshipsPartOf(part));
            root.Elements(ContentTypesNs + "Override")
                .Where(o => removed.Contains(((string?)o.Attribute("PartName"))?.TrimStart('/') ?? ""))
                .Remove();
            root.Elements(ContentTypesNs + "Default")
                .Where(d => MacroContentTypes.Contains((string?)d.Attribute("ContentType") ?? ""))
                .Remove();
        }

        var removedIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        using (var file = File.Create(output))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                if (removed.Contains(entry.FullName) || entry.FullName.EndsWith('/'))
                    continue;
                if (entry.FullName == "[Content_Types].xml")
                {
                    Save(zip, entry.FullName, types);
                }
                else if (removed.Count > 0 && entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    var rels = Load(source, entry.FullName)!;
                    var owner = OwnerOf(entry.FullName);
                    var dropped = rels.Root!.Elements(RelationshipsNs + "Relationship")
                        .Where(r => (string?)r.Attribute("TargetMode") != "External"
                                    && removed.Contains(Resolve(owner, (string?)r.Attribute("Target") ?? "")))
                        .ToList();
                    if (dropped.Count > 0)
                        removedIds[owner] = [.. dropped.Select(r => (string?)r.Attribute("Id") ?? "")];
                    dropped.Remove();
                    Save(zip, entry.FullName, rels);
                }
                else
                {
                    Copy(entry, zip);
                }
            }
        }

        if (target.Family == "excel" && removedIds.TryGetValue(main, out var sheetIds) && sheetIds.Count > 0)
            RemoveSheets(output, main, sheetIds);
    }

    private static void RemoveSheets(string package, string workbookPath, HashSet<string> ids)
    {
        using var zip = ZipFile.Open(package, ZipArchiveMode.Update);
        var entry = zip.GetEntry(workbookPath);
        if (entry is null)
            return;
        XDocument workbook;
        using (var stream = entry.Open())
            workbook = XDocument.Load(stream);
        var sheets = workbook.Root?.Element(SpreadsheetNs + "sheets")?.Elements(SpreadsheetNs + "sheet").ToList() ?? [];
        var kept = new List<int>();
        for (var i = 0; i < sheets.Count; i++)
        {
            if (ids.Contains((string?)sheets[i].Attribute(OfficeRelationshipsNs + "id") ?? ""))
                sheets[i].Remove();
            else
                kept.Add(i);
        }
        foreach (var name in workbook.Root?.Element(SpreadsheetNs + "definedNames")?.Elements(SpreadsheetNs + "definedName").ToList() ?? [])
        {
            if (!int.TryParse((string?)name.Attribute("localSheetId"), out var local))
                continue;
            var index = kept.IndexOf(local);
            if (index < 0)
                name.Remove();
            else
                name.SetAttributeValue("localSheetId", index);
        }
        foreach (var view in workbook.Descendants(SpreadsheetNs + "workbookView"))
        {
            view.Attribute("activeTab")?.Remove();
            view.Attribute("firstSheet")?.Remove();
        }
        entry.Delete();
        Save(zip, workbookPath, workbook);
    }

    private static string? ContentTypeOf(XElement types, string partPath)
    {
        var name = "/" + partPath;
        var over = types.Elements(ContentTypesNs + "Override")
            .FirstOrDefault(o => string.Equals((string?)o.Attribute("PartName"), name, StringComparison.OrdinalIgnoreCase));
        if (over is not null)
            return (string?)over.Attribute("ContentType");
        var extension = Path.GetExtension(partPath).TrimStart('.');
        return (string?)types.Elements(ContentTypesNs + "Default")
            .FirstOrDefault(d => string.Equals((string?)d.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType");
    }

    private static string RelationshipsPartOf(string part)
    {
        var slash = part.LastIndexOf('/');
        return slash < 0 ? $"_rels/{part}.rels" : $"{part[..slash]}/_rels/{part[(slash + 1)..]}.rels";
    }

    private static string OwnerOf(string relsPath)
    {
        var folder = relsPath[..relsPath.IndexOf("_rels/", StringComparison.Ordinal)];
        var name = Path.GetFileName(relsPath);
        return folder + name[..^".rels".Length];
    }

    private static string Resolve(string sourcePart, string target)
    {
        if (target.StartsWith('/'))
            return target.TrimStart('/');
        var parts = new List<string>(sourcePart.Contains('/') ? sourcePart[..sourcePart.LastIndexOf('/')].Split('/') : []);
        foreach (var segment in Uri.UnescapeDataString(target).Split('/'))
        {
            if (segment == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
            }
            else if (segment is not ("." or ""))
            {
                parts.Add(segment);
            }
        }
        return string.Join('/', parts);
    }

    private static XDocument? Load(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
            return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void Save(ZipArchive zip, string name, XDocument xml)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }

    private static void Copy(ZipArchiveEntry entry, ZipArchive zip)
    {
        using var source = entry.Open();
        using var target = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open();
        source.CopyTo(target);
    }
}

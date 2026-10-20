using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZestDrop.Engines.Data;

public static class DataEngine
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static readonly string[] Formats = ["json", "jsonl", "yaml", "toml", "xml", "csv", "tsv", "vcf"];

    public static bool CanHandle(string fromExt, string toExt)
    {
        fromExt = fromExt.TrimStart('.').ToLowerInvariant();
        toExt = toExt.TrimStart('.').ToLowerInvariant();

        if (fromExt == toExt) return false;

        return Formats.Contains(fromExt) && Formats.Contains(toExt);
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
            var from = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
            var to = targetFormat.TrimStart('.').ToLowerInvariant();

            progress?.Report(0.1);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // vCard contacts
            if (from == "vcf")
            {
                var contacts = VCard.Read(File.ReadAllBytes(inputPath));
                if (contacts.Count == 0)
                    throw new InvalidDataException("The file has no vCard contacts.");

                cancellationToken.ThrowIfCancellationRequested();
                if (to is "csv" or "tsv")
                {
                    CsvFormat.WriteRows(VCard.ToRows(contacts), outputPath, to == "tsv" ? '\t' : ',');
                }
                else
                {
                    // Convert contacts to DataTree and export as JSON/YAML/XML/TOML
                    var rows = VCard.ToRows(contacts);
                    var tree = CsvData.ReadFromRows(rows);
                    WriteTree(tree, to, outputPath);
                }

                progress?.Report(1.0);
                return (IReadOnlyList<string>)[outputPath];
            }

            if (to == "vcf")
            {
                var rows = from is "csv" or "tsv"
                    ? CsvData.ReadRows(inputPath)
                    : CsvData.Table(Read(inputPath, from));

                cancellationToken.ThrowIfCancellationRequested();
                var contacts = VCard.FromRows(rows);
                File.WriteAllText(outputPath, VCard.Write(contacts), Utf8);
                progress?.Report(1.0);
                return (IReadOnlyList<string>)[outputPath];
            }

            // General structured data
            var dataTree = Read(inputPath, from);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.6);

            if (to is "csv" or "tsv")
            {
                var rows = CsvData.Table(dataTree);
                CsvFormat.WriteRows(rows, outputPath, to == "tsv" ? '\t' : ',');
            }
            else
            {
                WriteTree(dataTree, to, outputPath);
            }

            progress?.Report(1.0);
            return (IReadOnlyList<string>)[outputPath];
        }, cancellationToken);
    }

    private static void WriteTree(object? tree, string targetFormat, string outputPath)
    {
        var text = targetFormat switch
        {
            "json" => JsonData.Write(tree),
            "jsonl" => JsonData.WriteLines(tree),
            "yaml" => YamlData.Write(tree),
            "toml" => TomlData.Write(tree),
            "xml" => XmlData.Write(tree),
            _ => throw new NotSupportedException($"Data → {targetFormat} is not supported.")
        };

        File.WriteAllText(outputPath, text, Utf8);
    }

    internal static object? Read(string path, string format) => format switch
    {
        "xml" => XmlData.Read(path),
        "csv" or "tsv" => CsvData.Read(path),
        _ => ReadText(TextDecoder.DecodeText(File.ReadAllBytes(path)), format)
    };

    private static object? ReadText(string text, string format) => format switch
    {
        "json" => JsonData.Read(text),
        "jsonl" => JsonData.ReadLines(text),
        "yaml" => YamlData.Read(text),
        "toml" => TomlData.Read(text),
        _ => throw new NotSupportedException($"{format} → data is not supported.")
    };

    public static void Convert(string inputPath, string targetFormat, string outputPath)
    {
        ConvertAsync(inputPath, targetFormat, outputPath, null, CancellationToken.None).GetAwaiter().GetResult();
    }
}


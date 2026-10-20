// Spreadsheets between XLSX, XLS, ODS, CSV and TSV without Excel or LibreOffice.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZestDrop.Engines.Office.Sheets;

internal static class SpreadsheetEngine
{
    private static readonly string[] Sources = ["xlsx", "xlsm", "xltx", "xls", "xlt", "ods", "csv", "tsv"];
    private static readonly string[] Targets = ["xlsx", "ods", "csv", "tsv"];

    public static IReadOnlyList<string> SupportedInputs => Sources;
    public static IReadOnlyList<string> SupportedOutputs => Targets;

    public static bool CanConvert(string from, string to)
    {
        from = from.ToLowerInvariant();
        to = to.ToLowerInvariant();
        if (from == to)
            return false;
        // XLSM / XLTX -> XLSX is left to OoxmlEngine (repacking without re-serializing)
        if (to == "xlsx" && from is "xlsm" or "xltx")
            return false;
        return Sources.Contains(from) && Targets.Contains(to);
    }

    public static string Convert(string inputPath, string targetFormat, string? outputPath = null)
    {
        var from = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var to = targetFormat.TrimStart('.').ToLowerInvariant();
        outputPath ??= Path.ChangeExtension(inputPath, "." + to);
        ConvertAsync(inputPath, outputPath, from, to).GetAwaiter().GetResult();
        return outputPath;
    }

    public static async Task ConvertAsync(string inputPath, string outputPath, string from, string to, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        await Task.Run(() =>
        {
            progress?.Report(0.1);
            var book = Read(inputPath, from);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.6);

            switch (to.ToLowerInvariant())
            {
                case "xlsx":
                    XlsxWriter.Write(book, outputPath);
                    break;
                case "ods":
                    OdsWriter.Write(book, outputPath);
                    break;
                case "tsv":
                case "csv":
                    var sheet = book.Sheets.FirstOrDefault(s => s.UsedRange() is not null) ?? (book.Sheets.Count > 0 ? book.Sheets[0] : new Worksheet("Sheet1"));
                    CsvFormat.Write(sheet, outputPath, to.ToLowerInvariant() == "tsv" ? CsvFormat.Tab : ',');
                    break;
                default:
                    throw new NotSupportedException($"Unsupported target spreadsheet format: {to}");
            }
            progress?.Report(1.0);
        }, cancellationToken);
    }

    /// <summary>Reads a spreadsheet of a format id this engine accepts.</summary>
    public static Workbook Read(string path, string format) => format.ToLowerInvariant() switch
    {
        "xls" or "xlt" => XlsReader.Read(path),
        "ods" => OdsReader.Read(path),
        "csv" => CsvFormat.Read(path),
        "tsv" => CsvFormat.ReadTsv(path),
        _ => XlsxReader.Read(path),
    };
}

// XLSX (SpreadsheetML) → Workbook, read directly (no Excel, no LibreOffice): cell values formatted the way Excel
// shows them (number formats through ExcelNumberFormat, MIT), fonts, fills, borders, alignment, merged cells,
// column widths, custom row heights, hidden rows / columns / sheets and frozen header rows.
// Charts, pictures and conditional formatting are not read.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ZestDrop.Engines.Office.Ooxml;

namespace ZestDrop.Engines.Office.Sheets;

internal sealed class XlsxReader
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Days between the 1900 and 1904 date systems (1 January 1904 is serial 1462 in the 1900 system).</summary>
    private const double Date1904Offset = 1462;

    private readonly OpcPackage _package;
    private readonly List<string> _sharedStrings = [];
    private readonly List<(CellStyle Style, string Format)> _cellFormats = [];
    private NumberFormatter _formatter = new();
    private bool _date1904;

    private XlsxReader(OpcPackage package) => _package = package;

    /// <summary>Reads every visible sheet of <paramref name="path"/>.</summary>
    public static Workbook Read(string path)
    {
        using var package = new OpcPackage(path);
        return new XlsxReader(package).ReadWorkbook();
    }

    private Workbook ReadWorkbook()
    {
        var workbookPath = _package.MainPartPath ?? "xl/workbook.xml";
        var workbook = _package.Part(workbookPath)?.Root ?? throw new InvalidDataException("The file is not an Excel workbook (no xl/workbook.xml).");
        _date1904 = (string?)workbook.Element(S + "workbookPr")?.Attribute("date1904") is "1" or "true";
        _formatter = new NumberFormatter(_date1904);

        var colors = OoxmlColors.FromTheme(_package.TargetOfType(workbookPath, "/theme") is { } theme ? _package.Part(theme) : null);
        if (_package.TargetOfType(workbookPath, "/sharedStrings") is { } shared)
            ReadSharedStrings(_package.Part(shared));
        if (_package.TargetOfType(workbookPath, "/styles") is { } styles)
            ReadStyles(_package.Part(styles), colors);

        var result = new Workbook();
        foreach (var sheet in workbook.Element(S + "sheets")?.Elements(S + "sheet") ?? [])
        {
            if ((string?)sheet.Attribute("state") is "hidden" or "veryHidden")
                continue;
            var target = _package.Target(workbookPath, (string?)sheet.Attribute(R + "id"));
            if (target is null || _package.Part(target)?.Root is not { } xml || xml.Name != S + "worksheet")
                continue; // chart sheets and dialog sheets have no cells
            result.Sheets.Add(ReadSheet((string?)sheet.Attribute("name") ?? "Sheet", xml));
        }
        return result;
    }

    private void ReadSharedStrings(XDocument? xml)
    {
        foreach (var item in xml?.Root?.Elements(S + "si") ?? [])
            _sharedStrings.Add(RichText(item));
    }

    /// <summary>Text of a shared or inline string: plain &lt;t&gt; or rich text runs; phonetic hints skipped.</summary>
    private static string RichText(XElement item)
    {
        if (item.Element(S + "t") is { } plain)
            return plain.Value;
        var sb = new StringBuilder();
        foreach (var run in item.Elements(S + "r"))
            sb.Append(run.Element(S + "t")?.Value);
        return sb.ToString();
    }

    // ───────────────────────── Styles ─────────────────────────

    private void ReadStyles(XDocument? xml, OoxmlColors colors)
    {
        var root = xml?.Root;
        if (root is null)
            return;
        var formats = root.Element(S + "numFmts")?.Elements(S + "numFmt")
            .Where(f => int.TryParse((string?)f.Attribute("numFmtId"), out _))
            .ToDictionary(f => int.Parse((string)f.Attribute("numFmtId")!, CultureInfo.InvariantCulture), f => (string?)f.Attribute("formatCode") ?? "General")
            ?? [];

        var fonts = (root.Element(S + "fonts")?.Elements(S + "font") ?? []).Select(f => new
        {
            Bold = On(f.Element(S + "b")),
            Italic = On(f.Element(S + "i")),
            Underline = f.Element(S + "u") is { } u && (string?)u.Attribute("val") != "none",
            Strike = On(f.Element(S + "strike")),
            Size = double.TryParse((string?)f.Element(S + "sz")?.Attribute("val"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size : (double?)null,
            Color = colors.Spreadsheet(f.Element(S + "color")),
        }).ToList();

        var fills = (root.Element(S + "fills")?.Elements(S + "fill") ?? []).Select(f =>
            colors.Spreadsheet(f.Element(S + "patternFill")?.Element(S + "fgColor"))).ToList();

        var borders = (root.Element(S + "borders")?.Elements(S + "border") ?? []).Select(b => new
        {
            Left = Border(b.Element(S + "left"), colors),
            Right = Border(b.Element(S + "right"), colors),
            Top = Border(b.Element(S + "top"), colors),
            Bottom = Border(b.Element(S + "bottom"), colors),
        }).ToList();

        foreach (var xf in root.Element(S + "cellXfs")?.Elements(S + "xf") ?? [])
        {
            var font = Index(xf, "fontId", fonts.Count) is { } fi ? fonts[fi] : null;
            var fill = Index(xf, "fillId", fills.Count) is { } li ? fills[li] : null;
            var border = Index(xf, "borderId", borders.Count) is { } bi ? borders[bi] : null;
            var align = xf.Element(S + "alignment");
            var style = new CellStyle
            {
                Bold = font?.Bold ?? false,
                Italic = font?.Italic ?? false,
                Underline = font?.Underline ?? false,
                Strike = font?.Strike ?? false,
                FontSize = font?.Size,
                Color = font?.Color,
                Fill = fill,
                Align = (string?)align?.Attribute("horizontal") switch
                {
                    "left" => CellAlign.Left,
                    "center" => CellAlign.Center,
                    "right" => CellAlign.Right,
                    _ => CellAlign.General,
                },
                VerticalAlign = (string?)align?.Attribute("vertical") switch
                {
                    "top" => CellVerticalAlign.Top,
                    "center" => CellVerticalAlign.Center,
                    _ => CellVerticalAlign.Bottom,
                },
                Wrap = (string?)align?.Attribute("wrapText") is "1" or "true",
                Left = border?.Left ?? CellBorder.None,
                Right = border?.Right ?? CellBorder.None,
                Top = border?.Top ?? CellBorder.None,
                Bottom = border?.Bottom ?? CellBorder.None,
            };

            var format = int.TryParse((string?)xf.Attribute("numFmtId"), out var numId)
                ? formats.GetValueOrDefault(numId, NumberFormatter.BuiltInFormats.GetValueOrDefault(numId, "General"))
                : "General";
            _cellFormats.Add((style, format));
        }
    }

    private static CellBorder Border(XElement? side, OoxmlColors colors) =>
        side is not null && (string?)side.Attribute("style") is { Length: > 0 } style && style != "none"
            ? new CellBorder(style, colors.Spreadsheet(side.Element(S + "color")))
            : CellBorder.None;

    private static int? Index(XElement xf, string attribute, int count) =>
        int.TryParse((string?)xf.Attribute(attribute), out var i) && i >= 0 && i < count ? i : null;

    private static bool On(XElement? element) => element is not null && (string?)element.Attribute("val") is not ("0" or "false");

    // ───────────────────────── Worksheet ─────────────────────────

    private Worksheet ReadSheet(string name, XElement xml)
    {
        var sheet = new Worksheet(name);
        if (double.TryParse((string?)xml.Element(S + "sheetFormatPr")?.Attribute("defaultColWidth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var defaultWidth))
            sheet.DefaultColumnWidth = defaultWidth;

        // Frozen panes: <pane ySplit="1" state="frozen"/> keeps row 1 frozen at the top.
        if (int.TryParse((string?)xml.Descendants(S + "pane").FirstOrDefault()?.Attribute("ySplit"), out var frozen) && frozen > 0)
            sheet.FrozenRows = frozen;

        foreach (var col in xml.Element(S + "cols")?.Elements(S + "col") ?? [])
        {
            var min = int.TryParse((string?)col.Attribute("min"), out var c1) ? c1 - 1 : -1;
            var max = int.TryParse((string?)col.Attribute("max"), out var c2) ? c2 - 1 : -1;
            var width = double.TryParse((string?)col.Attribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : (double?)null;
            var hidden = (string?)col.Attribute("hidden") is "1" or "true";
            if (min < 0 || max < 0 || width is null)
                continue;
            for (var c = min; c <= max; c++)
            {
                sheet.ColumnWidths[c] = width.Value;
                if (hidden)
                    sheet.HiddenColumns.Add(c);
            }
        }

        foreach (var row in xml.Element(S + "sheetData")?.Elements(S + "row") ?? [])
        {
            if (!int.TryParse((string?)row.Attribute("r"), out var r))
                continue;
            r--;
            if (double.TryParse((string?)row.Attribute("ht"), NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
                && (string?)row.Attribute("customHeight") is "1" or "true")
                sheet.RowHeights[r] = height;
            if ((string?)row.Attribute("hidden") is "1" or "true")
                sheet.HiddenRows.Add(r);

            var nextColumn = 0;
            foreach (var cell in row.Elements(S + "c"))
            {
                var (rowIdx, colIdx) = CellRange.TryParseCell((string?)cell.Attribute("r") ?? "", out var cr, out var cc)
                    ? (cr, cc)
                    : (r, nextColumn);
                nextColumn = colIdx + 1;
                sheet.Set(rowIdx, colIdx, ReadCell(cell));
            }
        }

        foreach (var merge in xml.Element(S + "mergeCells")?.Elements(S + "mergeCell") ?? [])
        {
            if (CellRange.Parse((string?)merge.Attribute("ref")) is { } range)
                sheet.Merges.Add(range);
        }

        foreach (var brk in xml.Element(S + "rowBreaks")?.Elements(S + "brk") ?? [])
        {
            if (int.TryParse((string?)brk.Attribute("id"), out var rowBreak) && rowBreak > 0)
                sheet.RowBreaks.Add(rowBreak - 1);
        }

        return sheet;
    }

    private SheetCell ReadCell(XElement cell)
    {
        var (style, format) = Index(cell, "s", _cellFormats.Count) is { } s ? _cellFormats[s] : (CellStyle.Default, "General");
        var type = (string?)cell.Attribute("t");
        var raw = cell.Element(S + "v")?.Value;
        var formula = cell.Element(S + "f")?.Value;

        switch (type)
        {
            case "s" when int.TryParse(raw, out var idx) && idx >= 0 && idx < _sharedStrings.Count:
                return new SheetCell(_sharedStrings[idx], style) { Formula = formula };

            case "inlineStr" when cell.Element(S + "is") is { } inline:
                return new SheetCell(RichText(inline), style) { Formula = formula };

            case "b":
                return new SheetCell(raw is "1" or "true" ? "TRUE" : "FALSE", style) { Value = raw is "1" or "true" ? 1 : 0, IsBoolean = true, Formula = formula };

            case "e": // #N/A, #VALUE!, ...
                return new SheetCell(raw ?? "", style) { Formula = formula, IsError = true };

            case "str": // cached string of a formula
                return new SheetCell(raw ?? "", style) { Formula = formula };

            default:
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    var serial = _date1904 && _formatter.IsDate(format) ? number + Date1904Offset : number;
                    return new SheetCell(_formatter.Format(number, format), style, IsNumber: true)
                    {
                        Value = serial,
                        NumberFormat = NumberFormatter.IsGeneral(format) ? null : format,
                        Formula = formula,
                    };
                }
                return new SheetCell(raw ?? "", style) { Formula = formula };
        }
    }
}

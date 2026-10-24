// Document body: sections (page setup, columns, headers and footers), paragraphs, runs and tables.

using System.Text;
using ZestDrop.Engines.Documents;

namespace ZestDrop.Engines.Documents.Docx;

internal sealed partial class DocxWriter
{
    /// <summary>A4 with one-inch margins and headers/footers 1.5 cm from the edge (Word's A4 default).</summary>
    private static readonly HPage DefaultPage = new(59528, 84188, 7200, 7200, 4255, 4255, 2945, 2945, 0);

    /// <summary>Word's highlight colours, by "#RRGGBB".</summary>
    private static readonly Dictionary<string, string> Highlights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FFFF00"] = "yellow",
        ["#00FF00"] = "green",
        ["#00FFFF"] = "cyan",
        ["#FF00FF"] = "magenta",
        ["#0000FF"] = "blue",
        ["#FF0000"] = "red",
        ["#000080"] = "darkBlue",
        ["#008080"] = "darkCyan",
        ["#008000"] = "darkGreen",
        ["#800080"] = "darkMagenta",
        ["#800000"] = "darkRed",
        ["#808000"] = "darkYellow",
        ["#808080"] = "darkGray",
        ["#C0C0C0"] = "lightGray",
        ["#000000"] = "black",
    };

    /// <summary>Width (HWPUNIT) available to the content being written: column, table cell or text box.</summary>
    private int _width = DefaultPage.TextWidth;

    private readonly HashSet<string> _headerTypes = [];
    private readonly HashSet<string> _footerTypes = [];

    private readonly record struct RunContext(bool StyleBold, bool Link);

    // ───────────────────────── Sections ─────────────────────────

    private string Body(HDocument document)
    {
        var sections = document.Sections.Count > 0 ? document.Sections : [new HSection()];
        _evenAndOddHeaders = sections.Any(s => s.Headers.Concat(s.Footers).Any(h => h.Pages == HPageType.Even));

        var sb = new StringBuilder();
        for (var s = 0; s < sections.Count; s++)
        {
            var section = sections[s];
            var page = section.Page ?? DefaultPage;
            var parts = SplitAtColumnChanges(section);
            for (var p = 0; p < parts.Count; p++)
            {
                var (columns, blocks) = parts[p];
                _width = page.TextWidth;
                var sectPr = SectionProperties(section, page, columns, continuous: p > 0);
                _width = ColumnWidth(page.TextWidth, columns);
                var last = s == sections.Count - 1 && p == parts.Count - 1;
                if (last)
                    sb.Append(Blocks(blocks, requireParagraph: true)).Append(sectPr);
                else
                    sb.Append(Blocks(blocks, sectionEnd: sectPr));
            }
        }
        return sb.ToString();
    }

    private static List<(HColumns Columns, List<HBlock> Blocks)> SplitAtColumnChanges(HSection section)
    {
        var parts = new List<(HColumns, List<HBlock>)>();
        var columns = section.Columns;
        var current = new List<HBlock>();
        foreach (var block in section.Blocks)
        {
            if (block is HParagraph { ColumnsChange: { } change })
            {
                if (current.Count > 0)
                {
                    parts.Add((columns, current));
                    current = [];
                }
                columns = change;
            }
            current.Add(block);
        }
        parts.Add((columns, current));
        return parts;
    }

    private static int ColumnWidth(int textWidth, HColumns columns) =>
        columns.Count <= 1 ? textWidth : Math.Max(2000, (textWidth - columns.Gap * (columns.Count - 1)) / columns.Count);

    private string SectionProperties(HSection section, HPage page, HColumns columns, bool continuous)
    {
        var sb = new StringBuilder("<w:sectPr>");
        var titlePage = !continuous && (section.HideFirstHeader || section.HideFirstFooter);
        if (!continuous)
        {
            HeaderFooterReferences(sb, "header", section.Headers, section.HideFirstHeader, titlePage, _headerTypes);
            HeaderFooterReferences(sb, "footer", section.Footers, section.HideFirstFooter, titlePage, _footerTypes);
        }
        else
        {
            sb.Append("<w:type w:val=\"continuous\"/>");
        }

        sb.Append($"<w:pgSz w:w=\"{Number(Twips(page.Width))}\" w:h=\"{Number(Twips(page.Height))}\"{(page.Landscape ? " w:orient=\"landscape\"" : "")}/>");
        sb.Append($"<w:pgMar w:top=\"{Number(Twips(page.Top + page.Header))}\" w:right=\"{Number(Twips(page.Right))}\" w:bottom=\"{Number(Twips(page.Bottom + page.Footer))}\" w:left=\"{Number(Twips(page.Left))}\" ")
          .Append($"w:header=\"{Number(Twips(page.Top))}\" w:footer=\"{Number(Twips(page.Bottom))}\" w:gutter=\"{Number(Twips(page.Gutter))}\"/>");
        if (!continuous && section.StartPageNumber is { } start)
            sb.Append($"<w:pgNumType w:start=\"{Number(start)}\"/>");
        sb.Append(columns.Count > 1
            ? $"<w:cols w:num=\"{Number(Math.Min(columns.Count, 45))}\" w:space=\"{Number(Twips(columns.Gap))}\"{(columns.Separator ? " w:sep=\"1\"" : "")}/>"
            : "<w:cols w:space=\"720\"/>");
        if (titlePage)
            sb.Append("<w:titlePg/>");
        return sb.Append("</w:sectPr>").ToString();
    }

    private void HeaderFooterReferences(StringBuilder sb, string kind, List<HHeaderFooter> items, bool hideFirst, bool titlePage, HashSet<string> used)
    {
        var byType = new Dictionary<string, List<HBlock>>();
        foreach (var item in items)
            byType[item.Pages == HPageType.Even ? "even" : "default"] = item.Blocks;
        if (_evenAndOddHeaders && !byType.ContainsKey("even") && items.LastOrDefault(i => i.Pages == HPageType.Both) is { } both)
            byType["even"] = both.Blocks;
        if (titlePage)
            byType["first"] = hideFirst ? [] : byType.GetValueOrDefault("default") ?? [];
        foreach (var type in used)
            byType.TryAdd(type, []);

        foreach (var (type, blocks) in byType)
        {
            var id = HeaderFooter(kind, () => Blocks(blocks, requireParagraph: true));
            sb.Append($"<w:{kind}Reference w:type=\"{type}\" r:id=\"{id}\"/>");
            used.Add(type);
        }
    }

    // ───────────────────────── Blocks ─────────────────────────

    private string Blocks(IReadOnlyList<HBlock> blocks, string? sectionEnd = null, bool requireParagraph = false,
        string? paragraphStyle = null, string? leadingRuns = null)
    {
        var sb = new StringBuilder();
        if (leadingRuns is not null && (blocks.Count == 0 || blocks[0] is not HParagraph))
            blocks = [new HParagraph(), .. blocks];
        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case HParagraph paragraph:
                    var isLast = i == blocks.Count - 1;
                    sb.Append(Paragraph(paragraph, isLast ? sectionEnd : null, paragraphStyle, i == 0 ? leadingRuns : null));
                    break;
                case HTable table:
                    if (i > 0 && blocks[i - 1] is HTable)
                        sb.Append("<w:p/>");
                    sb.Append(Table(table));
                    break;
            }
        }
        var endsWithParagraph = blocks.Count > 0 && blocks[^1] is HParagraph;
        if (sectionEnd is not null && !endsWithParagraph)
            sb.Append($"<w:p><w:pPr>{sectionEnd}</w:pPr></w:p>");
        else if (requireParagraph && !endsWithParagraph)
            sb.Append("<w:p/>");
        return sb.ToString();
    }

    // ───────────────────────── Paragraphs ─────────────────────────

    private string Paragraph(HParagraph paragraph, string? sectPr = null, string? style = null, string? leadingRuns = null)
    {
        var heading = paragraph.HeadingLevel is >= 1 and <= 9 ? paragraph.HeadingLevel : 0;
        if (heading > 0)
            style = $"Heading{heading}";
        var context = new RunContext(StyleBold: heading > 0, Link: false);

        var sb = new StringBuilder("<w:p>");
        sb.Append(ParagraphProperties(paragraph, style, heading > 0, sectPr, context));
        if (paragraph.ColumnBreakBefore)
            sb.Append("<w:r><w:br w:type=\"column\"/></w:r>");
        sb.Append(leadingRuns);
        Inlines(paragraph.Inlines, sb, context);
        return sb.Append("</w:p>").ToString();
    }

    private string ParagraphProperties(HParagraph paragraph, string? style, bool heading, string? sectPr, RunContext context)
    {
        var format = paragraph.Format;
        var sb = new StringBuilder();
        if (style is not null)
            sb.Append($"<w:pStyle w:val=\"{style}\"/>");
        if (format.KeepWithNext == true)
            sb.Append("<w:keepNext/>");
        else if (format.KeepWithNext == false && heading)
            sb.Append("<w:keepNext w:val=\"0\"/>");
        if (format.KeepLines == true)
            sb.Append("<w:keepLines/>");
        else if (format.KeepLines == false && heading)
            sb.Append("<w:keepLines w:val=\"0\"/>");
        if (paragraph.PageBreakBefore)
            sb.Append("<w:pageBreakBefore/>");
        if (format.WidowOrphan == false)
            sb.Append("<w:widowControl w:val=\"0\"/>");
        if (paragraph.List is { Numbered: true } list)
            sb.Append($"<w:numPr><w:ilvl w:val=\"{Number(Math.Clamp(list.Level, 0, 8))}\"/><w:numId w:val=\"{Number(NumberingId(list.Numbering))}\"/></w:numPr>");

        if (format.Tabs is { Count: > 0 } tabs)
        {
            sb.Append("<w:tabs>");
            foreach (var tab in tabs.OrderBy(t => t.Position))
            {
                var kind = tab.Kind switch
                {
                    HTabKind.Right => "right",
                    HTabKind.Center => "center",
                    HTabKind.Decimal => "decimal",
                    _ => "left",
                };
                var leader = tab.Leader switch
                {
                    "DOT" => " w:leader=\"dot\"",
                    "DASH" => " w:leader=\"hyphen\"",
                    "SOLID" => " w:leader=\"underscore\"",
                    _ => "",
                };
                sb.Append($"<w:tab w:val=\"{kind}\"{leader} w:pos=\"{Number(Twips(tab.Position))}\"/>");
            }
            sb.Append("</w:tabs>");
        }

        var spacing = new StringBuilder();
        if (format.Before is { } before)
            spacing.Append($" w:before=\"{Number(Math.Max(0, Twips(before)))}\"");
        if (format.After is { } after)
            spacing.Append($" w:after=\"{Number(Math.Max(0, Twips(after)))}\"");
        if (format.LineSpacing is { } line)
        {
            spacing.Append(line.Kind switch
            {
                HLineSpacingKind.Fixed => $" w:line=\"{Number(Math.Max(20, Twips(line.Value)))}\" w:lineRule=\"exact\"",
                HLineSpacingKind.AtLeast => $" w:line=\"{Number(Math.Max(20, Twips(line.Value)))}\" w:lineRule=\"atLeast\"",
                _ => $" w:line=\"{Number(Math.Clamp((int)Math.Round(line.Value * 240 / 130.0), 24, 2400))}\" w:lineRule=\"auto\"",
            });
        }
        if (spacing.Length > 0)
            sb.Append("<w:spacing").Append(spacing).Append("/>");

        var left = format.Left;
        var firstLine = format.FirstLine;
        if (paragraph.List is { } item && left is null)
        {
            if (!item.Numbered)
                sb.Append($"<w:ind w:left=\"{Number(ListIndent(item.Level))}\"/>");
        }
        else if (left is not null || format.Right is not null || firstLine is not null)
        {
            if (paragraph.List is not null || (left ?? 0) != 0 || (format.Right ?? 0) != 0 || (firstLine ?? 0) != 0)
            {
                sb.Append("<w:ind");
                if (left is { } l)
                    sb.Append($" w:left=\"{Number(Twips(l))}\"");
                if (format.Right is { } r)
                    sb.Append($" w:right=\"{Number(Twips(r))}\"");
                if (firstLine is < 0)
                    sb.Append($" w:hanging=\"{Number(Twips(-firstLine.Value))}\"");
                else if (firstLine is { } f)
                    sb.Append($" w:firstLine=\"{Number(Twips(f))}\"");
                sb.Append("/>");
            }
        }

        if (format.Align is { } align and not HAlign.Left)
        {
            sb.Append(align switch
            {
                HAlign.Center => "<w:jc w:val=\"center\"/>",
                HAlign.Right => "<w:jc w:val=\"right\"/>",
                HAlign.Distribute => "<w:jc w:val=\"distribute\"/>",
                _ => "<w:jc w:val=\"both\"/>",
            });
        }

        var mark = RunProperties(paragraph.MarkFormat, context);
        if (mark.Length > 0)
            sb.Append("<w:rPr>").Append(mark).Append("</w:rPr>");
        sb.Append(sectPr);
        return sb.Length == 0 ? "" : $"<w:pPr>{sb}</w:pPr>";
    }

    // ───────────────────────── Runs ─────────────────────────

    private void Inlines(IEnumerable<HInline> inlines, StringBuilder sb, RunContext context)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case HText text:
                    TextRuns(sb, text.Text, text.Format, context);
                    break;
                case HLineBreak lineBreak:
                    sb.Append("<w:r>").Append(RunPr(lineBreak.Format, context)).Append("<w:br/></w:r>");
                    break;
                case HTab tab:
                    sb.Append("<w:r>").Append(RunPr(tab.Format, context)).Append("<w:tab/></w:r>");
                    break;
                case HLink link:
                    Hyperlink(link, sb, context);
                    break;
                case HBookmark bookmark:
                    {
                        var id = Number(_nextBookmarkId++);
                        sb.Append($"<w:bookmarkStart w:id=\"{id}\" w:name=\"{Escape(BookmarkName(bookmark.Name))}\"/><w:bookmarkEnd w:id=\"{id}\"/>");
                        break;
                    }
                case HField field:
                    Field(sb, field.Kind == HFieldKind.PageNumber ? "PAGE" : "NUMPAGES", RunPr(field.Format, context));
                    break;
                case HNote note:
                    sb.Append(NoteReference(note));
                    break;
                case HImage image:
                    sb.Append(Picture(image));
                    break;
                case HTextBox box:
                    sb.Append(TextBox(box, context));
                    break;
                case HShape shape:
                    sb.Append(Shape(shape));
                    break;
            }
        }
    }

    private static void TextRuns(StringBuilder sb, string text, HCharFormat format, RunContext context)
    {
        if (text.Length == 0)
            return;
        sb.Append("<w:r>").Append(RunPr(format, context));
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] is not ('\n' or '\t' or '\r'))
                continue;
            if (i > start)
                sb.Append("<w:t xml:space=\"preserve\">").Append(Escape(text[start..i])).Append("</w:t>");
            if (i < text.Length)
            {
                if (text[i] == '\t')
                    sb.Append("<w:tab/>");
                else if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
                    sb.Append("<w:br/>");
            }
            start = i + 1;
        }
        sb.Append("</w:r>");
    }

    private static string RunPr(HCharFormat format, RunContext context, string? characterStyle = null)
    {
        var properties = RunProperties(format, context, characterStyle);
        return properties.Length == 0 ? "" : $"<w:rPr>{properties}</w:rPr>";
    }

    private static string RunProperties(HCharFormat format, RunContext context, string? characterStyle = null)
    {
        var sb = new StringBuilder();
        characterStyle ??= context.Link ? "Hyperlink" : null;
        if (characterStyle is not null)
            sb.Append($"<w:rStyle w:val=\"{characterStyle}\"/>");
        if (format.Font is not null || format.EastAsianFont is not null)
        {
            sb.Append("<w:rFonts");
            if (format.Font is { } latin)
                sb.Append($" w:ascii=\"{Escape(latin)}\" w:hAnsi=\"{Escape(latin)}\"");
            if ((format.EastAsianFont ?? format.Font) is { } eastAsian)
                sb.Append($" w:eastAsia=\"{Escape(eastAsian)}\"");
            if (format.Font is { } complex)
                sb.Append($" w:cs=\"{Escape(complex)}\"");
            sb.Append("/>");
        }
        if (format.Bold == true)
            sb.Append("<w:b/><w:bCs/>");
        else if (format.Bold == false && context.StyleBold)
            sb.Append("<w:b w:val=\"0\"/><w:bCs w:val=\"0\"/>");
        if (format.Italic == true)
            sb.Append("<w:i/><w:iCs/>");
        if (format.Strike == true)
            sb.Append("<w:strike/>");
        if (Hex(format.Color) is { } color)
            sb.Append($"<w:color w:val=\"{color}\"/>");
        var size = format.Size ?? DefaultSize;
        if (format.Spacing is { } spacing and not 0)
            sb.Append($"<w:spacing w:val=\"{Number((int)Math.Round(spacing * size / 500.0))}\"/>");
        if (format.Size is { } explicitSize)
        {
            var halfPoints = Number(Math.Clamp((int)Math.Round(explicitSize / 50.0), 2, 3276));
            sb.Append($"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/>");
        }
        var highlight = format.Shade is { } shade ? Highlights.GetValueOrDefault(shade) : null;
        if (highlight is not null)
            sb.Append($"<w:highlight w:val=\"{highlight}\"/>");
        if (format.Underline == true)
            sb.Append("<w:u w:val=\"single\"/>");
        else if (format.Underline == false && characterStyle == "Hyperlink")
            sb.Append("<w:u w:val=\"none\"/>");
        if (highlight is null && Hex(format.Shade) is { } fill)
            sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/>");
        if (format.Superscript == true)
            sb.Append("<w:vertAlign w:val=\"superscript\"/>");
        else if (format.Subscript == true)
            sb.Append("<w:vertAlign w:val=\"subscript\"/>");
        return sb.ToString();
    }

    private static void Field(StringBuilder sb, string instruction, string runProperties)
    {
        sb.Append($"<w:r>{runProperties}<w:fldChar w:fldCharType=\"begin\"/></w:r>");
        sb.Append($"<w:r>{runProperties}<w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>");
        sb.Append($"<w:r>{runProperties}<w:fldChar w:fldCharType=\"separate\"/></w:r>");
        sb.Append($"<w:r>{runProperties}<w:t>1</w:t></w:r>");
        sb.Append($"<w:r>{runProperties}<w:fldChar w:fldCharType=\"end\"/></w:r>");
    }

    private string NoteReference(HNote note)
    {
        var (kind, style) = note.Endnote ? ("endnote", "Endnote") : ("footnote", "Footnote");
        if (_part != _document || _textBoxDepth > 0)
        {
            var text = string.Concat(note.Blocks.OfType<HParagraph>().SelectMany(p => p.Inlines).OfType<HText>().Select(t => t.Text));
            var sb = new StringBuilder();
            TextRuns(sb, $" ({text})", default, default);
            return sb.ToString();
        }

        var part = note.Endnote
            ? _endnotesPart ??= new Part("word/endnotes.xml", ContentTypeBase + "endnotes+xml")
            : _footnotesPart ??= new Part("word/footnotes.xml", ContentTypeBase + "footnotes+xml");
        var notes = note.Endnote ? _endnotes : _footnotes;
        var id = notes.Count + 1;
        notes.Add("");
        var number = $"<w:r><w:rPr><w:rStyle w:val=\"{style}Reference\"/></w:rPr><w:{kind}Ref/></w:r><w:r><w:t xml:space=\"preserve\"> </w:t></w:r>";
        notes[id - 1] = WithPart(part, () => Blocks(note.Blocks, requireParagraph: true, paragraphStyle: style + "Text", leadingRuns: number));
        return $"<w:r><w:rPr><w:rStyle w:val=\"{style}Reference\"/></w:rPr><w:{kind}Reference w:id=\"{Number(id)}\"/></w:r>";
    }

    // ───────────────────────── Tables ─────────────────────────

    private string Table(HTable table)
    {
        if (table.Rows.Count == 0)
            return "";
        var (placed, columnCount) = PlaceCells(table);
        var available = Math.Max(4000, _width - (table.Align == HAlign.Left ? table.Indent : 0));
        var widths = ColumnWidths(table, columnCount, available);
        var borders = table.Borders ?? HBorders.Grid;
        var margin = table.CellMargin;

        var continuations = new Dictionary<(int Row, int Column), HCell>();
        for (var r = 0; r < placed.Count; r++)
        {
            foreach (var (cell, column) in placed[r])
            {
                for (var below = 1; below < cell.RowSpan; below++)
                    continuations[(r + below, column)] = cell;
            }
        }

        var sb = new StringBuilder("<w:tbl><w:tblPr>");
        sb.Append($"<w:tblW w:w=\"{Number(Twips(widths.Sum()))}\" w:type=\"dxa\"/>");
        if (table.Align is HAlign.Center or HAlign.Right)
            sb.Append($"<w:jc w:val=\"{(table.Align == HAlign.Center ? "center" : "right")}\"/>");
        else if (table.Indent != 0)
            sb.Append($"<w:tblInd w:w=\"{Number(Twips(table.Indent))}\" w:type=\"dxa\"/>");
        sb.Append("<w:tblBorders>")
          .Append(Border("top", borders.Top)).Append(Border("left", borders.Left))
          .Append(Border("bottom", borders.Bottom)).Append(Border("right", borders.Right))
          .Append(Border("insideH", borders.Top)).Append(Border("insideV", borders.Left))
          .Append("</w:tblBorders>");
        sb.Append("<w:tblLayout w:type=\"fixed\"/>");
        if (margin is { } m)
            sb.Append($"<w:tblCellMar><w:top w:w=\"{Number(Twips(m.Top))}\" w:type=\"dxa\"/><w:left w:w=\"{Number(Twips(m.Left))}\" w:type=\"dxa\"/><w:bottom w:w=\"{Number(Twips(m.Bottom))}\" w:type=\"dxa\"/><w:right w:w=\"{Number(Twips(m.Right))}\" w:type=\"dxa\"/></w:tblCellMar>");
        sb.Append("<w:tblLook w:val=\"04A0\"/></w:tblPr>");
        sb.Append("<w:tblGrid>").Append(string.Concat(widths.Select(w => $"<w:gridCol w:w=\"{Number(Twips(w))}\"/>"))).Append("</w:tblGrid>");

        var horizontalPadding = margin is { } cellMargin ? cellMargin.Left + cellMargin.Right : 1080;
        var outerWidth = _width;
        var headerRows = table.Rows.TakeWhile(r => r.Header).Count();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            sb.Append("<w:tr>");
            if (row.Height is > 0 || r < headerRows)
            {
                sb.Append("<w:trPr>");
                if (row.Height is > 0 and var height)
                    sb.Append($"<w:trHeight w:val=\"{Number(Twips(height))}\" w:hRule=\"atLeast\"/>");
                if (r < headerRows)
                    sb.Append("<w:tblHeader/>");
                sb.Append("</w:trPr>");
            }

            var starts = r < placed.Count ? placed[r].GroupBy(p => p.Column).ToDictionary(g => g.Key, g => g.First().Cell) : [];
            for (var column = 0; column < columnCount;)
            {
                if (starts.TryGetValue(column, out var cell))
                {
                    var span = Math.Clamp(cell.ColSpan, 1, columnCount - column);
                    var width = widths.Skip(column).Take(span).Sum();
                    _width = Math.Max(1000, width - horizontalPadding);
                    sb.Append(Cell(cell, width, span, cell.RowSpan > 1 ? "<w:vMerge w:val=\"restart\"/>" : "", Blocks(cell.Blocks, requireParagraph: true)));
                    column += span;
                }
                else if (continuations.TryGetValue((r, column), out var origin))
                {
                    var span = Math.Clamp(origin.ColSpan, 1, columnCount - column);
                    sb.Append(Cell(origin, widths.Skip(column).Take(span).Sum(), span, "<w:vMerge/>", "<w:p/>"));
                    column += span;
                }
                else
                {
                    sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{Number(Twips(widths[column]))}\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc>");
                    column++;
                }
            }
            sb.Append("</w:tr>");
        }
        _width = outerWidth;
        sb.Append("</w:tbl>");
        if (table.Caption.Count > 0)
            sb.Append(Blocks(table.Caption));
        return sb.ToString();
    }

    internal static (List<List<(HCell Cell, int Column)>> Placed, int Columns) PlaceCells(HTable table)
    {
        var placed = new List<List<(HCell, int)>>();
        var occupied = new HashSet<(int Row, int Column)>();
        var columnCount = table.ColumnCount;
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = new List<(HCell, int)>();
            var column = 0;
            foreach (var cell in table.Rows[r].Cells)
            {
                if (cell.Column >= 0)
                    column = cell.Column;
                while (occupied.Contains((r, column)))
                    column++;
                for (var dr = 0; dr < cell.RowSpan; dr++)
                {
                    for (var dc = 0; dc < cell.ColSpan; dc++)
                        occupied.Add((r + dr, column + dc));
                }
                row.Add((cell, column));
                column += cell.ColSpan;
                columnCount = Math.Max(columnCount, column);
            }
            placed.Add(row);
        }
        return (placed, Math.Max(1, columnCount));
    }

    internal static int[] ColumnWidths(HTable table, int columns, int available)
    {
        if (table.ColumnWidths is { Length: > 0 } absolute)
        {
            var widths = Enumerable.Range(0, columns).Select(i => i < absolute.Length ? absolute[i] : (int)absolute.Average()).ToArray();
            var total = widths.Sum();
            return total <= available ? widths : widths.Select(w => (int)((long)w * available / total)).ToArray();
        }

        var fractions = Enumerable.Range(0, columns).Select(i => table.RelativeWidths is { } rel && i < rel.Length ? rel[i] : 0).ToArray();
        var known = fractions.Where(f => f > 0).Sum();
        var unknown = fractions.Count(f => f <= 0);
        var scale = known > 1 ? 1 / known : 1;
        var rest = unknown == 0 ? 0 : Math.Max(0, 1 - known * scale) / unknown;
        if (unknown > 0 && rest <= 0)
            return Enumerable.Repeat(available / columns, columns).ToArray();
        return fractions.Select(f => (int)(available * (f > 0 ? f * scale : rest))).ToArray();
    }

    private static string Cell(HCell cell, int width, int span, string vMerge, string content)
    {
        var sb = new StringBuilder("<w:tc><w:tcPr>");
        sb.Append($"<w:tcW w:w=\"{Number(Twips(width))}\" w:type=\"dxa\"/>");
        if (span > 1)
            sb.Append($"<w:gridSpan w:val=\"{Number(span)}\"/>");
        sb.Append(vMerge);
        if (cell.Borders is { } borders)
        {
            sb.Append("<w:tcBorders>")
              .Append(Border("top", borders.Top)).Append(Border("left", borders.Left))
              .Append(Border("bottom", borders.Bottom)).Append(Border("right", borders.Right))
              .Append("</w:tcBorders>");
        }
        if (Hex(cell.Fill) is { } fill)
            sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/>");
        if (cell.VerticalAlign != HVerticalAlign.Top)
            sb.Append($"<w:vAlign w:val=\"{(cell.VerticalAlign == HVerticalAlign.Center ? "center" : "bottom")}\"/>");
        sb.Append("</w:tcPr>").Append(content);
        return sb.Append("</w:tc>").ToString();
    }

    private static string Border(string edge, HBorder border)
    {
        if (border.Style == HBorderStyle.None)
            return $"<w:{edge} w:val=\"nil\"/>";
        var style = border.Style switch
        {
            HBorderStyle.Dash => "dashed",
            HBorderStyle.Dot => "dotted",
            HBorderStyle.DashDot => "dotDash",
            HBorderStyle.Double => "double",
            _ => "single",
        };
        var eighths = Math.Clamp((int)Math.Round(border.WidthMm / 25.4 * 72 * 8), 2, 96);
        return $"<w:{edge} w:val=\"{style}\" w:sz=\"{Number(eighths)}\" w:space=\"0\" w:color=\"{Hex(border.Color) ?? "auto"}\"/>";
    }
}


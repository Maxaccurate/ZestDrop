// HTML / XHTML → HDocument with AngleSharp.
// Headings, paragraphs, inline formatting, basic inline CSS, line breaks, nested lists,
// tables with spans/header rows/borders, images, links and anchors, block quotes, pre, hr.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SkiaSharp;

namespace ZestDrop.Engines.Documents;

/// <summary>How an HTML document's resources, links and anchors are resolved.</summary>
internal sealed class HtmlReadOptions
{
    public required string BaseFolder { get; init; }
    public required string MediaFolder { get; init; }
    public string? ResourceRoot { get; init; }
    public Func<string, string?>? MapLink { get; init; }
    public Func<string, string?>? MapId { get; init; }
}

/// <summary>What <see cref="HtmlReader"/> read from one HTML document.</summary>
internal sealed record HtmlContent(List<HBlock> Blocks, string? Title, string? Language);

/// <summary>Reads HTML into the document model.</summary>
internal sealed partial class HtmlReader
{
    private const int QuoteIndent = 2000;
    private const int MaxDepth = 200;
    private const int BaseSize = 1000;
    internal const string CodeShade = "#F1F0F5";
    internal const string MarkShade = "#FFF3A3";

    private static readonly HashSet<string> Skipped =
    [
        "head", "title", "meta", "link", "script", "style", "noscript", "template", "nav", "button", "select",
        "option", "textarea", "input", "iframe", "object", "embed", "video", "audio", "canvas", "map", "param",
        "source", "track", "dialog", "rp", "base",
    ];

    private static readonly HashSet<string> BlockElements =
    [
        "p", "div", "section", "article", "main", "header", "footer", "aside", "address", "center", "details",
        "summary", "fieldset", "legend", "form", "hgroup", "body", "html", "dl", "dt", "dd", "menu", "caption",
        "figure", "figcaption", "blockquote", "pre", "li", "listing", "plaintext", "xmp",
    ];

    private readonly HtmlReadOptions _options;
    private readonly CssStyleSheet _sheet = new();
    private int _embedded;

    private HtmlReader(HtmlReadOptions options) => _options = options;

    // ───────────────────────── Entry points ─────────────────────────

    public static HDocument Read(string path, string mediaFolder)
    {
        var content = ReadFile(path, new HtmlReadOptions
        {
            BaseFolder = Path.GetDirectoryName(Path.GetFullPath(path))!,
            MediaFolder = mediaFolder,
        });
        var section = new HSection();
        section.Blocks.AddRange(content.Blocks);
        var document = new HDocument { Title = content.Title };
        document.Sections.Add(section);
        PruneBookmarks(document);
        return document;
    }

    public static HtmlContent ReadFile(string path, HtmlReadOptions options) =>
        Parse(Decode(File.ReadAllBytes(path)), options, IsXhtml(path));

    public static HtmlContent Parse(string html, HtmlReadOptions options, bool xhtml = false)
    {
        if (xhtml || html.StartsWith("<?xml", StringComparison.Ordinal))
            html = ExpandSelfClosing(html);
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var reader = new HtmlReader(options);
        reader.LoadStyleSheets(document);

        var blocks = new List<HBlock>();
        var sink = new Sink(blocks);
        if (document.Body is { } body)
            OnLargeStack(() => reader.Children(body, sink, new Context(default, null, 0, 0, null, 0, false, null)));
        reader.Flush(sink);
        if (sink.PendingBookmarks.Count > 0 && blocks.OfType<HParagraph>().LastOrDefault() is { } last)
            last.Inlines.AddRange(sink.PendingBookmarks);
        if (blocks.FirstOrDefault() is HParagraph first)
            first.PageBreakBefore = false;

        var title = document.Title?.Trim();
        var language = document.DocumentElement?.GetAttribute("lang") ?? document.DocumentElement?.GetAttribute("xml:lang");
        return new HtmlContent(blocks, string.IsNullOrWhiteSpace(title) ? null : title, string.IsNullOrWhiteSpace(language) ? null : language);
    }

    private static void OnLargeStack(Action work)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }, 16 * 1024 * 1024);
        thread.Start();
        thread.Join();
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    internal static void PruneBookmarks(HDocument document)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inline in document.Sections.SelectMany(s => HDocumentWalker.Inlines(s.Blocks)))
            if (inline is HLink { Target: ['#', ..] target })
                targets.Add(target[1..]);
        foreach (var inlines in document.Sections.SelectMany(s => HDocumentWalker.InlineLists(s.Blocks)).ToList())
            inlines.RemoveAll(i => i is HBookmark bookmark && !targets.Contains(bookmark.Name));
    }

    // ───────────────────────── Encoding ─────────────────────────

    internal static string Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
            return new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd();

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
        var declared = XmlEncoding().Match(head) is { Success: true } xml ? xml.Groups[1].Value
            : MetaCharset().Match(head) is { Success: true } meta ? meta.Groups[1].Value
            : null;
        if (declared is not null && !declared.StartsWith("utf-16", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Encoding.GetEncoding(declared).GetString(bytes);
            }
            catch (ArgumentException)
            {
            }
        }
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }

    private static bool IsXhtml(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".xhtml" or ".xht" or ".xml" or ".opf";

    internal static string ExpandSelfClosing(string html) =>
        SelfClosing().Replace(html, m => $"<{m.Groups[1].Value}{m.Groups[2].Value}></{m.Groups[1].Value}>");

    [GeneratedRegex(@"<\?xml[^>]*encoding\s*=\s*[""']([\w.:-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex XmlEncoding();

    [GeneratedRegex(@"<meta[^>]+charset\s*=\s*[""']?\s*([\w.:-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharset();

    [GeneratedRegex(@"<(?!(?:area|base|br|col|embed|hr|img|input|link|meta|param|source|track|wbr)[\s/>])([A-Za-z][\w:.-]*)(\s[^<>]*?)?\s*/>")]
    private static partial Regex SelfClosing();

    // ───────────────────────── Style sheets ─────────────────────────

    private void LoadStyleSheets(AngleSharp.Html.Dom.IHtmlDocument document)
    {
        foreach (var element in document.QuerySelectorAll("style, link"))
        {
            if (element.LocalName == "style")
            {
                _sheet.Add(element.TextContent);
                continue;
            }
            if (!(element.GetAttribute("rel") ?? "").Contains("stylesheet", StringComparison.OrdinalIgnoreCase)
                || LocalFile(element.GetAttribute("href")) is not { } css)
                continue;
            try
            {
                _sheet.Add(Decode(File.ReadAllBytes(css)));
            }
            catch (IOException)
            {
            }
        }
    }

    private CssDeclarations StyleOf(IElement element)
    {
        var style = _sheet.IsEmpty ? new CssDeclarations() : _sheet.Match(element.LocalName, element.ClassList.ToList());
        if (element.GetAttribute("style") is { Length: > 0 } inline)
            foreach (var (name, value) in CssDeclarations.Parse(inline))
                style[name] = value;
        return style;
    }

    // ───────────────────────── Walking ─────────────────────────

    private readonly record struct Context(
        HCharFormat Format, HAlign? Align, int Indent, int ListLevel, ListItem? Item, int Heading, bool Pre, int? FirstLine, int Depth = 0);

    private sealed class ListItem(HNumbering numbering, int level)
    {
        public HNumbering Numbering { get; } = numbering;
        public int Level { get; } = level;
        public bool Used { get; set; }
    }

    private sealed class Sink(List<HBlock> blocks)
    {
        public List<HBlock> Blocks { get; } = blocks;
        public HParagraph? Paragraph { get; set; }
        public ListItem? ParagraphItem { get; set; }
        public bool Preformatted { get; set; }
        public string? LinkTarget { get; set; }
        public HLink? Link { get; set; }
        public bool LineStart { get; set; } = true;
        public bool EndsWithSpace { get; set; }
        public bool BreakPending { get; set; }
        public List<HInline> PendingBookmarks { get; } = [];
    }

    private void Children(INode parent, Sink sink, Context context)
    {
        foreach (var child in parent.ChildNodes)
            Node(child, sink, context);
    }

    private void Node(INode node, Sink sink, Context context)
    {
        switch (node)
        {
            case IText text:
                Text(text.Data, sink, context);
                break;
            case IElement element:
                Element(element, sink, context);
                break;
        }
    }

    private void Element(IElement element, Sink sink, Context context)
    {
        var name = element.LocalName.ToLowerInvariant();
        if (Skipped.Contains(name) || element.HasAttribute("hidden"))
            return;
        if (context.Depth > MaxDepth)
        {
            Text(element.TextContent, sink, context);
            return;
        }
        var style = StyleOf(element);
        if (style.Get("display") is "none")
            return;

        if (IsPageBreak(style.Get("page-break-before") ?? style.Get("break-before")))
        {
            Flush(sink);
            sink.BreakPending = true;
        }

        var c = context with { Format = CharFormat(name, element, style, context.Format), Depth = context.Depth + 1 };
        if (Align(element.GetAttribute("align") ?? style.Get("text-align")) is { } align)
            c = c with { Align = align };
        if (style.Get("white-space") is { } whiteSpace)
            c = c with { Pre = whiteSpace.StartsWith("pre", StringComparison.OrdinalIgnoreCase) };
        if (CssValues.Length(style.Get("text-indent"), BaseSize) is { } indent)
            c = c with { FirstLine = indent };

        switch (name)
        {
            case "br":
                AddInline(new HLineBreak(c.Format), sink, c);
                sink.LineStart = true;
                sink.EndsWithSpace = false;
                break;
            case "img":
                Image(element, element.GetAttribute("src"), style, sink, c);
                break;
            case "image":
                Image(element, element.GetAttribute("xlink:href") ?? element.GetAttribute("href"), style, sink, c);
                break;
            case "svg":
                foreach (var image in element.Descendants<IElement>().Where(e => e.LocalName.Equals("image", StringComparison.OrdinalIgnoreCase)))
                    Element(image, sink, c);
                break;
            case "a":
                Anchor(element, sink, c);
                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                Block(element, sink, c with { Heading = name[1] - '0' });
                break;
            case "ul" or "ol" or "menu" or "dir":
                List(element, name == "ol", style, sink, c);
                break;
            case "table":
                Table(element, style, sink, c);
                break;
            case "blockquote":
                Block(element, sink, c with { Indent = c.Indent + QuoteIndent });
                break;
            case "dd":
                Block(element, sink, c with { Indent = c.Indent + QuoteIndent });
                break;
            case "pre" or "listing" or "xmp" or "plaintext":
                Block(element, sink, c with { Pre = true, Format = c.Format with { Shade = CodeShade } });
                break;
            case "figure" or "center":
                Block(element, sink, c with { Align = c.Align ?? HAlign.Center });
                break;
            case "figcaption":
                Block(element, sink, c with { Align = c.Align ?? HAlign.Center, Format = c.Format with { Italic = c.Format.Italic ?? true } });
                break;
            case "hr":
                Flush(sink);
                HorizontalRule(sink, c);
                break;
            case "mbp:pagebreak":
                Flush(sink);
                sink.BreakPending = true;
                Children(element, sink, c);
                break;
            case "q":
                AddText("“", sink, c);
                Inline(element, sink, c);
                AddText("”", sink, c);
                break;
            case "rt":
                AddText("(", sink, c);
                Inline(element, sink, c);
                AddText(")", sink, c);
                break;
            default:
                if (IsBlock(name, style))
                    Block(element, sink, c);
                else
                    Inline(element, sink, c);
                break;
        }

        if (IsPageBreak(style.Get("page-break-after") ?? style.Get("break-after")))
        {
            Flush(sink);
            sink.BreakPending = true;
        }
    }

    private static bool IsBlock(string name, CssDeclarations style) => style.Get("display")?.ToLowerInvariant() switch
    {
        "block" or "flex" or "grid" or "list-item" or "table" or "flow-root" => true,
        "inline" or "inline-block" or "inline-flex" or "contents" => false,
        _ => BlockElements.Contains(name),
    };

    private static bool IsPageBreak(string? value) =>
        value?.Trim().ToLowerInvariant() is "always" or "page" or "left" or "right" or "recto" or "verso";

    private void Block(IElement element, Sink sink, Context context)
    {
        Flush(sink);
        Bookmark(element, sink);
        Children(element, sink, context);
        Flush(sink);
    }

    private void Inline(IElement element, Sink sink, Context context)
    {
        Bookmark(element, sink);
        Children(element, sink, context);
    }

    private void Bookmark(IElement element, Sink sink)
    {
        foreach (var id in new[] { element.Id, element.LocalName == "a" ? element.GetAttribute("name") : null })
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var name = _options.MapId is { } map ? map(id) : id;
            if (name is null)
                continue;
            if (sink.Paragraph is { } paragraph)
                paragraph.Inlines.Add(new HBookmark(name));
            else
                sink.PendingBookmarks.Add(new HBookmark(name));
        }
    }

    private void Anchor(IElement element, Sink sink, Context context)
    {
        Bookmark(element, sink);
        var href = element.GetAttribute("href")?.Trim();
        var target = string.IsNullOrEmpty(href) ? null : _options.MapLink is { } map ? map(href) : href;
        if (target is null || sink.LinkTarget is not null)
        {
            Children(element, sink, context);
            return;
        }
        sink.LinkTarget = target;
        sink.Link = null;
        Children(element, sink, context);
        sink.LinkTarget = null;
        sink.Link = null;
    }

    private void HorizontalRule(Sink sink, Context context)
    {
        var paragraph = NewParagraph(sink, context with { Align = HAlign.Center, Heading = 0, Item = null, FirstLine = null });
        paragraph.Inlines.Add(new HShape(HShapeKind.Line) { Width = 14000, Height = 0, Line = HBorder.Thin with { Color = "#808080" } });
        sink.LineStart = false;
        Flush(sink);
    }

    // ───────────────────────── Paragraphs and text ─────────────────────────

    private HParagraph NewParagraph(Sink sink, Context context)
    {
        var paragraph = new HParagraph
        {
            Format = new HParaFormat(
                Align: context.Align,
                Left: context.Indent > 0 ? context.Indent : null,
                FirstLine: context.FirstLine is { } first && first != 0 ? first : null),
            HeadingLevel = context.Heading,
            PageBreakBefore = sink.BreakPending,
        };
        sink.BreakPending = false;
        if (context.Item is { } item)
        {
            paragraph.List = new HListRef(item.Numbering, item.Level, Numbered: !item.Used);
            item.Used = true;
        }
        paragraph.Inlines.AddRange(sink.PendingBookmarks);
        sink.PendingBookmarks.Clear();
        sink.Paragraph = paragraph;
        sink.ParagraphItem = context.Item;
        sink.Preformatted = context.Pre;
        sink.Link = null;
        sink.LineStart = true;
        sink.EndsWithSpace = false;
        sink.Blocks.Add(paragraph);
        return paragraph;
    }

    private void AddInline(HInline inline, Sink sink, Context context)
    {
        var paragraph = sink.Paragraph ?? NewParagraph(sink, context);
        if (sink.LinkTarget is { } target)
        {
            if (sink.Link is null)
            {
                sink.Link = new HLink(target);
                paragraph.Inlines.Add(sink.Link);
            }
            sink.Link.Content.Add(inline);
        }
        else
        {
            paragraph.Inlines.Add(inline);
        }
    }

    private void AddText(string text, Sink sink, Context context)
    {
        if (text.Length == 0)
            return;
        AddInline(new HText(text, context.Format), sink, context);
        sink.LineStart = false;
        sink.EndsWithSpace = text[^1] == ' ';
    }

    private void Text(string data, Sink sink, Context context)
    {
        if (context.Pre)
        {
            var lines = data.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    if (sink.Paragraph is null)
                        NewParagraph(sink, context);
                    Flush(sink, keepEmpty: true);
                }
                AddText(lines[i].Replace("\t", "    "), sink, context);
            }
            return;
        }

        var text = Whitespace().Replace(data, " ");
        if (text.StartsWith(' ') && (sink.Paragraph is null || sink.LineStart || sink.EndsWithSpace))
            text = text[1..];
        AddText(text, sink, context);
    }

    private void Flush(Sink sink, bool keepEmpty = false)
    {
        if (sink.Paragraph is not { } paragraph)
            return;
        sink.Paragraph = null;
        sink.Link = null;

        var hadBreak = !sink.Preformatted && Trim(paragraph.Inlines);
        MergeRuns(paragraph.Inlines);
        foreach (var link in paragraph.Inlines.OfType<HLink>())
            MergeRuns(link.Content);
        if (keepEmpty || hadBreak || sink.Preformatted || HasContent(paragraph.Inlines))
            return;

        sink.Blocks.Remove(paragraph);
        sink.BreakPending |= paragraph.PageBreakBefore;
        sink.PendingBookmarks.AddRange(HDocumentWalker.InlinesOf(paragraph.Inlines).OfType<HBookmark>());
        if (paragraph.List is { Numbered: true } && sink.ParagraphItem is { } item)
            item.Used = false;
    }

    private static bool Trim(List<HInline> inlines)
    {
        var onlyBreak = false;
        if (LastVisible(inlines) is HLineBreak lineBreak)
        {
            inlines.Remove(lineBreak);
            onlyBreak = !HasContent(inlines);
        }
        TrimEnd(inlines);
        for (var i = 0; i < inlines.Count; i++)
        {
            switch (inlines[i])
            {
                case HBookmark:
                    continue;
                case HText text:
                    var trimmed = text.Text.TrimStart(' ');
                    if (trimmed.Length == 0)
                    {
                        inlines.RemoveAt(i--);
                        continue;
                    }
                    inlines[i] = new HText(trimmed, text.Format);
                    break;
            }
            break;
        }
        for (var i = 0; i + 1 < inlines.Count; i++)
            if (inlines[i] is HText text && inlines[i + 1] is HLineBreak && text.Text.EndsWith(' '))
                inlines[i] = new HText(text.Text.TrimEnd(' '), text.Format);
        return onlyBreak;
    }

    private static HInline? LastVisible(List<HInline> inlines) =>
        inlines.LastOrDefault(i => i is not HBookmark) is HLink link ? LastVisible(link.Content) ?? link : inlines.LastOrDefault(i => i is not HBookmark);

    private static void TrimEnd(List<HInline> inlines)
    {
        for (var i = inlines.Count - 1; i >= 0; i--)
        {
            switch (inlines[i])
            {
                case HBookmark:
                    continue;
                case HLink link:
                    TrimEnd(link.Content);
                    return;
                case HText text:
                    var trimmed = text.Text.TrimEnd(' ');
                    if (trimmed.Length == 0)
                    {
                        inlines.RemoveAt(i);
                        continue;
                    }
                    inlines[i] = new HText(trimmed, text.Format);
                    return;
                default:
                    return;
            }
        }
    }

    private static bool HasContent(List<HInline> inlines) => inlines.Any(i => i switch
    {
        HText text => text.Text.Length > 0,
        HLink link => HasContent(link.Content),
        HBookmark => false,
        _ => true,
    });

    internal static void MergeRuns(List<HInline> inlines)
    {
        for (var i = inlines.Count - 1; i > 0; i--)
            if (inlines[i] is HText b && inlines[i - 1] is HText a && a.Format == b.Format)
            {
                inlines[i - 1] = new HText(a.Text + b.Text, a.Format);
                inlines.RemoveAt(i);
            }
    }

    [GeneratedRegex(@"[ \t\n\r\f]+")]
    private static partial Regex Whitespace();

    // ───────────────────────── Formatting ─────────────────────────

    private static HCharFormat CharFormat(string name, IElement element, CssDeclarations style, HCharFormat format)
    {
        var size = format.Size ?? BaseSize;
        format = name switch
        {
            "b" or "strong" => format with { Bold = true },
            "i" or "em" or "cite" or "dfn" or "var" or "address" => format with { Italic = true },
            "u" or "ins" => format with { Underline = true },
            "s" or "strike" or "del" => format with { Strike = true },
            "sub" => format with { Subscript = true, Superscript = null },
            "sup" => format with { Superscript = true, Subscript = null },
            "code" or "kbd" or "samp" or "tt" => format with { Shade = CodeShade },
            "mark" => format with { Shade = MarkShade },
            "small" => format with { Size = (int)(size * 0.83) },
            "big" => format with { Size = (int)(size * 1.2) },
            "th" => format with { Bold = format.Bold ?? true },
            "dt" => format with { Bold = true },
            _ => format,
        };
        if (name == "font")
        {
            if (CssValues.Color(element.GetAttribute("color")) is { } fontColor)
                format = format with { Color = fontColor };
            if (FontTagSize(element.GetAttribute("size")) is { } factor)
                format = format with { Size = (int)(BaseSize * factor) };
        }

        if (style.Get("font-weight")?.ToLowerInvariant() is { } weight)
            format = format with { Bold = weight is "bold" or "bolder" || (int.TryParse(weight, out var w) && w >= 600) };
        if (style.Get("font-style")?.ToLowerInvariant() is { } fontStyle)
            format = format with { Italic = fontStyle.StartsWith("italic", StringComparison.Ordinal) || fontStyle.StartsWith("oblique", StringComparison.Ordinal) };
        if ((style.Get("text-decoration-line") ?? style.Get("text-decoration"))?.ToLowerInvariant() is { } decoration)
        {
            if (decoration.Contains("none", StringComparison.Ordinal))
                format = format with { Underline = false, Strike = false };
            if (decoration.Contains("underline", StringComparison.Ordinal))
                format = format with { Underline = true };
            if (decoration.Contains("line-through", StringComparison.Ordinal))
                format = format with { Strike = true };
        }
        switch (style.Get("vertical-align")?.ToLowerInvariant())
        {
            case "super":
                format = format with { Superscript = true, Subscript = null };
                break;
            case "sub":
                format = format with { Subscript = true, Superscript = null };
                break;
        }
        if (CssValues.Color(style.Get("color")) is { } color)
            format = format with { Color = color };
        if (CssValues.FirstColor(style.Get("background-color") ?? style.Get("background")) is { } shade && shade != "#FFFFFF")
            format = format with { Shade = shade };
        if (CssValues.FontSize(style.Get("font-size"), size) is { } fontSize)
            format = format with { Size = fontSize };
        return format;
    }

    private static double? FontTagSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        if (!int.TryParse(text.TrimStart('+'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            return null;
        var size = text[0] is '+' or '-' ? 3 + number : number;
        return Math.Clamp(size, 1, 7) switch
        {
            1 => 0.63,
            2 => 0.82,
            3 => 1.0,
            4 => 1.13,
            5 => 1.5,
            6 => 2.0,
            _ => 3.0,
        };
    }

    private static HAlign? Align(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "left" or "start" => HAlign.Left,
        "right" or "end" => HAlign.Right,
        "center" or "middle" or "-webkit-center" => HAlign.Center,
        "justify" => HAlign.Justify,
        _ => null,
    };

    // ───────────────────────── Lists ─────────────────────────

    private void List(IElement list, bool ordered, CssDeclarations style, Sink sink, Context context)
    {
        Flush(sink);
        Bookmark(list, sink);
        var type = (style.Get("list-style-type") ?? style.Get("list-style") ?? list.GetAttribute("type") ?? "").Trim();
        var unmarked = type.StartsWith("none", StringComparison.OrdinalIgnoreCase);
        var start = int.TryParse(list.GetAttribute("start"), out var value) ? value : 1;
        var level = Math.Min(context.ListLevel, 6);
        var numbering = unmarked ? null : Numbering(ordered, start, type, level);
        var inner = context with { ListLevel = context.ListLevel + 1, Heading = 0 };

        foreach (var child in list.ChildNodes)
        {
            if (child is not IElement { LocalName: "li" } item)
            {
                Node(child, sink, inner with { Item = context.Item });
                continue;
            }
            var itemStyle = StyleOf(item);
            if (item.HasAttribute("hidden") || itemStyle.Get("display") is "none")
                continue;
            Flush(sink);
            Bookmark(item, sink);
            var itemContext = numbering is null
                ? inner with { Indent = context.Indent + QuoteIndent, Item = null }
                : inner with { Item = new ListItem(numbering, level) };
            if (Align(item.GetAttribute("align") ?? itemStyle.Get("text-align")) is { } align)
                itemContext = itemContext with { Align = align };
            Children(item, sink, itemContext with { Format = CharFormat("li", item, itemStyle, context.Format) });
            Flush(sink);
        }
        Flush(sink);
    }

    internal static HNumbering Numbering(bool ordered, int start, string type, int level)
    {
        string[] bullets = ["●", "○", "■"];
        var numbering = new HNumbering();
        for (var l = 0; l < 7; l++)
        {
            if (!ordered)
            {
                var bullet = l == level ? type.ToLowerInvariant() switch
                {
                    "disc" => "●",
                    "circle" => "○",
                    "square" => "■",
                    _ => bullets[l % 3],
                } : bullets[l % 3];
                numbering.Levels.Add(new HNumberingLevel("DIGIT", bullet, 1, Bullet: true));
                continue;
            }
            var format = l != level ? "DIGIT" : type switch
            {
                "a" or "lower-alpha" or "lower-latin" => "LATIN_SMALL",
                "A" or "upper-alpha" or "upper-latin" => "LATIN_CAPITAL",
                "i" or "lower-roman" => "ROMAN_SMALL",
                "I" or "upper-roman" => "ROMAN_CAPITAL",
                "hangul" or "korean-hangul-formal" => "HANGUL_SYLLABLE",
                _ => "DIGIT",
            };
            numbering.Levels.Add(new HNumberingLevel(format, $"^{l + 1}.", l == level ? Math.Max(0, start) : 1, Bullet: false));
        }
        return numbering;
    }

    // ───────────────────────── Tables ─────────────────────────

    private void Table(IElement table, CssDeclarations style, Sink sink, Context context)
    {
        Flush(sink);
        var hTable = new HTable
        {
            Indent = context.Indent,
            Align = context.Align ?? HAlign.Left,
            Borders = TableBorders(table, style),
        };
        var rows = new List<(IElement Row, bool Head)>();
        foreach (var child in table.Children)
        {
            switch (child.LocalName)
            {
                case "caption":
                    {
                        var captionSink = new Sink(hTable.Caption);
                        Children(child, captionSink, context with { Indent = 0, Item = null, ListLevel = 0, Align = HAlign.Center });
                        Flush(captionSink);
                        break;
                    }
                case "thead" or "tbody" or "tfoot":
                    rows.AddRange(child.Children.Where(r => r.LocalName == "tr").Select(r => (r, child.LocalName == "thead")));
                    break;
                case "tr":
                    rows.Add((child, false));
                    break;
            }
        }

        foreach (var (row, head) in rows)
        {
            var cells = row.Children.Where(c => c.LocalName is "td" or "th").ToList();
            var hRow = new HRow { Header = head || (cells.Count > 0 && cells.All(c => c.LocalName == "th")) };
            foreach (var cell in cells)
                hRow.Cells.Add(Cell(cell, context, hRow.Header));
            if (hRow.Cells.Count > 0)
                hTable.Rows.Add(hRow);
        }
        if (hTable.Rows.Count == 0)
        {
            sink.Blocks.AddRange(hTable.Caption);
            return;
        }
        var leading = true;
        foreach (var row in hTable.Rows)
            row.Header = leading &= row.Header;

        hTable.ColumnCount = ColumnCount(hTable);
        hTable.RelativeWidths = RelativeWidths(table, rows.Select(r => r.Row).FirstOrDefault(), hTable.ColumnCount);

        Bookmark(table, sink);
        if (sink.BreakPending)
        {
            NewParagraph(sink, context with { Item = null, Heading = 0 });
            sink.Paragraph = null;
        }
        else if (sink.PendingBookmarks.Count > 0)
        {
            var cell = hTable.Rows[0].Cells[0];
            if (cell.Blocks.FirstOrDefault() is not HParagraph first)
                cell.Blocks.Insert(0, first = new HParagraph());
            first.Inlines.InsertRange(0, sink.PendingBookmarks);
            sink.PendingBookmarks.Clear();
        }
        sink.Blocks.Add(hTable);
    }

    private HCell Cell(IElement cell, Context context, bool header)
    {
        var style = StyleOf(cell);
        var hCell = new HCell
        {
            RowSpan = Math.Clamp(int.TryParse(cell.GetAttribute("rowspan"), out var rows) ? rows : 1, 1, 1000),
            ColSpan = Math.Clamp(int.TryParse(cell.GetAttribute("colspan"), out var columns) ? columns : 1, 1, 1000),
            Fill = CssValues.Color(cell.GetAttribute("bgcolor")) ?? CssValues.FirstColor(style.Get("background-color") ?? style.Get("background")),
            VerticalAlign = (cell.GetAttribute("valign") ?? style.Get("vertical-align"))?.Trim().ToLowerInvariant() switch
            {
                "middle" or "center" => HVerticalAlign.Center,
                "bottom" => HVerticalAlign.Bottom,
                _ => HVerticalAlign.Top,
            },
        };
        var cellSink = new Sink(hCell.Blocks);
        var format = CharFormat(cell.LocalName, cell, style, context.Format);
        if (header)
            format = format with { Bold = format.Bold ?? true };
        var cellContext = new Context(format, Align(cell.GetAttribute("align") ?? style.Get("text-align")) ?? (cell.LocalName == "th" ? HAlign.Center : null), 0, 0, null, 0, false, null, context.Depth + 1);
        Bookmark(cell, cellSink);
        Children(cell, cellSink, cellContext);
        Flush(cellSink);
        if (cellSink.PendingBookmarks.Count > 0)
        {
            var paragraph = hCell.Blocks.OfType<HParagraph>().FirstOrDefault() ?? new HParagraph();
            if (!hCell.Blocks.Contains(paragraph))
                hCell.Blocks.Insert(0, paragraph);
            paragraph.Inlines.InsertRange(0, cellSink.PendingBookmarks);
        }
        return hCell;
    }

    private static int ColumnCount(HTable table)
    {
        var busy = new List<int>();
        var width = 0;
        foreach (var row in table.Rows)
        {
            var column = 0;
            foreach (var cell in row.Cells)
            {
                while (column < busy.Count && busy[column] > 0)
                    column++;
                for (var c = column; c < column + cell.ColSpan; c++)
                {
                    while (busy.Count <= c)
                        busy.Add(0);
                    busy[c] = cell.RowSpan;
                }
                column += cell.ColSpan;
            }
            width = Math.Max(width, column);
            for (var c = 0; c < busy.Count; c++)
                busy[c] = Math.Max(0, busy[c] - 1);
        }
        return Math.Max(1, width);
    }

    private double[]? RelativeWidths(IElement table, IElement? firstRow, int columns)
    {
        var sources = table.Children.Where(c => c.LocalName == "colgroup").SelectMany(g => g.Children).Concat(table.Children)
            .Where(c => c.LocalName == "col").ToList();
        if (sources.Count == 0 && firstRow is not null)
            sources = firstRow.Children.Where(c => c.LocalName is "td" or "th").ToList();
        if (sources.Count == 0 || sources.Any(s => int.TryParse(s.GetAttribute("colspan") ?? s.GetAttribute("span"), out var span) && span > 1))
            return null;

        var widths = sources.Take(columns).Select(s =>
        {
            var text = (StyleOf(s).Get("width") ?? s.GetAttribute("width") ?? "").Trim();
            if (text.EndsWith('%') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                return (Value: percent, Percent: true);
            return (Value: CssValues.Length(text, BaseSize) ?? 0, Percent: false);
        }).ToList();
        while (widths.Count < columns)
            widths.Add((0, false));
        if (widths.All(w => w.Value <= 0))
            return null;
        if (widths.All(w => w.Percent || w.Value <= 0))
            return [.. widths.Select(w => Math.Max(0, w.Value) / 100)];
        if (widths.All(w => !w.Percent && w.Value > 0))
        {
            var total = widths.Sum(w => w.Value);
            return [.. widths.Select(w => w.Value / total)];
        }
        return null;
    }

    private static HBorders? TableBorders(IElement table, CssDeclarations style)
    {
        var border = style.Get("border") ?? style.Get("border-style");
        if (table.GetAttribute("border") is "0" || border?.Trim().ToLowerInvariant() is "none" or "0" or "hidden")
            return HBorders.Empty;
        return null;
    }

    // ───────────────────────── Images ─────────────────────────

    private void Image(IElement element, string? source, CssDeclarations style, Sink sink, Context context)
    {
        if (ResolveImage(source) is not { } path)
            return;
        var image = new HImage(path)
        {
            Width = ImageLength(style.Get("width") ?? element.GetAttribute("width")),
            Height = ImageLength(style.Get("height") ?? element.GetAttribute("height")),
        };
        AddInline(image, sink, context);
        sink.LineStart = false;
        sink.EndsWithSpace = false;
    }

    private static int? ImageLength(string? value) =>
        value is null || value.Contains("em", StringComparison.OrdinalIgnoreCase) ? null : HwpxUnits.ParseLength(value) is > 0 and var length ? length : null;

    private string? ResolveImage(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        source = source.Trim();
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return SaveDataUri(source);
        return LocalFile(source) is { } path ? UsableImage(path, _options.MediaFolder) : null;
    }

    private string? LocalFile(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        try
        {
            if (reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return new Uri(reference).LocalPath is var local && InsideFolder(local, _options.ResourceRoot) && File.Exists(local) ? local : null;
            if (reference.Contains("://", StringComparison.Ordinal) || reference.StartsWith("//", StringComparison.Ordinal)
                || reference.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return null;
            var cut = reference.IndexOfAny(['?', '#']);
            var relative = Uri.UnescapeDataString(cut >= 0 ? reference[..cut] : reference);
            if (relative.Length == 0)
                return null;
            var candidate = Path.GetFullPath(Path.Combine(_options.BaseFolder, relative));
            return InsideFolder(candidate, _options.ResourceRoot) && File.Exists(candidate) ? candidate : null;
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static bool InsideFolder(string path, string? root)
    {
        if (root is null)
            return true;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Path.GetFullPath(path).StartsWith(root, comparison);
    }

    private string? SaveDataUri(string uri)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0)
            return null;
        var header = uri[5..comma].ToLowerInvariant();
        var extension = header.Split(';')[0] switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/bmp" => "bmp",
            "image/svg+xml" => "svg",
            "image/avif" => "avif",
            _ => null,
        };
        if (extension is null)
            return null;
        byte[] data;
        try
        {
            data = header.EndsWith(";base64", StringComparison.Ordinal)
                ? Convert.FromBase64String(Uri.UnescapeDataString(uri[(comma + 1)..]).Trim())
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(uri[(comma + 1)..]));
        }
        catch (FormatException)
        {
            return null;
        }
        Directory.CreateDirectory(_options.MediaFolder);
        var path = Path.Combine(_options.MediaFolder, $"embedded-{Guid.NewGuid():N}-{++_embedded}.{extension}");
        File.WriteAllBytes(path, data);
        return UsableImage(path, _options.MediaFolder);
    }

    internal static string? UsableImage(string path, string mediaFolder)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp")
            return path;
        try
        {
            if (extension is ".svg" or ".svgz")
                return path;
            using var codec = SKCodec.Create(path);
            if (codec != null)
                return path;
            return path;
        }
        catch
        {
            return null;
        }
    }
}

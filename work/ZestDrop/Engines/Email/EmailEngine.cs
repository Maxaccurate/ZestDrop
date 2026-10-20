// Native Email engine: EML and MSG → HTML, TXT, ZIP (attachments); MSG → EML.

using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MimeKit;

namespace ZestDrop.Engines.Email;

public static partial class EmailEngine
{
    private static readonly HashSet<string> SupportedInput = new(StringComparer.OrdinalIgnoreCase) { "eml", "msg" };
    private static readonly HashSet<string> SupportedOutput = new(StringComparer.OrdinalIgnoreCase) { "eml", "html", "txt", "zip" };

    static EmailEngine()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool CanHandle(string fromExt, string toExt)
    {
        fromExt = fromExt.TrimStart('.').ToLowerInvariant();
        toExt = toExt.TrimStart('.').ToLowerInvariant();

        if (!SupportedInput.Contains(fromExt) || !SupportedOutput.Contains(toExt))
            return false;

        if (fromExt == "eml" && toExt == "eml")
            return false;

        return true;
    }

    public static Task<IReadOnlyList<string>> ConvertAsync(string inputPath, string targetExtension, string? outputPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            progress?.Report(0.1);
            var fromExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
            var toExt = targetExtension.TrimStart('.').ToLowerInvariant();

            var message = fromExt == "msg" ? OutlookMessage.Load(inputPath) : LoadEml(inputPath, cancellationToken);
            progress?.Report(0.5);

            var resolvedOutput = OutputPathHelper.Resolve(inputPath, toExt, outputPath);

            if (toExt == "eml")
            {
                using (var file = File.Create(resolvedOutput))
                    message.WriteTo(FormatOptions.Default, file, cancellationToken);
                progress?.Report(1.0);
                return [resolvedOutput];
            }

            if (toExt == "zip")
            {
                var attachments = Attachments(message);
                if (attachments.Count == 0)
                    throw new InvalidOperationException("The e-mail has no attachments.");
                WriteZip(attachments, resolvedOutput, cancellationToken);
                progress?.Report(1.0);
                return [resolvedOutput];
            }

            var text = toExt == "html" ? Html(message) : PlainText(message);
            File.WriteAllText(resolvedOutput, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: toExt == "txt"));
            progress?.Report(1.0);
            return [resolvedOutput];
        }, cancellationToken);
    }

    internal static MimeMessage LoadEml(string path, CancellationToken cancellationToken = default)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var options = ParserOptions.Default.Clone();
        options.CharsetEncoding = Encoding.GetEncoding(949);
        using var stream = File.OpenRead(path);
        return MimeMessage.Load(options, stream, cancellationToken);
    }

    private static List<(string Name, string Value)> Headers(MimeMessage message)
    {
        var headers = new List<(string, string)>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                headers.Add((name, value.Trim()));
        }
        Add("From", Addresses(message.From));
        Add("To", Addresses(message.To));
        Add("Cc", Addresses(message.Cc));
        if (message.Headers.Contains(HeaderId.Date))
            Add("Date", message.Date.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        Add("Subject", message.Subject);
        var attachments = Attachments(message).Select(a => a.Name).ToList();
        if (attachments.Count > 0)
            Add("Attachments", string.Join(", ", attachments));
        return headers;
    }

    private static string Addresses(InternetAddressList list) =>
        string.Join(", ", list.Mailboxes.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : $"{m.Name} <{m.Address}>"));

    internal static string Html(MimeMessage message)
    {
        var styles = new StringBuilder();
        string body;
        if (message.HtmlBody is { } html)
        {
            var safe = SafeHtml(InlinePictures(html, message));
            body = safe.Body;
            styles.Append(safe.Styles);
        }
        else
        {
            body = $"<pre class=\"zestdrop-mail-text\">{WebUtility.HtmlEncode(message.TextBody ?? "")}</pre>";
        }

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">\n");
        sb.Append($"<title>{WebUtility.HtmlEncode(message.Subject ?? "")}</title>\n");
        sb.Append("<style>\n.zestdrop-mail-headers { border-collapse: collapse; margin: 0 0 12px 0; font-family: sans-serif; font-size: 14px; }\n")
          .Append(".zestdrop-mail-headers th { text-align: left; vertical-align: top; padding: 2px 16px 2px 0; color: #555; font-weight: 600; }\n")
          .Append(".zestdrop-mail-headers td { padding: 2px 0; }\n")
          .Append(".zestdrop-mail-text { white-space: pre-wrap; font-family: inherit; }\n</style>\n");
        sb.Append(styles);
        sb.Append("</head>\n<body>\n<table class=\"zestdrop-mail-headers\">\n");
        foreach (var (name, value) in Headers(message))
            sb.Append($"<tr><th>{name}</th><td>{WebUtility.HtmlEncode(value)}</td></tr>\n");
        sb.Append("</table>\n<hr>\n");
        sb.Append(body);
        sb.Append("\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string InlinePictures(string html, MimeMessage message)
    {
        var parts = new Dictionary<string, MimePart>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            if (part.ContentId is { Length: > 0 } id)
                parts.TryAdd(id.Trim('<', '>'), part);
        }
        if (parts.Count == 0)
            return html;
        return ContentIdReference().Replace(html, match =>
        {
            var id = Uri.UnescapeDataString(match.Groups[1].Value);
            if (!parts.TryGetValue(id, out var part) || part.Content is null)
                return match.Value;
            using var data = new MemoryStream();
            part.Content.DecodeTo(data);
            return $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(data.ToArray())}";
        });
    }

    private static (string Body, string Styles) SafeHtml(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.QuerySelectorAll("script, noscript, iframe, object, embed, applet, frame, frameset, portal, input, button, textarea, select, option, meta, link, base, svg, math, template, video, audio, source, track, noembed, noframes, xmp, plaintext").ToArray())
            element.Parent?.RemoveChild(element);
        foreach (var comment in document.Descendants<IComment>().ToArray())
            comment.Parent?.RemoveChild(comment);
        foreach (var form in document.QuerySelectorAll("form").ToArray())
        {
            while (form.FirstChild is { } child)
                form.Parent!.InsertBefore(child, form);
            form.Parent!.RemoveChild(form);
        }

        foreach (var element in document.QuerySelectorAll("*").ToArray())
        {
            foreach (var attribute in element.Attributes.ToArray())
            {
                var name = attribute.Name;
                if (SafeHtmlAttributes.Contains(name)
                    || name == "alt" && element.LocalName == "img"
                    || name == "href" && element.LocalName == "a" && SafeLink(attribute.Value)
                    || name == "src" && element.LocalName == "img" && SafeInlineImage(attribute.Value))
                    continue;
                element.RemoveAttribute(name);
            }
        }

        var styles = string.Concat(document.QuerySelectorAll("style").Select(element => element.OuterHtml));
        foreach (var style in document.Body?.QuerySelectorAll("style").ToArray() ?? [])
            style.Parent?.RemoveChild(style);
        return (document.Body?.InnerHtml ?? "", styles);
    }

    private static readonly HashSet<string> SafeHtmlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "class", "id", "title", "lang", "dir", "style", "align", "valign", "width", "height", "colspan", "rowspan",
        "cellpadding", "cellspacing", "border", "bgcolor", "color", "face", "size", "nowrap", "start", "type",
        "span", "scope", "headers", "abbr", "summary", "hspace", "vspace", "clear", "noshade", "frame", "rules", "open",
    };

    private static bool SafeLink(string value) => value.StartsWith('#')
        || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";

    private static bool SafeInlineImage(string value) => RasterDataImage().IsMatch(value);

    internal static string PlainText(MimeMessage message)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in Headers(message))
            sb.Append(name).Append(": ").Append(value).Append("\r\n");
        sb.Append("\r\n");
        var text = message.TextBody ?? (message.HtmlBody is { } html ? HtmlToText(html) : "");
        sb.Append(text.Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd()).Append("\r\n");
        return sb.ToString();
    }

    internal static string HtmlToText(string html)
    {
        var text = ScriptBlock().Replace(html, "");
        text = StyleBlock().Replace(text, "");
        text = HeadBlock().Replace(text, "");
        text = LineBreakTag().Replace(text, "\n");
        text = BlockEndTag().Replace(text, "\n");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => SpaceRun().Replace(l, " ").Trim());
        return MultipleBlankLines().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    internal static List<(string Name, MimeEntity Entity)> Attachments(MimeMessage message)
    {
        var result = new List<(string, MimeEntity)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in message.BodyParts)
        {
            string? name = entity switch
            {
                MessagePart attached => (attached.ContentDisposition?.FileName ?? attached.Message?.Subject ?? "message") + ".eml",
                MimePart part when part.IsAttachment || (part.FileName is { Length: > 0 } && part.ContentId is null) =>
                    part.FileName ?? $"attachment{MimeTypes.TryGetExtension(part.ContentType.MimeType, out var extension) switch { true => extension, false => ".bin" }}",
                _ => null,
            };
            if (name is null)
                continue;
            name = SafeName(name);
            var unique = name;
            for (var i = 2; !names.Add(unique); i++)
                unique = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
            result.Add((unique, entity));
        }
        return result;
    }

    private static void WriteZip(List<(string Name, MimeEntity Entity)> attachments, string path, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var (name, entity) in attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                switch (entity)
                {
                    case MessagePart { Message: { } attached }:
                        attached.WriteTo(target, cancellationToken);
                        break;
                    case MimePart { Content: { } content }:
                        content.DecodeTo(target, cancellationToken);
                        break;
                }
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    private static string SafeName(string name)
    {
        name = Path.GetFileName(name.Replace('\\', '/').Split('/')[^1]);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string([.. name.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch)]).Trim(' ', '.');
        return name.Length == 0 ? "attachment" : name.Length > 150 ? name[..150] : name;
    }

    [GeneratedRegex("<head[^>]*>.*?</head>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadBlock();

    [GeneratedRegex("<style[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [GeneratedRegex("<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex("^data:image/(?!svg)[a-z0-9.+-]+;base64,", RegexOptions.IgnoreCase)]
    private static partial Regex RasterDataImage();

    [GeneratedRegex("cid:([^\"'\\s)>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ContentIdReference();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex("</(p|div|tr|li|h[1-6]|table|blockquote)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndTag();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex("[ \\t]+")]
    private static partial Regex SpaceRun();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex MultipleBlankLines();
}

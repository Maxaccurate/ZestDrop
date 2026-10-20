// Readers of the line-based text subtitle formats: SubRip (SRT), WebVTT, ASS/SSA, LRC and MicroDVD. SAMI, TTML and
// SCC have their own files. Readers are lenient.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ZestDrop.Engines.Subtitles;

/// <summary>Reads any supported subtitle format into a <see cref="SubtitleDocument"/>.</summary>
internal static partial class SubtitleReader
{
    public static readonly string[] Formats = ["srt", "ass", "ssa", "vtt", "smi", "sami", "lrc", "ttml", "dfxp", "sub", "scc"];

    public static SubtitleDocument Read(string format, byte[] bytes)
    {
        format = format.ToLowerInvariant();
        if (format == "sub" && LooksBinary(bytes))
            throw new InvalidDataException("This .sub file holds VobSub picture subtitles (from a DVD), not MicroDVD text. Picture subtitles can't be turned into text without OCR.");
        var text = SubtitleEncoding.Decode(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
        var document = format switch
        {
            "srt" => ReadSrt(text),
            "vtt" => ReadVtt(text),
            "ass" or "ssa" => ReadAss(text),
            "lrc" => ReadLrc(text),
            "sub" => ReadMicroDvd(text),
            "smi" or "sami" => SamiReader.Read(text),
            "ttml" or "dfxp" => TtmlReader.Read(text),
            "scc" => SccReader.Read(text),
            _ => throw new NotSupportedException($"Subtitles in '{format}' can't be read."),
        };
        document.Finish();
        if (document.Cues.Count == 0)
            throw new InvalidDataException($"No subtitles were found in this file (it is empty or not a {format.ToUpperInvariant()} subtitle file).");
        return document;
    }

    private static bool LooksBinary(byte[] bytes) =>
        bytes is [0x00, 0x00, 0x01, 0xBA, ..] || bytes.AsSpan(0, Math.Min(bytes.Length, 4096)).Contains((byte)0);

    // ───────────────────────── SubRip ─────────────────────────

    private static SubtitleDocument ReadSrt(string text)
    {
        var document = new SubtitleDocument();
        var lines = text.Split('\n');
        TimeSpan? start = null, end = null;
        List<string> body = [];

        void Flush(bool beforeNextCue)
        {
            if (start is null)
                return;
            while (body.Count > 0 && body[^1].Trim().Length == 0)
                body.RemoveAt(body.Count - 1);
            if (beforeNextCue && body.Count > 0 && IsNumber(body[^1]))
                body.RemoveAt(body.Count - 1);
            var cue = AssOverride().Replace(string.Join('\n', body), "");
            document.Add(start.Value, end!.Value, SubtitleMarkup.FromHtml(cue, decodeEntities: false));
        }

        foreach (var line in lines)
        {
            if (SrtTiming().Match(line) is { Success: true } timing)
            {
                Flush(beforeNextCue: true);
                start = Time(timing, "s");
                end = Time(timing, "e");
                body = [];
            }
            else if (start is not null)
            {
                body.Add(line);
            }
        }
        Flush(beforeNextCue: false);
        return document;
    }

    private static bool IsNumber(string line) => line.Trim() is { Length: > 0 } t && t.All(char.IsAsciiDigit);

    private static TimeSpan Time(Match match, string prefix) => SubtitleTime.From(
        match.Groups[prefix + "h"].Value, match.Groups[prefix + "m"].Value, match.Groups[prefix + "s"].Value, match.Groups[prefix + "f"].Value);

    // ───────────────────────── WebVTT ─────────────────────────

    private static SubtitleDocument ReadVtt(string text)
    {
        var document = new SubtitleDocument();
        var blocks = BlankLines().Split(text.Trim('\n', ' ', '\uFEFF'));
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timingLine = Array.FindIndex(lines, l => l.Contains("-->", StringComparison.Ordinal));
            if (timingLine < 0 && lines[0].StartsWith("WEBVTT", StringComparison.Ordinal))
            {
                if (lines.FirstOrDefault(l => l.StartsWith("Language:", StringComparison.OrdinalIgnoreCase)) is { } language)
                    document.Language = language["Language:".Length..].Trim();
                continue;
            }
            if (timingLine is < 0 or > 1 || lines[0].StartsWith("NOTE", StringComparison.Ordinal))
                continue;
            if (VttTiming().Match(lines[timingLine]) is not { Success: true } timing)
                continue;
            var cue = string.Join('\n', lines.Skip(timingLine + 1));
            document.Add(Time(timing, "s"), Time(timing, "e"), SubtitleMarkup.FromHtml(cue, decodeEntities: true));
        }
        return document;
    }

    // ───────────────────────── ASS / SSA ─────────────────────────

    private static SubtitleDocument ReadAss(string text)
    {
        var document = new SubtitleDocument();
        var section = "";
        string[] styleFormat = ["name", "fontname", "fontsize", "primarycolour", "secondarycolour", "outlinecolour", "backcolour", "bold", "italic", "underline"];
        string[] eventFormat = ["layer", "start", "end", "style", "name", "marginl", "marginr", "marginv", "effect", "text"];
        var styles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].TrimStart();

            if (section == "script info" && key == "title" && value.Length > 0 && value != "<untitled>")
                document.Title = value;
            else if (key == "format" && section.Contains("styles", StringComparison.Ordinal))
                styleFormat = value.Split(',').Select(f => f.Trim().ToLowerInvariant()).ToArray();
            else if (key == "format" && section == "events")
                eventFormat = value.Split(',').Select(f => f.Trim().ToLowerInvariant()).ToArray();
            else if (key == "style" && section.Contains("styles", StringComparison.Ordinal))
            {
                var fields = value.Split(',');
                string Field(string name) => Array.IndexOf(styleFormat, name) is var i and >= 0 && i < fields.Length ? fields[i].Trim() : "";
                static bool On(string flag) => flag is "-1" or "1";
                var open = (On(Field("bold")) ? "<b>" : "") + (On(Field("italic")) ? "<i>" : "") + (On(Field("underline")) ? "<u>" : "");
                styles[Field("name")] = open;
            }
            else if (key == "dialogue" && section == "events")
            {
                var fields = value.Split(',', eventFormat.Length);
                string Field(string name) => Array.IndexOf(eventFormat, name) is var i and >= 0 && i < fields.Length ? fields[i].Trim() : "";
                if (AssTime().Match(Field("start")) is not { Success: true } start || AssTime().Match(Field("end")) is not { Success: true } end)
                    continue;
                var textIndex = Array.IndexOf(eventFormat, "text");
                var cueText = textIndex >= 0 && textIndex < fields.Length ? fields[textIndex] : "";
                if (AssText(cueText) is not { } markup)
                    continue;
                document.Add(AssTimeOf(start), AssTimeOf(end), styles.GetValueOrDefault(Field("style").TrimStart('*'), "") + markup);
            }
        }
        return document;
    }

    private static TimeSpan AssTimeOf(Match match) =>
        SubtitleTime.From(match.Groups["h"].Value, match.Groups["m"].Value, match.Groups["s"].Value, match.Groups["f"].Value);

    internal static string? AssText(string text)
    {
        var sb = new StringBuilder();
        var position = 0;
        var drawing = false;
        foreach (Match block in AssOverride().Matches(text))
        {
            if (!drawing)
                sb.Append(AssPlain(text[position..block.Index]));
            position = block.Index + block.Length;
            foreach (Match tag in AssTag().Matches(block.Value[1..^1]))
            {
                var name = tag.Groups["name"].Value;
                var argument = tag.Groups["arg"].Value.Trim();
                switch (name)
                {
                    case "i" or "b" or "u":
                        var on = argument.Length > 0 && argument != "0" && !(name == "b" && int.TryParse(argument, out var weight) && weight is > 1 and < 600);
                        sb.Append(on ? $"<{name}>" : $"</{name}>");
                        break;
                    case "c" or "1c":
                        sb.Append("</font>");
                        if (AssColor(argument) is { } color)
                            sb.Append($"<font color=\"{color}\">");
                        break;
                    case "r":
                        sb.Append("</font></u></b></i>");
                        break;
                    case "p":
                        drawing = argument.Length > 0 && argument != "0";
                        break;
                }
            }
        }
        if (!drawing)
            sb.Append(AssPlain(text[position..]));
        var result = sb.ToString();
        return SubtitleMarkup.PlainText(SubtitleMarkup.Normalize(result)).Trim().Length == 0 && text.Contains("\\p", StringComparison.Ordinal) ? null : result;
    }

    private static string AssPlain(string text) =>
        text.Replace("\\N", "\n").Replace("\\n", " ").Replace("\\h", " ");

    private static string? AssColor(string value)
    {
        var hex = value.Trim('&').TrimStart('H', 'h');
        if (hex.Length == 0 || !hex.All(Uri.IsHexDigit))
            return null;
        hex = hex.PadLeft(6, '0');
        hex = hex[^6..];
        return $"#{hex[4..6]}{hex[2..4]}{hex[..2]}".ToUpperInvariant();
    }

    // ───────────────────────── LRC ─────────────────────────

    private static SubtitleDocument ReadLrc(string text)
    {
        var document = new SubtitleDocument();
        var offset = TimeSpan.Zero;
        List<(TimeSpan Time, string Text)> entries = [];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (LrcMetadata().Match(line) is { Success: true } metadata)
            {
                var value = metadata.Groups["value"].Value.Trim();
                switch (metadata.Groups["key"].Value.ToLowerInvariant())
                {
                    case "ti" when value.Length > 0:
                        document.Title = value;
                        break;
                    case "la" or "lang" when value.Length > 0:
                        document.Language = value;
                        break;
                    case "offset" when int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms):
                        offset = TimeSpan.FromMilliseconds(ms);
                        break;
                }
                continue;
            }
            var times = LrcTime().Matches(line);
            if (times.Count == 0 || times[0].Index != 0)
                continue;
            var count = 0;
            for (var expected = 0; count < times.Count && times[count].Index == expected; count++)
                expected = times[count].Index + times[count].Length;
            var lyric = line[(times[count - 1].Index + times[count - 1].Length)..];
            lyric = LrcWordTime().Replace(lyric, "").Trim();
            for (var i = 0; i < count; i++)
            {
                var time = SubtitleTime.From("0", times[i].Groups["m"].Value, times[i].Groups["s"].Value, times[i].Groups["f"].Value);
                entries.Add((time, lyric));
            }
        }

        entries = [.. entries.OrderBy(e => e.Time)];
        for (var i = 0; i < entries.Count; i++)
        {
            var (time, lyric) = entries[i];
            if (lyric.Length == 0)
                continue;
            var end = i + 1 < entries.Count ? entries[i + 1].Time : time + SubtitleDocument.DefaultDuration;
            document.Add(time - offset, end - offset, lyric);
        }
        return document;
    }

    // ───────────────────────── MicroDVD ─────────────────────────

    internal const double MicroDvdDefaultFps = 23.976;

    private static SubtitleDocument ReadMicroDvd(string text)
    {
        var document = new SubtitleDocument();
        var fps = MicroDvdDefaultFps;
        List<(long Start, long? End, string Text)> lines = [];
        foreach (var raw in text.Split('\n'))
        {
            if (MicroDvdLine().Match(raw.Trim().TrimStart('\uFEFF')) is not { Success: true } match)
                continue;
            var start = long.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture);
            long? end = match.Groups["end"].Length > 0 ? long.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture) : null;
            var body = match.Groups["text"].Value;
            if (lines.Count == 0 && start <= 1 && double.TryParse(body.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate is > 1 and < 200)
            {
                fps = rate;
                continue;
            }
            lines.Add((start, end, body));
        }
        if (lines.Count == 0)
            throw new InvalidDataException("This .sub file is not a MicroDVD subtitle ({frame}{frame}text lines).");

        TimeSpan FrameTime(long frame) => TimeSpan.FromSeconds(frame / fps);
        for (var i = 0; i < lines.Count; i++)
        {
            var (start, end, body) = lines[i];
            var endTime = end is { } e && e > start ? FrameTime(e)
                : i + 1 < lines.Count ? FrameTime(lines[i + 1].Start) : FrameTime(start) + SubtitleDocument.DefaultDuration;
            document.Add(FrameTime(start), endTime, MicroDvdText(body));
        }
        return document;
    }

    private static string MicroDvdText(string body)
    {
        List<string> all = [];
        List<string> lines = [];
        foreach (var part in body.Split('|'))
        {
            List<string> line = [];
            foreach (Match code in MicroDvdCode().Matches(part))
                (char.IsUpper(code.Groups["key"].Value[0]) ? all : line).AddRange(MicroDvdStyles(code.Groups["key"].Value, code.Groups["value"].Value));
            lines.Add(Open(line) + MicroDvdCode().Replace(part, "") + Close(line));
        }
        return Open(all) + string.Join("\n", lines) + Close(all);

        static string Open(List<string> styles) => string.Concat(styles.Select(s => s.StartsWith('#') ? $"<font color=\"{s}\">" : $"<{s}>"));
        static string Close(List<string> styles) => string.Concat(styles.AsEnumerable().Reverse().Select(s => s.StartsWith('#') ? "</font>" : $"</{s}>"));
    }

    private static IEnumerable<string> MicroDvdStyles(string key, string value) => char.ToLowerInvariant(key[0]) switch
    {
        'y' => value.ToLowerInvariant().Where(c => c is 'i' or 'b' or 'u').Select(c => c.ToString()),
        'c' when AssColor(value.TrimStart('$')) is { } color => [color],
        _ => [],
    };

    // ───────────────────────── Patterns ─────────────────────────

    [GeneratedRegex(@"(?<sh>\d+):(?<sm>\d{1,2}):(?<ss>\d{1,2})(?:[,.:](?<sf>\d{1,3}))?\s*-+>\s*(?<eh>\d+):(?<em>\d{1,2}):(?<es>\d{1,2})(?:[,.:](?<ef>\d{1,3}))?")]
    private static partial Regex SrtTiming();

    [GeneratedRegex(@"^\s*(?:(?<sh>\d+):)?(?<sm>\d{1,2}):(?<ss>\d{2})[.,](?<sf>\d{1,3})\s+-->\s+(?:(?<eh>\d+):)?(?<em>\d{1,2}):(?<es>\d{2})[.,](?<ef>\d{1,3})")]
    private static partial Regex VttTiming();

    [GeneratedRegex(@"\n[ \t]*\n+")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^(?<h>\d+):(?<m>\d{1,2}):(?<s>\d{1,2})(?:[.:](?<f>\d{1,3}))?$")]
    private static partial Regex AssTime();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssOverride();

    [GeneratedRegex(@"\\(?<name>1c|2c|3c|4c|1a|2a|3a|4a|alpha|an|fn|fs|fscx|fscy|fsp|fr[xyz]?|fe|bord|xbord|ybord|shad|xshad|yshad|be|blur|pos|move|org|clip|iclip|fad|fade|kf|ko|k|K|q|t|p|r|i|b|u|s|c|a)(?<arg>[^\\]*)")]
    private static partial Regex AssTag();

    [GeneratedRegex(@"^\[(?<key>[A-Za-z]+):(?<value>[^\]]*)\]\s*$")]
    private static partial Regex LrcMetadata();

    [GeneratedRegex(@"\[(?<m>\d+):(?<s>\d{1,2})(?:[.:](?<f>\d{1,3}))?\]")]
    private static partial Regex LrcTime();

    [GeneratedRegex(@"<\d+:\d{1,2}(?:[.:]\d{1,3})?>")]
    private static partial Regex LrcWordTime();

    [GeneratedRegex(@"^\{(?<start>\d+)\}\{(?<end>\d*)\}(?<text>.*)$")]
    private static partial Regex MicroDvdLine();

    [GeneratedRegex(@"\{(?<key>[A-Za-z]):(?<value>[^}]*)\}")]
    private static partial Regex MicroDvdCode();
}

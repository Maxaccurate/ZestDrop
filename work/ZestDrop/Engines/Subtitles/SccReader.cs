// Scenarist SCC: broadcast closed captions (CEA-608) as hex byte pairs with SMPTE time codes. A small 608 decoder
// for caption channel 1 (CC1): pop-on captions (loaded off screen, shown at End Of Caption, removed at Erase
// Displayed Memory), paint-on and roll-up captions, row addresses, italics, and the special and extended character
// sets. Every byte pair takes one frame at 29.97 fps; control codes sent twice (as broadcasters do) count once.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ZestDrop.Engines.Subtitles;

internal static partial class SccReader
{
    private const double FrameSeconds = 1001.0 / 30000;

    public static SubtitleDocument Read(string text)
    {
        if (!text.TrimStart('﻿', ' ', '\n').StartsWith("Scenarist_SCC", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This is not an SCC file (it must start with \"Scenarist_SCC V1.0\").");
        var document = new SubtitleDocument();
        var decoder = new Decoder(document);
        var last = TimeSpan.Zero;
        foreach (var line in text.Split('\n'))
        {
            if (TimedLine().Match(line.Trim()) is not { Success: true } match)
                continue;
            var start = TimeCode(match);
            var words = match.Groups["data"].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length != 4 || !int.TryParse(words[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var word))
                    continue;
                last = start + TimeSpan.FromSeconds(i * FrameSeconds);
                decoder.Pair(last, (byte)((word >> 8) & 0x7F), (byte)(word & 0x7F));
            }
        }
        decoder.Finish(last);
        return document;
    }

    private static TimeSpan TimeCode(Match match)
    {
        int Part(string name) => int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture);
        int h = Part("h"), m = Part("m"), s = Part("s"), f = Part("f");
        long frames = ((h * 60L + m) * 60 + s) * 30 + f;
        if (match.Groups["sep"].Value is ";" or ".")
        {
            var minutes = 60L * h + m;
            frames -= 2 * (minutes - minutes / 10);
        }
        return TimeSpan.FromSeconds(frames * FrameSeconds);
    }

    private sealed class Decoder(SubtitleDocument document)
    {
        private enum Mode { PopOn, PaintOn, RollUp }

        private Mode _mode = Mode.PopOn;
        private int _rollUpRows = 2;
        private Memory _displayed = new();
        private Memory _hidden = new();
        private TimeSpan? _shownSince;
        private bool _channelOne = true;
        private (byte, byte)? _lastControl;

        private Memory Target => _mode == Mode.PopOn ? _hidden : _displayed;

        public void Pair(TimeSpan time, byte hi, byte lo)
        {
            if (hi < 0x10)
            {
                _lastControl = null;
                return;
            }
            if (hi is >= 0x10 and <= 0x1F)
            {
                if (_lastControl == (hi, lo))
                {
                    _lastControl = null;
                    return;
                }
                _lastControl = (hi, lo);
                Control(time, hi, lo);
                return;
            }
            _lastControl = null;
            if (!_channelOne)
                return;
            Character(time, hi);
            if (lo >= 0x20)
                Character(time, lo);
        }

        private void Control(TimeSpan time, byte hi, byte lo)
        {
            var channel = hi >= 0x18 ? 2 : 1;
            var basic = (byte)(hi & 0xF7);
            if (basic is 0x14 or 0x15 && lo is >= 0x20 and <= 0x2F)
            {
                _channelOne = channel == 1 && basic == 0x14;
                if (_channelOne)
                    Misc(time, lo);
                return;
            }
            _channelOne = channel == 1;
            if (!_channelOne)
                return;

            if (lo >= 0x40)
            {
                Preamble(basic, lo);
            }
            else if (basic == 0x11 && lo is >= 0x20 and <= 0x2F)
            {
                Target.Append(time, " ");
                Target.SetItalic((lo & 0x0E) == 0x0E);
            }
            else if (basic == 0x11 && lo is >= 0x30 and <= 0x3F)
            {
                Target.Append(time, Special[lo - 0x30].ToString());
            }
            else if (basic is 0x12 or 0x13 && lo is >= 0x20 and <= 0x3F)
            {
                Target.Backspace();
                Target.Append(time, (basic == 0x12 ? Extended1 : Extended2)[lo - 0x20].ToString());
            }
        }

        private void Misc(TimeSpan time, byte code)
        {
            switch (code)
            {
                case 0x20:
                    _mode = Mode.PopOn;
                    break;
                case 0x21:
                    Target.Backspace();
                    break;
                case 0x24:
                    Target.ClearRow();
                    break;
                case 0x25 or 0x26 or 0x27:
                    if (_mode != Mode.RollUp)
                        Show(time, new Memory());
                    _mode = Mode.RollUp;
                    _rollUpRows = code - 0x23;
                    break;
                case 0x29:
                    _mode = Mode.PaintOn;
                    break;
                case 0x2C:
                    Show(time, new Memory());
                    break;
                case 0x2D when _mode == Mode.RollUp:
                    Show(time, _displayed.RolledUp(_rollUpRows));
                    _shownSince = null;
                    break;
                case 0x2E:
                    _hidden = new Memory();
                    break;
                case 0x2F:
                    var next = _hidden;
                    _hidden = new Memory();
                    Show(time, next);
                    _mode = Mode.PopOn;
                    break;
            }
        }

        private void Preamble(byte basic, byte lo)
        {
            var row = Array.IndexOf(PreambleRows, basic) is var index and >= 0
                ? (basic == 0x10 ? 11 : PreambleFirstRow[index] + (lo >= 0x60 ? 1 : 0))
                : 15;
            if (_mode == Mode.RollUp)
                row = 15;
            Target.MoveTo(row, italic: (lo & 0x1E) == 0x0E);
        }

        private void Character(TimeSpan time, byte code)
        {
            if (code < 0x20)
                return;
            Target.Append(time, Basic(code).ToString());
            if (_mode != Mode.PopOn)
                _shownSince ??= _displayed.FirstWrite;
        }

        private void Show(TimeSpan time, Memory next)
        {
            var shown = _shownSince ?? _displayed.FirstWrite;
            if (shown is { } since && _displayed.Text is { Length: > 0 } text)
                document.Add(since, time, text);
            _displayed = next;
            _shownSince = next.IsEmpty ? null : time;
        }

        public void Finish(TimeSpan time)
        {
            var since = _shownSince ?? _displayed.FirstWrite;
            if (since is { } start)
                Show(time > start + SubtitleDocument.DefaultDuration ? time : start + SubtitleDocument.DefaultDuration, new Memory());
        }

        private static char Basic(byte code) => code switch
        {
            0x2A => 'á',
            0x5C => 'é',
            0x5E => 'í',
            0x5F => 'ó',
            0x60 => 'ú',
            0x7B => 'ç',
            0x7C => '÷',
            0x7D => 'Ñ',
            0x7E => 'ñ',
            0x7F => '█',
            _ => (char)code,
        };
    }

    private sealed class Memory
    {
        private readonly SortedDictionary<int, StringBuilder> _rows = [];
        private int _row = 15;
        private bool _italic;

        public TimeSpan? FirstWrite { get; private set; }

        public bool IsEmpty => _rows.Values.All(r => SubtitleMarkup.PlainText(r.ToString()).Trim().Length == 0);

        public string Text => string.Join('\n', _rows.Values.Select(r => r.ToString() + (r.ToString().Contains("<i>", StringComparison.Ordinal) ? "</i>" : "")));

        private StringBuilder Row => _rows.TryGetValue(_row, out var row) ? row : _rows[_row] = new StringBuilder();

        public void MoveTo(int row, bool italic)
        {
            if (_italic)
                Row.Append("</i>");
            _row = row;
            _italic = false;
            SetItalic(italic);
        }

        public void SetItalic(bool italic)
        {
            if (italic != _italic)
                Row.Append(italic ? "<i>" : "</i>");
            _italic = italic;
        }

        public void Append(TimeSpan time, string text)
        {
            FirstWrite ??= time;
            Row.Append(text);
        }

        public void Backspace()
        {
            var row = Row;
            if (row.Length > 0 && row[^1] != '>')
                row.Length--;
        }

        public void ClearRow() => _rows.Remove(_row);

        public Memory RolledUp(int rows)
        {
            var next = new Memory();
            var kept = _rows.Where(r => SubtitleMarkup.PlainText(r.Value.ToString()).Trim().Length > 0).TakeLast(rows - 1).ToList();
            for (var i = 0; i < kept.Count; i++)
                next._rows[15 - kept.Count + i] = new StringBuilder(kept[i].Value.ToString());
            return next;
        }
    }

    private static readonly byte[] PreambleRows = [0x11, 0x12, 0x15, 0x16, 0x17, 0x10, 0x13, 0x14];
    private static readonly int[] PreambleFirstRow = [1, 3, 5, 7, 9, 11, 12, 14];
    private const string Special = "®°½¿™¢£♪à èâêîôû";
    private const string Extended1 = "ÁÉÓÚÜü‘¡*’—©℠•“”ÀÂÇÈÊËëÎÏïÔÙùÛ«»";
    private const string Extended2 = "ÃãÍÌìÒòÕõ{}\\^_|~ÄäÖöß¥¤│ÅåØø┌┐└┘";

    [GeneratedRegex(@"^(?<h>\d{2}):(?<m>\d{2}):(?<s>\d{2})(?<sep>[:;.,])(?<f>\d{2})\s+(?<data>.+)$")]
    private static partial Regex TimedLine();
}

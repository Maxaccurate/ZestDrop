// Subtitle files come in whatever encoding their author's PC used. Korean SAMI and SRT files are usually CP949
// (EUC-KR plus Microsoft's extra Hangul), Chinese ones GBK, Western ones Windows-1252.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ZestDrop.Engines.Subtitles;

/// <summary>Decodes subtitle files.</summary>
internal static partial class SubtitleEncoding
{
    public static string Decode(byte[] bytes) => Decode(bytes, CultureInfo.CurrentCulture.TextInfo.ANSICodePage);

    internal static string Decode(byte[] bytes, int ansiCodePage)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
            return new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd();
        if (bytes.Length >= 4 && bytes[1] == 0 && bytes[3] == 0 && bytes[0] != 0)
            return Encoding.Unicode.GetString(bytes);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (Declared(bytes) is { } declared)
            return declared.GetString(bytes);
        if (LooksKorean(bytes))
            return Encoding.GetEncoding(949).GetString(bytes);
        if (ansiCodePage is 932 or 936 or 950)
            return Encoding.GetEncoding(ansiCodePage).GetString(bytes);
        return Encoding.GetEncoding(ansiCodePage is > 0 and not (949 or 1361) and not 65001 ? ansiCodePage : 1252).GetString(bytes);
    }

    private static Encoding? Declared(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        if (DeclaredCharset().Match(head) is not { Success: true } match)
            return null;
        try
        {
            var encoding = Encoding.GetEncoding(match.Groups["name"].Value);
            return encoding.CodePage is 65001 or 1200 or 1201 ? null : encoding;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static bool LooksKorean(byte[] bytes)
    {
        int pairs = 0, hangul = 0, high = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var lead = bytes[i];
            if (lead < 0x80)
                continue;
            if (lead is < 0x81 or > 0xFE || i + 1 >= bytes.Length)
                return false;
            var trail = bytes[++i];
            if (trail is not (>= 0x41 and <= 0x5A or >= 0x61 and <= 0x7A or >= 0x81 and <= 0xFE))
                return false;
            pairs++;
            if (lead is >= 0xB0 and <= 0xC8 && trail >= 0xA1)
                hangul++;
            else if (lead > 0xC8)
                high++;
        }
        return pairs > 0 && hangul * 2 >= pairs && high * 10 <= pairs;
    }

    [GeneratedRegex("""(?:charset|encoding)\s*=\s*["']?(?<name>[A-Za-z0-9_.:-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex DeclaredCharset();
}

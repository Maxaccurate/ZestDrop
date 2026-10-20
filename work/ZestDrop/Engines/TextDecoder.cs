using System;
using System.IO;
using System.Text;

namespace ZestDrop.Engines;

internal static class TextDecoder
{
    static TextDecoder()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch { }
    }

    /// <summary>
    /// Decodes a text byte buffer, automatically recognizing UTF-8 (with or without BOM), UTF-16 BOM,
    /// or falling back to GB18030 / CP949 / UTF-8 replacement.
    /// </summary>
    public static string DecodeText(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        // UTF-16 LE / BE
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        // UTF-8
        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            // Try GB18030 / GBK (common on Windows Chinese systems)
            try
            {
                return Encoding.GetEncoding("GB18030").GetString(bytes);
            }
            catch
            {
                try
                {
                    return Encoding.GetEncoding(949).GetString(bytes);
                }
                catch
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }
    }
}

using System.Security;
using System.Text;

namespace ZestDrop.Engines.Documents;

internal static class DocEscape
{
    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var clean = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '\t' or '\n' or '\r' || (ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF))
                clean.Append(ch);
        }
        return SecurityElement.Escape(clean.ToString()) ?? "";
    }
}

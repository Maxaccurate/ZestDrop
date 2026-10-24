using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ZestDrop.Engines.Office.Sheets;

internal static class FormulaTranslator
{
    // Match references only at token boundaries. Quoted identifiers retain their
    // doubled apostrophes; Unicode letters/marks are valid in unquoted sheet names.
    private const string Sheet = @"(?:'(?:[^']|'')+'|[\p{L}\p{M}\p{N}_\\][\p{L}\p{M}\p{N}_.\\]*)";
    private const string Cell = @"\$?[A-Za-z]{1,3}\$?[1-9][0-9]*";
    private static readonly Regex ExcelReference = new(
        @"(?<![\p{L}\p{M}\p{N}_.\\$!'])" +
        @"(?:(?<sheet1>" + Sheet + @")!)?(?<cell1>" + Cell + @")" +
        @"(?::(?:(?<sheet2>" + Sheet + @")!)?(?<cell2>" + Cell + @"))?" +
        @"(?![\p{L}\p{M}\p{N}_.\\]|\s*\()", RegexOptions.Compiled);

    public static string ToOpenFormula(string excelFormula)
    {
        var f = Body(excelFormula);
        if (f.Length == 0) return "";
        if (excelFormula.TrimStart().StartsWith("of:=", StringComparison.OrdinalIgnoreCase)) return "of:=" + f;
        var output = new StringBuilder("of:=");
        int arrays = 0;
        for (int i = 0; i < f.Length;)
        {
            if (f[i] == '"')
            {
                int end = QuotedEnd(f, i, '"');
                output.Append(f, i, end - i);
                i = end;
                continue;
            }
            var reference = ExcelReference.Match(f, i);
            if (reference.Success && reference.Index == i)
            {
                var sheet1 = reference.Groups["sheet1"].Value;
                var sheet2 = reference.Groups["sheet2"].Success ? reference.Groups["sheet2"].Value : sheet1;
                output.Append('[').Append(sheet1).Append('.').Append(reference.Groups["cell1"].Value);
                if (reference.Groups["cell2"].Success)
                    output.Append(':').Append(sheet2).Append('.').Append(reference.Groups["cell2"].Value);
                output.Append(']');
                i += reference.Length;
                continue;
            }
            if (f[i] == '\'')
            {
                // A quoted sheet-local named expression is not an A1 reference.
                // Preserve its identifier as a token rather than editing its text.
                int end = QuotedEnd(f, i, '\'');
                output.Append(f, i, end - i);
                i = end;
                continue;
            }
            char ch = f[i++];
            if (ch == '{') arrays++;
            else if (ch == '}') arrays--;
            if (ch == ',') output.Append(';');
            else if (ch == ';' && arrays > 0) output.Append('|');
            else output.Append(ch);
        }
        return output.ToString();
    }

    public static string ToExcelFormula(string openFormula)
    {
        var f = Body(openFormula);
        var output = new StringBuilder();
        int arrays = 0;
        for (int i = 0; i < f.Length;)
        {
            char ch = f[i];
            if (ch is '"' or '\'')
            {
                int end = QuotedEnd(f, i, ch);
                output.Append(f, i, end - i);
                i = end;
                continue;
            }
            if (ch == '[')
            {
                int end = FindOutsideQuotes(f, ']', i + 1);
                if (end < 0) throw new FormatException("The OpenFormula reference is not closed.");
                output.Append(ToExcelReference(f[(i + 1)..end]));
                i = end + 1;
                continue;
            }
            if (ch == '{') arrays++;
            else if (ch == '}') arrays--;
            output.Append(ch == ';' ? ',' : ch == '|' && arrays > 0 ? ';' : ch);
            i++;
        }
        return output.ToString();
    }

    private static string Body(string formula)
    {
        var f = formula?.Trim() ?? "";
        if (f.StartsWith("of:=", StringComparison.OrdinalIgnoreCase)) return f[4..];
        if (f.StartsWith("oooc:=", StringComparison.OrdinalIgnoreCase)) return f[6..];
        return f.StartsWith('=') ? f[1..] : f;
    }

    private static string ToExcelReference(string reference)
    {
        int colon = FindOutsideQuotes(reference, ':');
        if (colon < 0) return ToExcelPart(reference);
        var first = ToExcelPart(reference[..colon]);
        var second = ToExcelPart(reference[(colon + 1)..]);
        int bang1 = FindOutsideQuotes(first, '!');
        int bang2 = FindOutsideQuotes(second, '!');
        if (bang1 >= 0 && bang2 >= 0 && first[..bang1] == second[..bang2])
            second = second[(bang2 + 1)..];
        return first + ":" + second;
    }

    private static string ToExcelPart(string part)
    {
        part = part.Trim();
        if (part.StartsWith('.')) return part[1..];
        int dot = FindOutsideQuotes(part, '.', last: true);
        if (dot < 0) return part;
        var sheet = part[..dot];
        if (sheet.StartsWith('$')) sheet = sheet[1..];
        return sheet + "!" + part[(dot + 1)..];
    }

    // Both formula strings and sheet names escape a quote by doubling it.
    private static int QuotedEnd(string text, int start, char quote)
    {
        for (int i = start + 1; i < text.Length; i++)
        {
            if (text[i] != quote) continue;
            if (i + 1 < text.Length && text[i + 1] == quote) { i++; continue; }
            return i + 1;
        }
        throw new FormatException("The formula contains an unterminated quoted token.");
    }

    private static int FindOutsideQuotes(string text, char separator, int start = 0, bool last = false)
    {
        int found = -1;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] is '"' or '\'') { i = QuotedEnd(text, i, text[i]) - 1; continue; }
            if (text[i] != separator) continue;
            if (!last) return i;
            found = i;
        }
        return found;
    }
}

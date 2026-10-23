using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ZestDrop.Engines.Office.Sheets;

internal static partial class FormulaTranslator
{
    private static readonly Regex StringLiteralRegex = new("\"[^\"]*\"", RegexOptions.Compiled);

    private static readonly Regex ExcelRefRegex = new(
        @"(?<![A-Za-z0-9_.\$])(?:(?:'([^']+)'|([A-Za-z0-9_]+))!)?(\$?[A-Za-z]{1,3}\$?[1-9][0-9]*)(?::(?:(?:'([^']+)'|([A-Za-z0-9_]+))!)?(\$?[A-Za-z]{1,3}\$?[1-9][0-9]*))?(?![A-Za-z0-9_]|\s*\()",
        RegexOptions.Compiled);

    private static readonly Regex OpenFormulaBracketRegex = new(@"\[([^\[\]]+)\]", RegexOptions.Compiled);

    /// <summary>
    /// Translates an Excel formula (e.g. "SUM(A1:A2)", "IF(A1>0,A1,0)") to OpenFormula syntax
    /// (e.g. "of:=SUM([.A1:.A2])", "of:=IF([.A1]>0;[.A1];0)").
    /// </summary>
    public static string ToOpenFormula(string excelFormula)
    {
        if (string.IsNullOrWhiteSpace(excelFormula)) return "";
        var f = excelFormula.Trim();
        if (f.StartsWith("of:=", StringComparison.OrdinalIgnoreCase)) return f;
        if (f.StartsWith("oooc:=", StringComparison.OrdinalIgnoreCase)) return "of:=" + f[6..];
        if (f.StartsWith('=')) f = f[1..];

        // Mask string literals
        var strings = new List<string>();
        var masked = StringLiteralRegex.Replace(f, m =>
        {
            strings.Add(m.Value);
            return $"\0{strings.Count - 1}\0";
        });

        // Translate cell and range references
        masked = ExcelRefRegex.Replace(masked, m =>
        {
            var s1 = !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : (m.Groups[2].Success ? m.Groups[2].Value : null);
            var c1 = m.Groups[3].Value;
            var s2 = !string.IsNullOrEmpty(m.Groups[4].Value) ? m.Groups[4].Value : (m.Groups[5].Success ? m.Groups[5].Value : null);
            var c2 = m.Groups[6].Success ? m.Groups[6].Value : null;

            if (!string.IsNullOrEmpty(c2))
            {
                if (!string.IsNullOrEmpty(s1))
                {
                    var p1 = m.Groups[1].Success ? $"'{s1}'." : $"{s1}.";
                    var p2 = m.Groups[4].Success ? $"'{s2}'." : (!string.IsNullOrEmpty(s2) ? $"{s2}." : p1);
                    return $"[{p1}{c1}:{p2}{c2}]";
                }
                return $"[.{c1}:.{c2}]";
            }
            else
            {
                if (!string.IsNullOrEmpty(s1))
                {
                    var p1 = m.Groups[1].Success ? $"'{s1}'." : $"{s1}.";
                    return $"[{p1}{c1}]";
                }
                return $"[.{c1}]";
            }
        });

        // Replace , with ; outside strings and brackets
        var sb = new StringBuilder(masked.Length);
        bool inBracket = false;
        foreach (var ch in masked)
        {
            if (ch == '[') { inBracket = true; sb.Append(ch); }
            else if (ch == ']') { inBracket = false; sb.Append(ch); }
            else if (ch == ',' && !inBracket) { sb.Append(';'); }
            else { sb.Append(ch); }
        }
        var result = sb.ToString();

        // Restore string literals
        for (int i = 0; i < strings.Count; i++)
            result = result.Replace($"\0{i}\0", strings[i]);

        return "of:=" + result;
    }

    /// <summary>
    /// Translates an OpenFormula string (e.g. "of:=SUM([.A1:.A2])", "of:=IF([.A1]>0;[.A1];0)")
    /// to Excel formula syntax (e.g. "SUM(A1:A2)", "IF(A1>0,A1,0)").
    /// </summary>
    public static string ToExcelFormula(string openFormula)
    {
        if (string.IsNullOrWhiteSpace(openFormula)) return "";
        var f = openFormula.Trim();
        if (f.StartsWith("of:=", StringComparison.OrdinalIgnoreCase)) f = f[4..];
        else if (f.StartsWith("oooc:=", StringComparison.OrdinalIgnoreCase)) f = f[6..];
        else if (f.StartsWith('=')) f = f[1..];

        // Mask string literals
        var strings = new List<string>();
        var masked = StringLiteralRegex.Replace(f, m =>
        {
            strings.Add(m.Value);
            return $"\0{strings.Count - 1}\0";
        });

        // Replace parameter separators ; with ,
        masked = masked.Replace(';', ',');

        // Convert [...] OpenFormula references to Excel syntax
        masked = OpenFormulaBracketRegex.Replace(masked, m =>
        {
            var content = m.Groups[1].Value.Trim();
            if (content.Contains(':'))
            {
                var parts = content.Split(':', 2);
                var c1 = ConvertPart(parts[0]);
                var c2 = ConvertPart(parts[1]);
                if (c1.Contains('!') && c2.Contains('!'))
                {
                    var s1 = c1.Split('!', 2);
                    var s2 = c2.Split('!', 2);
                    if (s1[0] == s2[0]) return $"{s1[0]}!{s1[1]}:{s2[1]}";
                }
                return $"{c1}:{c2}";
            }
            return ConvertPart(content);
        });

        // Restore string literals
        for (int i = 0; i < strings.Count; i++)
            masked = masked.Replace($"\0{i}\0", strings[i]);

        return masked;
    }

    private static string ConvertPart(string p)
    {
        p = p.Trim();
        if (p.StartsWith('.')) return p[1..];
        var dotIndex = p.LastIndexOf('.');
        if (dotIndex > 0)
        {
            var sheet = p[..dotIndex];
            var cell = p[(dotIndex + 1)..];
            return $"{sheet}!{cell}";
        }
        return p;
    }
}

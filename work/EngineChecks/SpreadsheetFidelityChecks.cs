using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using ZestDrop.Engines.Office.Sheets;

namespace EngineChecks;

internal static class SpreadsheetFidelityChecks
{
    internal static void Run(Action<bool, string> assert)
    {
        Console.WriteLine("\n--- Quoted sheet references and explicit error types ---");
        var formulas = new[]
        {
            (Excel: "'Q1;Sales'!A1", Ods: "of:=['Q1;Sales'.A1]"),
            (Excel: "'Q1,Sales'!A1", Ods: "of:=['Q1,Sales'.A1]"),
            (Excel: "'O''Brien'!A1", Ods: "of:=['O''Brien'.A1]"),
            (Excel: "你好!A1", Ods: "of:=[你好.A1]"),
            (Excel: "'Q1;O''Brien'!$A$1:$B2", Ods: "of:=['Q1;O''Brien'.$A$1:'Q1;O''Brien'.$B2]"),
            (Excel: "'Q1!Sales.west'!A1", Ods: "of:=['Q1!Sales.west'.A1]"),
            (Excel: "IF(A1=1,\"He said \"\"A1;B2\"\", yes\",\"z\")", Ods: "of:=IF([.A1]=1;\"He said \"\"A1;B2\"\", yes\";\"z\")"),
            (Excel: "SUM({1,2;3,4})", Ods: "of:=SUM({1;2|3;4})"),
        };
        foreach (var (excel, ods) in formulas)
        {
            assert(FormulaTranslator.ToOpenFormula(excel) == ods, $"Excel → OpenFormula preserves quoted tokens: {excel}");
            assert(FormulaTranslator.ToExcelFormula(ods) == excel, $"OpenFormula → Excel preserves quoted tokens: {excel}");
        }

        var folder = Path.Combine(Path.GetTempPath(), "zestdrop-fidelity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var book = new Workbook();
            var sheet = new Worksheet("Results");
            book.Sheets.Add(sheet);
            for (int i = 0; i < 4; i++)
                sheet.Set(i, 0, new SheetCell(new[] { "#N/A", "#REF! is just text", "#DIV/0!", "#VALUE!" }[i], CellStyle.Default));
            sheet.Set(4, 0, new SheetCell("#N/A", CellStyle.Default) { Formula = "\"#N/A\"" });
            sheet.Set(5, 0, new SheetCell("#DIV/0!", CellStyle.Default) { Formula = "1/0", IsError = true });
            sheet.Set(6, 0, new SheetCell("#N/A", CellStyle.Default) { IsError = true });
            var xlsx = Path.Combine(folder, "direct.xlsx");
            XlsxWriter.Write(book, xlsx);
            CheckXlsxTypes(xlsx, assert, "Direct XLSX");

            var ods = Path.Combine(folder, "roundtrip.ods");
            OdsWriter.Write(book, ods);
            using (var zip = ZipFile.OpenRead(ods))
            using (var stream = zip.GetEntry("content.xml")!.Open())
            {
                var xml = XDocument.Load(stream);
                XNamespace calc = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";
                XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
                assert(xml.Descendants(table + "table-cell").Count(c => (string?)c.Attribute(calc + "value-type") == "error") == 2,
                    "ODS explicitly marks exactly the two real errors using Calc's error-type extension");
            }
            var read = OdsReader.Read(ods);
            for (int row = 0; row < 7; row++)
                assert(read.Sheets[0].Get(row, 0)?.IsError == (row >= 5), $"ODS distinguishes string and error state in row {row + 1}");
            var back = Path.Combine(folder, "returned.xlsx");
            XlsxWriter.Write(read, back);
            CheckXlsxTypes(back, assert, "ODS roundtrip XLSX");
            assert(XlsxReader.Read(back).Sheets[0].Get(4, 0)?.Formula == "\"#N/A\"", "String-returning formula survives ODS roundtrip");

            // An independently created ODS with string and error cells containing
            // identical displayed text: prefixes are intentionally arbitrary.
            var independent = Path.Combine(folder, "independent.ods");
            using (var zip = ZipFile.Open(independent, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("content.xml").Open()))
                writer.Write("""
                    <o:document-content xmlns:o="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                      xmlns:t="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                      xmlns:p="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
                      xmlns:c="urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0">
                      <o:body><o:spreadsheet><t:table t:name="Sheet1"><t:table-row>
                      <t:table-cell o:value-type="string"><p:p>#N/A</p:p></t:table-cell>
                      <t:table-cell o:value-type="string" c:value-type="error"><p:p>#N/A</p:p></t:table-cell>
                      </t:table-row></t:table></o:spreadsheet></o:body>
                    </o:document-content>
                    """);
            var cells = OdsReader.Read(independent).Sheets[0];
            assert(cells.Get(0, 0)?.IsError == false && cells.Get(0, 1)?.IsError == true,
                "Independent ODS error marker is recognized by namespace URI, without guessing from identical text");
        }
        finally
        {
            // This fresh, test-owned folder only contains files created above.
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void CheckXlsxTypes(string path, Action<bool, string> assert, string label)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = XDocument.Load(stream).Descendants(s + "c").ToDictionary(c => (string)c.Attribute("r")!);
        for (int row = 1; row <= 7; row++)
        {
            var type = (string?)cells["A" + row].Attribute("t");
            assert(type == (row >= 6 ? "e" : row == 5 ? "str" : "inlineStr"), $"{label} preserves cell type A{row}");
        }
    }
}

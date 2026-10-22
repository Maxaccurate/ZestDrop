using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZestDrop;
using ZestDrop.Cli;
using ZestDrop.Engines;
using ZestDrop.Engines.Documents.Docx;
using ZestDrop.Engines.Fonts.Cff;
using ZestDrop.Engines.Models3D;
using ZestDrop.Engines.Office.Sheets;

namespace EngineChecks;

internal static class Program
{
    private static int _failures;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== ZestDrop Engine Regression & Fidelity Checks ===");

        TestWatchFolderBoundaryChecks();
        TestArchiveToZipRouting();
        TestObjQuadTriangulation();
        TestGltfExportBufferFlushing();
        TestCffZeroSizedPrivateDict();
        TestSheetFormulaPreservation();
        TestDocxHtmlImageEmbedding();
        TestCliOptionsAndOutputFolder();

        await Task.CompletedTask;

        if (_failures == 0)
        {
            Console.WriteLine("\n[PASS] All engine regression checks passed successfully.");
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[FAIL] {_failures} check(s) failed.");
        Console.ResetColor();
        return 1;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            _failures++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  FAIL: {message}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  OK: {message}");
            Console.ResetColor();
        }
    }

    private static void TestWatchFolderBoundaryChecks()
    {
        Console.WriteLine("\n--- 1. Watch Folder & CLI Inbox Boundary Matching ---");

        var baseDir = Path.Combine(Path.GetTempPath(), "zestdrop_watch_test");
        var origDir = Path.Combine(baseDir, "originals");
        var convDir = Path.Combine(baseDir, "converted");

        var insideOrig = Path.Combine(origDir, "doc.pdf");
        var insideConv = Path.Combine(convDir, "doc.png");
        var siblingOrig = Path.Combine(baseDir, "originals-doc.pdf");
        var siblingConv = Path.Combine(baseDir, "converted-doc.png");

        Assert(WatchFolders.IsDescendantOf(insideOrig, origDir), "File inside originals/ is recognized as descendant (WatchFolders)");
        Assert(!WatchFolders.IsDescendantOf(siblingOrig, origDir), "Sibling file 'originals-*.pdf' is NOT recognized as descendant (WatchFolders)");
        Assert(WatchFolders.IsDescendantOf(insideConv, convDir), "File inside converted/ is recognized as descendant (WatchFolders)");
        Assert(!WatchFolders.IsDescendantOf(siblingConv, convDir), "Sibling file 'converted-*.png' is NOT recognized as descendant (WatchFolders)");

        Assert(CliRunner.IsDescendantOf(insideOrig, origDir), "File inside originals/ is recognized as descendant (CliRunner)");
        Assert(!CliRunner.IsDescendantOf(siblingOrig, origDir), "Sibling file 'originals-*.pdf' is NOT recognized as descendant (CliRunner)");
        Assert(CliRunner.IsDescendantOf(insideConv, convDir), "File inside converted/ is recognized as descendant (CliRunner)");
        Assert(!CliRunner.IsDescendantOf(siblingConv, convDir), "Sibling file 'converted-*.png' is NOT recognized as descendant (CliRunner)");
    }

    private static void TestArchiveToZipRouting()
    {
        Console.WriteLine("\n--- 2. Archive-to-ZIP Format Routing ---");

        // .7z, .tar, .rar to .zip should NOT be handled by NativeEngines (which creates loose ZIP container),
        // but delegated to the full backend archive repacking.
        Assert(!NativeEngines.CanHandlePair("7z", "zip"), "7z -> zip is routed away from NativeEngines to archive backend");
        Assert(!NativeEngines.CanHandlePair("tar", "zip"), "tar -> zip is routed away from NativeEngines to archive backend");
        Assert(!NativeEngines.CanHandlePair("rar", "zip"), "rar -> zip is routed away from NativeEngines to archive backend");

        // Loose files to .zip should still be handled natively
        Assert(NativeEngines.CanHandlePair("txt", "zip"), "txt -> zip is handled natively as loose zip packaging");
        Assert(NativeEngines.CanHandlePair("png", "zip"), "png -> zip is handled natively as loose zip packaging");
    }

    private static void TestObjQuadTriangulation()
    {
        Console.WriteLine("\n--- 3. 3D OBJ Quad/Polygon Triangulation ---");

        var tempObj = Path.Combine(Path.GetTempPath(), $"test_quad_{Guid.NewGuid():N}.obj");
        var objContent = """
            v 0.0 0.0 0.0
            v 1.0 0.0 0.0
            v 1.0 1.0 0.0
            v 0.0 1.0 0.0
            f 1 2 3 4
            """;

        try
        {
            File.WriteAllText(tempObj, objContent);
            var model = ObjFormat.Read(tempObj, null, CancellationToken.None);

            Assert(model != null, "OBJ with quad face parsed without crashing");
            Assert(model!.Parts.Count == 1, "Model has 1 mesh part");
            var part = model.Parts[0];
            Assert(part.Positions.Count == 12, $"Quad produced 4 3D vertices (12 coordinates, got {part.Positions.Count})");
            Assert(part.Indices.Count == 6, $"Quad triangulated into 2 triangles (6 indices, got {part.Indices.Count})");
        }
        finally
        {
            if (File.Exists(tempObj)) File.Delete(tempObj);
        }
    }

    private static void TestGltfExportBufferFlushing()
    {
        Console.WriteLine("\n--- 4. glTF Export Buffer Flush & Valid JSON ---");

        var part = new MeshPart();
        part.Positions.AddRange([0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f]);
        part.Indices.AddRange([0, 1, 2]);

        var model = new Model3D();
        model.Parts.Add(part);

        using var outMs = new MemoryStream();
        GltfFormat.Write(model, outMs, binary: false, CancellationToken.None);

        var gltfBytes = outMs.ToArray();
        Assert(gltfBytes.Length > 0, $"glTF exported stream is non-empty ({gltfBytes.Length} bytes)");

        var json = Encoding.UTF8.GetString(gltfBytes);
        Assert(json.Contains("\"asset\""), "glTF JSON contains 'asset' section");
        Assert(json.Contains("\"buffers\""), "glTF JSON contains 'buffers' section");
        Assert(json.Contains("\"meshes\""), "glTF JSON contains 'meshes' section");

        // Verify valid JSON parsing
        bool validJson = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            validJson = doc.RootElement.TryGetProperty("asset", out _);
        }
        catch { }

        Assert(validJson, "glTF output parses as valid JSON");
    }

    private static void TestCffZeroSizedPrivateDict()
    {
        Console.WriteLine("\n--- 5. CFF Font with Zero-Sized Private DICT ---");

        var dummyData = new byte[100];
        // Dict containing OpPrivate (18) with size 0, and offset 9999 (which would throw if read)
        var dict = new Dictionary<int, double[]>
        {
            [18] = [0.0, 9999.0]
        };

        bool threw = false;
        CffPrivate? priv = null;
        try
        {
            priv = CffFont.ReadPrivate(dummyData, dict);
        }
        catch (Exception ex)
        {
            threw = true;
            Console.WriteLine($"  Error reading CFF Private: {ex.Message}");
        }

        Assert(!threw && priv != null, "Zero-sized Private DICT handled gracefully without throwing outside table exception");
        Assert(priv?.Subrs.Length == 0, "Returned CffPrivate has empty Subrs");
    }

    private static void TestSheetFormulaPreservation()
    {
        Console.WriteLine("\n--- 6. Sheet Formula Preservation (XLSX <-> ODS) ---");

        var tempXlsx = Path.Combine(Path.GetTempPath(), $"test_formula_{Guid.NewGuid():N}.xlsx");
        var tempOds = Path.Combine(Path.GetTempPath(), $"test_formula_{Guid.NewGuid():N}.ods");

        try
        {
            // Create workbook with formula
            var wb = new Workbook();
            var sheet = new Worksheet("Sheet1");
            wb.Sheets.Add(sheet);
            sheet.Set(0, 0, new SheetCell("10", CellStyle.Default, IsNumber: true) { Value = 10 });
            sheet.Set(1, 0, new SheetCell("20", CellStyle.Default, IsNumber: true) { Value = 20 });
            sheet.Set(2, 0, new SheetCell("30", CellStyle.Default, IsNumber: true) { Value = 30, Formula = "SUM(A1:A2)" });

            // Test XLSX Writer & Reader
            XlsxWriter.Write(wb, tempXlsx);
            var readXlsxWb = XlsxReader.Read(tempXlsx);
            var readXlsxCell = readXlsxWb.Sheets[0].Get(2, 0);
            Assert(readXlsxCell?.Formula == "SUM(A1:A2)", $"XLSX roundtrip preserved formula (expected 'SUM(A1:A2)', got '{readXlsxCell?.Formula}')");

            // Test ODS Writer & Reader
            OdsWriter.Write(wb, tempOds);
            var readOdsWb = OdsReader.Read(tempOds);
            var readOdsCell = readOdsWb.Sheets[0].Get(2, 0);
            Assert(readOdsCell?.Formula == "SUM(A1:A2)", $"ODS roundtrip preserved formula (expected 'SUM(A1:A2)', got '{readOdsCell?.Formula}')");
        }
        finally
        {
            if (File.Exists(tempXlsx)) File.Delete(tempXlsx);
            if (File.Exists(tempOds)) File.Delete(tempOds);
        }
    }

    private static void TestDocxHtmlImageEmbedding()
    {
        Console.WriteLine("\n--- 7. DOCX -> HTML Embedded Images ---");

        var tempDocx = Path.Combine(Path.GetTempPath(), $"test_img_{Guid.NewGuid():N}.docx");
        var tempHtml = Path.Combine(Path.GetTempPath(), $"test_img_{Guid.NewGuid():N}.html");

        try
        {
            var pngBytes = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
                0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
                0x54, 0x78, 0x9C, 0x63, 0x60, 0x60, 0x60, 0x00,
                0x00, 0x00, 0x04, 0x00, 0x01, 0x27, 0x34, 0x27,
                0x0A, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
                0x44, 0xAE, 0x42, 0x60, 0x82
            };

            var docXml = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>Hello with Image</w:t></w:r></w:p>
                    <w:p><w:r><w:drawing>
                      <wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
                        <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                          <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                            <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                              <pic:blipFill>
                                <a:blip r:embed="rId1"/>
                              </pic:blipFill>
                            </pic:pic>
                          </a:graphicData>
                        </a:graphic>
                      </wp:inline>
                    </w:drawing></w:r></w:p>
                  </w:body>
                </w:document>
                """;

            var relsXml = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
                </Relationships>
                """;

            using (var zip = ZipFile.Open(tempDocx, ZipArchiveMode.Create))
            {
                var entryDoc = zip.CreateEntry("word/document.xml");
                using (var s = entryDoc.Open())
                using (var w = new StreamWriter(s, Encoding.UTF8))
                    w.Write(docXml);

                var entryRels = zip.CreateEntry("word/_rels/document.xml.rels");
                using (var s = entryRels.Open())
                using (var w = new StreamWriter(s, Encoding.UTF8))
                    w.Write(relsXml);

                var entryImg = zip.CreateEntry("word/media/image1.png");
                using (var s = entryImg.Open())
                    s.Write(pngBytes);
            }

            DocxEngine.Convert(tempDocx, "html", tempHtml);

            var htmlContent = File.ReadAllText(tempHtml);
            Assert(htmlContent.Contains("<img"), "Generated HTML contains <img> tag");
            Assert(htmlContent.Contains("src=\"data:image/png;base64,"), "<img> tag contains embedded base64 data URI");
        }
        finally
        {
            if (File.Exists(tempDocx)) File.Delete(tempDocx);
            if (File.Exists(tempHtml)) File.Delete(tempHtml);
        }
    }

    private static void TestCliOptionsAndOutputFolder()
    {
        Console.WriteLine("\n--- 8. CLI Options & Output Destination Creation ---");

        var tempOutDir = Path.Combine(Path.GetTempPath(), $"zestdrop_cli_dest_{Guid.NewGuid():N}");
        try
        {
            Assert(!Directory.Exists(tempOutDir), "Target destination folder initially does not exist");
            var job = new ConversionJob(["sample.txt"], "convert:pdf", new Dictionary<string, string> { ["_outputDir"] = tempOutDir });
            var resolved = OutputFolder.For(job);
            Assert(resolved == tempOutDir, "OutputFolder.For resolved to job's _outputDir");
            Assert(Directory.Exists(tempOutDir), "OutputFolder.For automatically created missing destination directory");
        }
        finally
        {
            if (Directory.Exists(tempOutDir)) Directory.Delete(tempOutDir);
        }

        // Test CLI option parsing aliases
        var optFormats = CliOptions.Parse(["formats", "cad"]);
        Assert(optFormats.Category == "cad", "Positional argument parsed as formats category ('formats cad' -> category='cad')");

        var optConvertAction = CliOptions.Parse(["convert", "in.txt", "--action", "pdf", "--out", "out_dir"]);
        Assert(optConvertAction.TargetFormat == "pdf", "'--action' alias correctly mapped to TargetFormat");
        Assert(optConvertAction.OutputFolder == "out_dir", "'--out' alias correctly mapped to OutputFolder");

        // Verify Catalog targets clean-up
        Assert(!Catalog.Targets("md").Contains("pdf"), "Catalog.Targets('md') no longer contains non-executable 'pdf'");
        Assert(!Catalog.Targets("html").Contains("pdf"), "Catalog.Targets('html') no longer contains non-executable 'pdf'");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using SkiaSharp;
using ZestDrop.Engines.Models3D;

namespace ZestDrop.Engines;

public static class NativeEngines
{
    static NativeEngines()
    {
        Initialize();
    }

    public static void Initialize()
    {
        Backend.NativeHandlerCanHandle = CanHandleRequest;
        Backend.NativeHandlerExecute = ExecuteAsync;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> RasterImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "bmp", "webp", "gif", "ico"
    };

    private static bool IsRasterImage(string ext) => RasterImages.Contains(ext.TrimStart('.'));

    public static bool CanHandleRequest(string requestPath)
    {
        try
        {
            var job = JsonSerializer.Deserialize<ConversionJob>(File.ReadAllText(requestPath), JsonOptions);
            return job != null && CanHandle(job);
        }
        catch
        {
            return false;
        }
    }

    internal static bool CanHandle(ConversionJob job)
    {
        if (job.Paths == null || job.Paths.Length == 0) return false;
        if (!job.Action.StartsWith("convert:")) return false;

        var targetExt = job.Action.Substring("convert:".Length).Trim().ToLowerInvariant();

        foreach (var path in job.Paths)
        {
            var fromExt = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (!CanHandlePair(fromExt, targetExt))
                return false;
        }

        return true;
    }

    public static bool CanHandlePair(string fromExt, string targetExt)
    {
        // 3D Engine
        if (Model3DEngine.CanHandle(fromExt, targetExt))
            return true;

        // CAD Engine
        if (Cad.CadEngine.CanHandle(fromExt, targetExt))
            return true;

        // Font Engine
        if (Fonts.FontEngine.CanHandle(fromExt, targetExt))
            return true;

        // Data Engine
        if (Data.DataEngine.CanHandle(fromExt, targetExt))
            return true;

        // Email Engine
        if (Email.EmailEngine.CanHandle(fromExt, targetExt))
            return true;

        // Ebook Engine
        if (Ebooks.EbookEngine.CanHandle(fromExt, targetExt))
            return true;

        // Subtitle Engine
        if (Subtitles.SubtitleEngine.CanHandle(fromExt, targetExt))
            return true;

        // Vector Tracing Engine
        if (Vector.Tracing.VectorTracingEngine.CanHandle(fromExt, targetExt))
            return true;

        // ICNS Engine
        if (Images.IcnsEngine.CanHandle(fromExt, targetExt))
            return true;

        // OOXML Engine
        if (Office.Ooxml.OoxmlEngine.CanConvert(fromExt, targetExt))
            return true;

        // Spreadsheet Engine
        if (Office.Sheets.SpreadsheetEngine.CanConvert(fromExt, targetExt))
            return true;

        // Docx Engine
        if (Documents.Docx.DocxEngine.CanConvert(fromExt, targetExt))
            return true;

        // Native Image to PDF and ICO
        if (IsRasterImage(fromExt) && (targetExt is "pdf" or "ico"))
            return true;

        // Native ZIP packaging
        if (targetExt == "zip")
            return true;

        // Route Planner (multi-hop graph)
        if (Planner.CanConvert(fromExt, targetExt))
            return true;

        return false;
    }

    private static readonly Routing.RoutePlanner Planner = InitRoutePlanner();

    private static Routing.RoutePlanner InitRoutePlanner()
    {
        var planner = new Routing.RoutePlanner();

        // 3D Engine
        foreach (var f in Model3DEngine.InputFormats)
            foreach (var t in Model3DEngine.Formats)
                if (!string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
                    planner.Register(f, t, 1, (src, dst) => { Model3DEngine.Convert(src, t, dst); return dst; });

        // CAD Engine
        planner.Register("dwg", "dxf", 1, (src, dst) => { Cad.CadEngine.Convert(src, "dxf", dst); return dst; });
        planner.Register("dxf", "dwg", 1, (src, dst) => { Cad.CadEngine.Convert(src, "dwg", dst); return dst; });
        foreach (var f in new[] { "dwg", "dxf" })
            foreach (var t in new[] { "pdf", "svg", "png" })
                planner.Register(f, t, 2, (src, dst) => { Cad.CadEngine.Convert(src, t, dst); return dst; });

        // Font Engine
        foreach (var f in Fonts.FontEngine.Formats)
            foreach (var t in Fonts.FontEngine.Formats)
                if (!string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
                    planner.Register(f, t, 1, (src, dst) => { Fonts.FontEngine.Convert(src, t, dst); return dst; });

        // Structured Data
        foreach (var f in Data.DataEngine.Formats)
            foreach (var t in Data.DataEngine.Formats)
                if (!string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
                    planner.Register(f, t, 1, (src, dst) => { Data.DataEngine.Convert(src, t, dst); return dst; });

        // Subtitles
        foreach (var f in Subtitles.SubtitleEngine.SupportedInputs)
            foreach (var t in Subtitles.SubtitleEngine.SupportedOutputs)
                if (!string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
                    planner.Register(f, t, 1, (src, dst) => { Subtitles.SubtitleEngine.Convert(src, dst, f, t); return dst; });

        // Vector Tracing
        foreach (var f in new[] { "png", "jpg", "jpeg", "bmp", "webp", "gif" })
            planner.Register(f, "svg", 2, (src, dst) => { Vector.Tracing.VectorTracingEngine.Convert(src, dst); return dst; });

        // ICNS
        foreach (var f in new[] { "png", "jpg", "bmp", "webp", "ico" })
        {
            planner.Register(f, "icns", 1, (src, dst) => { Images.IcnsEngine.Convert(src, dst, f, "icns"); return dst; });
            planner.Register("icns", f, 1, (src, dst) => { Images.IcnsEngine.Convert(src, dst, "icns", f); return dst; });
        }

        // OOXML
        foreach (var (f, t) in Office.Ooxml.OoxmlEngine.SupportedPairs)
            planner.Register(f, t, 1, (src, dst) => { Office.Ooxml.OoxmlEngine.Convert(src, t, dst); return dst; });

        // Spreadsheet
        foreach (var f in Office.Sheets.SpreadsheetEngine.SupportedInputs)
            foreach (var t in Office.Sheets.SpreadsheetEngine.SupportedOutputs)
                if (!string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
                    planner.Register(f, t, 1, (src, dst) => { Office.Sheets.SpreadsheetEngine.Convert(src, t, dst); return dst; });

        // Docx & Document Engine
        foreach (var f in Documents.DocumentReaders.SupportedFormats)
            foreach (var t in new[] { "docx", "md", "txt", "html" })
                if (!string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
                    planner.Register(f, t, 2, (src, dst) => { Documents.Docx.DocxEngine.Convert(src, t, dst); return dst; });

        // Ebooks
        foreach (var f in Ebooks.EbookEngine.SupportedInputs)
            foreach (var t in Ebooks.EbookEngine.SupportedOutputs)
                if (!string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
                    planner.Register(f, t, 2, (src, dst) => { Ebooks.EbookEngine.Convert(src, t, dst); return dst; });

        // Image to PDF
        foreach (var f in RasterImages)
            planner.Register(f, "pdf", 1, (src, dst) => { ConvertImageToPdf(src, dst); return dst; });

        // Image to ICO
        foreach (var f in RasterImages)
            planner.Register(f, "ico", 1, (src, dst) => {
                using var codec = SKCodec.Create(src);
                if (codec == null) throw new InvalidOperationException($"Failed to decode: {src}");
                using var bmp = SKBitmap.Decode(codec);
                using var stream = File.Create(dst);
                Images.IcnsEngine.SaveAsIco(bmp, stream);
                return dst;
            });

        return planner;

    }


    public static Task ExecuteRequestAsync(
        string requestPath,
        string responsePath,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(requestPath, responsePath, cancellationToken, null);

    internal static async Task ExecuteAsync(
        string requestPath,
        string responsePath,
        CancellationToken cancellationToken = default,
        IProgress<JobProgress>? progress = null)
    {
        var job = JsonSerializer.Deserialize<ConversionJob>(File.ReadAllText(requestPath), JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse request JSON");

        var targetExt = job.Action.Substring("convert:".Length).Trim().ToLowerInvariant();
        var targetDir = OutputFolder.For(job);
        var fileResults = new List<FileResult>();
        int total = job.Paths.Length;

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputPath = job.Paths[i];
            var currentFile = Path.GetFileName(inputPath);

            Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile));

            try
            {
                var fromExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
                string resolvedOutput = OutputPathHelper.Resolve(inputPath, targetExt, targetDir);

                if (Model3DEngine.CanHandle(fromExt, targetExt))
                {
                    var outputs = await Model3DEngine.ConvertAsync(
                        inputPath,
                        targetExt,
                        resolvedOutput,
                        new Progress<double>(_ =>
                        {
                            Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile));
                        }),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, outputs.Count > 0 ? outputs[0] : resolvedOutput, null));
                }
                else if (Cad.CadEngine.CanHandle(fromExt, targetExt))
                {
                    var outputs = await Cad.CadEngine.ConvertAsync(
                        inputPath,
                        targetExt,
                        resolvedOutput,
                        new Progress<double>(_ =>
                        {
                            Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile));
                        }),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, outputs.Count > 0 ? outputs[0] : resolvedOutput, null));
                }
                else if (Fonts.FontEngine.CanHandle(fromExt, targetExt))
                {
                    var outputs = await Fonts.FontEngine.ConvertAsync(
                        inputPath,
                        targetExt,
                        resolvedOutput,
                        new Progress<double>(_ =>
                        {
                            Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile));
                        }),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, outputs.Count > 0 ? outputs[0] : resolvedOutput, null));
                }
                else if (Data.DataEngine.CanHandle(fromExt, targetExt))
                {
                    var outputs = await Data.DataEngine.ConvertAsync(
                        inputPath,
                        targetExt,
                        resolvedOutput,
                        new Progress<double>(_ =>
                        {
                            Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile));
                        }),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, outputs.Count > 0 ? outputs[0] : resolvedOutput, null));
                }
                else if (Email.EmailEngine.CanHandle(fromExt, targetExt))
                {
                    var outputs = await Email.EmailEngine.ConvertAsync(
                        inputPath,
                        targetExt,
                        resolvedOutput,
                        new Progress<double>(_ =>
                        {
                            Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile));
                        }),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, outputs.Count > 0 ? outputs[0] : resolvedOutput, null));
                }
                else if (Ebooks.EbookEngine.CanHandle(fromExt, targetExt))
                {
                    var outputs = await Ebooks.EbookEngine.ConvertAsync(
                        inputPath,
                        targetExt,
                        resolvedOutput,
                        new Progress<double>(_ =>
                        {
                            Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile));
                        }),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, outputs.Count > 0 ? outputs[0] : resolvedOutput, null));
                }
                else if (Subtitles.SubtitleEngine.CanHandle(fromExt, targetExt))
                {
                    await Subtitles.SubtitleEngine.ConvertAsync(
                        inputPath,
                        resolvedOutput,
                        fromExt,
                        targetExt,
                        _ => Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile)),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (Vector.Tracing.VectorTracingEngine.CanHandle(fromExt, targetExt))
                {
                    await Vector.Tracing.VectorTracingEngine.ConvertAsync(
                        inputPath,
                        resolvedOutput,
                        _ => Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile)),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (Images.IcnsEngine.CanHandle(fromExt, targetExt))
                {
                    await Images.IcnsEngine.ConvertAsync(
                        inputPath,
                        resolvedOutput,
                        fromExt,
                        targetExt,
                        _ => Report(progress, responsePath, new JobProgress(L.T("正在转换…"), i, total, currentFile)),
                        cancellationToken);

                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (Office.Ooxml.OoxmlEngine.CanConvert(fromExt, targetExt))
                {
                    Office.Ooxml.OoxmlEngine.Convert(inputPath, targetExt, resolvedOutput);
                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (Office.Sheets.SpreadsheetEngine.CanConvert(fromExt, targetExt))
                {
                    Office.Sheets.SpreadsheetEngine.Convert(inputPath, targetExt, resolvedOutput);
                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (Documents.Docx.DocxEngine.CanConvert(fromExt, targetExt))
                {
                    Documents.Docx.DocxEngine.Convert(inputPath, targetExt, resolvedOutput);
                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (IsRasterImage(fromExt) && targetExt == "pdf")
                {
                    ConvertImageToPdf(inputPath, resolvedOutput);
                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (IsRasterImage(fromExt) && targetExt == "ico")
                {
                    using var codec = SKCodec.Create(inputPath);
                    if (codec == null) throw new InvalidOperationException($"Failed to decode: {inputPath}");
                    using var bmp = SKBitmap.Decode(codec);
                    using var outStream = File.Create(resolvedOutput);
                    Images.IcnsEngine.SaveAsIco(bmp, outStream);
                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (targetExt == "zip")
                {
                    CreateZipArchive(inputPath, resolvedOutput);
                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else if (Planner.CanConvert(fromExt, targetExt))
                {
                    Planner.Execute(inputPath, targetExt, resolvedOutput);
                    fileResults.Add(new FileResult(inputPath, resolvedOutput, null));
                }
                else
                {
                    fileResults.Add(new FileResult(inputPath, null, $"Unsupported native conversion: {fromExt} -> {targetExt}"));
                }
            }
            catch (Exception ex)
            {
                fileResults.Add(new FileResult(inputPath, null, ex.Message));
            }
        }

        Report(progress, responsePath, new JobProgress(L.T("处理完成"), total, total, null));
        var progressFile = responsePath + ".progress";
        try { if (File.Exists(progressFile)) File.Delete(progressFile); } catch { }

        var batchResult = new BatchResult(fileResults.ToArray());
        var responseDir = Path.GetDirectoryName(responsePath);
        if (!string.IsNullOrEmpty(responseDir) && !Directory.Exists(responseDir))
            Directory.CreateDirectory(responseDir);

        await File.WriteAllTextAsync(responsePath, JsonSerializer.Serialize(batchResult), cancellationToken);
    }

    private static void Report(IProgress<JobProgress>? progress, string responsePath, JobProgress p)
    {
        progress?.Report(p);
        try
        {
            File.WriteAllText(responsePath + ".progress", JsonSerializer.Serialize(p));
        }
        catch { }
    }


    private static void ConvertImageToPdf(string inputPath, string outputPath)
    {
        using var codec = SKCodec.Create(inputPath);
        if (codec == null)
            throw new InvalidOperationException($"Failed to decode image: {inputPath}");

        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap == null)
            throw new InvalidOperationException($"Failed to load bitmap: {inputPath}");

        using var outputStream = File.Create(outputPath);
        using var pdfDoc = SKDocument.CreatePdf(outputStream);
        using var canvas = pdfDoc.BeginPage(bitmap.Width, bitmap.Height);
        canvas.DrawBitmap(bitmap, 0, 0);
        pdfDoc.EndPage();
        pdfDoc.Close();
    }

    private static void CreateZipArchive(string inputPath, string outputPath)
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        if (Directory.Exists(inputPath))
        {
            var dirInfo = new DirectoryInfo(inputPath);
            foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(inputPath, file.FullName);
                zip.CreateEntryFromFile(file.FullName, relPath, CompressionLevel.Optimal);
            }
        }
        else if (File.Exists(inputPath))
        {
            zip.CreateEntryFromFile(inputPath, Path.GetFileName(inputPath), CompressionLevel.Optimal);
        }
        else
        {
            throw new FileNotFoundException($"Input path not found: {inputPath}");
        }
    }
}

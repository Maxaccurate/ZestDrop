using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZestDrop.Engines;

namespace ZestDrop.Cli;

public static class CliRunner
{
    public const int Success = 0;
    public const int SomeFailed = 1;
    public const int UsageError = 2;
    public const int NothingToDo = 3;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter errors,
        CancellationToken cancellationToken)
    {
        var options = CliOptions.Parse(args);
        if (options.Error != null)
        {
            errors.WriteLine($"zestdrop: {options.Error}");
            errors.WriteLine("Run 'zestdrop help' for usage.");
            return UsageError;
        }

        switch (options.Command)
        {
            case "help":
                output.WriteLine(CliHelp.Text(options.Inputs.FirstOrDefault()));
                return Success;

            case "version":
                output.WriteLine($"zestdrop {CliHelp.Version}");
                return Success;

            case "formats":
                return ListFormats(options, output);

            case "convert":
                return await ConvertAsync(options, output, errors, cancellationToken);

            case "watch":
                return await WatchAsync(options, output, errors, cancellationToken);

            default:
                errors.WriteLine($"Unknown command: {options.Command}");
                return UsageError;
        }
    }

    private static async Task<int> ConvertAsync(
        CliOptions options,
        TextWriter output,
        TextWriter errors,
        CancellationToken cancellationToken)
    {
        var targetExt = options.TargetFormat!.TrimStart('.').ToLowerInvariant();
        var files = ExpandInputs(options.Inputs, options.Recursive, errors);
        if (files.Count == 0)
        {
            errors.WriteLine("zestdrop: No matching files found to convert.");
            return NothingToDo;
        }

        string tempFolder = Path.Combine(Path.GetTempPath(), "zestdrop-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string requestPath = Path.Combine(tempFolder, "request.json");
        string responsePath = Path.Combine(tempFolder, "response.json");

        try
        {
            if (!string.IsNullOrEmpty(options.OutputFolder))
            {
                Directory.CreateDirectory(Path.GetFullPath(options.OutputFolder));
            }

            var parameters = !string.IsNullOrEmpty(options.OutputFolder)
                ? new Dictionary<string, string> { ["_outputDir"] = Path.GetFullPath(options.OutputFolder) }
                : null;
            var job = new ConversionJob(files.ToArray(), "convert:" + targetExt, parameters);
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(job), cancellationToken);

            var sw = Stopwatch.StartNew();
            if (!options.Quiet && !options.Json)
            {
                output.WriteLine($"Converting {files.Count} file(s) to .{targetExt}...");
            }

            if (NativeEngines.CanHandle(job))
            {
                await NativeEngines.ExecuteRequestAsync(requestPath, responsePath, cancellationToken);
            }
            else
            {
                await Backend.Run(["--job", requestPath, responsePath]);
            }
            sw.Stop();

            if (!File.Exists(responsePath))
            {
                errors.WriteLine("zestdrop: Internal error - engine did not produce a response.");
                return SomeFailed;
            }

            var result = JsonSerializer.Deserialize<BatchResult>(await File.ReadAllTextAsync(responsePath, cancellationToken))!;
            int done = result.Files.Count(f => f.Error == null);
            int failed = result.Files.Count(f => f.Error != null);

            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!options.Quiet)
            {
                for (int i = 0; i < result.Files.Length; i++)
                {
                    var file = result.Files[i];
                    var name = Path.GetFileName(file.Input);
                    if (file.Error == null)
                    {
                        var outName = Path.GetFileName(file.Output);
                        output.WriteLine($"  [{i + 1}/{result.Files.Length}] {name} -> {outName} (Success)");
                    }
                    else
                    {
                        output.WriteLine($"  [{i + 1}/{result.Files.Length}] {name} -> FAILED: {file.Error}");
                    }
                }

                output.WriteLine(failed == 0
                    ? $"Completed {done} of {result.Files.Length} file(s) in {sw.ElapsedMilliseconds} ms."
                    : $"Completed {done} of {result.Files.Length} file(s), {failed} failed in {sw.ElapsedMilliseconds} ms.");
            }

            return failed == 0 ? Success : SomeFailed;
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch { }
        }
    }

    private static async Task<int> WatchAsync(
        CliOptions options,
        TextWriter output,
        TextWriter errors,
        CancellationToken cancellationToken)
    {
        string folder = Path.GetFullPath(options.Inputs[0]);
        if (!Directory.Exists(folder))
        {
            errors.WriteLine($"zestdrop watch: Folder not found: {folder}");
            return UsageError;
        }

        string targetExt = options.TargetFormat!.TrimStart('.').ToLowerInvariant();
        string outputFolder = !string.IsNullOrEmpty(options.OutputFolder)
            ? Path.GetFullPath(options.OutputFolder)
            : Path.Combine(folder, "converted");
        string origDir = Path.Combine(folder, "originals");

        if (!options.Quiet)
        {
            output.WriteLine($"Watching: {folder}");
            output.WriteLine($"Target:   .{targetExt}");
            output.WriteLine($"Output:   {outputFolder}");
            output.WriteLine($"Subdirs:  {(options.Subfolders ? "Yes" : "No")}");
            output.WriteLine($"Originals:{(options.MoveOriginals ? "Move to 'originals'" : "Keep in place")}");
            output.WriteLine("Press Ctrl+C to stop watching.\n");
        }

        using var watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = options.Subfolders,
            EnableRaisingEvents = true
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task ProcessFileAsync(string file)
        {
            if (Directory.Exists(file) || IsDescendantOf(file, origDir) || IsDescendantOf(file, outputFolder))
                return;

            lock (seen)
            {
                if (!seen.Add(file)) return;
            }
            try
            {
                await Task.Delay(600, cancellationToken);
                if (!File.Exists(file)) return;
                if (IsDescendantOf(file, origDir) || IsDescendantOf(file, outputFolder)) return;

                var fromExt = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                if (fromExt == targetExt) return;

                if (!options.Quiet)
                    output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Processing: {Path.GetFileName(file)}");

                Directory.CreateDirectory(outputFolder);
                string resolvedOutput = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file) + "." + targetExt);

                var job = new ConversionJob([file], "convert:" + targetExt, new Dictionary<string, string> { ["_outputDir"] = outputFolder });
                string req = Path.Combine(Path.GetTempPath(), $"zestdrop-watch-req-{Guid.NewGuid():N}.json");
                string res = Path.Combine(Path.GetTempPath(), $"zestdrop-watch-res-{Guid.NewGuid():N}.json");
                bool success = false;
                string? errorMsg = null;
                try
                {
                    await File.WriteAllTextAsync(req, JsonSerializer.Serialize(job), cancellationToken);
                    if (NativeEngines.CanHandle(job))
                        await NativeEngines.ExecuteRequestAsync(req, res, cancellationToken);
                    else
                        await Backend.Run(["--job", req, res]);

                    if (File.Exists(res))
                    {
                        var json = await File.ReadAllTextAsync(res, cancellationToken);
                        var batchRes = JsonSerializer.Deserialize<BatchResult>(json);
                        if (batchRes?.Files != null && batchRes.Files.Length > 0)
                        {
                            var fileRes = batchRes.Files[0];
                            if (!string.IsNullOrEmpty(fileRes.Error))
                            {
                                errorMsg = fileRes.Error;
                            }
                            else if (!string.IsNullOrEmpty(fileRes.Output) && File.Exists(fileRes.Output))
                            {
                                success = true;
                                resolvedOutput = fileRes.Output;
                            }
                        }
                    }

                    if (!success && errorMsg == null && File.Exists(resolvedOutput))
                    {
                        success = true;
                    }
                }
                finally
                {
                    try { File.Delete(req); } catch { }
                    try { File.Delete(res); } catch { }
                }

                if (!success)
                {
                    if (!options.Quiet)
                        output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed {Path.GetFileName(file)}" + (errorMsg != null ? $": {errorMsg}" : ""));
                    return;
                }

                if (options.MoveOriginals)
                {
                    Directory.CreateDirectory(origDir);
                    string dest = Path.Combine(origDir, Path.GetFileName(file));
                    dest = Engines.OutputPathHelper.GetUniqueFilePath(dest);
                    File.Move(file, dest);
                }

                if (!options.Quiet)
                    output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Done: {Path.GetFileName(file)} -> {Path.GetFileName(resolvedOutput)}");
            }
            catch (Exception ex)
            {
                if (!options.Quiet)
                    output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed {Path.GetFileName(file)}: {ex.Message}");
            }
            finally
            {
                lock (seen) { seen.Remove(file); }
            }
        }

        watcher.Created += (_, e) =>
        {
            if (!IsDescendantOf(e.FullPath, origDir) && !IsDescendantOf(e.FullPath, outputFolder))
                _ = ProcessFileAsync(e.FullPath);
        };
        watcher.Renamed += (_, e) =>
        {
            if (!IsDescendantOf(e.FullPath, origDir) && !IsDescendantOf(e.FullPath, outputFolder))
                _ = ProcessFileAsync(e.FullPath);
        };

        // Process any files already present in the watched directory
        var searchOption = options.Subfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        try
        {
            foreach (var existingFile in Directory.GetFiles(folder, "*", searchOption))
            {
                if (!IsDescendantOf(existingFile, origDir) && !IsDescendantOf(existingFile, outputFolder))
                    _ = ProcessFileAsync(existingFile);
            }
        }
        catch { }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!options.Quiet)
            {
                output.WriteLine("\nStopping watcher...");
            }
        }

        return Success;
    }

    internal static bool IsDescendantOf(string fullPath, string directoryPath)
    {
        var normalizedDir = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase);
    }

    private static int ListFormats(CliOptions options, TextWriter output)
    {
        var categories = new Dictionary<string, string[]>
        {
            ["3D Objects"] = ["glb", "gltf", "stl", "obj", "3mf", "ply", "fbx"],
            ["CAD"] = ["dwg", "dxf", "pdf", "svg"],
            ["Fonts"] = ["ttf", "otf", "woff", "woff2", "eot"],
            ["Structured Data"] = ["json", "jsonl", "yaml", "toml", "xml", "csv", "tsv", "vcf"],
            ["Office & Documents"] = ["docx", "xlsx", "xls", "ods", "pptx", "md", "txt", "html", "xhtml"],
            ["E-Books"] = ["epub", "kepub", "mobi", "azw3", "fb2", "cbz"],
            ["Email"] = ["eml", "msg"],
            ["Subtitles"] = ["srt", "vtt", "ass", "smi", "lrc", "ttml", "sub", "scc"],
            ["Vector & Icons"] = ["svg", "icns", "ico"],
            ["Images"] = ["png", "jpg", "jpeg", "webp", "bmp", "gif", "avif", "tiff", "tif"],
            ["Audio & Video"] = ["mp4", "mkv", "mov", "avi", "webm", "mp3", "wav", "flac", "aac", "ogg", "m4a"]
        };

        if (!string.IsNullOrEmpty(options.Category))
        {
            var match = categories.FirstOrDefault(c => c.Key.Contains(options.Category, StringComparison.OrdinalIgnoreCase));
            if (match.Key == null)
            {
                output.WriteLine($"No category matches '{options.Category}'. Available: {string.Join(", ", categories.Keys)}");
                return UsageError;
            }
            categories = new Dictionary<string, string[]> { [match.Key] = match.Value };
        }

        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(categories, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            output.WriteLine("Supported Format Categories in ZestDrop:\n");
            foreach (var kvp in categories)
            {
                output.WriteLine($"  {kvp.Key}:");
                output.WriteLine($"    {string.Join(", ", kvp.Value)}");
                output.WriteLine();
            }
        }

        return Success;
    }

    private static List<string> ExpandInputs(List<string> inputs, bool recursive, TextWriter errors)
    {
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (File.Exists(input))
            {
                files.Add(Path.GetFullPath(input));
            }
            else if (Directory.Exists(input))
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                files.AddRange(Directory.GetFiles(input, "*.*", option));
            }
            else if (input.Contains('*') || input.Contains('?'))
            {
                var dir = Path.GetDirectoryName(input);
                var pattern = Path.GetFileName(input);
                if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
                if (Directory.Exists(dir))
                {
                    files.AddRange(Directory.GetFiles(dir, pattern));
                }
                else
                {
                    errors.WriteLine($"Directory not found for pattern: {input}");
                }
            }
            else
            {
                errors.WriteLine($"File or directory not found: {input}");
            }
        }
        return files;
    }
}

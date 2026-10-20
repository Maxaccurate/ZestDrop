using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZestDrop;

// One watched folder: new files matching filter/kind are processed with this action,
// and results go to custom output folder or sub-folder of watched one.
internal sealed class WatchRule
{
    public string Folder { get; set; } = "";
    public string Kind { get; set; } = "image";
    public string Action { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = false;
    public string? OutputFolder { get; set; } = null;
    public bool MoveToOriginals { get; set; } = false;
    public string Filter { get; set; } = "";

    public WatchRule() { }
    public WatchRule(string folder, string kind, string action, bool includeSubfolders = false, string? outputFolder = null, bool moveToOriginals = false, string filter = "")
    {
        Folder = folder;
        Kind = kind;
        Action = action;
        IncludeSubfolders = includeSubfolders;
        OutputFolder = outputFolder;
        MoveToOriginals = moveToOriginals;
        Filter = filter;
    }
}

internal sealed class WatchFolders : IDisposable
{
    private const string Key = "watchRules";
    private readonly Action<ConversionJob> enqueue;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    public WatchFolders(Action<ConversionJob> enqueue) => this.enqueue = enqueue;

    public static string OutputName => L.T("ZestDrop 输出");

    public static List<WatchRule> Load()
    {
        try
        { return JsonSerializer.Deserialize<List<WatchRule>>(Settings.GetString(Key) ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }

    public static void Save(List<WatchRule> rules) => Settings.Set(Key, JsonSerializer.Serialize(rules));

    public void Reload()
    {
        foreach (var watcher in watchers)
            watcher.Dispose();
        watchers.Clear();
        foreach (var rule in Load().Where(r => Directory.Exists(r.Folder)))
        {
            var watcher = new FileSystemWatcher(rule.Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = rule.IncludeSubfolders,
                EnableRaisingEvents = true
            };
            watcher.Created += (_, e) => _ = Guarded(rule, e.FullPath);
            watcher.Renamed += (_, e) => _ = Guarded(rule, e.FullPath);
            watchers.Add(watcher);
        }
    }

    private async Task Guarded(WatchRule rule, string path)
    {
        lock (seen)
            if (!seen.Add(path))
                return;
        try
        { await Handle(rule, path); }
        catch (Exception ex) { Journal.Write("WatchFailed", new { rule.Folder, ex.Message }); }
        finally { lock (seen) seen.Remove(path); }
    }

    private async Task Handle(WatchRule rule, string path)
    {
        string name = Path.GetFileName(path);
        if (name.StartsWith(".zestdrop-") || name.StartsWith("~$") || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return;

        string outputDir = !string.IsNullOrWhiteSpace(rule.OutputFolder) && Directory.Exists(rule.OutputFolder)
            ? rule.OutputFolder
            : Path.Combine(rule.Folder, OutputName);

        string originalsDir = Path.Combine(rule.Folder, "originals");

        // Ignore files inside output directory or originals directory
        if (path.StartsWith(outputDir, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(originalsDir, StringComparison.OrdinalIgnoreCase))
            return;

        if (Directory.Exists(path))
            return;

        if (rule.Kind != "all" && Catalog.Category(path) != rule.Kind)
            return;

        if (!string.IsNullOrWhiteSpace(rule.Filter))
        {
            var patterns = rule.Filter.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool matched = false;
            foreach (var pattern in patterns)
            {
                if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return;
        }

        // Wait until file is done copying / downloading
        long last = -1;
        for (int attempt = 0; attempt < 600; attempt++)
        {
            await Task.Delay(1500).ConfigureAwait(false);
            try
            {
                if (!File.Exists(path))
                    return;
                long size = new FileInfo(path).Length;
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (size == last && size > 0)
                    break;
                last = size;
            }
            catch (IOException) { last = -1; }
        }

        try
        { Directory.CreateDirectory(outputDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        Dictionary<string, string> values = rule.Action.StartsWith("convert:")
            ? []
            : Catalog.Definition(rule.Action, rule.Kind == "all" ? Catalog.Category(path) : rule.Kind).Fields?.ToDictionary(f => f.Name, f => f.Default) ?? [];

        if (Presets.Last(rule.Action) is { } remembered)
            foreach (var (field, value) in remembered)
                if (values.ContainsKey(field))
                    values[field] = value;

        values["_outputDir"] = outputDir;
        if (rule.MoveToOriginals)
        {
            values["_moveToOriginals"] = "true";
            values["_originalsDir"] = originalsDir;
        }

        enqueue(new ConversionJob([path], rule.Action, values));
    }

    public static List<(string Action, string Label)> Actions(string kind)
    {
        return kind switch
        {
            "model3d" => [
                ("convert:glb", L.T("转换为 GLB (通用 3D)")),
                ("convert:obj", L.T("转换为 OBJ (带材质)")),
                ("convert:stl", L.T("转换为 STL (3D 打印)")),
                ("convert:3mf", L.T("转换为 3MF (制造/切片)")),
                ("convert:ply", L.T("转换为 PLY")),
                ("convert:png", L.T("渲染等轴测预览图 (PNG)"))
            ],
            "cad" => [
                ("convert:pdf", L.T("导出矢量 PDF")),
                ("convert:svg", L.T("导出矢量 SVG")),
                ("convert:png", L.T("渲染高清图片 (PNG)")),
                ("convert:dwg", L.T("转换为 DWG (AutoCAD 2018)")),
                ("convert:dxf", L.T("转换为 DXF (通用 CAD)"))
            ],
            "document" => [
                ("convert:pdf", L.T("转换为 PDF")),
                ("convert:docx", L.T("转换为 Word (DOCX)")),
                ("convert:txt", L.T("提取纯文本 (TXT)")),
                ("compress", L.T("压缩 PDF（沿用上次设置）"))
            ],
            "image" => [
                ("convert:jpg", L.T("转换为 JPG")),
                ("convert:png", L.T("转换为 PNG")),
                ("convert:webp", L.T("转换为 WebP")),
                ("convert:pdf", L.T("合并/导出为 PDF")),
                ("makeIcon", L.T("制作图标 (ICO)")),
                ("compress", L.T("压缩（沿用上次设置）")),
                ("removeMetadata", L.T("清除元数据 / EXIF"))
            ],
            "video" => [
                ("convert:mp4", L.T("转换为 MP4")),
                ("convert:webm", L.T("转换为 WebM")),
                ("convert:gif", L.T("转换为 GIF")),
                ("convert:mp3", L.T("提取音频 (MP3)")),
                ("muteVideo", L.T("静音 / 移除音轨")),
                ("compress", L.T("压缩（沿用上次设置）"))
            ],
            "audio" => [
                ("convert:mp3", L.T("转换为 MP3")),
                ("convert:m4a", L.T("转换为 M4A / AAC")),
                ("convert:wav", L.T("转换为 WAV")),
                ("convert:flac", L.T("转换为 FLAC (无损)")),
                ("normalizeAudio", L.T("标准化响度 (LUFS)")),
                ("compress", L.T("压缩（沿用上次设置）"))
            ],
            _ => [
                ("packArchive", L.T("打包为 ZIP 压缩包"))
            ]
        };
    }

    public void Dispose()
    {
        foreach (var watcher in watchers)
            watcher.Dispose();
        watchers.Clear();
    }
}

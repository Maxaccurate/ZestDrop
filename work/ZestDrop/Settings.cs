using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZestDrop;

// Small per-user settings file (%LOCALAPPDATA%\ZestDrop\settings.json). Each setting is one key; writing one key keeps the others.
internal static class Settings
{
    // Tests point this at a temporary file so they never touch the user's real settings.
    public static string? PathOverride
    {
        get => pathOverride;
        set { lock (Sync) { pathOverride = value; cache = null; cacheStamp = default; cacheLength = -1; } }
    }
    private static string? pathOverride;

    private static readonly object Sync = new();
    // Some settings are read many times a second (the wheel checks pinWheel on every poll), so the
    // parsed file is kept in memory and only read again after it changed on disk.
    private static JsonObject? cache;
    private static DateTime cacheStamp;
    private static long cacheLength = -1;

    private static string FilePath => PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZestDrop", "settings.json");

    public static string? GetString(string key)
    {
        lock (Sync)
            return Read()[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    public static bool GetBool(string key, bool fallback)
    {
        lock (Sync)
            return Read()[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
    }

    public static void Set(string key, string value) => Write(key, JsonValue.Create(value));

    public static void Set(string key, bool value) => Write(key, JsonValue.Create(value));

    private static JsonObject Read()
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists)
            {
                cache = new JsonObject();
                cacheStamp = default;
                cacheLength = -1;
                return cache;
            }
            DateTime stamp = info.LastWriteTimeUtc;
            long length = info.Length;
            if (cache != null && stamp == cacheStamp && length == cacheLength)
                return cache;
            cache = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject();
            cacheStamp = stamp;
            cacheLength = length;
            return cache;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            cache = new JsonObject();
            cacheStamp = default;
            cacheLength = -1;
            return cache;
        }
    }

    private static void Write(string key, JsonNode? value)
    {
        lock (Sync)
        {
            try
            {
                var settings = Read();
                settings[key] = value;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Write a temporary file first so a crash can never leave a half-written settings file.
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, settings.ToJsonString());
                File.Move(temp, FilePath, true);
                var info = new FileInfo(FilePath);
                cache = settings;
                cacheStamp = info.LastWriteTimeUtc;
                cacheLength = info.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

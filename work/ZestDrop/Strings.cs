using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZestDrop;

// Interface language. Source strings are written in Chinese and double as lookup keys:
// L.T("压缩") is "Compress" in English. L.F formats keys with {0}-style placeholders.
internal static partial class L
{
    private static string? language;

    public static event Action? Changed;

// "ar", "en", or "zh". Defaults to Arabic on Arabic Windows, Chinese on Chinese Windows, and English elsewhere.
    public static string Language
    {
        // ZESTDROP_LANG (set for worker processes) wins, so child processes such as the Office bridge match the app.
        get => language ??= Override() ?? Load() ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ar" => "ar",
            "zh" => "zh",
            _ => "en"
        });
        set
        {
            string next = value is "ar" or "zh" ? value : "en";
            if (next == Language)
                return;
            language = next;
            Settings.Set("language", next);
            Changed?.Invoke();
        }
    }

    public static bool English => Language == "en";
    public static bool Arabic => Language == "ar";

    public static string T(string chinese)
    {
        if (Arabic && Ar.TryGetValue(chinese, out var arabic))
            return arabic;
        if (English && En.TryGetValue(chinese, out var english))
            return english;
        return chinese;
    }

    public static string F(string chinese, params object?[] values) => string.Format(CultureInfo.InvariantCulture, T(chinese), values);

    // For tests and tools that must not touch the user's saved choice.
    public static void UseForSession(string value) => language = value is "ar" or "zh" ? value : "en";

    private static string? Override() => Environment.GetEnvironmentVariable("ZESTDROP_LANG") is "ar" or "zh" or "en" ? Environment.GetEnvironmentVariable("ZESTDROP_LANG") : null;

    private static string? Load() => Settings.GetString("language") is "ar" or "zh" or "en" ? Settings.GetString("language") : null;

    public static IReadOnlyDictionary<string, string> Translations => Arabic ? Ar : En;
}

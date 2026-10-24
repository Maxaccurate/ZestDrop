using System;
using System.IO;

namespace ZestDrop.Engines;

internal static class OutputPathHelper
{
    public static string ResolveWorkDir(string inputPath, string? chosenOutputDir = null)
    {
        if (!string.IsNullOrWhiteSpace(chosenOutputDir))
        {
            var full = Path.GetFullPath(chosenOutputDir);
            if (Directory.Exists(full))
                return full;
            try
            {
                if (!Path.HasExtension(full) || chosenOutputDir.EndsWith(Path.DirectorySeparatorChar) || chosenOutputDir.EndsWith(Path.AltDirectorySeparatorChar))
                {
                    Directory.CreateDirectory(full);
                    return full;
                }
            }
            catch { }
        }
        return Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
    }

    public static string Resolve(string inputPath, string targetExtension, string? chosenOutputDir = null) =>
        GetUniquePath(inputPath, targetExtension, chosenOutputDir);

    public static string GetUniquePath(string inputPath, string targetExtension, string? chosenOutputDir = null)
    {
        var parent = ResolveWorkDir(inputPath, chosenOutputDir);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var ext = targetExtension.TrimStart('.').ToLowerInvariant();

        var target = Path.Combine(parent, $"{baseName}.{ext}");
        if (!File.Exists(target))
            return target;

        for (int i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(parent, $"{baseName}-{i}.{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(parent, $"{baseName}-{Guid.NewGuid():N}.{ext}");
    }

    public static string GetUniqueFilePath(string destinationFilePath, IEnumerable<string>? reservedPaths = null)
    {
        var reserved = reservedPaths != null
            ? new HashSet<string>(reservedPaths, StringComparer.OrdinalIgnoreCase)
            : null;

        bool IsOccupied(string p) => File.Exists(p) || (reserved != null && reserved.Contains(p));

        if (!IsOccupied(destinationFilePath))
            return destinationFilePath;

        var dir = Path.GetDirectoryName(destinationFilePath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(destinationFilePath);
        var ext = Path.GetExtension(destinationFilePath);

        for (int i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{baseName}-{i}{ext}");
            if (!IsOccupied(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{baseName}-{Guid.NewGuid():N}{ext}");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace ZestDrop.Engines.Models3D;

public static class Model3DEngine
{
    public static readonly string[] Formats = ["glb", "gltf", "stl", "obj", "3mf", "ply"];
    public static readonly string[] InputFormats = ["glb", "gltf", "stl", "obj", "3mf", "ply", "fbx"];
    public static readonly string[] ImageFormats = ["png", "jpg", "jpeg", "webp"];

    public static bool CanHandle(string fromExt, string toExt)
    {
        fromExt = fromExt.TrimStart('.').ToLowerInvariant();
        toExt = toExt.TrimStart('.').ToLowerInvariant();

        if (!InputFormats.Contains(fromExt)) return false;
        return Formats.Contains(toExt) || ImageFormats.Contains(toExt);
    }

    public static Task<IReadOnlyList<string>> ConvertAsync(
        string inputPath,
        string targetFormat,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
            targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();

            var readingProgress = progress != null ? new SubProgress(progress, 0, 0.6) : null;
            var model = Read(inputPath, ext, readingProgress, cancellationToken);
            progress?.Report(0.6);

            var outputs = new List<string>();
            try
            {
                Write(model, targetFormat, outputPath, outputs, cancellationToken);
            }
            catch
            {
                foreach (var outf in outputs)
                {
                    try { if (File.Exists(outf)) File.Delete(outf); } catch { }
                }
                throw;
            }

            progress?.Report(1.0);
            return (IReadOnlyList<string>)outputs;
        }, cancellationToken);
    }

    public static void Convert(string inputPath, string targetFormat, string outputPath)
    {
        var ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var model = Read(inputPath, ext, null, CancellationToken.None);
        var outputs = new List<string>();
        Write(model, targetFormat.TrimStart('.').ToLowerInvariant(), outputPath, outputs, CancellationToken.None);
    }

    internal static Model3D Read(string path, string format, IProgress<double>? progress, CancellationToken cancellationToken)

    {
        var model = format switch
        {
            "stl" => StlFormat.Read(path, progress, cancellationToken),
            "obj" => ObjFormat.Read(path, progress, cancellationToken),
            "ply" => PlyFormat.Read(path, progress, cancellationToken),
            "3mf" => ThreeMfFormat.Read(path, progress, cancellationToken),
            "glb" or "gltf" => GltfFormat.Read(path, progress, cancellationToken),
            "fbx" => FbxFormat.Read(path, progress, cancellationToken),
            _ => throw new NotSupportedException($"Unknown 3D format '{format}'."),
        };
        model.Validate();
        return model;
    }

    internal static void Write(Model3D model, string format, string outputPath, List<string> outputs, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (format == "obj")
        {
            WriteObj(model, outputPath, outputs, cancellationToken);
            return;
        }

        outputs.Add(outputPath);

        if (ImageFormats.Contains(format))
        {
            WriteImage(model, format, outputPath, cancellationToken);
            return;
        }

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        switch (format)
        {
            case "stl":
                StlFormat.Write(model, stream, cancellationToken);
                break;
            case "ply":
                PlyFormat.Write(model, stream, cancellationToken);
                break;
            case "3mf":
                ThreeMfFormat.Write(model, stream, cancellationToken);
                break;
            case "glb":
                GltfFormat.Write(model, stream, binary: true, cancellationToken);
                break;
            case "gltf":
                GltfFormat.Write(model, stream, binary: false, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unsupported 3D target format '{format}'.");
        }
    }

    private static void WriteObj(Model3D model, string objPath, List<string> outputs, CancellationToken cancellationToken)
    {
        outputs.Add(objPath);
        string? mtlPath = null;
        var textures = new Dictionary<Texture3D, string>();
        var baseDir = Path.GetDirectoryName(objPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(objPath);

        if (model.Materials.Count > 0)
        {
            var desiredMtl = Path.Combine(baseDir, baseName + ".mtl");
            mtlPath = OutputPathHelper.GetUniqueFilePath(desiredMtl, outputs);
            outputs.Add(mtlPath);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var texture in model.Materials.Select(m => m.BaseColorTexture).OfType<Texture3D>().Distinct())
            {
                var name = SafeName(texture.Name);
                for (var i = 2; !names.Add(name); i++)
                    name = $"{SafeName(texture.Name)}_{i}";
                var ext = !string.IsNullOrEmpty(texture.Extension) ? texture.Extension : "png";
                var desiredTexture = Path.Combine(baseDir, $"{baseName}_{name}.{ext}");
                var textureFile = OutputPathHelper.GetUniqueFilePath(desiredTexture, outputs);
                outputs.Add(textureFile);
                File.WriteAllBytes(textureFile, texture.Data);
                textures[texture] = textureFile;
            }
        }

        ObjFormat.Write(model, objPath, mtlPath, textures, cancellationToken);
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string([.. name.Where(c => !invalid.Contains(c) && !char.IsWhiteSpace(c))]);
        return clean.Length == 0 ? "texture" : clean.Length > 40 ? clean[..40] : clean;
    }

    private static void WriteImage(Model3D model, string format, string outputPath, CancellationToken cancellationToken)
    {
        var pngBytes = MeshRenderer.RenderPng(model, 1024, cancellationToken);
        if (format == "png")
        {
            File.WriteAllBytes(outputPath, pngBytes);
            return;
        }

        using var bitmap = SKBitmap.Decode(pngBytes);
        var encodeFormat = format switch
        {
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(encodeFormat, 90);
        using var fs = File.OpenWrite(outputPath);
        data.SaveTo(fs);
    }

    private sealed class SubProgress(IProgress<double> parent, double start, double length) : IProgress<double>
    {
        public void Report(double value) => parent.Report(start + length * value);
    }
}

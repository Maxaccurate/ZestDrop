using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace ZestDrop.Engines.Images;

public static class IcnsEngine
{
    private static readonly HashSet<string> RasterInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "webp", "bmp", "gif", "ico", "tiff", "tif", "avif"
    };

    private static readonly HashSet<string> RasterOutputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "webp", "bmp", "ico"
    };

    public static bool CanHandle(string from, string to)
    {
        if (from.Equals("icns", StringComparison.OrdinalIgnoreCase))
            return RasterOutputs.Contains(to);

        if (to.Equals("icns", StringComparison.OrdinalIgnoreCase))
            return RasterInputs.Contains(from);

        return false;
    }

    public static async Task ConvertAsync(
        string inputPath,
        string outputPath,
        string from,
        string to,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            progress?.Invoke(0.1);
            cancellationToken.ThrowIfCancellationRequested();

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            if (from.Equals("icns", StringComparison.OrdinalIgnoreCase))
            {
                ConvertFromIcns(inputPath, outputPath, to, progress, cancellationToken);
            }
            else
            {
                ConvertToIcns(inputPath, outputPath, progress, cancellationToken);
            }

            progress?.Invoke(1.0);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ConvertToIcns(string inputPath, string outputPath, Action<double>? progress, CancellationToken ct)
    {
        using var original = SKBitmap.Decode(inputPath);
        if (original == null)
            throw new InvalidDataException($"Could not decode image at '{inputPath}'.");

        int side = Math.Max(original.Width, original.Height);
        using var square = new SKBitmap(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(square))
        {
            canvas.Clear(SKColors.Transparent);
            int x = (side - original.Width) / 2;
            int y = (side - original.Height) / 2;
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(original, x, y, paint);
            canvas.Flush();
        }

        ct.ThrowIfCancellationRequested();
        progress?.Invoke(0.25);

        var entries = new List<IcnsEntry>();
        var encoded = new Dictionary<int, byte[]>();

        for (int i = 0; i < IcnsFile.WrittenTypes.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (type, size) = IcnsFile.WrittenTypes[i];
            if (!encoded.TryGetValue(size, out var pngBytes))
            {
                using var resized = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                using (var canvas = new SKCanvas(resized))
                {
                    canvas.Clear(SKColors.Transparent);
                    using var paint = new SKPaint { IsAntialias = true };
                    canvas.DrawBitmap(square, new SKRect(0, 0, size, size), paint);
                    canvas.Flush();
                }

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                pngBytes = data.ToArray();
                encoded[size] = pngBytes;
            }

            entries.Add(new IcnsEntry(type, pngBytes));
            progress?.Invoke(0.25 + 0.65 * (i + 1) / IcnsFile.WrittenTypes.Length);
        }

        var icnsBytes = IcnsFile.Write(entries);
        File.WriteAllBytes(outputPath, icnsBytes);
    }

    private static void ConvertFromIcns(string inputPath, string outputPath, string to, Action<double>? progress, CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(inputPath);
        var entries = IcnsFile.Parse(bytes);
        var candidates = IcnsFile.Candidates(entries);
        if (candidates.Count == 0)
            throw new InvalidDataException("The ICNS file contains no decodable image.");

        progress?.Invoke(0.3);
        ct.ThrowIfCancellationRequested();

        using var bitmap = DecodeLargestCandidate(candidates, ct);
        if (bitmap == null)
            throw new InvalidDataException("Failed to decode any candidate image in ICNS.");

        progress?.Invoke(0.7);
        if (to.Equals("ico", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.Create(outputPath);
            SaveAsIco(bitmap, stream);
            return;
        }

        var format = to.ToLowerInvariant() switch
        {
            "png" => SKEncodedImageFormat.Png,
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            "bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        using var outStream = File.Create(outputPath);
        data.SaveTo(outStream);
    }

    public static void SaveAsIco(SKBitmap bitmap, Stream output)
    {
        int width = Math.Min(bitmap.Width, 256);
        int height = Math.Min(bitmap.Height, 256);

        byte[] pngBytes;
        if (width != bitmap.Width || height != bitmap.Height)
        {
            using var resized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (var canvas = new SKCanvas(resized))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), paint);
                canvas.Flush();
            }
            using var img = SKImage.FromBitmap(resized);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            pngBytes = data.ToArray();
        }
        else
        {
            using var img = SKImage.FromBitmap(bitmap);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            pngBytes = data.ToArray();
        }

        using var bw = new BinaryWriter(output, System.Text.Encoding.Default, leaveOpen: true);
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // icon type
        bw.Write((ushort)1); // image count
        bw.Write((byte)(width == 256 ? 0 : width));
        bw.Write((byte)(height == 256 ? 0 : height));
        bw.Write((byte)0); // colors
        bw.Write((byte)0); // reserved
        bw.Write((ushort)1); // color planes
        bw.Write((ushort)32); // bpp
        bw.Write((uint)pngBytes.Length);
        bw.Write((uint)22); // offset 6 + 16

        bw.Write(pngBytes);
    }

    private static SKBitmap DecodeLargestCandidate(List<IcnsFile.Candidate> candidates, CancellationToken ct)
    {
        Exception? first = null;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (candidate.Encoded != null)
                {
                    var bmp = SKBitmap.Decode(candidate.Encoded);
                    if (bmp != null) return bmp;
                }
                else if (candidate.Decode != null)
                {
                    var icnsBmp = candidate.Decode();
                    var bmp = new SKBitmap(new SKImageInfo(icnsBmp.Width, icnsBmp.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                    Marshal.Copy(icnsBmp.Rgba, 0, bmp.GetPixels(), icnsBmp.Rgba.Length);
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                first ??= ex;
            }
        }

        throw new InvalidDataException("No image in the ICNS file could be decoded.", first);
    }

    public static void Convert(string inputPath, string outputPath, string from, string to)
    {
        ConvertAsync(inputPath, outputPath, from, to, null, CancellationToken.None).GetAwaiter().GetResult();
    }
}


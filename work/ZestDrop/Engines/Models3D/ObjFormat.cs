// Wavefront OBJ with its MTL material library. Unitless (read as millimetres), Y up. Read: v (with the common
// "v x y z r g b" vertex colour extension), vt, vn, f with negative (relative) indices and polygons, o / g / usemtl
// as parts, mtllib with Kd, d / Tr and map_Kd textures next to the file. Written: one "o" per part, an .mtl next
// to the .obj and the textures next to that.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace ZestDrop.Engines.Models3D;

internal static class ObjFormat
{
    public static Model3D Read(string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var model = new Model3D();
        var positions = new List<float>();
        List<float>? colors = null;
        var uvs = new List<float>();
        var normals = new List<float>();
        // Materials of the libraries by name; only those a face uses become materials of the model.
        var library = new Dictionary<string, Material3D>(StringComparer.Ordinal);
        var materials = new Dictionary<string, int>(StringComparer.Ordinal);
        var parts = new Dictionary<(string, string?), PartBuilder>();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;

        string objectName = "", groupName = "";
        string? material = null;
        PartBuilder? current = null;
        Span<int> corners = stackalloc int[256];
        Span<float> numbers = stackalloc float[7];

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var length = Math.Max(1, stream.Length);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1 << 16);
        var lineNumber = 0;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            if ((lineNumber & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((double)stream.Position / length);
            }
            var line = raw;
            // A trailing backslash continues the statement on the next line.
            while (line.EndsWith('\\') && reader.ReadLine() is { } next)
            {
                line = line[..^1] + " " + next;
                lineNumber++;
            }
            var text = line.AsSpan().Trim();
            if (text.IsEmpty || text[0] == '#')
                continue;
            var space = text.IndexOfAny(' ', '\t');
            var keyword = space < 0 ? text : text[..space];
            var rest = space < 0 ? [] : text[(space + 1)..].Trim();

            switch (keyword)
            {
                case "v":
                    {
                        var values = numbers;
                        var n = ParseFloats(rest, values, lineNumber);
                        if (n < 3)
                            throw new InvalidDataException($"Line {lineNumber} of the OBJ file has a vertex with fewer than three coordinates.");
                        positions.Add(values[0]);
                        positions.Add(values[1]);
                        positions.Add(values[2]);
                        if (n >= 6)
                        {
                            colors ??= [.. Enumerable.Repeat(1f, (positions.Count / 3 - 1) * 3)];
                            var scale = values[3] > 1 || values[4] > 1 || values[5] > 1 ? 1 / 255f : 1;
                            colors.Add(Math.Clamp(values[3] * scale, 0, 1));
                            colors.Add(Math.Clamp(values[4] * scale, 0, 1));
                            colors.Add(Math.Clamp(values[5] * scale, 0, 1));
                        }
                        else if (colors is not null)
                        {
                            colors.Add(1);
                            colors.Add(1);
                            colors.Add(1);
                        }
                        break;
                    }
                case "vt":
                    {
                        var values = numbers[..3];
                        var n = ParseFloats(rest, values, lineNumber);
                        uvs.Add(n > 0 ? values[0] : 0);
                        uvs.Add(n > 1 ? values[1] : 0);
                        break;
                    }
                case "vn":
                    {
                        var values = numbers[..3];
                        if (ParseFloats(rest, values, lineNumber) < 3)
                            throw new InvalidDataException($"Line {lineNumber} of the OBJ file has a normal with fewer than three values.");
                        normals.Add(values[0]);
                        normals.Add(values[1]);
                        normals.Add(values[2]);
                        break;
                    }
                case "f":
                    {
                        current ??= GetPart(parts, objectName, groupName, material);
                        var count = 0;
                        var nPos = positions.Count / 3;
                        var nUv = uvs.Count / 2;
                        var nNorm = normals.Count / 3;
                        foreach (var token in rest.SplitAny(' ', '\t'))
                        {
                            var corner = rest[token].Trim();
                            if (corner.IsEmpty)
                                continue;
                            if (count == corners.Length)
                                throw new InvalidDataException($"Line {lineNumber} of the OBJ file has a polygon with too many corners.");
                            ParseCorner(corner, out var v, out var vt, out var vn);
                            var vi = RelativeIndex(v, nPos);
                            if (vi < 0 || vi >= nPos)
                                throw new InvalidDataException($"Line {lineNumber} of the OBJ file refers to vertex {v}, which does not exist.");
                            var ti = vt.HasValue ? RelativeIndex(vt.Value, nUv) : -1;
                            var ni = vn.HasValue ? RelativeIndex(vn.Value, nNorm) : -1;
                            corners[count++] = current.Add(vi, ti, ni);
                        }
                        current.AddPolygon(corners[..count]);
                        break;
                    }
                case "o":
                    objectName = rest.ToString();
                    groupName = "";
                    current = null;
                    break;
                case "g":
                    groupName = rest.ToString();
                    current = null;
                    break;
                case "usemtl":
                    material = rest.ToString();
                    current = null;
                    break;
                case "mtllib":
                    foreach (var token in rest.SplitAny(' ', '\t'))
                    {
                        var file = rest[token].Trim().ToString();
                        if (file.Length == 0)
                            continue;
                        if (Resolve(directory, file) is { } mtlPath)
                            MtlFormat.Read(mtlPath, library);
                    }
                    break;
            }
        }

        foreach (var builder in parts.Values)
        {
            var part = builder.Build(positions, colors, uvs, normals);
            if (part.TriangleCount == 0)
                continue;
            if (builder.Material is not null && library.TryGetValue(builder.Material, out var mat))
            {
                if (!materials.TryGetValue(builder.Material, out var index))
                {
                    index = model.Materials.Count;
                    materials.Add(builder.Material, index);
                    model.Materials.Add(mat);
                }
                part.Material = index;
            }
            model.Parts.Add(part);
        }
        return model;
    }

    private static int ParseFloats(ReadOnlySpan<char> text, Span<float> values, int lineNumber)
    {
        var count = 0;
        foreach (var range in text.SplitAny(' ', '\t'))
        {
            var token = text[range].Trim();
            if (token.IsEmpty)
                continue;
            if (count < values.Length && float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out values[count]))
                count++;
            else if (count >= values.Length)
                break;
            else
                throw new InvalidDataException($"Line {lineNumber} of the OBJ file has an invalid number.");
        }
        return count;
    }

    private static void ParseCorner(ReadOnlySpan<char> text, out int v, out int? vt, out int? vn)
    {
        v = 0;
        vt = null;
        vn = null;
        var firstSlash = text.IndexOf('/');
        if (firstSlash < 0)
        {
            v = int.Parse(text, CultureInfo.InvariantCulture);
            return;
        }
        v = int.Parse(text[..firstSlash], CultureInfo.InvariantCulture);
        var rest = text[(firstSlash + 1)..];
        var secondSlash = rest.IndexOf('/');
        if (secondSlash < 0)
        {
            if (!rest.IsEmpty)
                vt = int.Parse(rest, CultureInfo.InvariantCulture);
            return;
        }
        if (secondSlash > 0)
            vt = int.Parse(rest[..secondSlash], CultureInfo.InvariantCulture);
        var afterSecond = rest[(secondSlash + 1)..];
        if (!afterSecond.IsEmpty)
            vn = int.Parse(afterSecond, CultureInfo.InvariantCulture);
    }

    private static int RelativeIndex(int index, int total) => index > 0 ? index - 1 : total + index;

    private static PartBuilder GetPart(Dictionary<(string, string?), PartBuilder> parts, string objectName, string groupName, string? material)
    {
        var name = objectName.Length > 0 ? objectName : groupName.Length > 0 ? groupName : "OBJ";
        var key = (name, material);
        if (!parts.TryGetValue(key, out var builder))
        {
            builder = new PartBuilder(name, material);
            parts.Add(key, builder);
        }
        return builder;
    }

    internal static string? Resolve(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        if (File.Exists(path))
            return path;
        var name = Path.GetFileName(file);
        path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    private sealed class PartBuilder(string name, string? material)
    {
        public MeshPart Part { get; } = new() { Name = name };
        public string? Material { get; } = material;

        private readonly Dictionary<(int V, int Vt, int Vn), int> _corners = new();
        private readonly List<(int V, int Vt, int Vn)> _ordered = [];
        private readonly List<int[]> _polygons = [];

        public int Add(int v, int vt, int vn)
        {
            var key = (v, vt, vn);
            if (_corners.TryGetValue(key, out var index))
                return index;
            index = _corners.Count;
            _corners.Add(key, index);
            _ordered.Add(key);
            return index;
        }

        public void AddPolygon(ReadOnlySpan<int> corners)
        {
            _polygons.Add(corners.ToArray());
        }

        public MeshPart Build(List<float> positions, List<float>? colors, List<float> uvs, List<float> normals)
        {
            var count = _ordered.Count;
            Part.Positions.Capacity = count * 3;
            var hasUv = _ordered.Any(c => c.Vt >= 0);
            var hasNormal = _ordered.Any(c => c.Vn >= 0);
            var hasColors = colors is not null;
            if (hasUv)
                Part.TexCoords = new List<float>(count * 2);
            if (hasNormal)
                Part.Normals = new List<float>(count * 3);
            if (hasColors)
                Part.Colors = new List<float>(count * 4);

            foreach (var (v, vt, vn) in _ordered)
            {
                Part.Positions.Add(positions[v * 3]);
                Part.Positions.Add(positions[v * 3 + 1]);
                Part.Positions.Add(positions[v * 3 + 2]);
                if (hasColors)
                {
                    Part.Colors!.Add(colors![v * 3]);
                    Part.Colors.Add(colors[v * 3 + 1]);
                    Part.Colors.Add(colors[v * 3 + 2]);
                    Part.Colors.Add(1);
                }
                if (hasUv)
                {
                    if (vt >= 0 && vt * 2 + 1 < uvs.Count)
                    {
                        Part.TexCoords!.Add(uvs[vt * 2]);
                        Part.TexCoords.Add(uvs[vt * 2 + 1]);
                    }
                    else
                    {
                        Part.TexCoords!.Add(0);
                        Part.TexCoords.Add(0);
                    }
                }
                if (hasNormal)
                {
                    if (vn >= 0 && vn * 3 + 2 < normals.Count)
                    {
                        Part.Normals!.Add(normals[vn * 3]);
                        Part.Normals.Add(normals[vn * 3 + 1]);
                        Part.Normals.Add(normals[vn * 3 + 2]);
                    }
                    else
                    {
                        Part.Normals!.Add(0);
                        Part.Normals.Add(1);
                        Part.Normals.Add(0);
                    }
                }
            }

            foreach (var polygon in _polygons)
            {
                Triangulator.AddPolygon(Part, polygon);
            }

            return Part;
        }
    }

    public static void Write(Model3D model, string objPath, string? mtlPath, IReadOnlyDictionary<Texture3D, string> texturePaths, CancellationToken cancellationToken)
    {
        var materialNames = UniqueNames(model.Materials.Select(m => m.Name), "Material");
        var partNames = UniqueNames(model.Parts.Select(p => p.Name), "Part");

        using var writer = new StreamWriter(objPath, false, new UTF8Encoding(false), 1 << 20);
        writer.NewLine = "\n";
        writer.WriteLine("# Written by ZestDrop");
        if (mtlPath is not null)
            writer.WriteLine($"mtllib {RelativeReference(objPath, mtlPath)}");

        Span<char> buffer = stackalloc char[256];
        var vBase = 1;
        var tBase = 1;
        var nBase = 1;

        for (var p = 0; p < model.Parts.Count; p++)
        {
            var part = model.Parts[p];
            writer.WriteLine($"o {partNames[p]}");
            if (part.Material >= 0 && part.Material < materialNames.Count)
                writer.WriteLine($"usemtl {materialNames[part.Material]}");
            else if (mtlPath is not null)
                writer.WriteLine($"usemtl {DefaultMaterial}");

            var pos = part.PositionSpan;
            for (var i = 0; i + 2 < pos.Length; i += 3)
            {
                var line = new LineBuilder(buffer);
                line.Append("v");
                line.Append(pos[i]);
                line.Append(pos[i + 1]);
                line.Append(pos[i + 2]);
                if (part.Colors is not null)
                {
                    line.Append(part.Colors[i / 3 * 4]);
                    line.Append(part.Colors[i / 3 * 4 + 1]);
                    line.Append(part.Colors[i / 3 * 4 + 2]);
                }
                writer.WriteLine(line.Text);
            }

            if (part.TexCoords is not null)
            {
                for (var i = 0; i + 1 < part.TexCoords.Count; i += 2)
                {
                    var line = new LineBuilder(buffer);
                    line.Append("vt");
                    line.Append(part.TexCoords[i]);
                    line.Append(part.TexCoords[i + 1]);
                    writer.WriteLine(line.Text);
                }
            }

            if (part.Normals is not null)
            {
                for (var i = 0; i + 2 < part.Normals.Count; i += 3)
                {
                    var line = new LineBuilder(buffer);
                    line.Append("vn");
                    line.Append(part.Normals[i]);
                    line.Append(part.Normals[i + 1]);
                    line.Append(part.Normals[i + 2]);
                    writer.WriteLine(line.Text);
                }
            }

            var indices = part.IndexSpan;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                if ((i & 0xFFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                var line = new LineBuilder(buffer);
                line.Append("f");
                for (var k = 0; k < 3; k++)
                    line.AppendCorner(indices[i + k], vBase, part.TexCoords is null ? -1 : tBase, part.Normals is null ? -1 : nBase);
                writer.WriteLine(line.Text);
            }
            vBase += part.VertexCount;
            if (part.TexCoords is not null)
                tBase += part.VertexCount;
            if (part.Normals is not null)
                nBase += part.VertexCount;
        }

        if (mtlPath is not null)
            MtlFormat.Write(model, materialNames, mtlPath, texturePaths);
    }

    internal const string DefaultMaterial = "Default";

    internal static List<string> UniqueNames(IEnumerable<string> names, string fallback)
    {
        var used = new HashSet<string>(StringComparer.Ordinal) { DefaultMaterial };
        var result = new List<string>();
        foreach (var name in names)
        {
            var clean = new string([.. name.Select(c => char.IsWhiteSpace(c) || char.IsControl(c) ? '_' : c)]);
            if (clean.Length == 0)
                clean = fallback;
            var unique = clean;
            for (var i = 2; !used.Add(unique); i++)
                unique = $"{clean}_{i}";
            result.Add(unique);
        }
        return result;
    }

    internal static string RelativeReference(string from, string target) =>
        Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(from))!, Path.GetFullPath(target)).Replace('\\', '/');

    private ref struct LineBuilder(Span<char> buffer)
    {
        private readonly Span<char> _buffer = buffer;
        private int _length;

        public readonly ReadOnlySpan<char> Text => _buffer[.._length];

        public void Append(string keyword)
        {
            keyword.AsSpan().CopyTo(_buffer[_length..]);
            _length += keyword.Length;
        }

        public void Append(float value)
        {
            _buffer[_length++] = ' ';
            if (!float.IsFinite(value))
                value = 0;
            value.TryFormat(_buffer[_length..], out var written, default, CultureInfo.InvariantCulture);
            _length += written;
        }

        public void AppendCorner(int index, int vBase, int tBase, int nBase)
        {
            _buffer[_length++] = ' ';
            AppendInt(index + vBase);
            if (tBase < 0 && nBase < 0)
                return;
            _buffer[_length++] = '/';
            if (tBase >= 0)
                AppendInt(index + tBase);
            if (nBase >= 0)
            {
                _buffer[_length++] = '/';
                AppendInt(index + nBase);
            }
        }

        private void AppendInt(int value)
        {
            value.TryFormat(_buffer[_length..], out var written, default, CultureInfo.InvariantCulture);
            _length += written;
        }
    }
}

internal static class MtlFormat
{
    public static void Read(string path, Dictionary<string, Material3D> library)
    {
        var directory = Path.GetDirectoryName(path)!;
        Material3D? current = null;
        foreach (var raw in File.ReadLines(path))
        {
            var text = raw.AsSpan().Trim();
            if (text.IsEmpty || text[0] == '#')
                continue;
            var space = text.IndexOfAny(' ', '\t');
            var keyword = (space < 0 ? text : text[..space]).ToString();
            var rest = space < 0 ? "" : text[(space + 1)..].Trim().ToString();
            if (keyword == "newmtl")
            {
                current = new Material3D { Name = rest };
                library[rest] = current;
                continue;
            }
            if (current is null)
                continue;
            var values = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            switch (keyword.ToLowerInvariant())
            {
                case "kd" when values.Length >= 3 && Floats(values, 3) is { } kd:
                    current.BaseColor = new Vector4(kd[0], kd[1], kd[2], current.BaseColor.W);
                    break;
                case "d" when values.Length >= 1 && Floats(values[^1..], 1) is { } d:
                    current.BaseColor = current.BaseColor with { W = Math.Clamp(d[0], 0, 1) };
                    break;
                case "tr" when values.Length >= 1 && Floats(values[^1..], 1) is { } tr:
                    current.BaseColor = current.BaseColor with { W = Math.Clamp(1 - tr[0], 0, 1) };
                    break;
                case "map_kd" when TextureFile(values) is { } file && ObjFormat.Resolve(directory, file) is { } texture:
                    var data = File.ReadAllBytes(texture);
                    current.BaseColorTexture = new Texture3D(data, Texture3D.Sniff(data, Path.GetExtension(texture)), Path.GetFileNameWithoutExtension(texture));
                    break;
            }
        }
    }

    private static float[]? Floats(string[] values, int count)
    {
        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            if (!float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                return null;
        }
        return result;
    }

    private static string? TextureFile(string[] values)
    {
        var i = 0;
        while (i < values.Length && values[i].StartsWith('-'))
        {
            var option = values[i++].ToLowerInvariant();
            var arguments = option switch
            {
                "-o" or "-s" or "-t" => 3,
                "-mm" => 2,
                "-blendu" or "-blendv" or "-boost" or "-cc" or "-clamp" or "-texres" or "-bm" or "-imfchan" or "-type" => 1,
                _ => 0,
            };
            for (var a = 0; a < arguments && i < values.Length; a++)
            {
                if (arguments == 3 && a > 0 && !float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    break;
                i++;
            }
        }
        return i < values.Length ? string.Join(' ', values[i..]) : null;
    }

    public static void Write(Model3D model, IReadOnlyList<string> names, string path, IReadOnlyDictionary<Texture3D, string> texturePaths)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.NewLine = "\n";
        writer.WriteLine("# Written by ZestDrop");
        WriteMaterial(writer, ObjFormat.DefaultMaterial, new Material3D());
        for (var m = 0; m < model.Materials.Count; m++)
            WriteMaterial(writer, names[m], model.Materials[m]);

        void WriteMaterial(StreamWriter w, string name, Material3D material)
        {
            var c = material.BaseColor;
            w.WriteLine();
            w.WriteLine($"newmtl {name}");
            w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Kd {c.X} {c.Y} {c.Z}"));
            w.WriteLine("Ka 0 0 0");
            w.WriteLine("Ks 0 0 0");
            if (c.W < 1)
                w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"d {c.W}"));
            w.WriteLine("illum 1");
            if (material.BaseColorTexture is { } texture && texturePaths.TryGetValue(texture, out var file))
                w.WriteLine($"map_Kd {ObjFormat.RelativeReference(path, file)}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Cycon.Core.Transcript;
using Extensions.Deconstruction.Blocks;

namespace Extensions.Deconstruction.Stl;

public static class StlLoader
{
    public static StlBlock LoadBlock(BlockId id, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var data = Load(fullPath);
        return new StlBlock(
            id,
            fullPath,
            data.Vertices,
            data.Indices,
            data.Bounds);
    }

    public static StlData Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (TryReadBinary(stream, out var data))
        {
            return data;
        }

        stream.Position = 0;
        return ReadAscii(stream);
    }

    public readonly record struct StlData(Vector3[] Vertices, int[] Indices, StlBlock.Bounds Bounds);

    private static bool TryReadBinary(Stream stream, out StlData data)
    {
        data = default;
        if (!stream.CanSeek || stream.Length < 84)
        {
            return false;
        }

        var startPos = stream.Position;
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            _ = reader.ReadBytes(80);
            var triCount = reader.ReadUInt32();

            var expectedLength = 84L + (50L * triCount);
            if (stream.Length != expectedLength)
            {
                return false;
            }

            var vertexCount = checked((int)triCount * 3);
            var vertices = new Vector3[vertexCount];
            var indices = new int[vertexCount];

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            var index = 0;
            for (var t = 0; t < triCount; t++)
            {
                _ = ReadVector3(reader); // normal
                var v0 = ReadVector3(reader);
                var v1 = ReadVector3(reader);
                var v2 = ReadVector3(reader);
                _ = reader.ReadUInt16(); // attribute

                vertices[index] = v0;
                indices[index] = index;
                UpdateBounds(ref min, ref max, v0);
                index++;

                vertices[index] = v1;
                indices[index] = index;
                UpdateBounds(ref min, ref max, v1);
                index++;

                vertices[index] = v2;
                indices[index] = index;
                UpdateBounds(ref min, ref max, v2);
                index++;
            }

            if (vertexCount == 0)
            {
                min = Vector3.Zero;
                max = Vector3.Zero;
            }

            data = new StlData(vertices, indices, new StlBlock.Bounds(min, max));
            return true;
        }
        finally
        {
            stream.Position = startPos;
        }
    }

    private static StlData ReadAscii(Stream stream)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        var verts = new List<Vector3>();
        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            if (!TryParseFloat(parts[1], out var x) ||
                !TryParseFloat(parts[2], out var y) ||
                !TryParseFloat(parts[3], out var z))
            {
                continue;
            }

            var v = new Vector3(x, y, z);
            verts.Add(v);
            UpdateBounds(ref min, ref max, v);
        }

        if (verts.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
        }

        var vertices = verts.ToArray();
        var indices = new int[vertices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        return new StlData(vertices, indices, new StlBlock.Bounds(min, max));
    }

    private static bool TryParseFloat(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        var x = reader.ReadSingle();
        var y = reader.ReadSingle();
        var z = reader.ReadSingle();
        return new Vector3(x, y, z);
    }

    private static void UpdateBounds(ref Vector3 min, ref Vector3 max, Vector3 v)
    {
        min = Vector3.Min(min, v);
        max = Vector3.Max(max, v);
    }
}


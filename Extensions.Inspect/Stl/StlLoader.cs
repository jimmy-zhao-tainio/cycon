using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Cycon.Core.Transcript;
using Extensions.Inspect.Blocks;

namespace Extensions.Inspect.Stl;

public static class StlLoader
{
    public static StlBlock LoadBlock(BlockId id, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var data = Load(fullPath);

        LogDiagnostics(
            fullPath,
            data.Format,
            data.TriangleCount,
            data.VertexCount,
            data.BytesRead,
            data.FileLength,
            data.Bounds.Min,
            data.Bounds.Max,
            data.HasNaN);

        if (data.TriangleCount <= 0 || data.VertexCount <= 0)
        {
            throw new InvalidOperationException($"STL produced empty mesh. fmt={data.Format} len={data.FileLength} bytesRead={data.BytesRead}");
        }

        return new StlBlock(
            id,
            fullPath,
            data.VertexData,
            data.VertexCount,
            data.TriangleCount,
            data.Bounds);
    }

    public static StlData Load(string path)
    {
        var fileInfo = new FileInfo(path);
        var fileLength = fileInfo.Length;
        if (fileLength < 84)
        {
            throw new InvalidOperationException($"STL too small: len={fileLength}");
        }

        using var raw = File.OpenRead(path);
        using var stream = new CountingStream(raw);

        var (format, triCount) = DetectFormat(stream, fileLength);
        stream.Position = 0;
        stream.ResetCount();

        StlData data;
        try
        {
            data = format == StlFormat.Binary
                ? ReadBinary(stream, fileLength, triCount)
                : ReadAscii(stream, fileLength);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"STL parse failed: fmt={format} len={fileLength}", ex);
        }

        data = data with { Format = format, FileLength = fileLength, BytesRead = stream.BytesRead };

        // Fail loudly for empty/invalid content.
        if (data.TriangleCount <= 0 || data.VertexCount <= 0)
        {
            throw new InvalidOperationException($"STL produced empty mesh. fmt={format} len={fileLength} bytesRead={stream.BytesRead}");
        }

        if (data.HasNaN)
        {
            throw new InvalidOperationException($"STL has NaN/Inf coordinates. fmt={format} len={fileLength} bytesRead={stream.BytesRead}");
        }

        if (data.VertexCount % 3 != 0)
        {
            throw new InvalidOperationException($"STL vertex count not divisible by 3. fmt={format} verts={data.VertexCount} len={fileLength}");
        }

        return data;
    }

    public enum StlFormat
    {
        Binary,
        Ascii
    }

    public readonly record struct StlData(
        float[] VertexData,
        int VertexCount,
        StlBlock.Bounds Bounds,
        StlFormat Format,
        int TriangleCount,
        bool HasNaN,
        long BytesRead,
        long FileLength);

    private static (StlFormat Format, uint TriangleCount) DetectFormat(Stream stream, long fileLength)
    {
        if (!stream.CanSeek)
        {
            // Extremely defensive; this loader assumes file-backed streams.
            return (StlFormat.Ascii, 0);
        }

        stream.Position = 80;
        Span<byte> triBytes = stackalloc byte[4];
        var read = stream.Read(triBytes);
        if (read != 4)
        {
            return (StlFormat.Ascii, 0);
        }

        var triCount = BitConverter.ToUInt32(triBytes);
        long expectedLength;
        try
        {
            expectedLength = checked(84L + (50L * triCount));
        }
        catch
        {
            return (StlFormat.Ascii, triCount);
        }

        return fileLength == expectedLength ? (StlFormat.Binary, triCount) : (StlFormat.Ascii, triCount);
    }

    private static StlData ReadBinary(Stream stream, long fileLength, uint triCount)
    {
        if (!stream.CanSeek)
        {
            throw new InvalidOperationException($"STL binary parse requires seekable stream. len={fileLength}");
        }

        var expectedLength = checked(84L + (50L * triCount));
        if (fileLength != expectedLength)
        {
            throw new InvalidOperationException($"STL binary length mismatch. len={fileLength} expected={expectedLength} triCount={triCount}");
        }

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        _ = reader.ReadBytes(80);
        _ = reader.ReadUInt32(); // triCount already read for detection

        var vertexCount = checked((int)triCount * 3);
        var data = new float[checked(vertexCount * 6)];

        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var hasNaN = false;

        var index = 0;
        for (var t = 0; t < triCount; t++)
        {
            _ = ReadVector3(reader); // normal
            var v0 = ReadVector3(reader);
            var v1 = ReadVector3(reader);
            var v2 = ReadVector3(reader);
            _ = reader.ReadUInt16(); // attribute

            var n = ComputeFaceNormal(v0, v1, v2);

            WriteVertex(data, index++, v0, n);
            UpdateBoundsAndNaN(ref min, ref max, ref hasNaN, v0);

            WriteVertex(data, index++, v1, n);
            UpdateBoundsAndNaN(ref min, ref max, ref hasNaN, v1);

            WriteVertex(data, index++, v2, n);
            UpdateBoundsAndNaN(ref min, ref max, ref hasNaN, v2);
        }

        if (vertexCount == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
        }

        if (stream.Position != expectedLength)
        {
            throw new InvalidOperationException($"STL binary parse ended at unexpected offset: pos={stream.Position} expected={expectedLength}");
        }

        return new StlData(
            data,
            vertexCount,
            new StlBlock.Bounds(min, max),
            Format: StlFormat.Binary,
            TriangleCount: (int)triCount,
            HasNaN: hasNaN,
            BytesRead: 0,
            FileLength: fileLength);
    }

    private static StlData ReadAscii(Stream stream, long fileLength)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        var verts = new List<Vector3>();
        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var hasNaN = false;

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
            UpdateBoundsAndNaN(ref min, ref max, ref hasNaN, v);
        }

        if (verts.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
        }

        var vertices = verts.ToArray();
        var triCount = vertices.Length / 3;
        var vertexCount = vertices.Length;
        var data = new float[checked(vertexCount * 6)];
        for (var t = 0; t < triCount; t++)
        {
            var i = t * 3;
            var v0 = vertices[i];
            var v1 = vertices[i + 1];
            var v2 = vertices[i + 2];
            var n = ComputeFaceNormal(v0, v1, v2);
            WriteVertex(data, i, v0, n);
            WriteVertex(data, i + 1, v1, n);
            WriteVertex(data, i + 2, v2, n);
        }

        return new StlData(
            data,
            vertexCount,
            new StlBlock.Bounds(min, max),
            Format: StlFormat.Ascii,
            TriangleCount: triCount,
            HasNaN: hasNaN,
            BytesRead: 0,
            FileLength: fileLength);
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

    private static void UpdateBoundsAndNaN(ref Vector3 min, ref Vector3 max, ref bool hasNaN, Vector3 v)
    {
        if (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) ||
            float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z))
        {
            hasNaN = true;
            return;
        }

        min = Vector3.Min(min, v);
        max = Vector3.Max(max, v);
    }

    private static Vector3 ComputeFaceNormal(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var n = Vector3.Cross(v1 - v0, v2 - v0);
        if (n.LengthSquared() < 1e-20f)
        {
            return Vector3.UnitY;
        }

        return Vector3.Normalize(n);
    }

    private static void WriteVertex(float[] data, int vertexIndex, Vector3 pos, Vector3 normal)
    {
        var o = vertexIndex * 6;
        data[o] = pos.X;
        data[o + 1] = pos.Y;
        data[o + 2] = pos.Z;
        data[o + 3] = normal.X;
        data[o + 4] = normal.Y;
        data[o + 5] = normal.Z;
    }

    private static void LogDiagnostics(
        string path,
        StlFormat format,
        int triCount,
        int vertexCount,
        long bytesRead,
        long fileLength,
        Vector3 min,
        Vector3 max,
        bool hasNaN)
    {
        Console.WriteLine(
            $"STL {path}: fmt={format} tris={triCount} verts={vertexCount} bytesRead={bytesRead}/{fileLength} " +
            $"aabbMin=({min.X:0.####},{min.Y:0.####},{min.Z:0.####}) aabbMax=({max.X:0.####},{max.Y:0.####},{max.Z:0.####}) hasNaN={hasNaN}");
    }

    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        private long _bytesRead;

        public CountingStream(Stream inner) => _inner = inner;

        public long BytesRead => _bytesRead;

        public void ResetCount() => _bytesRead = 0;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            _bytesRead += n;
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            var n = _inner.Read(buffer);
            _bytesRead += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void Flush() => _inner.Flush();
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

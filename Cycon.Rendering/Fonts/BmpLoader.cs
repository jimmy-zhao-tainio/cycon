using System;
using System.IO;

namespace Cycon.Rendering.Fonts;

public static class BmpLoader
{
    private const ushort SignatureBm = 0x4D42; // 'BM'
    private const uint BitmapInfoHeaderSize = 40;
    private const uint CompressionBiRgb = 0;

    public static (int width, int height, byte[] rgba) LoadRgba24(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("BMP path must be provided.", nameof(path));
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (stream.Length < 14 + BitmapInfoHeaderSize)
        {
            throw new InvalidOperationException($"Invalid BMP '{path}': file is too small.");
        }

        var signature = reader.ReadUInt16();
        if (signature != SignatureBm)
        {
            throw new InvalidOperationException($"Invalid BMP '{path}': bad signature (expected 'BM').");
        }

        _ = reader.ReadUInt32(); // file size
        _ = reader.ReadUInt16(); // reserved1
        _ = reader.ReadUInt16(); // reserved2
        var dataOffset = reader.ReadUInt32();

        var headerSize = reader.ReadUInt32();
        if (headerSize != BitmapInfoHeaderSize)
        {
            throw new NotSupportedException($"Unsupported BMP '{path}': DIB header size {headerSize} (expected {BitmapInfoHeaderSize}).");
        }

        var width = reader.ReadInt32();
        var heightSigned = reader.ReadInt32();
        var planes = reader.ReadUInt16();
        var bitsPerPixel = reader.ReadUInt16();
        var compression = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // image size (may be 0 for BI_RGB)
        _ = reader.ReadInt32(); // x pixels per meter
        _ = reader.ReadInt32(); // y pixels per meter
        _ = reader.ReadUInt32(); // colors used
        _ = reader.ReadUInt32(); // important colors

        if (width <= 0)
        {
            throw new InvalidOperationException($"Invalid BMP '{path}': width must be positive (got {width}).");
        }

        var topDown = heightSigned < 0;
        var height = Math.Abs(heightSigned);
        if (height <= 0)
        {
            throw new InvalidOperationException($"Invalid BMP '{path}': height must be non-zero (got {heightSigned}).");
        }

        if (planes != 1)
        {
            throw new NotSupportedException($"Unsupported BMP '{path}': planes={planes} (expected 1).");
        }

        if (compression != CompressionBiRgb)
        {
            throw new NotSupportedException($"Unsupported BMP '{path}': compression={compression} (expected BI_RGB=0).");
        }

        if (bitsPerPixel != 24)
        {
            throw new NotSupportedException($"Unsupported BMP '{path}': bpp={bitsPerPixel} (expected 24).");
        }

        var rowStrideUnpadded = checked(width * 3);
        var rowStridePadded = (rowStrideUnpadded + 3) & ~3;
        var expectedDataSize = (long)rowStridePadded * height;
        var endOfData = checked((long)dataOffset + expectedDataSize);
        if (endOfData > stream.Length)
        {
            throw new InvalidOperationException($"Invalid BMP '{path}': pixel data exceeds file length.");
        }

        var rgba = new byte[checked(width * height * 4)];
        var row = new byte[rowStridePadded];

        for (var yOut = 0; yOut < height; yOut++)
        {
            var yIn = topDown ? yOut : (height - 1 - yOut);
            stream.Position = checked((long)dataOffset + (long)yIn * rowStridePadded);

            ReadExactly(reader, row);

            var dstRowOffset = yOut * width * 4;
            for (var x = 0; x < width; x++)
            {
                var src = x * 3;
                var b = row[src + 0];
                var g = row[src + 1];
                var r = row[src + 2];

                var dst = dstRowOffset + (x * 4);
                rgba[dst + 0] = r;
                rgba[dst + 1] = g;
                rgba[dst + 2] = b;
                rgba[dst + 3] = 255;
            }
        }

        return (width, height, rgba);
    }

    private static void ReadExactly(BinaryReader reader, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = reader.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading BMP pixel data.");
            }

            offset += read;
        }
    }
}

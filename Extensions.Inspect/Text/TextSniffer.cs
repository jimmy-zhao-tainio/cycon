using System;
using System.IO;

namespace Extensions.Inspect.Text;

public static class TextSniffer
{
    public static bool LooksLikeTextFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buf = new byte[4096];
            var read = stream.Read(buf, 0, buf.Length);
            return LooksLikeText(buf.AsSpan(0, read));
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return true;
        }

        var control = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b == 0)
            {
                return false;
            }

            // allow: tab/newline/carriage return and typical ASCII.
            if (b < 32 && b is not (byte)'\t' and not (byte)'\n' and not (byte)'\r')
            {
                control++;
            }
        }

        // If a meaningful chunk is mostly control chars, treat as binary.
        return control <= (bytes.Length / 20); // <=5%
    }
}


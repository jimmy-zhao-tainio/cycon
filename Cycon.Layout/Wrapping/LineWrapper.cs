using System.Collections.Generic;

namespace Cycon.Layout.Wrapping;

public static class LineWrapper
{
    public static IReadOnlyList<LineSpan> Wrap(string text, int columns)
    {
        var lines = new List<LineSpan>();

        if (columns <= 0)
        {
            lines.Add(new LineSpan(0, 0));
            return lines;
        }

        if (string.IsNullOrEmpty(text))
        {
            lines.Add(new LineSpan(0, 0));
            return lines;
        }

        var lineStart = 0;
        var lineLength = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r' || ch == '\n')
            {
                lines.Add(new LineSpan(lineStart, lineLength));
                lineStart = i + 1;
                lineLength = 0;

                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    lineStart++;
                    i++;
                }

                continue;
            }

            if (lineLength == columns)
            {
                lines.Add(new LineSpan(lineStart, lineLength));
                lineStart = i;
                lineLength = 0;
            }

            lineLength++;
        }

        lines.Add(new LineSpan(lineStart, lineLength));
        return lines;
    }
}

public readonly record struct LineSpan(int Start, int Length);

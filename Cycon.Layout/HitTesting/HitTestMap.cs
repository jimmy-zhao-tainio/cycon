using System.Collections.Generic;
using Cycon.Core.Transcript;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;

namespace Cycon.Layout.HitTesting;

public sealed class HitTestMap
{
    private readonly Dictionary<int, HitTestLine> _linesByRow = new();

    public HitTestMap(FixedCellGrid grid, IReadOnlyList<HitTestLine> lines, IReadOnlyList<HitTestActionSpan> actionSpans)
    {
        Grid = grid;
        Lines = lines;
        ActionSpans = actionSpans;

        foreach (var line in lines)
        {
            _linesByRow[line.RowIndex] = line;
        }
    }

    public FixedCellGrid Grid { get; }
    public IReadOnlyList<HitTestLine> Lines { get; }
    public IReadOnlyList<HitTestActionSpan> ActionSpans { get; }

    public bool TryGetLine(int rowIndex, out HitTestLine line) => _linesByRow.TryGetValue(rowIndex, out line);

    public bool TryGetActionAt(int pixelX, int pixelY, out string commandText)
    {
        commandText = string.Empty;
        for (var i = 0; i < ActionSpans.Count; i++)
        {
            var span = ActionSpans[i];
            if (span.RectPx.Contains(pixelX, pixelY))
            {
                commandText = span.CommandText;
                return !string.IsNullOrEmpty(commandText);
            }
        }

        return false;
    }

    public bool TryGetActionAt(int pixelX, int pixelY, out int spanIndex)
    {
        spanIndex = -1;
        for (var i = 0; i < ActionSpans.Count; i++)
        {
            if (ActionSpans[i].RectPx.Contains(pixelX, pixelY))
            {
                spanIndex = i;
                return true;
            }
        }

        return false;
    }
}

public readonly record struct HitTestLine(BlockId BlockId, int BlockIndex, int Start, int Length, int RowIndex);

public readonly record struct HitTestActionSpan(BlockId BlockId, PxRect RectPx, string CommandText);

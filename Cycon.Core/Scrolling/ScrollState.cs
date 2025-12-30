using System;
using Cycon.Core.Transcript;

namespace Cycon.Core.Scrolling;

public sealed class ScrollState
{
    public int ScrollOffsetRows { get; set; }
    public bool IsFollowingTail { get; set; } = true;
    public int ScrollRowsFromBottom { get; set; }
    public TopVisualLineAnchor? TopVisualLineAnchor { get; set; }

    public void ApplyUserScrollDelta(int deltaRows, int maxScrollOffsetRows)
    {
        if (deltaRows == 0)
        {
            return;
        }

        ScrollOffsetRows = Math.Clamp(ScrollOffsetRows + deltaRows, 0, maxScrollOffsetRows);
        IsFollowingTail = ScrollOffsetRows >= maxScrollOffsetRows;
        ScrollRowsFromBottom = maxScrollOffsetRows - ScrollOffsetRows;
    }
}

public readonly record struct TopVisualLineAnchor(BlockId BlockId, int CharIndex);

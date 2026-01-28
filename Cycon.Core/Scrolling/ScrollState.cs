using System;
using Cycon.Core.Transcript;

namespace Cycon.Core.Scrolling;

public sealed class ScrollState
{
    public int ScrollOffsetPx { get; set; }
    public bool IsFollowingTail { get; set; } = true;
    public int ScrollPxFromBottom { get; set; }
    public TopVisualLineAnchor? TopVisualLineAnchor { get; set; }
    public ScrollbarUiState ScrollbarUi { get; } = new();

    public void ApplyUserScrollDelta(int deltaPx, int maxScrollOffsetPx)
    {
        if (deltaPx == 0)
        {
            return;
        }

        ScrollOffsetPx = Math.Clamp(ScrollOffsetPx + deltaPx, 0, maxScrollOffsetPx);
        IsFollowingTail = ScrollOffsetPx >= maxScrollOffsetPx;
        ScrollPxFromBottom = maxScrollOffsetPx - ScrollOffsetPx;
    }
}

public readonly record struct TopVisualLineAnchor(BlockId BlockId, int CharIndex);

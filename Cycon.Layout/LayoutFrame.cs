using System.Collections.Generic;
using Cycon.Core.Transcript;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;

namespace Cycon.Layout;

public sealed class LayoutFrame
{
    public LayoutFrame(
        FixedCellGrid grid,
        IReadOnlyList<LayoutLine> lines,
        HitTestMap hitTestMap,
        int totalRows,
        ScrollbarLayout scrollbar,
        IReadOnlyList<Scene3DViewportLayout> scene3DViewports)
    {
        Grid = grid;
        Lines = lines;
        HitTestMap = hitTestMap;
        TotalRows = totalRows;
        Scrollbar = scrollbar;
        Scene3DViewports = scene3DViewports;
    }

    public FixedCellGrid Grid { get; }
    public IReadOnlyList<LayoutLine> Lines { get; }
    public HitTestMap HitTestMap { get; }
    public int TotalRows { get; }
    public ScrollbarLayout Scrollbar { get; }
    public IReadOnlyList<Scene3DViewportLayout> Scene3DViewports { get; }
}

public readonly record struct LayoutLine(BlockId BlockId, int BlockIndex, int Start, int Length, int RowIndex);

public readonly record struct Scene3DViewportLayout(
    BlockId BlockId,
    int BlockIndex,
    int RowIndex,
    PxRect ViewportRectPx,
    PxRect InnerViewportRectPx,
    BlockChromeSpec Chrome,
    double PreferredAspectRatio);

using System.Collections.Generic;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;

namespace Cycon.Layout;

public sealed class LayoutFrame
{
    public LayoutFrame(FixedCellGrid grid, IReadOnlyList<LayoutLine> lines, HitTestMap hitTestMap, int totalRows)
    {
        Grid = grid;
        Lines = lines;
        HitTestMap = hitTestMap;
        TotalRows = totalRows;
    }

    public FixedCellGrid Grid { get; }
    public IReadOnlyList<LayoutLine> Lines { get; }
    public HitTestMap HitTestMap { get; }
    public int TotalRows { get; }
}

public readonly record struct LayoutLine(int BlockIndex, int Start, int Length, int RowIndex);

using System.Collections.Generic;
using Cycon.Core.Transcript;
using Cycon.Layout.Metrics;

namespace Cycon.Layout.HitTesting;

public sealed class HitTestMap
{
    private readonly Dictionary<int, HitTestLine> _linesByRow = new();

    public HitTestMap(FixedCellGrid grid, IReadOnlyList<HitTestLine> lines)
    {
        Grid = grid;
        Lines = lines;

        foreach (var line in lines)
        {
            _linesByRow[line.RowIndex] = line;
        }
    }

    public FixedCellGrid Grid { get; }
    public IReadOnlyList<HitTestLine> Lines { get; }

    public bool TryGetLine(int rowIndex, out HitTestLine line) => _linesByRow.TryGetValue(rowIndex, out line);
}

public readonly record struct HitTestLine(BlockId BlockId, int BlockIndex, int Start, int Length, int RowIndex);

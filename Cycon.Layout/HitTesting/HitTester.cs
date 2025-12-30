using System;

namespace Cycon.Layout.HitTesting;

public sealed class HitTester
{
    public DocumentPosition? HitTest(HitTestMap map, int pixelX, int pixelY)
    {
        var grid = map.Grid;
        if (grid.CellWidthPx <= 0 || grid.CellHeightPx <= 0)
        {
            return null;
        }

        var localX = pixelX - grid.PaddingLeftPx;
        var localY = pixelY - grid.PaddingTopPx;

        if (localX < 0 || localY < 0)
        {
            return null;
        }

        var col = localX / grid.CellWidthPx;
        var row = localY / grid.CellHeightPx;

        if (!map.TryGetLine(row, out var line))
        {
            return null;
        }

        var clampedCol = col < 0 ? 0 : col;
        var charIndex = line.Start + Math.Min(clampedCol, line.Length);
        return new DocumentPosition(line.BlockIndex, charIndex);
    }
}

public readonly record struct DocumentPosition(int BlockIndex, int CharIndex);

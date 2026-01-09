using System;
using Cycon.Core.Fonts;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

internal static class CaretPass
{
    public static CaretQuad? TryComputeCaret(PromptBlock prompt, LayoutFrame layout, int promptBlockIndex)
    {
        var caretCharIndex = prompt.Prompt.Length + Math.Clamp(prompt.CaretIndex, 0, prompt.Input.Length);

        CaretCell? best = null;
        foreach (var line in layout.Lines)
        {
            if (line.BlockIndex != promptBlockIndex)
            {
                continue;
            }

            if (caretCharIndex < line.Start)
            {
                break;
            }

            if (caretCharIndex < line.Start + line.Length)
            {
                return new CaretQuad(line.RowIndex, caretCharIndex - line.Start);
            }

            if (caretCharIndex == line.Start + line.Length && line.Length < layout.Grid.Cols)
            {
                best = new CaretCell(line.RowIndex, caretCharIndex - line.Start);
            }
        }

        return best is { } c ? new CaretQuad(c.RowIndex, c.ColIndex) : null;
    }

    public static void RenderCaret(
        RenderFrame frame,
        FixedCellGrid grid,
        FontMetrics fontMetrics,
        int scrollOffsetRows,
        CaretQuad caretQuad,
        int caretColorRgba,
        byte caretAlpha)
    {
        var rowOnScreen = caretQuad.RowIndex - scrollOffsetRows;
        if (rowOnScreen < 0 || rowOnScreen >= grid.Rows)
        {
            return;
        }

        if (caretAlpha == 0)
        {
            return;
        }

        var cellX = grid.PaddingLeftPx + (caretQuad.ColIndex * grid.CellWidthPx);
        var cellY = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx);
        var underlineY = fontMetrics.GetUnderlineTopY(cellY);
        var underlineH = Math.Max(2, fontMetrics.UnderlineThicknessPx);

        var caretColor = WithAlpha(caretColorRgba, caretAlpha);
        frame.Add(new DrawQuad(cellX, underlineY, grid.CellWidthPx, underlineH, caretColor));
    }

    private static int WithAlpha(int rgba, byte alpha)
    {
        return (rgba & unchecked((int)0xFFFFFF00)) | alpha;
    }

    internal readonly record struct CaretCell(int RowIndex, int ColIndex);
    public readonly record struct CaretQuad(int RowIndex, int ColIndex);
}

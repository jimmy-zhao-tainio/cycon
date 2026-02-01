using System;
using Cycon.Core.Transcript;
using Cycon.Layout.Metrics;
using Cycon.Layout.Overlays;
using Cycon.Render;

namespace Cycon.Rendering.Renderer;

internal static class OverlayTextInputRenderer
{
    private const int SelectionBackgroundRgba = unchecked((int)0xEEEEEEFF);
    private const int SelectionForegroundRgba = unchecked((int)0x000000FF);

    public static void Render(
        RenderCanvas canvas,
        in FixedCellGrid grid,
        OverlayTextInputFrame input,
        int foregroundRgba,
        int backgroundRgba)
    {
        var cellW = Math.Max(1, grid.CellWidthPx);
        var cellH = Math.Max(1, grid.CellHeightPx);

        var r = input.OuterRectPx;
        var outer = new RectPx(r.X, r.Y, r.Width, r.Height);
        if (outer.Width <= 0 || outer.Height <= 0)
        {
            return;
        }

        // Border language: same rounded 2px frame used by buttons/slab.
        var frameRect = BlockChromeRenderer.GetFrameRect(BlockChromeSpec.ViewDefault, outer, out var thickness);
        var innerRect = BlockChromeRenderer.DeflateRect(frameRect, thickness);
        var radius = 6;
        RoundedRectRenderer.DrawRoundedFrame(canvas, frameRect, thickness, radiusPx: radius, rgba: foregroundRgba);

        // Text area clip (inside stroke).
        canvas.PushClipRect(innerRect);

        // Text start uses the same "2 border cols + 1 pad col" convention as overlay buttons.
        var textStartX = outer.X + (cellW * 3);
        var textBaselineY = outer.Y + cellH;
        var drawX = textStartX - input.ScrollXPx;

        // Selection highlight (single-line).
        int selStart = 0;
        int selEnd = 0;
        var hasSelection = input.SelectionAnchorIndex is { } selectionAnchor && selectionAnchor != input.CaretIndex;
        if (input.SelectionAnchorIndex is { } anchor && anchor != input.CaretIndex)
        {
            selStart = Math.Clamp(Math.Min(anchor, input.CaretIndex), 0, input.Text.Length);
            selEnd = Math.Clamp(Math.Max(anchor, input.CaretIndex), 0, input.Text.Length);
            if (selEnd > selStart)
            {
                var selX = drawX + (selStart * cellW);
                var selW = (selEnd - selStart) * cellW;
                canvas.FillRect(new RectPx(selX, textBaselineY, selW, cellH), SelectionBackgroundRgba);
            }
        }

        // Draw text, clipping handles scroll.
        if (!string.IsNullOrEmpty(input.Text))
        {
            canvas.DrawText(input.Text, 0, input.Text.Length, drawX, textBaselineY, foregroundRgba);
            if (hasSelection && selEnd > selStart)
            {
                canvas.DrawText(input.Text, selStart, selEnd - selStart, drawX + (selStart * cellW), textBaselineY, SelectionForegroundRgba);
            }
        }

        // Caret: match prompt caret exactly (underline with alpha).
        if (input.CaretAlpha != 0)
        {
            var caretIndex = Math.Clamp(input.CaretIndex, 0, input.Text.Length);
            var caretX = drawX + (caretIndex * cellW);

            // caret row is the middle row of the 3-row input.
            var caretCellY = outer.Y + cellH;
            var underlineY = canvas.FontMetrics.GetUnderlineTopY(caretCellY);
            var underlineH = Math.Max(2, canvas.FontMetrics.UnderlineThicknessPx);
            var caretW = Math.Max(1, cellW - 1);
            canvas.FillRect(new RectPx(caretX, underlineY, caretW, underlineH), WithAlpha(foregroundRgba, input.CaretAlpha));
        }

        canvas.PopClipRect();
    }

    private static int WithAlpha(int rgba, byte alpha) =>
        (rgba & unchecked((int)0xFFFFFF00)) | alpha;
}

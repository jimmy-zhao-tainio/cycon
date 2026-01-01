using System;
using System.Collections.Generic;
using Cycon.Core.Fonts;
using Cycon.Core.Settings;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

internal static class ActivityIndicatorPass
{
    public static void MaybeAddIndicator(
        RenderFrame frame,
        IReadOnlyList<IBlock> blocks,
        IReadOnlyList<LayoutLine> lines,
        int lineIndex,
        LayoutLine line,
        int lineLengthCols,
        IReadOnlyDictionary<BlockId, BlockId> commandIndicators,
        HashSet<BlockId> indicatedCommandBlocks,
        ActivityIndicatorSettings settings,
        FontMetrics fontMetrics,
        FixedCellGrid grid,
        int rowOnScreen,
        int lineForegroundRgba,
        double timeSeconds)
    {
        if (!commandIndicators.TryGetValue(line.BlockId, out var activityBlockId))
        {
            return;
        }

        if (!indicatedCommandBlocks.Add(line.BlockId))
        {
            return;
        }

        if (!IsLastLineForBlock(lines, lineIndex))
        {
            return;
        }

        if (!TryGetBlockById(blocks, activityBlockId, out var activityBlock) || activityBlock is not IRunnableBlock runnable)
        {
            return;
        }

        AddActivityIndicator(frame, settings, fontMetrics, activityBlock, runnable, grid, rowOnScreen, lineForegroundRgba, timeSeconds, lineLengthCols);
    }

    private static void AddActivityIndicator(
        RenderFrame frame,
        ActivityIndicatorSettings settings,
        FontMetrics fontMetrics,
        IBlock block,
        IRunnableBlock runnable,
        FixedCellGrid grid,
        int rowOnScreen,
        int blockForegroundRgba,
        double timeSeconds,
        int lineLengthCols)
    {
        if (runnable.State != BlockRunState.Running)
        {
            return;
        }

        var rectY = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx);
        var rectW = grid.CellWidthPx;
        if (rectW <= 0 || lineLengthCols >= grid.Cols)
        {
            return;
        }

        // Place in the caret cell immediately after the command text.
        // (Aligned to the fixed grid to feel like a "block caret".)
        var rectX = grid.PaddingLeftPx + (lineLengthCols * grid.CellWidthPx);
        var underlineBottomExclusiveY = fontMetrics.GetUnderlineBottomExclusiveY(rectY);
        var capBottomExclusiveY = Math.Clamp(underlineBottomExclusiveY, rectY, rectY + grid.CellHeightPx);
        var rectH = capBottomExclusiveY - rectY;
        if (rectH <= 0)
        {
            return;
        }

        var fraction = (block as IProgressBlock)?.Progress.Fraction;
        if (IsDeterminateFraction(fraction))
        {
            AddProgressCaret(frame, rectX, rectY, rectW, rectH, blockForegroundRgba, settings, fraction!.Value);
            return;
        }

        AddPulsingCaret(frame, rectX, rectY, rectW, rectH, blockForegroundRgba, settings, timeSeconds);
    }

    private static bool IsDeterminateFraction(double? fraction)
    {
        if (fraction is null)
        {
            return false;
        }

        if (double.IsNaN(fraction.Value) || double.IsInfinity(fraction.Value))
        {
            return false;
        }

        return fraction.Value >= 0.0 && fraction.Value <= 1.0;
    }

    private static void AddProgressCaret(
        RenderFrame frame,
        int x,
        int y,
        int w,
        int h,
        int rgba,
        ActivityIndicatorSettings settings,
        double fraction)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var clamped = Math.Clamp(fraction, 0, 1);
        var track = WithAlpha(rgba, settings.ProgressTrackAlpha);
        frame.Add(new DrawQuad(x, y, w, h, track));

        var fillH = (int)Math.Round(h * clamped);
        if (fillH <= 0)
        {
            return;
        }

        var fill = WithAlpha(rgba, settings.ProgressFillAlpha);
        frame.Add(new DrawQuad(x, y + (h - fillH), w, fillH, fill));
    }

    private static void AddPulsingCaret(
        RenderFrame frame,
        int x,
        int y,
        int w,
        int h,
        int rgba,
        ActivityIndicatorSettings settings,
        double timeSeconds)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var period = settings.PulsePeriodSeconds <= 0 ? 1.0 : settings.PulsePeriodSeconds;
        var phase = (timeSeconds % period) / period; // 0..1
        var pulse = 0.5 - (0.5 * Math.Cos(phase * Math.PI * 2.0)); // 0..1

        var minA = settings.PulseMinAlpha;
        var maxA = settings.PulseMaxAlpha;
        var alpha = (byte)Math.Clamp((int)Math.Round(minA + ((maxA - minA) * pulse)), 0, 255);
        frame.Add(new DrawQuad(x, y, w, h, WithAlpha(rgba, alpha)));
    }

    private static bool IsLastLineForBlock(IReadOnlyList<LayoutLine> lines, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= lines.Count)
        {
            return false;
        }

        if (lineIndex == lines.Count - 1)
        {
            return true;
        }

        return lines[lineIndex + 1].BlockId != lines[lineIndex].BlockId;
    }

    private static bool TryGetBlockById(IReadOnlyList<IBlock> blocks, BlockId id, out IBlock block)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == id)
            {
                block = blocks[i];
                return true;
            }
        }

        block = null!;
        return false;
    }

    private static int WithAlpha(int rgba, byte alpha)
    {
        return (rgba & unchecked((int)0xFFFFFF00)) | alpha;
    }
}

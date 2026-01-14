using System;
using Cycon.Render;

namespace Cycon.Rendering.Inspect;

public static class InspectChromeRenderer
{
    public static void Draw(
        IRenderCanvas canvas,
        in InspectLayoutResult layout,
        in InspectChromeSpec spec,
        ref InspectChromeDataBuilder data,
        in RenderTheme theme,
        in TextMetrics metrics)
    {
        if (!spec.Enabled)
        {
            return;
        }

        var cellW = Math.Max(1, metrics.CellWidthPx);
        var cellH = Math.Max(1, metrics.CellHeightPx);
        var fg = theme.ForegroundRgba;
        var thicknessPx = 2;

        if (spec.Panels is not null)
        {
            for (var i = 0; i < spec.Panels.Length; i++)
            {
                var panel = spec.Panels[i];
                if (!panel.DrawSeparator)
                {
                    continue;
                }

                switch (panel.Edge)
                {
                    case InspectEdge.Top:
                        if (layout.TopPanelRect is { } topPanel)
                        {
                            DrawTopSeparator(canvas, layout.OuterRect, topPanel, cellH, thicknessPx, fg);
                        }
                        break;
                    case InspectEdge.Bottom:
                        if (layout.BottomPanelRect is { } bottomPanel)
                        {
                            DrawBottomSeparator(canvas, layout.OuterRect, bottomPanel, cellH, thicknessPx, fg);
                        }
                        break;
                    case InspectEdge.Left:
                        if (layout.LeftPanelRect is { } leftPanel)
                        {
                            DrawLeftSeparator(canvas, leftPanel, thicknessPx, fg);
                        }
                        break;
                    case InspectEdge.Right:
                        if (layout.RightPanelRect is { } rightPanel)
                        {
                            DrawRightSeparator(canvas, rightPanel, thicknessPx, fg);
                        }
                        break;
                }
            }
        }

        if (spec.TextRows is not null)
        {
            for (var i = 0; i < spec.TextRows.Length; i++)
            {
                var row = spec.TextRows[i];
                if (!TryGetPanelRect(layout, row.Edge, out var panelRect))
                {
                    continue;
                }

                var maxRows = Math.Max(0, panelRect.Height / cellH);
                if (row.RowIndex < 0 || row.RowIndex >= maxRows)
                {
                    continue;
                }

                var yPx = panelRect.Y + (row.RowIndex * cellH);
                var cols = Math.Max(0, panelRect.Width / cellW);
                if (cols <= 0)
                {
                    continue;
                }

                DrawAlignedText(canvas, panelRect.X, yPx, cols, cellW, fg, row.LeftKey, Align.Left, ref data);
                DrawAlignedText(canvas, panelRect.X, yPx, cols, cellW, fg, row.CenterKey, Align.Center, ref data);
                DrawAlignedText(canvas, panelRect.X, yPx, cols, cellW, fg, row.RightKey, Align.Right, ref data);
            }
        }
    }

    private static bool TryGetPanelRect(in InspectLayoutResult layout, InspectEdge edge, out RectPx rect)
    {
        switch (edge)
        {
            case InspectEdge.Top:
                if (layout.TopPanelRect is { } top)
                {
                    rect = top;
                    return true;
                }
                break;
            case InspectEdge.Bottom:
                if (layout.BottomPanelRect is { } bottom)
                {
                    rect = bottom;
                    return true;
                }
                break;
            case InspectEdge.Left:
                if (layout.LeftPanelRect is { } left)
                {
                    rect = left;
                    return true;
                }
                break;
            case InspectEdge.Right:
                if (layout.RightPanelRect is { } right)
                {
                    rect = right;
                    return true;
                }
                break;
        }

        rect = default;
        return false;
    }

    private static void DrawTopSeparator(IRenderCanvas canvas, RectPx outerRect, RectPx topPanelRect, int cellH, int thicknessPx, int rgba)
    {
        if (topPanelRect.Height < cellH || outerRect.Width <= 0)
        {
            return;
        }

        var rowTop = topPanelRect.Y + (topPanelRect.Height - cellH);
        var yPx = rowTop + Math.Max(0, (cellH - thicknessPx) / 2);
        canvas.FillRect(new RectPx(outerRect.X, yPx, outerRect.Width, thicknessPx), rgba);
    }

    private static void DrawBottomSeparator(IRenderCanvas canvas, RectPx outerRect, RectPx bottomPanelRect, int cellH, int thicknessPx, int rgba)
    {
        if (bottomPanelRect.Height < cellH || outerRect.Width <= 0)
        {
            return;
        }

        var rowTop = bottomPanelRect.Y;
        var yPx = rowTop + Math.Max(0, (cellH - thicknessPx) / 2);
        canvas.FillRect(new RectPx(outerRect.X, yPx, outerRect.Width, thicknessPx), rgba);
    }

    private static void DrawLeftSeparator(IRenderCanvas canvas, RectPx leftPanelRect, int thicknessPx, int rgba)
    {
        if (leftPanelRect.Width <= 0 || leftPanelRect.Height <= 0)
        {
            return;
        }

        var xPx = leftPanelRect.X + Math.Max(0, leftPanelRect.Width - thicknessPx);
        canvas.FillRect(new RectPx(xPx, leftPanelRect.Y, thicknessPx, leftPanelRect.Height), rgba);
    }

    private static void DrawRightSeparator(IRenderCanvas canvas, RectPx rightPanelRect, int thicknessPx, int rgba)
    {
        if (rightPanelRect.Width <= 0 || rightPanelRect.Height <= 0)
        {
            return;
        }

        var xPx = rightPanelRect.X;
        canvas.FillRect(new RectPx(xPx, rightPanelRect.Y, thicknessPx, rightPanelRect.Height), rgba);
    }

    private static void DrawAlignedText(
        IRenderCanvas canvas,
        int panelLeftPx,
        int rowTopYPx,
        int cols,
        int cellW,
        int rgba,
        string? key,
        Align align,
        ref InspectChromeDataBuilder data)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!data.TryGet(key, out var text))
        {
            return;
        }

        if (string.IsNullOrEmpty(text) || cols <= 0)
        {
            return;
        }

        var len = Math.Min(cols, text.Length);
        if (len <= 0)
        {
            return;
        }

        var startCol = align switch
        {
            Align.Left => 0,
            Align.Center => Math.Max(0, (cols - len) / 2),
            Align.Right => Math.Max(0, cols - len),
            _ => 0
        };

        var maxLen = Math.Min(len, cols - startCol);
        if (maxLen <= 0)
        {
            return;
        }

        var xPx = panelLeftPx + (startCol * cellW);
        canvas.DrawText(text, 0, maxLen, xPx, rowTopYPx, rgba);
    }

    private enum Align
    {
        Left,
        Center,
        Right
    }
}

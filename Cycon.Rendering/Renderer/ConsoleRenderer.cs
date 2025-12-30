using System;
using System.Collections.Generic;
using Cycon.Core;
using Cycon.Core.Metrics;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Rendering.Commands;
using Cycon.Rendering.Glyphs;

namespace Cycon.Rendering.Renderer;

public sealed class ConsoleRenderer
{
    private const int DefaultColor = unchecked((int)0xFFFFFFFF);

    public RenderFrame Render(ConsoleDocument document, LayoutFrame layout, GlyphAtlas atlas)
    {
        var frame = new RenderFrame();
        frame.BuiltGrid = new GridSize(layout.Grid.Cols, layout.Grid.Rows);
        var grid = layout.Grid;
        var scrollOffsetRows = GetScrollOffsetRows(document, layout);
        var pendingCaret = default(CaretQuad?);

        foreach (var line in layout.Lines)
        {
            var rowOnScreen = line.RowIndex - scrollOffsetRows;
            if (rowOnScreen < 0 || rowOnScreen >= grid.Rows)
            {
                continue;
            }

            var block = document.Transcript.Blocks[line.BlockIndex];
            var text = GetBlockText(block);
            if (line.Start + line.Length > text.Length)
            {
                continue;
            }

            if (block is PromptBlock prompt && pendingCaret is null)
            {
                var caret = ComputeCaret(prompt, layout, line.BlockIndex);
                if (caret is { } c)
                {
                    pendingCaret = new CaretQuad(c.RowIndex, c.ColIndex);
                }
            }

            if (line.Length == 0)
            {
                continue;
            }

            var glyphs = new List<GlyphInstance>(line.Length);
            for (var i = 0; i < line.Length; i++)
            {
                var charIndex = line.Start + i;
                var codepoint = text[charIndex];

                if (!atlas.TryGetMetrics(codepoint, out var metrics))
                {
                    continue;
                }

                var cellX = grid.PaddingLeftPx + (i * grid.CellWidthPx);
                var cellY = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx);
                var baselineY = cellY + atlas.BaselinePx;

                var glyphX = cellX + metrics.BearingX;
                var glyphY = baselineY - metrics.BearingY;

                glyphs.Add(new GlyphInstance(codepoint, glyphX, glyphY, DefaultColor));
            }

            if (glyphs.Count > 0)
            {
                frame.Add(new DrawGlyphRun(0, 0, glyphs));
            }
        }

        if (pendingCaret is { } caretQuad)
        {
            var rowOnScreen = caretQuad.RowIndex - scrollOffsetRows;
            if (rowOnScreen >= 0 && rowOnScreen < grid.Rows)
            {
                var caretX = grid.PaddingLeftPx + (caretQuad.ColIndex * grid.CellWidthPx);
                var caretY = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx) + (grid.CellHeightPx - 2);
                frame.Add(new DrawQuad(caretX, caretY, grid.CellWidthPx, 2, DefaultColor));
            }
        }

        return frame;
    }

    private static int GetScrollOffsetRows(ConsoleDocument document, LayoutFrame layout)
    {
        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        return Math.Clamp(document.Scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
    }

    private static string GetBlockText(IBlock block)
    {
        return block switch
        {
            TextBlock textBlock => textBlock.Text,
            PromptBlock promptBlock => promptBlock.Prompt + promptBlock.Input,
            ImageBlock => throw new NotSupportedException("ImageBlock rendering not implemented in Blocks v0."),
            Scene3DBlock => throw new NotSupportedException("Scene3DBlock rendering not implemented in Blocks v0."),
            _ => string.Empty
        };
    }

    private static CaretCell? ComputeCaret(PromptBlock prompt, LayoutFrame layout, int promptBlockIndex)
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
                return new CaretCell(line.RowIndex, caretCharIndex - line.Start);
            }

            if (caretCharIndex == line.Start + line.Length && line.Length < layout.Grid.Cols)
            {
                best = new CaretCell(line.RowIndex, caretCharIndex - line.Start);
            }
        }

        return best;
    }

    private readonly record struct CaretCell(int RowIndex, int ColIndex);
    private readonly record struct CaretQuad(int RowIndex, int ColIndex);
}

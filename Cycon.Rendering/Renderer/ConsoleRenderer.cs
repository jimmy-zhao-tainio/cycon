using System;
using System.Collections.Generic;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Metrics;
using Cycon.Core.Selection;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Rendering.Commands;
using Cycon.Rendering.Styling;

namespace Cycon.Rendering.Renderer;

public sealed class ConsoleRenderer
{
    public RenderFrame Render(
        ConsoleDocument document,
        LayoutFrame layout,
        IConsoleFont font,
        SelectionStyle selectionStyle,
        byte caretAlpha = 0xFF)
    {
        var defaultForeground = document.Settings.DefaultTextStyle.ForegroundRgba;
        var frame = new RenderFrame();
        frame.BuiltGrid = new GridSize(layout.Grid.Cols, layout.Grid.Rows);
        var grid = layout.Grid;
        var scrollOffsetRows = GetScrollOffsetRows(document, layout);
        var pendingCaret = default(CaretQuad?);
        var selection = ComputeSelectionBounds(document);
        var selectionBackground = selectionStyle.SelectedBackgroundRgba;
        var fontMetrics = font.Metrics;

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

            if (selection is { } s && selectionBackground.HasValue)
            {
                AddSelectionBackgroundRuns(
                    frame,
                    block,
                    s,
                    line,
                    grid,
                    rowOnScreen,
                    selectionBackground.Value);
            }

            var glyphs = new List<GlyphInstance>(line.Length);
            for (var i = 0; i < line.Length; i++)
            {
                var charIndex = line.Start + i;
                var codepoint = text[charIndex];

                var glyph = font.MapGlyph(codepoint);

                var cellX = grid.PaddingLeftPx + (i * grid.CellWidthPx);
                var cellY = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx);
                var baselineY = cellY + fontMetrics.BaselinePx;

                var glyphX = cellX + glyph.BearingX;
                var glyphY = baselineY - glyph.BearingY;

                var color = selection is { } bounds && IsSelectablePromptChar(block, charIndex) && bounds.Contains(line.BlockIndex, charIndex)
                    ? selectionStyle.SelectedForegroundRgba
                    : defaultForeground;
                glyphs.Add(new GlyphInstance(glyph.Codepoint, glyphX, glyphY, color));
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
                const int caretCodepoint = '_';
                var glyph = font.MapGlyph(caretCodepoint);
                var cellX = grid.PaddingLeftPx + (caretQuad.ColIndex * grid.CellWidthPx);
                var cellY = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx);
                var baselineY = cellY + fontMetrics.BaselinePx;

                var glyphX = cellX + glyph.BearingX;
                var glyphY = baselineY - glyph.BearingY;
                if (caretAlpha != 0)
                {
                    var caretColor = WithAlpha(defaultForeground, caretAlpha);
                    frame.Add(new DrawGlyphRun(0, 0, new[] { new GlyphInstance(glyph.Codepoint, glyphX, glyphY, caretColor) }));
                }
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

    private static void AddSelectionBackgroundRuns(
        RenderFrame frame,
        IBlock block,
        SelectionBounds selection,
        LayoutLine line,
        FixedCellGrid grid,
        int rowOnScreen,
        int backgroundRgba)
    {
        var runStart = -1;
        for (var i = 0; i < line.Length; i++)
        {
            var charIndex = line.Start + i;
            var selected = IsSelectablePromptChar(block, charIndex) && selection.Contains(line.BlockIndex, charIndex);
            if (selected)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }
            }
            else if (runStart >= 0)
            {
                AddRun(frame, grid, rowOnScreen, runStart, i - runStart, backgroundRgba);
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            AddRun(frame, grid, rowOnScreen, runStart, line.Length - runStart, backgroundRgba);
        }
    }

    private static void AddRun(
        RenderFrame frame,
        FixedCellGrid grid,
        int rowOnScreen,
        int colStart,
        int colLength,
        int rgba)
    {
        if (colLength <= 0)
        {
            return;
        }

        var x = grid.PaddingLeftPx + (colStart * grid.CellWidthPx);
        var y = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx);
        var w = colLength * grid.CellWidthPx;
        var h = grid.CellHeightPx;
        frame.Add(new DrawQuad(x, y, w, h, rgba));
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

    private static bool IsSelectablePromptChar(IBlock block, int charIndex)
    {
        if (block is not PromptBlock prompt)
        {
            return true;
        }

        return charIndex >= prompt.PromptPrefixLength;
    }

    private static SelectionBounds? ComputeSelectionBounds(ConsoleDocument document)
    {
        if (document.Selection.ActiveRange is not { } range)
        {
            return null;
        }

        if (range.Anchor == range.Caret)
        {
            return null;
        }

        var blocks = document.Transcript.Blocks;
        if (!TryFindBlockIndex(blocks, range.Anchor.BlockId, out var aBlockIndex) ||
            !TryFindBlockIndex(blocks, range.Caret.BlockId, out var cBlockIndex))
        {
            return null;
        }

        var a = (BlockIndex: aBlockIndex, Index: range.Anchor.Index);
        var c = (BlockIndex: cBlockIndex, Index: range.Caret.Index);
        a = ClampPromptSelectionStart(blocks, a);
        c = ClampPromptSelectionStart(blocks, c);
        if (a.BlockIndex > c.BlockIndex || (a.BlockIndex == c.BlockIndex && a.Index > c.Index))
        {
            (a, c) = (c, a);
        }

        return new SelectionBounds(a.BlockIndex, a.Index, c.BlockIndex, c.Index);
    }

    private static (int BlockIndex, int Index) ClampPromptSelectionStart(
        IReadOnlyList<IBlock> blocks,
        (int BlockIndex, int Index) position)
    {
        if (position.BlockIndex < 0 || position.BlockIndex >= blocks.Count)
        {
            return position;
        }

        if (blocks[position.BlockIndex] is not PromptBlock prompt)
        {
            return position;
        }

        var clampedIndex = Math.Max(position.Index, prompt.PromptPrefixLength);
        return (position.BlockIndex, clampedIndex);
    }

    private static bool TryFindBlockIndex(IReadOnlyList<IBlock> blocks, Cycon.Core.Transcript.BlockId id, out int index)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == id)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private readonly record struct SelectionBounds(int StartBlockIndex, int StartIndex, int EndBlockIndex, int EndIndex)
    {
        public bool Contains(int blockIndex, int charIndex)
        {
            if (blockIndex < StartBlockIndex || blockIndex > EndBlockIndex)
            {
                return false;
            }

            if (StartBlockIndex == EndBlockIndex)
            {
                return charIndex >= StartIndex && charIndex < EndIndex;
            }

            if (blockIndex == StartBlockIndex)
            {
                return charIndex >= StartIndex;
            }

            if (blockIndex == EndBlockIndex)
            {
                return charIndex < EndIndex;
            }

            return true;
        }
    }

    private static int WithAlpha(int rgba, byte alpha)
    {
        return (rgba & unchecked((int)0xFFFFFF00)) | alpha;
    }
}

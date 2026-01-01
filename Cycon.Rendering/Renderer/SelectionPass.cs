using System;
using Cycon.Core;
using Cycon.Core.Selection;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

internal static class SelectionPass
{
    public static SelectionBounds? ComputeSelectionBounds(ConsoleDocument document)
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

    public static void AddSelectionBackgroundRuns(
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

    public static bool IsSelectablePromptChar(IBlock block, int charIndex)
    {
        if (block is not PromptBlock prompt)
        {
            return true;
        }

        return charIndex >= prompt.PromptPrefixLength;
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

    private static bool TryFindBlockIndex(IReadOnlyList<IBlock> blocks, BlockId id, out int index)
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

    public readonly record struct SelectionBounds(int StartBlockIndex, int StartIndex, int EndBlockIndex, int EndIndex)
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
}

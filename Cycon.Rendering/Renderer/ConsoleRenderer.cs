using System;
using System.Collections.Generic;
using System.Text;
using Cycon.Core;
using Cycon.Core.Metrics;
using Cycon.Core.Styling;
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

        foreach (var line in layout.Lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var rowOnScreen = line.RowIndex - scrollOffsetRows;
            if (rowOnScreen < 0 || rowOnScreen >= grid.Rows)
            {
                continue;
            }

            var block = document.Transcript.Blocks[line.BlockIndex];
            var blockText = GetBlockText(block);
            if (line.Start + line.Length > blockText.Text.Length)
            {
                continue;
            }

            var glyphs = new List<GlyphInstance>(line.Length);
            for (var i = 0; i < line.Length; i++)
            {
                var charIndex = line.Start + i;
                var codepoint = blockText.Text[charIndex];

                if (!atlas.TryGetMetrics(codepoint, out var metrics))
                {
                    continue;
                }

                var cellX = grid.PaddingLeftPx + (i * grid.CellWidthPx);
                var cellY = grid.PaddingTopPx + (rowOnScreen * grid.CellHeightPx);
                var baselineY = cellY + atlas.BaselinePx;

                var glyphX = cellX + metrics.BearingX;
                var glyphY = baselineY - metrics.BearingY;

                var color = blockText.GetColorAt(charIndex, DefaultColor);
                glyphs.Add(new GlyphInstance(codepoint, glyphX, glyphY, color));
            }

            if (glyphs.Count > 0)
            {
                frame.Add(new DrawGlyphRun(0, 0, glyphs));
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

    private static BlockText GetBlockText(IBlock block)
    {
        return block switch
        {
            TextBlock textBlock => BuildText(textBlock),
            PromptBlock promptBlock => new BlockText(promptBlock.PromptText, null),
            _ => new BlockText(string.Empty, null)
        };
    }

    private static BlockText BuildText(TextBlock block)
    {
        if (block.Spans.Count == 1)
        {
            return new BlockText(block.Spans[0].Text, block.Spans);
        }

        var builder = new StringBuilder();
        foreach (var span in block.Spans)
        {
            builder.Append(span.Text);
        }

        return new BlockText(builder.ToString(), block.Spans);
    }

    private readonly record struct BlockText(string Text, IReadOnlyList<TextSpan>? Spans)
    {
        public int GetColorAt(int charIndex, int fallbackColor)
        {
            if (Spans is null)
            {
                return fallbackColor;
            }

            var offset = 0;
            foreach (var span in Spans)
            {
                var spanLength = span.Text.Length;
                if (charIndex < offset + spanLength)
                {
                    return span.Style.ForegroundRgba;
                }

                offset += spanLength;
            }

            return fallbackColor;
        }
    }
}

using System;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Settings;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

internal static class TextPass
{
    public static string GetBlockText(IBlock block)
    {
        if (block.Kind == BlockKind.Scene3D)
        {
            return string.Empty;
        }

        return block switch
        {
            TextBlock textBlock => textBlock.Text,
            RichTextBlock richTextBlock => richTextBlock.Text,
            ActivityBlock activityBlock => activityBlock.ExportText(0, activityBlock.TextLength),
            PromptBlock promptBlock => promptBlock.Prompt + promptBlock.Input,
            ImageBlock => throw new NotSupportedException("ImageBlock rendering not implemented in Blocks v0."),
            Scene3DBlock => throw new NotSupportedException("Scene3DBlock rendering not implemented in Blocks v0."),
            _ => string.Empty
        };
    }

    public static int GetBlockForeground(ConsoleSettings settings, IBlock block)
    {
        if (block is TextBlock textBlock)
        {
            return textBlock.Stream switch
            {
                ConsoleTextStream.Stdout => settings.StdoutTextStyle.ForegroundRgba,
                ConsoleTextStream.Stderr => settings.StderrTextStyle.ForegroundRgba,
                ConsoleTextStream.System => settings.SystemTextStyle.ForegroundRgba,
                _ => settings.DefaultTextStyle.ForegroundRgba
            };
        }

        if (block is RichTextBlock richTextBlock)
        {
            return richTextBlock.Stream switch
            {
                ConsoleTextStream.Stdout => settings.StdoutTextStyle.ForegroundRgba,
                ConsoleTextStream.Stderr => settings.StderrTextStyle.ForegroundRgba,
                ConsoleTextStream.System => settings.SystemTextStyle.ForegroundRgba,
                _ => settings.DefaultTextStyle.ForegroundRgba
            };
        }

        if (block is ActivityBlock activityBlock)
        {
            return activityBlock.Stream switch
            {
                ConsoleTextStream.Stdout => settings.StdoutTextStyle.ForegroundRgba,
                ConsoleTextStream.Stderr => settings.StderrTextStyle.ForegroundRgba,
                ConsoleTextStream.System => settings.SystemTextStyle.ForegroundRgba,
                _ => settings.DefaultTextStyle.ForegroundRgba
            };
        }

        return settings.DefaultTextStyle.ForegroundRgba;
    }

    public static void AddGlyphRun(
        RenderFrame frame,
        IConsoleFont font,
        FontMetrics fontMetrics,
        FixedCellGrid grid,
        LayoutLine line,
        int rowOnScreen,
        IBlock block,
        string text,
        int lineForeground,
        SelectionPass.SelectionBounds? selection,
        int selectionForegroundRgba,
        HitTestActionSpan? hoveredActionSpan = null,
        HitTestActionSpan? selectedActionSpan = null,
        int? invertedTextRgba = null)
    {
        var glyphs = new List<GlyphInstance>(line.Length);
        var invert = invertedTextRgba.HasValue;
        var inv = invertedTextRgba.GetValueOrDefault();
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

            var color = lineForeground;
            if (selection is { } bounds &&
                SelectionPass.IsSelectablePromptChar(block, charIndex) &&
                bounds.Contains(line.BlockIndex, charIndex))
            {
                color = selectionForegroundRgba;
            }
            else if (invert &&
                     block.Id == selectedActionSpan?.BlockId &&
                     charIndex >= selectedActionSpan.Value.CharStart &&
                     charIndex < selectedActionSpan.Value.CharStart + selectedActionSpan.Value.CharLength)
            {
                color = inv;
            }
            else if (invert &&
                     block.Id == hoveredActionSpan?.BlockId &&
                     charIndex >= hoveredActionSpan.Value.CharStart &&
                     charIndex < hoveredActionSpan.Value.CharStart + hoveredActionSpan.Value.CharLength)
            {
                color = inv;
            }
            glyphs.Add(new GlyphInstance(glyph.Codepoint, glyphX, glyphY, color));
        }

        if (glyphs.Count > 0)
        {
            frame.Add(new DrawGlyphRun(0, 0, glyphs));
        }
    }
}

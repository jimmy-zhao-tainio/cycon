using System.Collections.Generic;
using Cycon.Rendering.Glyphs;
using BackendRender = Cycon.Backends.Abstractions.Rendering;
using RenderingCommands = Cycon.Rendering.Commands;

namespace Cycon.Host.Services;

public static class RenderFrameAdapter
{
    public static BackendRender.RenderFrame Adapt(Cycon.Rendering.RenderFrame frame)
    {
        var adapted = new BackendRender.RenderFrame();
        adapted.BuiltGrid = frame.BuiltGrid;
        foreach (var command in frame.Commands)
        {
            switch (command)
            {
                case RenderingCommands.DrawGlyphRun glyphRun:
                    adapted.Add(new BackendRender.DrawGlyphRun(
                        glyphRun.X,
                        glyphRun.Y,
                        AdaptGlyphs(glyphRun.Glyphs)));
                    break;
                case RenderingCommands.DrawQuad quad:
                    adapted.Add(new BackendRender.DrawQuad(
                        quad.X,
                        quad.Y,
                        quad.Width,
                        quad.Height,
                        quad.Rgba));
                    break;
                case RenderingCommands.PushClip clip:
                    adapted.Add(new BackendRender.PushClip(
                        clip.X,
                        clip.Y,
                        clip.Width,
                        clip.Height));
                    break;
                case RenderingCommands.PopClip:
                    adapted.Add(new BackendRender.PopClip());
                    break;
            }
        }

        return adapted;
    }

    public static BackendRender.GlyphAtlasData Adapt(GlyphAtlas atlas)
    {
        var metrics = new Dictionary<int, BackendRender.GlyphMetricsData>();
        foreach (var entry in atlas.Metrics)
        {
            var glyph = entry.Value;
            metrics[entry.Key] = new BackendRender.GlyphMetricsData(
                glyph.Codepoint,
                glyph.Width,
                glyph.Height,
                glyph.BearingX,
                glyph.BearingY,
                glyph.AdvanceX,
                glyph.AtlasX,
                glyph.AtlasY);
        }

        return new BackendRender.GlyphAtlasData(
            atlas.Width,
            atlas.Height,
            atlas.CellWidthPx,
            atlas.CellHeightPx,
            atlas.BaselinePx,
            metrics,
            atlas.Pixels);
    }

    private static IReadOnlyList<BackendRender.GlyphInstance> AdaptGlyphs(IReadOnlyList<Cycon.Rendering.Commands.GlyphInstance> glyphs)
    {
        var adapted = new BackendRender.GlyphInstance[glyphs.Count];
        for (var i = 0; i < glyphs.Count; i++)
        {
            var glyph = glyphs[i];
            adapted[i] = new BackendRender.GlyphInstance(
                glyph.Codepoint,
                glyph.X,
                glyph.Y,
                glyph.Rgba);
        }

        return adapted;
    }
}

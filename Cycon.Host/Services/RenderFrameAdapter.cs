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
                case RenderingCommands.DrawTriangles triangles:
                    adapted.Add(new BackendRender.DrawTriangles(AdaptVertices(triangles.Vertices)));
                    break;
                case RenderingCommands.DrawMesh3D drawMesh:
                    adapted.Add(new BackendRender.DrawMesh3D(
                        drawMesh.MeshId,
                        drawMesh.VertexData,
                        drawMesh.VertexCount,
                        drawMesh.ViewportRectPx,
                        drawMesh.Model,
                        drawMesh.View,
                        drawMesh.Proj,
                        drawMesh.LightDirView,
                        drawMesh.Settings));
                    break;
                case RenderingCommands.DrawVignetteQuad vignette:
                    adapted.Add(new BackendRender.DrawVignetteQuad(
                        vignette.X,
                        vignette.Y,
                        vignette.Width,
                        vignette.Height,
                        vignette.Strength,
                        vignette.Inner,
                        vignette.Outer));
                    break;
                case RenderingCommands.SetDebugTag tag:
                    adapted.Add(new BackendRender.SetDebugTag(tag.Tag));
                    break;
                case RenderingCommands.SetCullState cull:
                    adapted.Add(new BackendRender.SetCullState(cull.Enabled, cull.FrontFaceCcw));
                    break;
                case RenderingCommands.SetColorWrite colorWrite:
                    adapted.Add(new BackendRender.SetColorWrite(colorWrite.Enabled));
                    break;
                case RenderingCommands.SetDepthState depthState:
                    adapted.Add(new BackendRender.SetDepthState(depthState.Enabled, depthState.WriteEnabled, (BackendRender.DepthFuncKind)depthState.Func));
                    break;
                case RenderingCommands.ClearDepth clearDepth:
                    adapted.Add(new BackendRender.ClearDepth(clearDepth.Depth01));
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

    private static IReadOnlyList<BackendRender.SolidVertex> AdaptVertices(IReadOnlyList<Cycon.Rendering.Commands.SolidVertex> vertices)
    {
        var adapted = new BackendRender.SolidVertex[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            adapted[i] = new BackendRender.SolidVertex(v.X, v.Y, v.Rgba);
        }

        return adapted;
    }


}

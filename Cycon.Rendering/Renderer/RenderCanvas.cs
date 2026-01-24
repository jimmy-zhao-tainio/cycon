using System.Collections.Generic;
using System.Numerics;
using Cycon.Core.Fonts;
using Cycon.Render;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

internal sealed class RenderCanvas : IRenderCanvas
{
    private readonly RenderFrame _frame;
    private readonly IConsoleFont _font;

    public RenderCanvas(RenderFrame frame, IConsoleFont font)
    {
        _frame = frame;
        _font = font;
    }

    public void SetCullState(bool enabled, bool frontFaceCcw) =>
        _frame.Add(new SetCullState(enabled, frontFaceCcw));

    public void SetDebugTag(int tag) =>
        _frame.Add(new SetDebugTag(tag));

    public void PushClipRect(in RectPx rectPx) =>
        _frame.Add(new PushClip(rectPx.X, rectPx.Y, rectPx.Width, rectPx.Height));

    public void PopClipRect() =>
        _frame.Add(new PopClip());

    public void FillRect(in RectPx rectPx, int rgba) =>
        _frame.Add(new DrawQuad(rectPx.X, rectPx.Y, rectPx.Width, rectPx.Height, rgba));

    public void SetColorWrite(bool enabled) =>
        _frame.Add(new SetColorWrite(enabled));

    public void SetDepthState(bool enabled, bool writeEnabled, DepthFunc func) =>
        _frame.Add(new SetDepthState(enabled, writeEnabled, Map(func)));

    public void ClearDepth(float depth01) =>
        _frame.Add(new ClearDepth(depth01));

    public void ReleaseMesh3D(int meshId) =>
        _frame.Add(new ReleaseMesh3D(meshId));

    public void ReleaseImage2D(int imageId) =>
        _frame.Add(new ReleaseImage2D(imageId));

    public void DrawMesh3D(
        int meshId,
        float[] vertexData,
        int vertexCount,
        in RectPx viewportRectPx,
        in Matrix4x4 model,
        in Matrix4x4 view,
        in Matrix4x4 proj,
        in Vector3 lightDirView,
        in Scene3DRenderSettings settings) =>
        _frame.Add(new DrawMesh3D(meshId, vertexData, vertexCount, viewportRectPx, model, view, proj, lightDirView, settings));

    public void DrawVignette(in RectPx rectPx, float strength01, float inner, float outer) =>
        _frame.Add(new DrawVignetteQuad(rectPx.X, rectPx.Y, rectPx.Width, rectPx.Height, strength01, inner, outer));

    public void DrawText(string text, int start, int length, int xPx, int yPx, int rgba)
    {
        if (string.IsNullOrEmpty(text) || length <= 0 || start < 0 || start >= text.Length)
        {
            return;
        }

        length = Math.Min(length, text.Length - start);
        if (length <= 0)
        {
            return;
        }

        var metrics = _font.Metrics;
        var baselineY = yPx + metrics.BaselinePx;
        var glyphs = new List<GlyphInstance>(length);

        for (var i = 0; i < length; i++)
        {
            var ch = text[start + i];
            if (ch is '\r' or '\n')
            {
                continue;
            }

            if (ch < 32 && ch != '\t')
            {
                ch = ' ';
            }

            var glyph = _font.MapGlyph(ch);
            var cellX = xPx + (i * metrics.CellWidthPx);
            var glyphX = cellX + glyph.BearingX;
            var glyphY = baselineY - glyph.BearingY;
            glyphs.Add(new GlyphInstance(glyph.Codepoint, glyphX, glyphY, rgba));
        }

        if (glyphs.Count > 0)
        {
            _frame.Add(new DrawGlyphRun(0, 0, glyphs));
        }
    }

    public void DrawImage2D(int imageId, byte[] rgbaPixels, int width, int height, in RectF destRectPx, bool useNearest)
    {
        if (rgbaPixels is null || rgbaPixels.Length == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        if (destRectPx.Width <= 0 || destRectPx.Height <= 0)
        {
            return;
        }

        _frame.Add(new DrawImage2D(imageId, rgbaPixels, width, height, destRectPx, useNearest));
    }

    private static DepthFuncKind Map(DepthFunc func) =>
        func switch
        {
            DepthFunc.Less => DepthFuncKind.Less,
            DepthFunc.Lequal => DepthFuncKind.Lequal,
            DepthFunc.Greater => DepthFuncKind.Greater,
            DepthFunc.Always => DepthFuncKind.Always,
            _ => DepthFuncKind.Less
        };
}

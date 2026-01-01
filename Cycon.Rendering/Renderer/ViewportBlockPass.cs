using System;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Settings;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Render;

namespace Cycon.Rendering.Renderer;

internal static class ViewportBlockPass
{
    public static void RenderViewportsStartingAtRow(
        RenderCanvas canvas,
        ConsoleDocument document,
        LayoutFrame layout,
        FontMetrics fontMetrics,
        int scrollYPx,
        int rowIndex,
        ref int nextSceneViewportIndex,
        double timeSeconds)
    {
        var viewports = layout.Scene3DViewports;
        if (viewports.Count == 0)
        {
            return;
        }

        var theme = new RenderTheme(
            ForegroundRgba: document.Settings.DefaultTextStyle.ForegroundRgba,
            BackgroundRgba: document.Settings.DefaultTextStyle.BackgroundRgba);

        var textMetrics = new TextMetrics(
            CellWidthPx: fontMetrics.CellWidthPx,
            CellHeightPx: fontMetrics.CellHeightPx,
            BaselinePx: fontMetrics.BaselinePx,
            UnderlineThicknessPx: Math.Max(1, fontMetrics.UnderlineThicknessPx),
            UnderlineTopOffsetPx: fontMetrics.UnderlineTopOffsetPx);

        var scene3D = MapScene3D(document.Settings.Scene3D);

        var blocks = document.Transcript.Blocks;
        while (nextSceneViewportIndex < viewports.Count &&
               viewports[nextSceneViewportIndex].RowIndex == rowIndex)
        {
            var viewport = viewports[nextSceneViewportIndex++];
            if (viewport.BlockIndex < 0 || viewport.BlockIndex >= blocks.Count)
            {
                continue;
            }

            var rect = viewport.ViewportRectPx;
            var viewportRect = new RectPx(rect.X, rect.Y - scrollYPx, rect.Width, rect.Height);
            if (viewportRect.Width <= 0 || viewportRect.Height <= 0)
            {
                continue;
            }

            // Skip fully off-screen.
            if (viewportRect.Y >= layout.Grid.FramebufferHeightPx || viewportRect.Y + viewportRect.Height <= 0)
            {
                continue;
            }

            if (blocks[viewport.BlockIndex] is not IRenderBlock renderBlock)
            {
                continue;
            }

            canvas.PushClipRect(viewportRect);
            renderBlock.Render(canvas, new BlockRenderContext(viewportRect, timeSeconds, theme, textMetrics, scene3D));
            canvas.PopClipRect();
        }
    }

    private static Scene3DRenderSettings MapScene3D(Scene3DSettings s)
    {
        return new Scene3DRenderSettings(
            SolidAmbient: s.SolidAmbient,
            SolidDiffuseStrength: s.SolidDiffuseStrength,
            ToneGamma: s.ToneGamma,
            ToneGain: s.ToneGain,
            ToneLift: s.ToneLift,
            VignetteStrength: s.VignetteStrength,
            VignetteInner: s.VignetteInner,
            VignetteOuter: s.VignetteOuter,
            ShowVertexDots: s.ShowVertexDots,
            VertexDotMaxVertices: s.VertexDotMaxVertices,
            VertexDotMaxDots: s.VertexDotMaxDots);
    }
}

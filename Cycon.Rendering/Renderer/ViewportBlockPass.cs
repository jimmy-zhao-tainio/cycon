using System;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Settings;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Render;

namespace Cycon.Rendering.Renderer;

internal static class ViewportBlockPass
{
    private const int PanelBgRgba = unchecked((int)0xEEEEEEFF);
    private const int ViewportChromeBorderRgba = unchecked((int)0x000000FF);

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

            var outerRect = viewport.ViewportRectPx;
            var outerViewportRect = new RectPx(outerRect.X, outerRect.Y - scrollYPx, outerRect.Width, outerRect.Height);
            if (outerViewportRect.Width <= 0 || outerViewportRect.Height <= 0)
            {
                continue;
            }

            // Skip fully off-screen.
            if (outerViewportRect.Y >= layout.Grid.FramebufferHeightPx || outerViewportRect.Y + outerViewportRect.Height <= 0)
            {
                continue;
            }

            if (blocks[viewport.BlockIndex] is not IRenderBlock renderBlock)
            {
                continue;
            }

            if (viewport.Chrome.Enabled)
            {
                DrawChrome(canvas, viewport.Chrome, outerViewportRect, ViewportChromeBorderRgba);
            }

            var innerRect = viewport.InnerViewportRectPx;
            var innerViewportRect = new RectPx(innerRect.X, innerRect.Y - scrollYPx, innerRect.Width, innerRect.Height);
            if (innerViewportRect.Width <= 0 || innerViewportRect.Height <= 0)
            {
                continue;
            }

            canvas.SetDebugTag(viewport.BlockId.Value);
            var blockContext = new BlockRenderContext(innerViewportRect, timeSeconds, theme, textMetrics, scene3D);
            canvas.PushClipRect(innerViewportRect);
            renderBlock.Render(canvas, blockContext);

            canvas.PopClipRect();
            if (renderBlock is IBlockOverlayRenderer overlayRenderer)
            {
                overlayRenderer.RenderOverlay(canvas, outerViewportRect, blockContext);
            }
            canvas.SetDebugTag(0);
        }
    }

    private static void DrawChrome(RenderCanvas canvas, BlockChromeSpec chrome, RectPx rect, int borderRgba)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        switch (chrome.Style)
        {
            case BlockChromeStyle.PanelBg:
                canvas.FillRect(rect, PanelBgRgba);
                break;
            case BlockChromeStyle.Frame2Px:
            {
                var thickness = Math.Max(1, chrome.BorderPx);
                var reservation = Math.Max(0, chrome.PaddingPx + chrome.BorderPx);
                var inset = Math.Max(0, (reservation - thickness) / 2);
                var frameRect = inset > 0 ? DeflateRect(rect, inset) : rect;
                DrawFrame(canvas, frameRect, thickness, borderRgba);
                break;
            }
        }
    }

    private static void DrawFrame(RenderCanvas canvas, RectPx rect, int thickness, int rgba)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var maxThickness = Math.Min(rect.Width / 2, rect.Height / 2);
        thickness = Math.Max(1, Math.Min(thickness, maxThickness));

        if (thickness <= 0)
        {
            return;
        }

        canvas.FillRect(new RectPx(rect.X, rect.Y, rect.Width, thickness), rgba);
        canvas.FillRect(new RectPx(rect.X, rect.Y + rect.Height - thickness, rect.Width, thickness), rgba);
        canvas.FillRect(new RectPx(rect.X, rect.Y + thickness, thickness, rect.Height - (thickness * 2)), rgba);
        canvas.FillRect(new RectPx(rect.X + rect.Width - thickness, rect.Y + thickness, thickness, rect.Height - (thickness * 2)), rgba);
    }

    private static RectPx DeflateRect(RectPx rect, int inset)
    {
        if (inset <= 0)
        {
            return rect;
        }

        var x = rect.X + inset;
        var y = rect.Y + inset;
        var w = Math.Max(0, rect.Width - (inset * 2));
        var h = Math.Max(0, rect.Height - (inset * 2));
        return new RectPx(x, y, w, h);
    }

    private static Scene3DRenderSettings MapScene3D(Scene3DSettings s)
    {
        return new Scene3DRenderSettings(
            HorizontalFovDegrees: s.HorizontalFovDegrees,
            StlDebugMode: (int)s.StlDebugMode,
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

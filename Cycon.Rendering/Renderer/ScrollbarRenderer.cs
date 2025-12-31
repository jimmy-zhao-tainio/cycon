using System;
using Cycon.Core;
using Cycon.Core.Scrolling;
using Cycon.Layout;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

public static class ScrollbarRenderer
{
    public static void Add(RenderFrame frame, ConsoleDocument document, LayoutFrame layout)
    {
        var sb = layout.Scrollbar;
        if (!sb.IsScrollable)
        {
            return;
        }

        var ui = document.Scroll.ScrollbarUi;
        var visibility = Math.Clamp(ui.Visibility, 0f, 1f);
        if (visibility <= 0f)
        {
            return;
        }

        var settings = document.Settings.Scrollbar;
        var thumbOpacity = ui.IsDragging
            ? settings.ThumbOpacityDrag
            : ui.IsHovering
                ? settings.ThumbOpacityHover
                : settings.ThumbOpacityIdle;

        var trackAlpha = ToAlpha(visibility * settings.TrackOpacityIdle);
        var thumbAlpha = ToAlpha(visibility * thumbOpacity);
        if (trackAlpha == 0 && thumbAlpha == 0)
        {
            return;
        }

        var color = document.Settings.DefaultTextStyle.ForegroundRgba;
        var trackColor = WithAlpha(color, trackAlpha);
        var thumbColor = WithAlpha(color, thumbAlpha);

        frame.Add(new PushClip(0, 0, layout.Grid.FramebufferWidthPx, layout.Grid.FramebufferHeightPx));
        if (trackAlpha != 0)
        {
            frame.Add(new DrawQuad(sb.TrackRectPx.X, sb.TrackRectPx.Y, sb.TrackRectPx.Width, sb.TrackRectPx.Height, trackColor));
        }

        if (thumbAlpha != 0)
        {
            frame.Add(new DrawQuad(sb.ThumbRectPx.X, sb.ThumbRectPx.Y, sb.ThumbRectPx.Width, sb.ThumbRectPx.Height, thumbColor));
        }

        frame.Add(new PopClip());
    }

    private static byte ToAlpha(float alpha01)
    {
        alpha01 = Math.Clamp(alpha01, 0f, 1f);
        return (byte)Math.Clamp((int)Math.Round(alpha01 * 255f), 0, 255);
    }

    private static int WithAlpha(int rgba, byte alpha)
    {
        return (rgba & unchecked((int)0xFFFFFF00)) | alpha;
    }
}


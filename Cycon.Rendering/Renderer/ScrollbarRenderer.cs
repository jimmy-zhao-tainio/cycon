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

        var color = document.Settings.DefaultTextStyle.ForegroundRgba;

        frame.Add(new PushClip(0, 0, layout.Grid.FramebufferWidthPx, layout.Grid.FramebufferHeightPx));
        if (visibility > 0f)
        {
            frame.Add(new DrawQuad(sb.ThumbRectPx.X, sb.ThumbRectPx.Y, sb.ThumbRectPx.Width, sb.ThumbRectPx.Height, color));
        }

        frame.Add(new PopClip());
    }

}

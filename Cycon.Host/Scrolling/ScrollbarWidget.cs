using System;
using Cycon.Backends.Abstractions.Rendering;
using Cycon.Core.Scrolling;
using Cycon.Host.Input;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Scrolling;

public sealed class ScrollbarWidget
{
    private readonly IScrollModel _model;
    private readonly ScrollbarUiState _ui;
    private bool _thumbHover;
    private bool _interactedThisTick;

    public ScrollbarWidget(IScrollModel model, ScrollbarUiState ui)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    public void BeginTick()
    {
        _interactedThisTick = false;
    }

    public void OnPointerInWindowChanged(bool isInWindow)
    {
        if (isInWindow)
        {
            return;
        }

        if (!_ui.IsDragging)
        {
            _ui.IsHovering = false;
        }

        _thumbHover = false;
    }

    public void AdvanceAnimation(int dtMs, ScrollbarSettings settings)
    {
        if (_interactedThisTick)
        {
            _ui.MsSinceInteraction = 0;
        }
        else if (dtMs > 0)
        {
            _ui.MsSinceInteraction = (int)Math.Min(int.MaxValue, (long)_ui.MsSinceInteraction + dtMs);
        }

        var wantsVisible = _ui.IsDragging || _ui.IsHovering || _ui.MsSinceInteraction < settings.AutoHideDelayMs;
        var target = wantsVisible ? 1f : 0f;
        var fadeMs = wantsVisible ? settings.FadeInMs : settings.FadeOutMs;

        if (fadeMs <= 0)
        {
            _ui.Visibility = target;
            return;
        }

        var step = dtMs / (float)fadeMs;
        if (step <= 0f)
        {
            return;
        }

        _ui.Visibility = MoveTowards(_ui.Visibility, target, step);
    }

    public RenderFrame? BuildOverlayFrame(PxRect viewportRectPx, ScrollbarSettings settings, int rgba)
    {
        if (!_model.TryGetScrollbarLayout(viewportRectPx, settings, out var sb))
        {
            return null;
        }

        var visibility = Math.Clamp(_ui.Visibility, 0f, 1f);
        if (visibility <= 0f && !_ui.IsHovering && !_ui.IsDragging)
        {
            return null;
        }

        var thumbOpacity = _ui.IsDragging
            ? settings.ThumbOpacityDrag
            : _thumbHover
                ? settings.ThumbOpacityHover
                : settings.ThumbOpacityIdle;

        var trackAlpha = ToAlpha(visibility * settings.TrackOpacityIdle);
        var thumbAlpha = ToAlpha(visibility * thumbOpacity);

        if (trackAlpha == 0 && thumbAlpha == 0)
        {
            return null;
        }

        var trackColor = WithAlpha(rgba, trackAlpha);
        var thumbColor = WithAlpha(rgba, thumbAlpha);

        var frame = new RenderFrame();
        frame.Add(new PushClip(viewportRectPx.X, viewportRectPx.Y, viewportRectPx.Width, viewportRectPx.Height));

        if (trackAlpha != 0 && sb.IsScrollable)
        {
            frame.Add(new DrawQuad(sb.TrackRectPx.X, sb.TrackRectPx.Y, sb.TrackRectPx.Width, sb.TrackRectPx.Height, trackColor));
        }

        if (thumbAlpha != 0 && sb.IsScrollable)
        {
            frame.Add(new DrawQuad(sb.ThumbRectPx.X, sb.ThumbRectPx.Y, sb.ThumbRectPx.Width, sb.ThumbRectPx.Height, thumbColor));
        }

        frame.Add(new PopClip());
        return frame;
    }

    public bool HandleMouse(HostMouseEvent e, PxRect viewportRectPx, ScrollbarSettings settings, out bool scrollChanged)
    {
        scrollChanged = false;

        if (!_model.TryGetScrollbarLayout(viewportRectPx, settings, out var sb))
        {
            if (_ui.IsDragging)
            {
                _ui.IsDragging = false;
                _thumbHover = false;
                return false;
            }

            return false;
        }

        if (!_ui.IsDragging && !sb.IsScrollable)
        {
            return false;
        }

        if (e.Kind == HostMouseEventKind.Move && !_ui.IsDragging)
        {
            var inTrack = sb.HitTrackRectPx.Contains(e.X, e.Y);
            var wasHovering = _ui.IsHovering;
            _ui.IsHovering = inTrack;
            _thumbHover = sb.ThumbRectPx.Contains(e.X, e.Y);
            if (_ui.IsHovering && !wasHovering)
            {
                _interactedThisTick = true;
            }

            return false;
        }

        if (e.Kind == HostMouseEventKind.Wheel)
        {
            if (e.WheelDelta == 0)
            {
                return false;
            }

            var scrolled = _model.ApplyWheelDelta(e.WheelDelta, viewportRectPx);
            if (scrolled)
            {
                _interactedThisTick = true;
                scrollChanged = true;
                return true;
            }

            return false;
        }

        if (e.Kind == HostMouseEventKind.Down)
        {
            if ((e.Buttons & HostMouseButtons.Left) == 0)
            {
                return false;
            }

            if (!sb.IsScrollable)
            {
                return false;
            }

            if (!sb.ThumbRectPx.Contains(e.X, e.Y))
            {
                return false;
            }

            _ui.IsDragging = true;
            _ui.IsHovering = true;
            _thumbHover = true;
            _ui.DragGrabOffsetYPx = e.Y - sb.ThumbRectPx.Y;
            _interactedThisTick = true;
            return true;
        }

        if (e.Kind == HostMouseEventKind.Up)
        {
            if (!_ui.IsDragging)
            {
                return false;
            }

            if ((e.Buttons & HostMouseButtons.Left) == 0)
            {
                return false;
            }

            _ui.IsDragging = false;
            _interactedThisTick = true;
            return true;
        }

        if (e.Kind == HostMouseEventKind.Move)
        {
            if (!_ui.IsDragging)
            {
                return false;
            }

            var grab = Math.Clamp(_ui.DragGrabOffsetYPx, 0, sb.ThumbRectPx.Height);
            var changed = _model.DragThumbTo(e.Y, grab, viewportRectPx, sb);
            _interactedThisTick = true;
            scrollChanged = changed;
            return true;
        }

        return false;
    }

    private static float MoveTowards(float value, float target, float maxDelta)
    {
        if (value < target)
        {
            return Math.Min(value + maxDelta, target);
        }

        if (value > target)
        {
            return Math.Max(value - maxDelta, target);
        }

        return value;
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

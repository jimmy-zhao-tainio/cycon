using Cycon.Core.Transcript;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Overlays;
using Cycon.Render;

namespace Cycon.Rendering.Renderer;

internal static class OverlaySlabPass
{
    public static void Render(
        RenderCanvas canvas,
        in FixedCellGrid grid,
        OverlaySlabFrame slab,
        UIActionState slabActions,
        int foregroundRgba,
        int backgroundRgba)
    {
        var outer = slab.OuterRectPx;
        var outerRect = new RectPx(outer.X, outer.Y, outer.Width, outer.Height);
        if (outerRect.Width <= 0 || outerRect.Height <= 0)
        {
            return;
        }

        var chrome = BlockChromeSpec.ViewDefault;

        var frameRect = BlockChromeRenderer.GetFrameRect(chrome, outerRect, out var thickness);
        var slabFillRect = BlockChromeRenderer.DeflateRect(frameRect, thickness);
        if (slabFillRect.Width > 0 && slabFillRect.Height > 0)
        {
            // Fill everything inside the border (including the inner gutter band), but keep the outer gutter
            // band transparent so the modal scrim/blur shows through up to the stroke.
            canvas.FillRect(slabFillRect, backgroundRgba);
        }

        var cellW = grid.CellWidthPx <= 0 ? 8 : grid.CellWidthPx;
        var cellH = grid.CellHeightPx <= 0 ? 16 : grid.CellHeightPx;

        var content = slab.ContentRectPx;
        var contentX = content.X;
        var contentY = content.Y;
        var contentRect = new RectPx(content.X, content.Y, content.Width, content.Height);

        RoundedRectRenderer.DrawRoundedFrame(canvas, frameRect, thickness, radiusPx: 6, rgba: foregroundRgba);

        const int ContentInsetCols = 2;
        const int ContentInsetRowsTop = 1; // below header separator
        const int ContentInsetRowsBottom = 1;

        var baseTextX = contentX + (ContentInsetCols * cellW);
        if (slab.TextInput is { } inputFrame)
        {
            // Align slab text to the framed input's left stroke (cell-authored, consistent with control chrome).
            baseTextX = inputFrame.OuterRectPx.X + cellW;
        }

        var titleX = baseTextX;
        var titleY = contentY;
        if (!string.IsNullOrEmpty(slab.Title))
        {
            canvas.DrawText(slab.Title, 0, slab.Title.Length, titleX, titleY, foregroundRgba);
        }

        // Clip to the slab interior (inside the border stroke).
        canvas.PushClipRect(slabFillRect);

        // Action highlight ladder: focused (invert), pressed, hovered.
        var focused = TryFindAction(slab.Actions, slabActions.FocusedId);
        var pressed = TryFindAction(slab.Actions, slabActions.PressedId);
        var hovered = TryFindAction(slab.Actions, slabActions.HoveredId);
        var hasFocus = slabActions.FocusedId is { } fid && !fid.IsEmpty;

        static RectPx GetActionFillRect(in UIAction action)
        {
            var r = action.RectPx;
            return new RectPx(r.X, r.Y, r.Width, r.Height);
        }

        static bool IsTextInput(in UIAction action) =>
            action.Kind == UIActionKind.TextInput;

        static bool IsButton(in UIAction action, int cellH) =>
            !IsTextInput(action) && action.RectPx.Height == (cellH * 3);

        void GetButtonRects(in UIAction action, bool isPressed, out RectPx btnOuter, out RectPx btnFrame, out RectPx btnInner, out int btnThickness, out int btnRadius, out int btnInnerRadius)
        {
            var r = action.RectPx;
            btnOuter = new RectPx(r.X, r.Y, r.Width, r.Height);
            if (isPressed)
            {
                btnOuter = BlockChromeRenderer.DeflateRect(btnOuter, 1);
            }
            btnFrame = BlockChromeRenderer.GetFrameRect(BlockChromeSpec.ViewDefault, btnOuter, out btnThickness);
            btnRadius = 6;
            btnInner = BlockChromeRenderer.DeflateRect(btnFrame, btnThickness);
            btnInnerRadius = Math.Max(0, btnRadius - btnThickness);
        }

        // Action highlight ladder: pressed (invert + shrink), focused (invert), hovered (subtle).
        if (pressed is { } p)
        {
            if (!IsTextInput(p))
            {
                if (IsButton(p, cellH))
                {
                    GetButtonRects(p, isPressed: true, out _, out _, out var btnInner, out _, out _, out var btnInnerRadius);
                    RoundedRectRenderer.FillRoundedRect(canvas, btnInner, btnInnerRadius, foregroundRgba);
                }
                else
                {
                    canvas.FillRect(GetActionFillRect(p), unchecked((int)0x303030FF));
                }
            }
        }

        if (focused is { } f && (pressed is null || f.Id != pressed.Value.Id))
        {
            if (!IsTextInput(f))
            {
                if (IsButton(f, cellH))
                {
                    GetButtonRects(f, isPressed: false, out _, out _, out var btnInner, out _, out _, out var btnInnerRadius);
                    RoundedRectRenderer.FillRoundedRect(canvas, btnInner, btnInnerRadius, foregroundRgba);
                }
                else
                {
                    canvas.FillRect(GetActionFillRect(f), foregroundRgba);
                }
            }
        }

        if (!hasFocus && hovered is { } h &&
            (pressed is null || h.Id != pressed.Value.Id) &&
            (focused is null || h.Id != focused.Value.Id))
        {
            if (!IsTextInput(h))
            {
                if (IsButton(h, cellH))
                {
                    GetButtonRects(h, isPressed: false, out _, out _, out var btnInner, out _, out _, out var btnInnerRadius);
                    RoundedRectRenderer.FillRoundedRect(canvas, btnInner, btnInnerRadius, foregroundRgba);
                }
                else
                {
                    canvas.FillRect(GetActionFillRect(h), unchecked((int)0x202020FF));
                }
            }
        }

        var footerTopY = int.MaxValue;
        for (var i = 0; i < slab.Actions.Count; i++)
        {
            var a = slab.Actions[i];
            if (IsButton(a, cellH))
            {
                footerTopY = Math.Min(footerTopY, a.RectPx.Y);
            }
        }

        for (var i = 0; i < slab.Lines.Count; i++)
        {
            var line = slab.Lines[i] ?? string.Empty;
            // 1 blank row between header and first content line.
            var y = contentY + ((1 + ContentInsetRowsTop) * cellH) + (i * cellH);
            var footerClipY = footerTopY == int.MaxValue
                ? contentY + contentRect.Height - ((2 + ContentInsetRowsBottom) * cellH)
                : Math.Max(contentY, footerTopY - (ContentInsetRowsBottom * cellH));
            if (y >= footerClipY)
            {
                break;
            }
            if (y + cellH <= outerRect.Y || y >= outerRect.Y + outerRect.Height)
            {
                continue;
            }

            canvas.DrawText(line, 0, line.Length, baseTextX, y, foregroundRgba);
        }

        // Action labels (including header close).
        for (var i = 0; i < slab.Actions.Count; i++)
        {
            var a = slab.Actions[i];
            if (IsTextInput(a))
            {
                continue;
            }
            if (string.IsNullOrEmpty(a.Label))
            {
                continue;
            }

            var isPressed = pressed is { } pressedAction && pressedAction.Id == a.Id;
            var isHovered = hovered is { } hoveredAction && hoveredAction.Id == a.Id;
            var isInverted = (focused is { } focusedAction && focusedAction.Id == a.Id) || (IsButton(a, cellH) && (isPressed || (isHovered && !hasFocus)));
            var rgba = isInverted ? backgroundRgba : foregroundRgba;
            var r = a.RectPx;
            if (IsButton(a, cellH))
            {
                // Keep label position stable; pressed visual shrink is handled by the button frame geometry.
                var labelX = r.X + (cellW * 3);
                var labelY = r.Y + cellH;
                canvas.DrawText(a.Label, 0, a.Label.Length, labelX, labelY, rgba);
            }
            else
            {
                canvas.DrawText(a.Label, 0, a.Label.Length, r.X, r.Y, rgba);
            }
        }

        if (slab.TextInput is { } input)
        {
            OverlayTextInputRenderer.Render(canvas, grid, input, foregroundRgba, backgroundRgba);
        }

        // Button borders (after highlight fill, before leaving clip).
        for (var i = 0; i < slab.Actions.Count; i++)
        {
            var a = slab.Actions[i];
            if (!IsButton(a, cellH))
            {
                continue;
            }

            var borderRgba = foregroundRgba;
            var isPressed = pressed is { } pressedBorderAction && pressedBorderAction.Id == a.Id;
            GetButtonRects(a, isPressed: isPressed, out _, out var btnFrame, out _, out var btnThickness, out _, out _);
            RoundedRectRenderer.DrawRoundedFrame(canvas, btnFrame, btnThickness, radiusPx: 6, rgba: borderRgba);
        }

        canvas.PopClipRect();
    }

    private static UIAction? TryFindAction(IReadOnlyList<UIAction> actions, UIActionId? id)
    {
        if (id is null || id.Value.IsEmpty || actions.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].Id == id.Value)
            {
                return actions[i];
            }
        }

        return null;
    }
}

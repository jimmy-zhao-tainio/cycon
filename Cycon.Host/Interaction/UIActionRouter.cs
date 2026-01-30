using Cycon.Layout;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Scrolling;
using Cycon.Core.Transcript;

namespace Cycon.Host.Interaction;

internal sealed class UIActionRouter
{
    public UIActionState State { get; private set; } = UIActionState.Empty;

    public bool ClearHover()
    {
        if (State.HoveredId is null)
        {
            return false;
        }

        State = State with { HoveredId = null };
        return true;
    }

    public bool ClearPressed()
    {
        if (State.PressedId is null)
        {
            return false;
        }

        State = State with { PressedId = null };
        return true;
    }

    public bool SetPressed(UIActionId? id)
    {
        var next = State with { PressedId = id };
        if (next == State)
        {
            return false;
        }

        State = next;
        return true;
    }

    public bool ClearFocus()
    {
        if (State.FocusedId is null)
        {
            return false;
        }

        State = State with { FocusedId = null };
        return true;
    }

    public bool SetFocus(UIActionId? id)
    {
        var next = State with { FocusedId = id, PressedId = null };
        if (next == State)
        {
            return false;
        }

        State = next;
        return true;
    }

    public bool ClearAll()
    {
        if (State == UIActionState.Empty)
        {
            return false;
        }

        State = UIActionState.Empty;
        return true;
    }

    public bool UpdateHover(LayoutFrame layout, int mouseX, int mouseYDocPx)
    {
        var hovered = TryGetActionIdAt(layout, mouseX, mouseYDocPx, out var id) ? id : (UIActionId?)null;
        if (State.HoveredId == hovered)
        {
            return false;
        }

        State = State with { HoveredId = hovered };
        return true;
    }

    public bool UpdateHover(IReadOnlyList<UIAction> actions, int mouseX, int mouseY)
    {
        var hovered = TryGetActionIdAt(actions, mouseX, mouseY, out var id) ? id : (UIActionId?)null;
        if (State.HoveredId == hovered)
        {
            return false;
        }

        State = State with { HoveredId = hovered };
        return true;
    }

    public bool UpdateHover(IReadOnlyList<UIAction> actions, int mouseX, int mouseY, int buttonCellH)
    {
        var hovered = TryGetActionIdAt(actions, mouseX, mouseY, buttonCellH, out var id) ? id : (UIActionId?)null;
        if (State.HoveredId == hovered)
        {
            return false;
        }

        State = State with { HoveredId = hovered };
        return true;
    }

    public bool HandleMouseDown(LayoutFrame layout, int mouseX, int mouseYDocPx, out UIActionId? focusedIdChangedTo)
    {
        focusedIdChangedTo = null;
        if (!TryGetActionIdAt(layout, mouseX, mouseYDocPx, out var id))
        {
            var changed = State.PressedId is not null || State.FocusedId is not null;
            State = State with { PressedId = null, FocusedId = null };
            return changed;
        }

        focusedIdChangedTo = id;
        var next = State with { PressedId = id, FocusedId = id };
        var didChange = next != State;
        State = next;
        return didChange;
    }

    public bool HandleMouseDown(IReadOnlyList<UIAction> actions, int mouseX, int mouseY, out UIActionId? focusedIdChangedTo)
    {
        focusedIdChangedTo = null;
        if (!TryGetActionIdAt(actions, mouseX, mouseY, out var id))
        {
            var changed = State.PressedId is not null || State.FocusedId is not null;
            State = State with { PressedId = null, FocusedId = null };
            return changed;
        }

        focusedIdChangedTo = id;
        var next = State with { PressedId = id, FocusedId = id };
        var didChange = next != State;
        State = next;
        return didChange;
    }

    public bool HandleMouseDown(IReadOnlyList<UIAction> actions, int mouseX, int mouseY, int buttonCellH, out UIActionId? focusedIdChangedTo)
    {
        focusedIdChangedTo = null;
        if (!TryGetActionIdAt(actions, mouseX, mouseY, buttonCellH, out var id))
        {
            var changed = State.PressedId is not null || State.FocusedId is not null;
            State = State with { PressedId = null, FocusedId = null };
            return changed;
        }

        focusedIdChangedTo = id;
        var next = State with { PressedId = id, FocusedId = id };
        var didChange = next != State;
        State = next;
        return didChange;
    }

    public bool HandleMouseUp(LayoutFrame layout, int mouseX, int mouseYDocPx, out UIActionId? activatedId)
    {
        activatedId = null;
        var pressed = State.PressedId;
        if (pressed is null)
        {
            return false;
        }

        var stillInside = TryGetActionIdAt(layout, mouseX, mouseYDocPx, out var idAtUp) && idAtUp == pressed.Value;
        State = State with { PressedId = null };

        if (stillInside)
        {
            activatedId = pressed;
            return true;
        }

        return true;
    }

    public bool HandleMouseUp(IReadOnlyList<UIAction> actions, int mouseX, int mouseY, out UIActionId? activatedId)
    {
        activatedId = null;
        var pressed = State.PressedId;
        if (pressed is null)
        {
            return false;
        }

        var stillInside = TryGetActionIdAt(actions, mouseX, mouseY, out var idAtUp) && idAtUp == pressed.Value;
        State = State with { PressedId = null };

        if (stillInside)
        {
            activatedId = pressed;
            return true;
        }

        return true;
    }

    public bool HandleMouseUp(IReadOnlyList<UIAction> actions, int mouseX, int mouseY, int buttonCellH, out UIActionId? activatedId)
    {
        activatedId = null;
        var pressed = State.PressedId;
        if (pressed is null)
        {
            return false;
        }

        var stillInside = TryGetActionIdAt(actions, mouseX, mouseY, buttonCellH, out var idAtUp) && idAtUp == pressed.Value;
        State = State with { PressedId = null };

        if (stillInside)
        {
            activatedId = pressed;
            return true;
        }

        return true;
    }

    private static bool TryGetActionIdAt(LayoutFrame layout, int mouseX, int mouseYDocPx, out UIActionId id)
    {
        id = default;

        // Ignore interactions over the scrollbar region.
        if (layout.Scrollbar.IsScrollable)
        {
            var sb = layout.Scrollbar;
            // Scrollbar rects are in screen space; action spans are in document space. Avoid mixed Y-units by
            // filtering solely on X since the scrollbar spans the full height.
            if (mouseX >= sb.HitTrackRectPx.X || mouseX >= sb.TrackRectPx.X)
            {
                return false;
            }
        }

        if (!layout.HitTestMap.TryGetActionAt(mouseX, mouseYDocPx, out int spanIndex) ||
            spanIndex < 0 ||
            spanIndex >= layout.HitTestMap.ActionSpans.Count)
        {
            return false;
        }

        var span = layout.HitTestMap.ActionSpans[spanIndex];
        if (string.IsNullOrWhiteSpace(span.CommandText))
        {
            return false;
        }

        id = UIActionFactory.GetId(span);
        return !id.IsEmpty;
    }

    private static bool TryGetActionIdAt(IReadOnlyList<UIAction> actions, int mouseX, int mouseY, out UIActionId id)
    {
        id = default;
        if (actions is null || actions.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (!a.Enabled || a.Id.IsEmpty)
            {
                continue;
            }

            if (a.RectPx.Contains(mouseX, mouseY))
            {
                id = a.Id;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetActionIdAt(IReadOnlyList<UIAction> actions, int mouseX, int mouseY, int buttonCellH, out UIActionId id)
    {
        id = default;
        if (actions is null || actions.Count == 0)
        {
            return false;
        }

        var chrome = BlockChromeSpec.ViewDefault;
        var thickness = Math.Max(1, chrome.BorderPx);
        var reservation = Math.Max(0, chrome.PaddingPx + chrome.BorderPx);
        var inset = Math.Max(0, (reservation - thickness) / 2);

        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (!a.Enabled || a.Id.IsEmpty)
            {
                continue;
            }

            var r = a.RectPx;
            if (buttonCellH > 0 && r.Height == (buttonCellH * 3) && inset > 0)
            {
                // Buttons have centered-gutter borders; use the visible frame bounds as the hit target
                // so hover/click matches the button stroke.
                r = DeflateRect(r, inset);
            }

            if (r.Contains(mouseX, mouseY))
            {
                id = a.Id;
                return true;
            }
        }

        return false;
    }

    private static PxRect DeflateRect(PxRect rect, int inset)
    {
        if (inset <= 0)
        {
            return rect;
        }

        var x = rect.X + inset;
        var y = rect.Y + inset;
        var w = Math.Max(0, rect.Width - (inset * 2));
        var h = Math.Max(0, rect.Height - (inset * 2));
        return new PxRect(x, y, w, h);
    }
}

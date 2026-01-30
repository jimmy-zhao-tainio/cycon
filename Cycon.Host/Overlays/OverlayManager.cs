using System;
using System.Collections.Generic;
using Cycon.Core.Transcript;
using Cycon.Host.Hosting;
using Cycon.Host.Input;
using Cycon.Host.Interaction;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Overlays;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Overlays;

internal sealed class OverlayManager
{
    private OverlaySlab? _slab;
    private OverlaySlabFrame? _frame;
    private FixedCellGrid _lastGrid;
    private bool _hasGrid;
    private readonly UIActionRouter _actions = new();

    public int Version { get; private set; }

    public bool IsOpen => _slab is not null;

    public bool IsModal => _slab?.IsModal ?? false;

    public UIActionState ActionState => _actions.State;

    public bool ClearAllActions() => _actions.ClearAll();

    public void Close()
    {
        if (_slab is null)
        {
            return;
        }

        _slab = null;
        _frame = null;
        _hasGrid = false;
        _ = _actions.ClearAll();
        Version++;
    }

    public void OpenHelpControls(in FixedCellGrid grid)
    {
        _slab = CreateHelpControlsSlab();
        _frame = null;
        _lastGrid = grid;
        _hasGrid = true;
        _ = _actions.ClearAll();
        Version++;
    }

    public OverlaySlabFrame? GetFrame(in FixedCellGrid grid)
    {
        if (_slab is null)
        {
            return null;
        }

        if (!_hasGrid ||
            _lastGrid.CellWidthPx != grid.CellWidthPx ||
            _lastGrid.CellHeightPx != grid.CellHeightPx ||
            _lastGrid.Cols != grid.Cols ||
            _lastGrid.Rows != grid.Rows ||
            _lastGrid.PaddingLeftPx != grid.PaddingLeftPx ||
            _lastGrid.PaddingTopPx != grid.PaddingTopPx ||
            _lastGrid.PaddingRightPx != grid.PaddingRightPx ||
            _lastGrid.PaddingBottomPx != grid.PaddingBottomPx ||
            _lastGrid.FramebufferWidthPx != grid.FramebufferWidthPx ||
            _lastGrid.FramebufferHeightPx != grid.FramebufferHeightPx)
        {
            _lastGrid = grid;
            _hasGrid = true;
            _frame = null;
        }

        _frame ??= BuildFrame(_slab, grid);
        return _frame;
    }

    public bool HandleKey(in PendingEvent.Key key, long nowTicks)
    {
        if (_slab is null)
        {
            return false;
        }

        if (key.KeyCode == HostKey.Enter)
        {
            if (!_hasGrid)
            {
                _ = _actions.ClearPressed();
                return true;
            }

            var frame = GetFrame(_lastGrid);
            if (frame is null)
            {
                _ = _actions.ClearPressed();
                return true;
            }

            if (key.IsDown)
            {
                if (_actions.State.FocusedId is { } focused)
                {
                    _ = _actions.SetPressed(focused);
                }

                return true;
            }

            // Key up: activate if still focused.
            if (_actions.State.PressedId is { } pressed &&
                _actions.State.FocusedId is { } focusedUp &&
                pressed == focusedUp &&
                TryFindActionById(frame.Actions, focusedUp, out var action) &&
                action.Enabled)
            {
                Activate(action);
            }

            _ = _actions.ClearPressed();
            return true;
        }

        if (!key.IsDown)
        {
            return true;
        }

        if (key.KeyCode == HostKey.Tab)
        {
            if (!_hasGrid)
            {
                return true;
            }

            var frame = GetFrame(_lastGrid);
            if (frame is null || frame.Actions.Count == 0)
            {
                return true;
            }

            var reverse = (key.Mods & HostKeyModifiers.Shift) != 0;
            var next = FindNextActionId(frame.Actions, _actions.State.FocusedId, reverse);
            if (next is not null)
            {
                _ = _actions.SetFocus(next);
            }

            return true;
        }

        if (key.KeyCode == HostKey.Escape)
        {
            Close();
            return true;
        }

        // Overlay steals keyboard focus while open.
        return true;
    }

    private static UIActionId? FindNextActionId(IReadOnlyList<UIAction> actions, UIActionId? current, bool reverse)
    {
        if (actions.Count == 0)
        {
            return null;
        }

        var firstEnabled = -1;
        var lastEnabled = -1;
        for (var i = 0; i < actions.Count; i++)
        {
            if (!actions[i].Enabled || actions[i].Id.IsEmpty)
            {
                continue;
            }

            if (firstEnabled < 0) firstEnabled = i;
            lastEnabled = i;
        }

        if (firstEnabled < 0)
        {
            return null;
        }

        if (current is null || current.Value.IsEmpty)
        {
            return reverse ? actions[lastEnabled].Id : actions[firstEnabled].Id;
        }

        var currentIndex = -1;
        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].Id == current.Value)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
        {
            return reverse ? actions[lastEnabled].Id : actions[firstEnabled].Id;
        }

        if (!reverse)
        {
            for (var i = currentIndex + 1; i < actions.Count; i++)
            {
                if (actions[i].Enabled && !actions[i].Id.IsEmpty)
                {
                    return actions[i].Id;
                }
            }

            return actions[firstEnabled].Id;
        }

        for (var i = currentIndex - 1; i >= 0; i--)
        {
            if (actions[i].Enabled && !actions[i].Id.IsEmpty)
            {
                return actions[i].Id;
            }
        }

        return actions[lastEnabled].Id;
    }

    public bool HandleText(in PendingEvent.Text text)
    {
        return _slab is not null;
    }

    public bool HandleMouse(in HostMouseEvent e, in FixedCellGrid grid)
    {
        var slab = _slab;
        if (slab is null)
        {
            return false;
        }

        var frame = GetFrame(grid);
        if (frame is null)
        {
            return slab.IsModal;
        }

        var inside = frame.OuterRectPx.Contains(e.X, e.Y);

        if (!inside)
        {
            _ = _actions.UpdateHover(Array.Empty<UIAction>(), e.X, e.Y);
        }

        if (e.Kind == HostMouseEventKind.Move)
        {
            if (inside)
            {
                _ = _actions.UpdateHover(frame.Actions, e.X, e.Y, Math.Max(1, grid.CellHeightPx));
                return true;
            }

            return slab.IsModal;
        }

        if (e.Kind == HostMouseEventKind.Down && (e.Buttons & HostMouseButtons.Left) != 0)
        {
            if (!inside)
            {
                if (!slab.IsModal && slab.CloseOnOutsideClick)
                {
                    Close();
                    return true;
                }

                return slab.IsModal;
            }

            _ = _actions.HandleMouseDown(frame.Actions, e.X, e.Y, Math.Max(1, grid.CellHeightPx), out _);
            return true;
        }

        if (e.Kind == HostMouseEventKind.Up && (e.Buttons & HostMouseButtons.Left) != 0)
        {
            if (_actions.State.PressedId is not null)
            {
                _ = _actions.HandleMouseUp(frame.Actions, e.X, e.Y, Math.Max(1, grid.CellHeightPx), out var activatedId);
                if (activatedId is { } id && TryFindActionById(frame.Actions, id, out var action) && action.Enabled)
                {
                    Activate(action);
                }

                return true;
            }

            return inside || slab.IsModal;
        }

        // Consume wheel when modal; otherwise let ledger scroll.
        if (e.Kind == HostMouseEventKind.Wheel)
        {
            return inside || slab.IsModal;
        }

        return inside || slab.IsModal;
    }

    private void Activate(in UIAction action)
    {
        if (action.Kind == UIActionKind.CloseOverlay)
        {
            Close();
            return;
        }
    }

    private static bool TryFindActionById(IReadOnlyList<UIAction> actions, UIActionId id, out UIAction action)
    {
        action = default;
        if (actions is null || actions.Count == 0 || id.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (a.Id == id)
            {
                action = a;
                return true;
            }
        }

        return false;
    }

    private static OverlaySlab CreateHelpControlsSlab()
    {
        var lines = new[]
        {
            "Keyboard",
            "  Esc        Close slab",
            "  Enter      Activate focused action",
            "",
            "Mouse",
            "  Hover      Highlight action",
            "  Click      Focus action",
            "  DoubleClick Activate action"
        };

        var closeLabel = "Close";
        var actions = new[]
        {
            new OverlaySlabActionSpec(
                Id: new UIActionId(1),
                Kind: UIActionKind.NoOp,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "Back",
                CommandText: string.Empty)
            ,
            new OverlaySlabActionSpec(
                Id: new UIActionId(2),
                Kind: UIActionKind.NoOp,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "Next",
                CommandText: string.Empty)
            ,
            new OverlaySlabActionSpec(
                Id: new UIActionId(3),
                Kind: UIActionKind.CloseOverlay,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: closeLabel,
                CommandText: string.Empty)
        };

        return new OverlaySlab(
            title: "Help / Controls",
            isModal: true,
            closeOnOutsideClick: false,
            lines: lines,
            actions: actions);
    }

    private static OverlaySlabFrame BuildFrame(OverlaySlab slab, in FixedCellGrid grid)
    {
        // Overlay layout constants (cell-authored).
        const int ContentInsetCols = 2;
        const int ContentInsetRowsTop = 1; // below header separator
        const int ContentInsetRowsBottom = 1;
        const int SectionGapRows = 1;

        var cellW = Math.Max(1, grid.CellWidthPx);
        var cellH = Math.Max(1, grid.CellHeightPx);

        var contentCols = Math.Max(1, grid.Cols);
        var contentRows = Math.Max(1, grid.Rows);

        var maxLineLen = 0;
        for (var i = 0; i < slab.Lines.Count; i++)
        {
            maxLineLen = Math.Max(maxLineLen, slab.Lines[i]?.Length ?? 0);
        }

        var headerRightLen = 0;
        for (var i = 0; i < slab.Actions.Count; i++)
        {
            if (slab.Actions[i].Area == OverlaySlabActionArea.Header && slab.Actions[i].Align == OverlaySlabActionAlign.Right)
            {
                headerRightLen = Math.Max(headerRightLen, slab.Actions[i].Label?.Length ?? 0);
            }
        }

        var titleLen = slab.Title?.Length ?? 0;
        var titleRowRequired = titleLen == 0 ? headerRightLen : (titleLen + 1 + headerRightLen);

        // Chrome gutters: 16px on each side (2 cols), 16px top/bottom (1 row).
        const int chromeCols = 2;
        const int chromeRows = 1;

        // Extra breathing room inside slab interior: 1 cell padding (8px L/R, 16px T/B).
        const int padCols = 1;
        const int padRows = 1;

        var textColsMax = Math.Max(1, contentCols - (chromeCols * 2) - (padCols * 2));
        var textRowsMax = Math.Max(1, contentRows - (chromeRows * 2) - (padRows * 2));

        var textCols = Math.Clamp(Math.Max(maxLineLen, titleRowRequired) + (ContentInsetCols * 2), 20, textColsMax);
        // Content includes: 1-row title line + 1-row separator gutter + body lines.
        // Content includes: 1-row separator band + body lines + 2 footer rows (button overlaps bottom pad row).
        var sectionGapRows = slab.Lines.Count > 0 ? SectionGapRows : 0;
        var bodyRows = slab.Lines.Count + sectionGapRows;
        var gapBeforeFooterRows = ContentInsetRowsBottom; // match bottom inset for symmetry
        var textRows = Math.Clamp(
            Math.Max(ContentInsetRowsTop + bodyRows + gapBeforeFooterRows + ContentInsetRowsBottom + 2 /*footer rows*/, 6),
            6,
            textRowsMax);

        var slabCols = textCols + (padCols * 2) + (chromeCols * 2);
        var slabRows = textRows + (padRows * 2) + (chromeRows * 2);

        var slabWidthPx = slabCols * cellW;
        var slabHeightPx = slabRows * cellH;

        var contentLeftPx = grid.PaddingLeftPx;
        var contentTopPx = grid.PaddingTopPx;
        var contentWidthPx = grid.ContentWidthPx;
        var contentHeightPx = grid.ContentHeightPx;

        var startCol = Math.Max(0, (contentWidthPx - slabWidthPx) / 2 / cellW);
        var startRow = Math.Max(0, (contentHeightPx - slabHeightPx) / 2 / cellH);

        var x = contentLeftPx + (startCol * cellW);
        var y = contentTopPx + (startRow * cellH);

        var bounds = SnapToCellGrid(new PxRect(x, y, slabWidthPx, slabHeightPx), cellW, cellH);

        var actions = new List<UIAction>(slab.Actions.Count);

        // Slab fill rect is the area inside the centered-gutter 2px border stroke.
        // Use it as the alignment reference for right-aligned footer buttons so they don't inherit extra
        // padding from ContentRect.
        var borderThickness = Math.Max(1, BlockChromeSpec.ViewDefault.BorderPx);
        var borderReservation = Math.Max(0, BlockChromeSpec.ViewDefault.PaddingPx + BlockChromeSpec.ViewDefault.BorderPx);
        var borderInset = Math.Max(0, (borderReservation - borderThickness) / 2);
        var fillInsetPx = borderInset + borderThickness;
        var slabFillRightX = (bounds.X + bounds.Width) - fillInsetPx;
        var slabFillBottomY = (bounds.Y + bounds.Height) - fillInsetPx;

        var innerRect = new PxRect(
            bounds.X + (chromeCols * cellW),
            bounds.Y + (chromeRows * cellH),
            bounds.Width - (chromeCols * 2 * cellW),
            bounds.Height - (chromeRows * 2 * cellH));
        var contentRect = new PxRect(
            innerRect.X + (padCols * cellW),
            innerRect.Y + (padRows * cellH),
            innerRect.Width - (padCols * 2 * cellW),
            innerRect.Height - (padRows * 2 * cellH));

        // Header sits at the first row of the content rect; the row above is reserved as extra spacing.
        var headerX = contentRect.X;
        var headerY = contentRect.Y;
        var footerY = slabFillBottomY - (ContentInsetRowsBottom * cellH) - (cellH * 3);

        // Precompute right-aligned footer buttons from right to left with a 1-col gap.
        var footerLayoutsById = new Dictionary<UIActionId, ButtonLayoutResult>();
        {
            var gapCols = 1;
            var rightX = slabFillRightX - (ContentInsetCols * cellW);
            for (var i = slab.Actions.Count - 1; i >= 0; i--)
            {
                var spec = slab.Actions[i];
                if (spec.Area != OverlaySlabActionArea.Footer || spec.Align != OverlaySlabActionAlign.Right)
                {
                    continue;
                }

                var layout = ButtonLayout.LayoutRightAligned3Row(grid, rightX, footerY, spec.Label ?? string.Empty);
                footerLayoutsById[spec.Id] = layout;
                rightX = layout.OuterRectPx.X - (gapCols * cellW);
            }
        }

        for (var i = 0; i < slab.Actions.Count; i++)
        {
            var spec = slab.Actions[i];
            var label = spec.Label ?? string.Empty;
            var labelLen = label.Length;
            var aw = Math.Max(1, labelLen * cellW);
            var ah = cellH;

            int ax;
            int ay;

            if (spec.Area == OverlaySlabActionArea.Header)
            {
                ay = headerY + (Math.Max(0, spec.RowIndex) * cellH);
                if (spec.Align == OverlaySlabActionAlign.Right)
                {
                    ax = headerX + contentRect.Width - aw - (Math.Max(0, spec.ColIndex) * cellW);
                }
                else
                {
                    ax = headerX + (Math.Max(0, spec.ColIndex) * cellW);
                }
            }
            else if (spec.Area == OverlaySlabActionArea.Footer)
            {
                if (footerLayoutsById.TryGetValue(spec.Id, out var layout))
                {
                    ax = layout.OuterRectPx.X;
                    ay = layout.OuterRectPx.Y;
                    aw = layout.OuterRectPx.Width;
                    ah = layout.OuterRectPx.Height;
                }
                else
                {
                    ay = footerY;
                    ax = headerX + contentRect.Width - aw;
                }
            }
            else
            {
                ax = contentRect.X + (Math.Max(0, spec.ColIndex) * cellW);
                ay = contentRect.Y + cellH + (Math.Max(0, spec.RowIndex) * cellH);
            }

            var rect = new PxRect(ax, ay, aw, ah);
            actions.Add(new UIAction(
                Id: spec.Id,
                RectPx: rect,
                Label: label,
                CommandText: spec.CommandText ?? string.Empty,
                Enabled: spec.Enabled,
                Kind: spec.Kind,
                BlockId: default,
                CharStart: 0,
                CharLength: 0));
        }

        return new OverlaySlabFrame(bounds, contentRect, slab.IsModal, slab.Title ?? string.Empty, slab.Lines, actions);
    }

    private static PxRect SnapToCellGrid(PxRect rect, int cellW, int cellH)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return rect;
        }

        cellW = Math.Max(1, cellW);
        cellH = Math.Max(1, cellH);

        var snappedW = rect.Width >= cellW ? rect.Width - (rect.Width % cellW) : rect.Width;
        var snappedH = rect.Height >= cellH ? rect.Height - (rect.Height % cellH) : rect.Height;
        return new PxRect(rect.X, rect.Y, snappedW, snappedH);
    }
}

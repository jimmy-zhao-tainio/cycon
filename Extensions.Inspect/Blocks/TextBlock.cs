using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cycon.Core.Transcript;
using Cycon.Host.Input;
using Cycon.Host.Scrolling;
using Cycon.Layout.Scrolling;
using Cycon.Render;
using Cycon.Core.Scrolling;

namespace Extensions.Inspect.Blocks;

public sealed class InspectTextBlock : IBlock, IMouseFocusableViewportBlock, IBlockKeyHandler, IBlockTextSelection, IRenderBlock, IBlockOverlayRenderer, IMeasureBlock, IBlockPointerHandler, IBlockWheelHandler, IBlockPointerCaptureState, IBlockChromeProvider, IInspectResettableBlock
{
    private const int PaddingLeftRightPx = 0;
    private const int PaddingTopBottomPx = 0;
    private const int SelectionBackgroundRgba = unchecked((int)0xEEEEEEFF);
    private const int SelectionForegroundRgba = unchecked((int)0x000000FF);

    private readonly record struct TextPos(int LineIndex, int CharIndex);
    private readonly record struct TextRange(TextPos Anchor, TextPos Caret);

    private readonly IReadOnlyList<string> _lines;
    private readonly TextScrollModel _scrollModel;
    private readonly ScrollbarWidget _scrollbar;
    private readonly ScrollbarUiState _scrollbarUi = new();
    private readonly ScrollbarSettings _scrollbarSettings = new();
    private double _lastRenderTimeSeconds;
    private int _initialHeightRows = -1;
    private int _fixedHeightPx = -1;
    private bool _hasMouseFocus;
    private bool _isSelectingText;
    private TextRange? _selection;
    private TextPos? _selectionCaret;
    private int _cellW = 8;
    private int _cellH = 16;
    private int _reservedScrollbarPx;
    private PxRect _lastViewportRectPx;

    public InspectTextBlock(BlockId id, string path)
    {
        Id = id;
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _lines = LoadLines(path);
        _scrollModel = new TextScrollModel(_lines);
        _scrollbar = new ScrollbarWidget(_scrollModel, _scrollbarUi);
    }

    public BlockId Id { get; }

    public BlockKind Kind => BlockKind.Scene3D;

    public string Path { get; }

    public BlockChromeSpec ChromeSpec => BlockChromeSpec.ViewDefault;

    public int LineCount => _lines.Count;

    public bool HasPointerCapture => _scrollbarUi.IsDragging || _isSelectingText;

    public bool HasMouseFocus
    {
        get => _hasMouseFocus;
        set
        {
            if (_hasMouseFocus == value)
            {
                return;
            }

            _hasMouseFocus = value;
            if (!_hasMouseFocus)
            {
                _isSelectingText = false;
                _selection = null;
                _selectionCaret = null;
            }
        }
    }

    public bool HandlePointer(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        _lastViewportRectPx = viewportRectPx;
        _reservedScrollbarPx = UpdateViewportForText(viewportRectPx);

        if (e.Kind == HostMouseEventKind.Move && !_scrollbarUi.IsDragging && !_isSelectingText)
        {
            var beforeHover = _scrollbarUi.IsHovering;
            var beforeGrab = _scrollbarUi.DragGrabOffsetYPx;

            _scrollbar.HandleMouse(e, viewportRectPx, _scrollbarSettings, out _);

            var hoverChanged = beforeHover != _scrollbarUi.IsHovering;
            var grabChanged = beforeGrab != _scrollbarUi.DragGrabOffsetYPx;

            if (hoverChanged)
            {
                _scrollbarUi.MsSinceInteraction = 0;
            }

            return hoverChanged || grabChanged;
        }

        var consumed = _scrollbar.HandleMouse(e, viewportRectPx, _scrollbarSettings, out var scrollChanged);
        if (consumed)
        {
            _scrollbarUi.MsSinceInteraction = 0;
        }

        if (consumed || scrollChanged || _scrollbarUi.IsDragging)
        {
            return true;
        }

        if (e.Kind == HostMouseEventKind.Move &&
            !_isSelectingText)
        {
            return false;
        }

        if (e.Kind == HostMouseEventKind.Down &&
            (e.Buttons & HostMouseButtons.Left) != 0)
        {
            var shift = (e.Mods & HostKeyModifiers.Shift) != 0;
            if (!TryHitTestTextPos(e.X, e.Y, viewportRectPx, out var caret))
            {
                ClearSelection();
                _selectionCaret = null;
                _isSelectingText = false;
                return true;
            }

            if (shift)
            {
                var anchor = _selection?.Anchor ?? _selectionCaret ?? caret;
                _selection = new TextRange(anchor, caret);
                _selectionCaret = caret;
                _isSelectingText = false;
                return true;
            }

            ClearSelection();
            _selectionCaret = caret;
            _selection = new TextRange(caret, caret);
            _isSelectingText = true;
            return true;
        }

        if (e.Kind == HostMouseEventKind.Move &&
            _isSelectingText)
        {
            if ((e.Buttons & HostMouseButtons.Left) == 0)
            {
                _isSelectingText = false;
                if (_selection is { } emptyRange && emptyRange.Anchor == emptyRange.Caret)
                {
                    _selection = null;
                }
                return true;
            }

            if (!TryHitTestTextPos(e.X, e.Y, viewportRectPx, out var caret))
            {
                return false;
            }

            if (_selection is { } range)
            {
                var updated = new TextRange(range.Anchor, caret);
                if (updated == range)
                {
                    return false;
                }

                _selection = updated;
                _selectionCaret = caret;
                return true;
            }

            _selection = new TextRange(caret, caret);
            _selectionCaret = caret;
            return true;
        }

        if (e.Kind == HostMouseEventKind.Up &&
            (e.Buttons & HostMouseButtons.Left) != 0)
        {
            if (!_isSelectingText)
            {
                return true;
            }

            _isSelectingText = false;
            if (_selection is { } range && range.Anchor == range.Caret)
            {
                _selection = null;
            }

            return true;
        }

        return e.Kind is HostMouseEventKind.Down or HostMouseEventKind.Up;
    }

    public bool HandleWheel(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        _lastViewportRectPx = viewportRectPx;
        _reservedScrollbarPx = UpdateViewportForText(viewportRectPx);

        var consumed = _scrollbar.HandleMouse(e, viewportRectPx, _scrollbarSettings, out var scrollChanged);
        if (consumed || scrollChanged)
        {
            _scrollbarUi.MsSinceInteraction = 0;
        }

        return consumed || scrollChanged;
    }

    public BlockSize Measure(in BlockMeasureContext ctx)
    {
        var usableWidth = Math.Max(0, ctx.ContentWidthPx - (PaddingLeftRightPx * 2) - _scrollbarSettings.ThicknessPx);
        var cols = Math.Max(1, usableWidth / Math.Max(1, ctx.CellWidthPx));
        var viewportRows = Math.Max(1, ctx.ViewportRows);
        var cellH = Math.Max(1, ctx.CellHeightPx);

        var maxRows = Math.Max(1, viewportRows - 2);
        var chromeInsetPx = ChromeSpec.Enabled ? Math.Max(0, ChromeSpec.PaddingPx + ChromeSpec.BorderPx) : 0;
        var chromeRows = (chromeInsetPx * 2 + (cellH - 1)) / cellH;

        var capRows = Math.Max(1, maxRows + 1);
        var contentRows = _scrollModel.ComputeWrappedRowsCapped(cols, capRows);

        if (_initialHeightRows < 0)
        {
            _initialHeightRows = Math.Min(maxRows, contentRows + chromeRows);
        }

        if (_fixedHeightPx < 0)
        {
            var heightRows = Math.Min(maxRows, Math.Min(contentRows + chromeRows, _initialHeightRows));
            _fixedHeightPx = Math.Max(cellH, heightRows * cellH);
        }

        return new BlockSize(ctx.ContentWidthPx, _fixedHeightPx);
    }

    public void ResetViewModeSize()
    {
        _fixedHeightPx = -1;
        _initialHeightRows = -1;
    }

    public void Render(IRenderCanvas canvas, in BlockRenderContext ctx)
    {
        var viewport = ctx.ViewportRectPx;
        var viewportPx = new PxRect(viewport.X, viewport.Y, viewport.Width, viewport.Height);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        _cellW = Math.Max(1, ctx.TextMetrics.CellWidthPx);
        _cellH = Math.Max(1, ctx.TextMetrics.CellHeightPx);
        _scrollModel.UpdateTextMetrics(_cellW, _cellH);
        _reservedScrollbarPx = UpdateViewportForText(viewportPx);
        _lastViewportRectPx = viewportPx;

        var dtMs = _lastRenderTimeSeconds <= 0
            ? 0
            : (int)Math.Clamp((ctx.TimeSeconds - _lastRenderTimeSeconds) * 1000.0, 0, 250);
        _lastRenderTimeSeconds = ctx.TimeSeconds;

        _scrollbar.BeginTick();
        _scrollbar.AdvanceAnimation(dtMs, _scrollbarSettings);

        canvas.FillRect(viewport, ctx.Theme.BackgroundRgba);

        var cellW = _cellW;
        var cellH = _cellH;
        var cols = Math.Max(1, Math.Max(0, viewport.Width - (PaddingLeftRightPx * 2) - _reservedScrollbarPx) / cellW);
        var rows = Math.Max(1, Math.Max(0, viewport.Height - (PaddingTopBottomPx * 2)) / cellH);

        var fg = ctx.Theme.ForegroundRgba;
        var hasSelection = TryGetNormalizedSelection(out var selStart, out var selEnd);
        var lineIndex = _scrollModel.TopLineIndex;
        var subRow = _scrollModel.TopLineSubRow;
        var screenRow = 0;

        while (screenRow < rows && lineIndex < _lines.Count)
        {
            var wrapped = _scrollModel.GetWrappedLine(lineIndex);
            var spans = wrapped.Spans;

            for (var r = subRow; r < spans.Length && screenRow < rows; r++)
            {
                var span = spans[r];
                var yPx = viewport.Y + PaddingTopBottomPx + (screenRow * cellH);
                var xPx = viewport.X + PaddingLeftRightPx;
                var line = _lines[lineIndex];

                var drawLen = Math.Min(span.Length, cols);
                if (drawLen > 0)
                {
                    var spanStart = span.Start;
                    var spanEnd = checked(spanStart + drawLen);

                    if (!hasSelection || lineIndex < selStart.LineIndex || lineIndex > selEnd.LineIndex)
                    {
                        canvas.DrawText(line, spanStart, drawLen, xPx, yPx, fg);
                    }
                    else
                    {
                        var lineSelStart = lineIndex == selStart.LineIndex ? selStart.CharIndex : 0;
                        var lineSelEnd = lineIndex == selEnd.LineIndex ? selEnd.CharIndex : line.Length;
                        lineSelStart = Math.Clamp(lineSelStart, 0, line.Length);
                        lineSelEnd = Math.Clamp(lineSelEnd, 0, line.Length);

                        var selRunStart = Math.Clamp(lineSelStart, spanStart, spanEnd);
                        var selRunEnd = Math.Clamp(lineSelEnd, spanStart, spanEnd);

                        if (selRunStart < selRunEnd)
                        {
                            var colStart = selRunStart - spanStart;
                            var colLen = selRunEnd - selRunStart;
                            canvas.FillRect(new RectPx(xPx + (colStart * cellW), yPx, colLen * cellW, cellH), SelectionBackgroundRgba);
                        }

                        var beforeLen = Math.Max(0, Math.Min(drawLen, selRunStart - spanStart));
                        var selectedLen = Math.Max(0, Math.Min(drawLen - beforeLen, selRunEnd - selRunStart));
                        var afterLen = Math.Max(0, drawLen - beforeLen - selectedLen);

                        if (beforeLen > 0)
                        {
                            canvas.DrawText(line, spanStart, beforeLen, xPx, yPx, fg);
                        }

                        if (selectedLen > 0)
                        {
                            canvas.DrawText(line, spanStart + beforeLen, selectedLen, xPx + (beforeLen * cellW), yPx, SelectionForegroundRgba);
                        }

                        if (afterLen > 0)
                        {
                            canvas.DrawText(line, spanStart + beforeLen + selectedLen, afterLen, xPx + ((beforeLen + selectedLen) * cellW), yPx, fg);
                        }
                    }
                }

                screenRow++;
            }

            lineIndex++;
            subRow = 0;
        }

    }

    public void RenderOverlay(IRenderCanvas canvas, RectPx outerViewportRectPx, in BlockRenderContext ctx)
    {
        var viewport = ctx.ViewportRectPx;
        var viewportPx = new PxRect(viewport.X, viewport.Y, viewport.Width, viewport.Height);
        var outerViewportPx = new PxRect(outerViewportRectPx.X, outerViewportRectPx.Y, outerViewportRectPx.Width, outerViewportRectPx.Height);

        _scrollModel.SetScrollbarChromeInsetPx(GetChromeInsetPx());
        var overlayFrame = _scrollbar.BuildOverlayFrame(viewportPx, _scrollbarSettings, ctx.Theme.ForegroundRgba, "TextBlock", outerViewportPx);
        if (overlayFrame is not null)
        {
            ReplayOverlay(canvas, overlayFrame);
        }
    }

    public bool HandleKey(in HostKeyEvent e)
    {
        if (!_hasMouseFocus || !e.IsDown || _lines.Count == 0)
        {
            return false;
        }

        if ((e.Mods & HostKeyModifiers.Control) != 0)
        {
            return false;
        }

        if (e.Key is not (HostKey.Left or HostKey.Right or HostKey.Up or HostKey.Down))
        {
            return false;
        }

        var shift = (e.Mods & HostKeyModifiers.Shift) != 0;

        if (!TryGetCaretForNavigation(out var caret))
        {
            return false;
        }

        if (shift)
        {
            var anchor = _selection?.Anchor ?? _selectionCaret ?? caret;
            if (!TryMoveCaret(caret, e.Key, out var next))
            {
                return false;
            }

            _selection = new TextRange(anchor, next);
            _selectionCaret = next;
            EnsureCaretVisible(next);
            return true;
        }

        if (_selection is { } selection && selection.Anchor != selection.Caret)
        {
            var (start, end) = NormalizeRange(selection);
            var collapsed = e.Key is HostKey.Left or HostKey.Up ? start : end;
            _selection = null;
            _selectionCaret = collapsed;
            EnsureCaretVisible(collapsed);
            return true;
        }

        if (!TryMoveCaret(caret, e.Key, out var moved))
        {
            return false;
        }

        _selectionCaret = moved;
        EnsureCaretVisible(moved);
        return true;
    }

    public bool HasSelection => _selection is { } r && r.Anchor != r.Caret;

    public bool TryGetSelectedText(out string text)
    {
        text = string.Empty;
        if (!HasSelection || _selection is not { } range)
        {
            return false;
        }

        var (start, end) = NormalizeRange(range);
        start = ClampTextPos(start);
        end = ClampTextPos(end);

        if (start == end)
        {
            return false;
        }

        var builder = new StringBuilder();
        for (var i = start.LineIndex; i <= end.LineIndex; i++)
        {
            var line = _lines[i];
            var from = i == start.LineIndex ? start.CharIndex : 0;
            var to = i == end.LineIndex ? end.CharIndex : line.Length;
            from = Math.Clamp(from, 0, line.Length);
            to = Math.Clamp(to, 0, line.Length);
            if (to > from)
            {
                builder.Append(line.AsSpan(from, to - from));
            }

            if (i != end.LineIndex)
            {
                builder.Append('\n');
            }
        }

        text = builder.ToString();
        return text.Length > 0;
    }

    public void ClearSelection()
    {
        _selection = null;
        _isSelectingText = false;
    }

    public void SelectAll()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        var start = new TextPos(0, 0);
        var lastLine = Math.Max(0, _lines.Count - 1);
        var end = new TextPos(lastLine, _lines[lastLine].Length);
        _selection = new TextRange(start, end);
        _selectionCaret = end;
        EnsureCaretVisible(end);
    }

    private static void ReplayOverlay(IRenderCanvas canvas, Cycon.Backends.Abstractions.Rendering.RenderFrame overlayFrame)
    {
        foreach (var command in overlayFrame.Commands)
        {
            switch (command)
            {
                case Cycon.Backends.Abstractions.Rendering.PushClip clip:
                    canvas.PushClipRect(new RectPx(clip.X, clip.Y, clip.Width, clip.Height));
                    break;
                case Cycon.Backends.Abstractions.Rendering.DrawQuad quad:
                    canvas.FillRect(new RectPx(quad.X, quad.Y, quad.Width, quad.Height), quad.Rgba);
                    break;
                case Cycon.Backends.Abstractions.Rendering.PopClip:
                    canvas.PopClipRect();
                    break;
            }
        }
    }

    private bool TryGetNormalizedSelection(out TextPos start, out TextPos end)
    {
        start = default;
        end = default;

        if (!HasSelection || _selection is not { } range)
        {
            return false;
        }

        (start, end) = NormalizeRange(range);
        start = ClampTextPos(start);
        end = ClampTextPos(end);
        return start != end;
    }

    private bool TryHitTestTextPos(int xPx, int yPx, in PxRect viewportRectPx, out TextPos pos)
    {
        pos = default;

        if (_lines.Count == 0 || _cellW <= 0 || _cellH <= 0)
        {
            return false;
        }

        var localX = xPx - (viewportRectPx.X + PaddingLeftRightPx);
        var localY = yPx - (viewportRectPx.Y + PaddingTopBottomPx);
        if (localX < 0) localX = 0;
        if (localY < 0) localY = 0;

        var cols = Math.Max(1, Math.Max(0, viewportRectPx.Width - (PaddingLeftRightPx * 2) - _reservedScrollbarPx) / _cellW);
        var rows = Math.Max(1, Math.Max(0, viewportRectPx.Height - (PaddingTopBottomPx * 2)) / _cellH);
        var col = Math.Clamp(localX / _cellW, 0, cols);
        var row = Math.Clamp(localY / _cellH, 0, rows - 1);

        var lineIndex = _scrollModel.TopLineIndex;
        var subRow = _scrollModel.TopLineSubRow;
        var remainingRow = row;

        while (lineIndex < _lines.Count)
        {
            var wrapped = _scrollModel.GetWrappedLine(lineIndex);
            var spans = wrapped.Spans;
            var available = spans.Length - subRow;
            if (remainingRow < available)
            {
                var span = spans[subRow + remainingRow];
                var charIndex = span.Start + Math.Min(col, span.Length);
                charIndex = Math.Clamp(charIndex, 0, _lines[lineIndex].Length);
                pos = new TextPos(lineIndex, charIndex);
                return true;
            }

            remainingRow -= available;
            lineIndex++;
            subRow = 0;
        }

        var lastLine = Math.Max(0, _lines.Count - 1);
        pos = new TextPos(lastLine, _lines[lastLine].Length);
        return true;
    }

    private bool TryGetCaretForNavigation(out TextPos caret)
    {
        if (_selection is { } range)
        {
            caret = ClampTextPos(range.Caret);
            return true;
        }

        if (_selectionCaret is { } stored)
        {
            caret = ClampTextPos(stored);
            return true;
        }

        caret = new TextPos(_scrollModel.TopLineIndex, 0);
        caret = ClampTextPos(caret);
        return _lines.Count > 0;
    }

    private bool TryMoveCaret(TextPos caret, HostKey key, out TextPos moved)
    {
        moved = caret;
        if (_lines.Count == 0)
        {
            return false;
        }

        caret = ClampTextPos(caret);
        if (!TryGetSpanCursor(caret, out var spanRow, out var col))
        {
            return false;
        }

        var wrapped = _scrollModel.GetWrappedLine(caret.LineIndex);
        var spans = wrapped.Spans;
        var span = spans[Math.Clamp(spanRow, 0, Math.Max(0, spans.Length - 1))];

        switch (key)
        {
            case HostKey.Left:
                if (col > 0)
                {
                    moved = new TextPos(caret.LineIndex, span.Start + (col - 1));
                    return true;
                }
                if (spanRow > 0)
                {
                    var prev = spans[spanRow - 1];
                    moved = new TextPos(caret.LineIndex, prev.Start + prev.Length);
                    return true;
                }
                if (caret.LineIndex > 0)
                {
                    var prevLine = caret.LineIndex - 1;
                    var prevWrapped = _scrollModel.GetWrappedLine(prevLine);
                    var prevSpan = prevWrapped.Spans[^1];
                    moved = new TextPos(prevLine, prevSpan.Start + prevSpan.Length);
                    return true;
                }
                return false;

            case HostKey.Right:
                if (col < span.Length)
                {
                    moved = new TextPos(caret.LineIndex, span.Start + (col + 1));
                    return true;
                }
                if (spanRow + 1 < spans.Length)
                {
                    var nextSpan = spans[spanRow + 1];
                    moved = new TextPos(caret.LineIndex, nextSpan.Start);
                    return true;
                }
                if (caret.LineIndex + 1 < _lines.Count)
                {
                    moved = new TextPos(caret.LineIndex + 1, 0);
                    return true;
                }
                return false;

            case HostKey.Up:
                return TryMoveCaretVertical(caret, spanRow, col, deltaRows: -1, out moved);
            case HostKey.Down:
                return TryMoveCaretVertical(caret, spanRow, col, deltaRows: 1, out moved);
            default:
                return false;
        }
    }

    private bool TryMoveCaretVertical(TextPos caret, int spanRow, int col, int deltaRows, out TextPos moved)
    {
        moved = caret;
        if (_lines.Count == 0)
        {
            return false;
        }

        var wrapped = _scrollModel.GetWrappedLine(caret.LineIndex);
        var spans = wrapped.Spans;

        var targetLine = caret.LineIndex;
        var targetSpanRow = spanRow;
        var step = Math.Sign(deltaRows);
        if (step < 0)
        {
            if (spanRow > 0)
            {
                targetSpanRow = spanRow - 1;
            }
            else
            {
                targetLine = caret.LineIndex - 1;
                if (targetLine < 0)
                {
                    return false;
                }

                var prevWrapped = _scrollModel.GetWrappedLine(targetLine);
                targetSpanRow = Math.Max(0, prevWrapped.Spans.Length - 1);
            }
        }
        else if (step > 0)
        {
            if (spanRow + 1 < spans.Length)
            {
                targetSpanRow = spanRow + 1;
            }
            else
            {
                targetLine = caret.LineIndex + 1;
                if (targetLine >= _lines.Count)
                {
                    return false;
                }

                targetSpanRow = 0;
            }
        }
        else
        {
            return false;
        }

        var targetWrappedLine = _scrollModel.GetWrappedLine(targetLine);
        var targetSpan = targetWrappedLine.Spans[Math.Clamp(targetSpanRow, 0, Math.Max(0, targetWrappedLine.Spans.Length - 1))];
        var targetCol = Math.Clamp(col, 0, targetSpan.Length);
        moved = new TextPos(targetLine, targetSpan.Start + targetCol);
        moved = ClampTextPos(moved);
        return true;
    }

    private bool TryGetSpanCursor(TextPos caret, out int spanRow, out int col)
    {
        spanRow = 0;
        col = 0;

        if (_lines.Count == 0)
        {
            return false;
        }

        caret = ClampTextPos(caret);
        var wrapped = _scrollModel.GetWrappedLine(caret.LineIndex);
        var spans = wrapped.Spans;
        if (spans.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < spans.Length; i++)
        {
            var span = spans[i];
            var endInclusive = span.Start + span.Length;
            if (caret.CharIndex <= endInclusive)
            {
                spanRow = i;
                col = Math.Clamp(caret.CharIndex - span.Start, 0, span.Length);
                return true;
            }
        }

        spanRow = spans.Length - 1;
        var last = spans[^1];
        col = Math.Clamp(caret.CharIndex - last.Start, 0, last.Length);
        return true;
    }

    private void EnsureCaretVisible(TextPos caret)
    {
        if (_lastViewportRectPx.Width <= 0 || _lastViewportRectPx.Height <= 0)
        {
            return;
        }

        if (!TryGetSpanCursor(caret, out var spanRow, out _))
        {
            return;
        }

        _scrollModel.EnsureAnchorVisible(caret.LineIndex, spanRow, _lastViewportRectPx);
    }

    private TextPos ClampTextPos(TextPos pos)
    {
        if (_lines.Count == 0)
        {
            return default;
        }

        var lineIndex = Math.Clamp(pos.LineIndex, 0, _lines.Count - 1);
        var line = _lines[lineIndex];
        var charIndex = Math.Clamp(pos.CharIndex, 0, line.Length);
        return new TextPos(lineIndex, charIndex);
    }

    private static (TextPos Start, TextPos End) NormalizeRange(TextRange range)
    {
        var a = range.Anchor;
        var b = range.Caret;
        if (a.LineIndex > b.LineIndex ||
            (a.LineIndex == b.LineIndex && a.CharIndex > b.CharIndex))
        {
            (a, b) = (b, a);
        }
        return (a, b);
    }

    private int GetChromeInsetPx()
    {
        if (!ChromeSpec.Enabled)
        {
            return 0;
        }

        var reservation = Math.Max(0, ChromeSpec.PaddingPx + ChromeSpec.BorderPx);
        var border = Math.Max(0, ChromeSpec.BorderPx);
        return Math.Max(0, (reservation - border) / 2);
    }

    private int UpdateViewportForText(PxRect viewportRectPx)
    {
        _scrollModel.SetScrollbarChromeInsetPx(GetChromeInsetPx());
        _scrollModel.SetContentInsetsPx(PaddingLeftRightPx, PaddingTopBottomPx, PaddingLeftRightPx, PaddingTopBottomPx);

        _scrollModel.SetRightPaddingPx(_scrollbarSettings.ThicknessPx);
        _scrollModel.UpdateViewport(viewportRectPx);

        if (_scrollModel.TryGetScrollbarLayout(viewportRectPx, _scrollbarSettings, out var layout) && layout.IsScrollable)
        {
            return _scrollbarSettings.ThicknessPx;
        }

        _scrollModel.SetRightPaddingPx(0);
        _scrollModel.UpdateViewport(viewportRectPx);
        return 0;
    }

    private static IReadOnlyList<string> LoadLines(string path)
    {
        var lines = new List<string>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }
        return lines;
    }
}

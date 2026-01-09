using System;
using System.Collections.Generic;
using System.IO;
using Cycon.Core.Transcript;
using Cycon.Host.Input;
using Cycon.Host.Scrolling;
using Cycon.Layout.Scrolling;
using Cycon.Render;
using Cycon.Core.Scrolling;

namespace Extensions.Inspect.Blocks;

public sealed class InspectTextBlock : IBlock, IRenderBlock, IMeasureBlock, IBlockPointerHandler, IBlockWheelHandler, IBlockPointerCaptureState
{
    private const int PaddingLeftRightPx = 5;
    private const int PaddingTopBottomPx = 3;

    private readonly IReadOnlyList<string> _lines;
    private readonly TextScrollModel _scrollModel;
    private readonly ScrollbarWidget _scrollbar;
    private readonly ScrollbarUiState _scrollbarUi = new();
    private readonly ScrollbarSettings _scrollbarSettings = new();
    private double _lastRenderTimeSeconds;

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

    public int LineCount => _lines.Count;

    public bool HasPointerCapture => _scrollbarUi.IsDragging;

    public bool HandlePointer(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        _scrollModel.SetRightPaddingPx(_scrollbarSettings.ThicknessPx + PaddingLeftRightPx);
        _scrollModel.SetContentInsetsPx(PaddingLeftRightPx, PaddingTopBottomPx, PaddingLeftRightPx, PaddingTopBottomPx);
        _scrollModel.UpdateViewport(viewportRectPx);

        if (e.Kind == HostMouseEventKind.Move && !_scrollbarUi.IsDragging)
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

        // Prevent transcript selection/interaction inside the viewport; this block is not selectable yet.
        return e.Kind is HostMouseEventKind.Down or HostMouseEventKind.Up;
    }

    public bool HandleWheel(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        _scrollModel.SetRightPaddingPx(_scrollbarSettings.ThicknessPx + PaddingLeftRightPx);
        _scrollModel.SetContentInsetsPx(PaddingLeftRightPx, PaddingTopBottomPx, PaddingLeftRightPx, PaddingTopBottomPx);
        _scrollModel.UpdateViewport(viewportRectPx);

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

        var promptReservedRows = 2;
        var cellH = Math.Max(1, ctx.CellHeightPx);
        var availableRows = Math.Max(0, viewportRows - promptReservedRows);
        var availableHeightPx = checked(availableRows * cellH);

        var capRows = Math.Max(1, availableRows + 1);
        var contentRows = _scrollModel.ComputeWrappedRowsCapped(cols, capRows);
        var contentHeightPx = checked((contentRows * cellH) + (PaddingTopBottomPx * 2));

        var heightPx = Math.Min(availableHeightPx, contentHeightPx);
        heightPx = Math.Max(cellH, heightPx);
        return new BlockSize(ctx.ContentWidthPx, heightPx);
    }

    public void Render(IRenderCanvas canvas, in BlockRenderContext ctx)
    {
        var viewport = ctx.ViewportRectPx;
        var viewportPx = new PxRect(viewport.X, viewport.Y, viewport.Width, viewport.Height);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        _scrollModel.UpdateTextMetrics(ctx.TextMetrics.CellWidthPx, ctx.TextMetrics.CellHeightPx);
        _scrollModel.SetRightPaddingPx(_scrollbarSettings.ThicknessPx + PaddingLeftRightPx);
        _scrollModel.SetContentInsetsPx(PaddingLeftRightPx, PaddingTopBottomPx, PaddingLeftRightPx, PaddingTopBottomPx);
        _scrollModel.UpdateViewport(viewportPx);

        var dtMs = _lastRenderTimeSeconds <= 0
            ? 0
            : (int)Math.Clamp((ctx.TimeSeconds - _lastRenderTimeSeconds) * 1000.0, 0, 250);
        _lastRenderTimeSeconds = ctx.TimeSeconds;

        _scrollbar.BeginTick();
        _scrollbar.AdvanceAnimation(dtMs, _scrollbarSettings);

        canvas.FillRect(viewport, ctx.Theme.BackgroundRgba);

        var cellW = Math.Max(1, ctx.TextMetrics.CellWidthPx);
        var cellH = Math.Max(1, ctx.TextMetrics.CellHeightPx);
        var cols = Math.Max(1, Math.Max(0, viewport.Width - (PaddingLeftRightPx * 2) - _scrollbarSettings.ThicknessPx) / cellW);
        var rows = Math.Max(1, Math.Max(0, viewport.Height - (PaddingTopBottomPx * 2)) / cellH);

        var fg = ctx.Theme.ForegroundRgba;
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
                    canvas.DrawText(line, span.Start, drawLen, xPx, yPx, fg);
                }

                screenRow++;
            }

            lineIndex++;
            subRow = 0;
        }

        var overlayFrame = _scrollbar.BuildOverlayFrame(viewportPx, _scrollbarSettings, ctx.Theme.ForegroundRgba);
        if (overlayFrame is not null)
        {
            ReplayOverlay(canvas, overlayFrame);
        }
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

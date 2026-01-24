using System;
using System.Collections.Generic;
using Cycon.BlockCommands;
using Cycon.Host.Commands;
using Cycon.Core.Transcript;
using Cycon.Host.Input;
using Cycon.Host.Thumbnails;
using Cycon.Layout.Scrolling;
using Cycon.Render;

namespace Cycon.Host.Commands.Blocks;

internal sealed class ThumbnailGridBlock : IBlock, IRenderBlock, IMeasureBlock, IBlockPointerHandler, IBlockWheelHandler, IBlockChromeProvider, IMouseFocusableViewportBlock, IBlockCommandInsertionProvider
{
    private const int PromptReservedRows = 2;
    private const int DefaultMaxInitialRows = 24;

    private readonly IReadOnlyList<FileSystemEntry> _entries;
    private readonly int _sizePx;
    private readonly int _ownerId;
    private int _generation = 1;
    private int _scrollRow;
    private int _maxScrollRow;
    private int _initialHeightRows = -1;
    private bool _hasMouseFocus;
    private int _lastCellW = 8;
    private int _lastCellH = 16;
    private int _lastTileW;
    private int _lastTileH;
    private int _lastCols = 1;
    private int _lastVisibleRows = 1;

    public ThumbnailGridBlock(BlockId id, string directoryPath, IReadOnlyList<FileSystemEntry> entries, int sizePx)
    {
        Id = id;
        DirectoryPath = directoryPath ?? string.Empty;
        _entries = entries ?? Array.Empty<FileSystemEntry>();
        _sizePx = Math.Clamp(sizePx, 16, 512);
        _ownerId = id.Value;
        ShellThumbnailService.Instance.SetOwnerGeneration(_ownerId, _generation);
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Scene3D;
    public string DirectoryPath { get; }
    public BlockChromeSpec ChromeSpec => BlockChromeSpec.ViewDefault;

    public bool HasMouseFocus
    {
        get => _hasMouseFocus;
        set => _hasMouseFocus = value;
    }

    public BlockSize Measure(in BlockMeasureContext ctx)
    {
        var width = Math.Max(0, ctx.ContentWidthPx);
        var cellH = Math.Max(1, ctx.CellHeightPx);
        var viewportRows = Math.Max(1, ctx.ViewportRows);
        var availableRows = Math.Max(1, viewportRows - PromptReservedRows);

        if (_initialHeightRows < 0)
        {
            _initialHeightRows = Math.Min(availableRows, DefaultMaxInitialRows);
        }

        var heightRows = Math.Min(availableRows, Math.Max(1, _initialHeightRows));
        return new BlockSize(width, checked(heightRows * cellH));
    }

    public void Render(IRenderCanvas canvas, in BlockRenderContext ctx)
    {
        DrainThumbnailReleases(canvas);

        var viewport = ctx.ViewportRectPx;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var cellW = Math.Max(1, ctx.TextMetrics.CellWidthPx);
        var cellH = Math.Max(1, ctx.TextMetrics.CellHeightPx);
        var gap = Math.Max(4, cellW);

        var tileW = _sizePx + (gap * 2);
        var textH = cellH;
        var tileH = _sizePx + textH + (gap * 2);

        var cols = Math.Max(1, viewport.Width / Math.Max(1, tileW));
        var totalRows = (int)Math.Ceiling(_entries.Count / (double)cols);
        var visibleRows = Math.Max(1, viewport.Height / Math.Max(1, tileH));
        _maxScrollRow = Math.Max(0, totalRows - visibleRows);
        _scrollRow = Math.Clamp(_scrollRow, 0, _maxScrollRow);

        if (_lastCols != 0 && (_lastCols != cols || _lastVisibleRows != visibleRows))
        {
            _generation++;
            ShellThumbnailService.Instance.SetOwnerGeneration(_ownerId, _generation);
        }

        _lastCellW = cellW;
        _lastCellH = cellH;
        _lastTileW = tileW;
        _lastTileH = tileH;
        _lastCols = cols;
        _lastVisibleRows = visibleRows;

        var bg = ctx.Theme.BackgroundRgba;
        canvas.FillRect(viewport, bg);

        var startRow = _scrollRow;
        var endRow = Math.Min(totalRows, startRow + visibleRows + 1);

        for (var row = startRow; row < endRow; row++)
        {
            var y = viewport.Y + ((row - startRow) * tileH);
            for (var col = 0; col < cols; col++)
            {
                var index = (row * cols) + col;
                if ((uint)index >= (uint)_entries.Count)
                {
                    break;
                }

                var entry = _entries[index];
                var x = viewport.X + (col * tileW);
                DrawTile(canvas, ctx, entry, x, y, tileW, tileH, gap, cellW, cellH);
            }
        }
    }

    public bool HandleWheel(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        if (e.Kind != HostMouseEventKind.Wheel || e.WheelDelta == 0)
        {
            return false;
        }

        if (!viewportRectPx.Contains(e.X, e.Y))
        {
            return false;
        }

        var delta = e.WheelDelta;
        if (Math.Abs(delta) >= 10)
        {
            delta /= 120;
        }

        delta = Math.Clamp(delta, -3, 3);
        if (delta == 0)
        {
            delta = e.WheelDelta > 0 ? 1 : -1;
        }

        var next = _scrollRow - delta;
        if (next == _scrollRow)
        {
            return true;
        }

        _scrollRow = Math.Clamp(next, 0, _maxScrollRow);
        _generation++;
        ShellThumbnailService.Instance.SetOwnerGeneration(_ownerId, _generation);
        return true;
    }

    public bool HandlePointer(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        // Click is handled by the host via IBlockCommandInsertionProvider.
        return e.Kind is HostMouseEventKind.Down or HostMouseEventKind.Up or HostMouseEventKind.Move;
    }

    public bool TryGetInsertionCommand(int x, int y, in PxRect viewportRectPx, out string commandText)
    {
        commandText = string.Empty;
        if (x < viewportRectPx.X || y < viewportRectPx.Y || x >= viewportRectPx.X + viewportRectPx.Width || y >= viewportRectPx.Y + viewportRectPx.Height)
        {
            return false;
        }

        var cellW = _lastCellW;
        var cellH = _lastCellH;
        var tileW = _lastTileW > 0 ? _lastTileW : (_sizePx + (Math.Max(4, cellW) * 2));
        var tileH = _lastTileH > 0 ? _lastTileH : (_sizePx + cellH + (Math.Max(4, cellW) * 2));
        var cols = _lastCols > 0 ? _lastCols : Math.Max(1, viewportRectPx.Width / Math.Max(1, tileW));
        var scrollRow = Math.Clamp(_scrollRow, 0, _maxScrollRow);

        var localX = x - viewportRectPx.X;
        var localY = y - viewportRectPx.Y;
        var col = localX / Math.Max(1, tileW);
        var row = localY / Math.Max(1, tileH);
        if (col < 0 || row < 0)
        {
            return false;
        }

        var index = ((scrollRow + row) * cols) + col;
        if ((uint)index >= (uint)_entries.Count)
        {
            return false;
        }

        var entry = _entries[index];
        var quoted = CommandLineQuote.Quote(entry.FullPath);
        commandText = entry.IsDirectory ? $"cd {quoted}" : $"view {quoted}";
        return true;
    }

    private void DrawTile(
        IRenderCanvas canvas,
        in BlockRenderContext ctx,
        in FileSystemEntry entry,
        int x,
        int y,
        int tileW,
        int tileH,
        int gap,
        int cellW,
        int cellH)
    {
        var tileRect = new RectPx(x, y, tileW, tileH);
        var border = unchecked((int)0xFF303030);
        canvas.FillRect(tileRect, unchecked((int)0xFF101010));

        // Border
        canvas.FillRect(new RectPx(tileRect.X, tileRect.Y, tileRect.Width, 1), border);
        canvas.FillRect(new RectPx(tileRect.X, tileRect.Y + tileRect.Height - 1, tileRect.Width, 1), border);
        canvas.FillRect(new RectPx(tileRect.X, tileRect.Y, 1, tileRect.Height), border);
        canvas.FillRect(new RectPx(tileRect.X + tileRect.Width - 1, tileRect.Y, 1, tileRect.Height), border);

        var thumbRectF = new RectF(
            x + gap,
            y + gap,
            _sizePx,
            _sizePx);

        var svc = ShellThumbnailService.Instance;
        if (!svc.TryGetCached(entry.FullPath, _sizePx, out var image))
        {
            svc.RequestThumbnail(_ownerId, _generation, entry.FullPath, _sizePx);
            image = svc.GetFallbackIcon(entry.IsDirectory, _sizePx);
        }

        canvas.DrawImage2D(image.ImageId, image.RgbaPixels, image.Width, image.Height, thumbRectF, image.UseNearest);

        var maxChars = Math.Max(1, (tileW - (gap * 2)) / Math.Max(1, cellW));
        var name = entry.Name ?? string.Empty;
        if (name.Length > maxChars)
        {
            name = name.Substring(0, Math.Max(1, maxChars - 1)) + "…";
        }

        var textX = x + gap;
        var textY = y + gap + _sizePx + (gap / 2);
        canvas.DrawText(name, 0, name.Length, textX, textY, ctx.Theme.ForegroundRgba);
    }

    private static void DrainThumbnailReleases(IRenderCanvas canvas)
    {
        var svc = ShellThumbnailService.Instance;
        var drained = 0;
        while (drained < 8 && svc.TryDequeueRelease(out var imageId))
        {
            canvas.ReleaseImage2D(imageId);
            drained++;
        }
    }
}

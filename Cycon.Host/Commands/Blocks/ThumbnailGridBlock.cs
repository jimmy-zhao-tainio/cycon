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
    private readonly IReadOnlyList<FileSystemEntry> _entries;
    private readonly int _sizePx;
    private readonly int _ownerId;
    private int _generation = 1;
    private int _maxScrollRow;
    private bool _hasMouseFocus;
    private int _lastCellW = 8;
    private int _lastCellH = 16;
    private int _lastTileW;
    private int _lastTileH;
    private int _lastCols = 1;
    private int _lastVisibleRows = 1;
    private int _lastBandStartRow = -1;
    private int _lastBandEndRow = -1;
    private int _lastBandStartCol = -1;
    private int _lastBandEndCol = -1;

    public ThumbnailGridBlock(BlockId id, string directoryPath, IReadOnlyList<FileSystemEntry> entries, int sizePx)
    {
        Id = id;
        DirectoryPath = directoryPath ?? string.Empty;
        _entries = entries ?? Array.Empty<FileSystemEntry>();
        _sizePx = Math.Clamp(sizePx, 16, 512);
        _ownerId = id.Value;
        if (OperatingSystem.IsWindows())
        {
            ShellThumbnailService.Instance.SetOwnerGeneration(_ownerId, _generation);
        }
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
        var cellW = Math.Max(1, ctx.CellWidthPx);
        var chromeInset = ChromeSpec.Enabled ? Math.Max(0, ChromeSpec.BorderPx + ChromeSpec.PaddingPx) : 0;
        var innerWidth = Math.Max(0, width - (chromeInset * 2));

        var gap = Math.Max(4, cellW);
        var tileW = _sizePx + (gap * 2);
        var baseTileH = _sizePx + cellH + (gap * 2);
        var tileH = SnapToStep(baseTileH, cellH);
        var cols = Math.Max(1, innerWidth / Math.Max(1, tileW));
        var totalRows = Math.Max(1, (int)Math.Ceiling(_entries.Count / (double)cols));
        var heightPxLong = (long)totalRows * tileH;
        var heightPx = heightPxLong >= int.MaxValue ? int.MaxValue : (int)heightPxLong;
        heightPx = Math.Max(0, heightPx) + (chromeInset * 2);
        return new BlockSize(width, heightPx);
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
        var baseTileH = _sizePx + cellH + (gap * 2);
        var tileH = SnapToStep(baseTileH, cellH);

        var cols = Math.Max(1, viewport.Width / Math.Max(1, tileW));
        var totalRows = (int)Math.Ceiling(_entries.Count / (double)cols);
        var visibleRows = Math.Max(1, viewport.Height / Math.Max(1, tileH));
        _maxScrollRow = Math.Max(0, totalRows - visibleRows);

        if (_lastCols != 0 && (_lastCols != cols || _lastVisibleRows != visibleRows))
        {
            _generation++;
            if (OperatingSystem.IsWindows())
            {
                ShellThumbnailService.Instance.SetOwnerGeneration(_ownerId, _generation);
            }
        }

        _lastCellW = cellW;
        _lastCellH = cellH;
        _lastTileW = tileW;
        _lastTileH = tileH;
        _lastCols = cols;
        _lastVisibleRows = visibleRows;

        canvas.FillRect(viewport, unchecked((int)0x000000FF));

        var fbW = Math.Max(0, ctx.FramebufferWidthPx);
        var fbH = Math.Max(0, ctx.FramebufferHeightPx);

        var screenY0 = Math.Max(0, viewport.Y);
        var screenY1 = fbH > 0 ? Math.Min(fbH, viewport.Y + viewport.Height) : (viewport.Y + viewport.Height);
        var visibleLocalY0 = Math.Max(0, screenY0 - viewport.Y);
        var visibleLocalY1 = Math.Max(0, screenY1 - viewport.Y);

        var startRow = Math.Max(0, visibleLocalY0 / Math.Max(1, tileH));
        var endRow = Math.Min(totalRows, (visibleLocalY1 + tileH - 1) / Math.Max(1, tileH));
        if (endRow <= startRow)
        {
            endRow = Math.Min(totalRows, startRow + 1);
        }

        var screenX0 = Math.Max(0, viewport.X);
        var screenX1 = fbW > 0 ? Math.Min(fbW, viewport.X + viewport.Width) : (viewport.X + viewport.Width);
        var visibleLocalX0 = Math.Max(0, screenX0 - viewport.X);
        var visibleLocalX1 = Math.Max(0, screenX1 - viewport.X);
        var startCol = Math.Max(0, visibleLocalX0 / Math.Max(1, tileW));
        var endCol = Math.Min(cols, (visibleLocalX1 + tileW - 1) / Math.Max(1, tileW));
        if (endCol <= startCol)
        {
            endCol = Math.Min(cols, startCol + 1);
        }

        // Cancel pending thumbnail work when the visible band changes (e.g. transcript scroll).
        if (_lastBandStartRow >= 0 &&
            (_lastBandStartRow != startRow ||
             _lastBandEndRow != endRow ||
             _lastBandStartCol != startCol ||
             _lastBandEndCol != endCol))
        {
            _generation++;
            if (OperatingSystem.IsWindows())
            {
                ShellThumbnailService.Instance.SetOwnerGeneration(_ownerId, _generation);
            }
        }

        _lastBandStartRow = startRow;
        _lastBandEndRow = endRow;
        _lastBandStartCol = startCol;
        _lastBandEndCol = endCol;

        for (var row = startRow; row < endRow; row++)
        {
            var y = viewport.Y + (row * tileH);

            for (var col = startCol; col < endCol; col++)
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
        return false;
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

        var localX = x - viewportRectPx.X;
        var localY = y - viewportRectPx.Y;
        var col = localX / Math.Max(1, tileW);
        var row = localY / Math.Max(1, tileH);
        if (col < 0 || row < 0)
        {
            return false;
        }

        var index = (row * cols) + col;
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
        canvas.FillRect(tileRect, unchecked((int)0x000000FF));

        var iconSize = Math.Max(8, _sizePx - (cellW * 4));
        var iconInset = Math.Max(0, (_sizePx - iconSize) / 2);
        var thumbRectF = new RectF(
            x + gap + iconInset,
            y + gap + iconInset,
            iconSize,
            iconSize);

        if (!OperatingSystem.IsWindows())
        {
            DrawFallbackIcon(canvas, thumbRectF, entry.IsDirectory);
            goto DrawText;
        }

        var svc = ShellThumbnailService.Instance;
        if (!svc.TryGetCached(entry.FullPath, _sizePx, out var image))
        {
            svc.RequestThumbnail(_ownerId, _generation, entry.FullPath, _sizePx);
            image = svc.GetFallbackIcon(entry.IsDirectory, _sizePx);
        }

        canvas.DrawImage2D(image.ImageId, image.RgbaPixels, image.Width, image.Height, thumbRectF, image.UseNearest);

    DrawText:
        var maxChars = Math.Max(1, tileW / Math.Max(1, cellW));
        var name = entry.Name ?? string.Empty;
        if (name.Length > maxChars)
        {
            if (maxChars <= 3)
            {
                name = name.Substring(0, maxChars);
            }
            else
            {
                name = name.Substring(0, maxChars - 3) + "...";
            }
        }

        var textW = Math.Min(tileW, name.Length * cellW);
        var textX = x + Math.Max(0, (tileW - textW) / 2);
        var textY = y + gap + _sizePx + (gap / 2);
        canvas.DrawText(name, 0, name.Length, textX, textY, ctx.Theme.ForegroundRgba);
    }

    private static void DrainThumbnailReleases(IRenderCanvas canvas)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var svc = ShellThumbnailService.Instance;
        var drained = 0;
        while (drained < 8 && svc.TryDequeueRelease(out var imageId))
        {
            canvas.ReleaseImage2D(imageId);
            drained++;
        }
    }

    private static void DrawFallbackIcon(IRenderCanvas canvas, in RectF rect, bool isDirectory)
    {
        var x = (int)Math.Round(rect.X);
        var y = (int)Math.Round(rect.Y);
        var w = Math.Max(1, (int)Math.Round(rect.Width));
        var h = Math.Max(1, (int)Math.Round(rect.Height));

        _ = isDirectory;
        var bg = unchecked((int)0x000000FF);
        var fg = unchecked((int)0xB0B0B0FF);

        canvas.FillRect(new RectPx(x, y, w, h), bg);
        var inset = Math.Max(1, Math.Min(w, h) / 8);
        canvas.FillRect(new RectPx(x + inset, y + inset, Math.Max(1, w - (inset * 2)), Math.Max(1, h - (inset * 2))), fg);
    }

    private static int SnapToStep(int value, int step)
    {
        step = Math.Max(1, step);
        return Math.Max(step, (int)Math.Ceiling(value / (double)step) * step);
    }
}

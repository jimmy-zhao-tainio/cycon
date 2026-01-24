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

internal sealed class ThumbnailGridBlock : IBlock, IRenderBlock, IMeasureBlock, IBlockPointerHandler, IBlockWheelHandler, IBlockChromeProvider, IMouseFocusableViewportBlock, IBlockCommandInsertionProvider, IBlockKeyHandler, IBlockCommandActivationProvider
{
    private const int MaxLabelRows = 3;
    private const int TileGutterCols = 1;
    private const int TileExtraCols = 6;
    private const int IconTextGapPx = 4;
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
    private int _lastStrideW;
    private int _lastCols = 1;
    private int _lastTotalRows = 0;
    private int _lastBandStartRow = -1;
    private int _lastBandEndRow = -1;
    private int _lastBandStartCol = -1;
    private int _lastBandEndCol = -1;
    private int[] _rowStartsPx = Array.Empty<int>();
    private int[] _rowHeightsPx = Array.Empty<int>();
    private int[] _rowLabelLines = Array.Empty<int>();
    private int _hoverIndex = -1;
    private int _selectedIndex = -1;
    private BlockCommandActivation? _pendingActivation;

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
    public int SizePx => _sizePx;
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
        var tileW = ComputeTileWidth(cellW, gap);
        var strideW = tileW + (TileGutterCols * cellW);
        var cols = Math.Max(1, innerWidth / Math.Max(1, strideW));
        var totalRows = Math.Max(1, (int)Math.Ceiling(_entries.Count / (double)cols));
        var maxChars = Math.Max(1, tileW / Math.Max(1, cellW));

        var heightPxLong = 0L;
        for (var row = 0; row < totalRows; row++)
        {
            var maxLines = 1;
            var start = row * cols;
            var end = Math.Min(_entries.Count, start + cols);
            for (var i = start; i < end; i++)
            {
                var lines = ComputeRequiredLabelLines(_entries[i].Name, maxChars);
                if (lines > maxLines)
                {
                    maxLines = lines;
                    if (maxLines >= MaxLabelRows)
                    {
                        break;
                    }
                }
            }

            var rowH = SnapToStep(_sizePx + IconTextGapPx + (maxLines * cellH) + (gap * 2), cellH);
            heightPxLong += rowH;
            if (heightPxLong >= int.MaxValue)
            {
                heightPxLong = int.MaxValue;
                break;
            }
        }

        var heightPx = (int)heightPxLong;
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

        var tileW = ComputeTileWidth(cellW, gap);
        var strideW = tileW + (TileGutterCols * cellW);
        var maxChars = Math.Max(1, tileW / Math.Max(1, cellW));

        var cols = Math.Max(1, viewport.Width / Math.Max(1, strideW));
        var totalRows = (int)Math.Ceiling(_entries.Count / (double)cols);
        totalRows = Math.Max(1, totalRows);

        EnsureRowBuffers(totalRows);
        var cursorY = 0;
        for (var row = 0; row < totalRows; row++)
        {
            var maxLines = 1;
            var start = row * cols;
            var end = Math.Min(_entries.Count, start + cols);
            for (var i = start; i < end; i++)
            {
                var lines = ComputeRequiredLabelLines(_entries[i].Name, maxChars);
                if (lines > maxLines)
                {
                    maxLines = lines;
                    if (maxLines >= MaxLabelRows)
                    {
                        break;
                    }
                }
            }

            var rowH = SnapToStep(_sizePx + IconTextGapPx + (maxLines * cellH) + (gap * 2), cellH);
            _rowStartsPx[row] = cursorY;
            _rowHeightsPx[row] = rowH;
            _rowLabelLines[row] = maxLines;
            cursorY = checked(cursorY + rowH);
        }

        _maxScrollRow = Math.Max(0, totalRows - 1);

        if (_lastCols != 0 && (_lastCols != cols || _lastStrideW != strideW))
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
        _lastTileH = cursorY;
        _lastStrideW = strideW;
        _lastCols = cols;
        _lastTotalRows = totalRows;

        canvas.FillRect(viewport, unchecked((int)0x000000FF));

        var fbW = Math.Max(0, ctx.FramebufferWidthPx);
        var fbH = Math.Max(0, ctx.FramebufferHeightPx);

        var screenY0 = Math.Max(0, viewport.Y);
        var screenY1 = fbH > 0 ? Math.Min(fbH, viewport.Y + viewport.Height) : (viewport.Y + viewport.Height);
        var visibleLocalY0 = Math.Max(0, screenY0 - viewport.Y);
        var visibleLocalY1 = Math.Max(0, screenY1 - viewport.Y);

        var startRow = FindRowAtOrBeforeY(visibleLocalY0, totalRows);
        var endRow = FindRowAtOrBeforeY(Math.Max(0, visibleLocalY1 - 1), totalRows) + 1;
        endRow = Math.Min(totalRows, Math.Max(startRow + 1, endRow));
        if (endRow <= startRow)
        {
            endRow = Math.Min(totalRows, startRow + 1);
        }

        var screenX0 = Math.Max(0, viewport.X);
        var screenX1 = fbW > 0 ? Math.Min(fbW, viewport.X + viewport.Width) : (viewport.X + viewport.Width);
        var visibleLocalX0 = Math.Max(0, screenX0 - viewport.X);
        var visibleLocalX1 = Math.Max(0, screenX1 - viewport.X);
        var startCol = Math.Max(0, visibleLocalX0 / Math.Max(1, strideW));
        var endCol = Math.Min(cols, (visibleLocalX1 + strideW - 1) / Math.Max(1, strideW));
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
            var y = viewport.Y + _rowStartsPx[row];
            var rowH = _rowHeightsPx[row];
            var rowMaxLines = _rowLabelLines[row];

            for (var col = startCol; col < endCol; col++)
            {
                var index = (row * cols) + col;
                if ((uint)index >= (uint)_entries.Count)
                {
                    break;
                }

                var entry = _entries[index];
                var x = viewport.X + (col * strideW);
                DrawTile(canvas, ctx, entry, index, x, y, tileW, rowH, rowMaxLines, gap, cellW, cellH);
            }
        }
    }

    public bool HandleWheel(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        return false;
    }

    public bool HandlePointer(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        // Click selection is handled by the host. Pointer is still consumed to prevent upstream selection.
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
        var tileW = _lastTileW > 0 ? _lastTileW : ComputeTileWidth(cellW, Math.Max(4, cellW));
        var strideW = _lastStrideW > 0 ? _lastStrideW : tileW + (TileGutterCols * cellW);
        var cols = _lastCols > 0 ? _lastCols : Math.Max(1, viewportRectPx.Width / Math.Max(1, strideW));
        var totalRows = _rowStartsPx.Length;

        var localX = x - viewportRectPx.X;
        var localY = y - viewportRectPx.Y;
        var col = localX / Math.Max(1, strideW);
        if (col < 0)
        {
            return false;
        }

        var inTileX = localX - (col * strideW);
        if (inTileX < 0 || inTileX >= tileW)
        {
            return false;
        }

        totalRows = _lastTotalRows;
        if (totalRows <= 0)
        {
            return false;
        }

        var row = FindRowAtOrBeforeY(localY, totalRows);
        if (row < 0 || row >= totalRows)
        {
            return false;
        }

        var inRowY = localY - _rowStartsPx[row];
        if (inRowY < 0 || inRowY >= _rowHeightsPx[row])
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

    public bool TryGetEntryIndexAt(int x, int y, in PxRect viewportRectPx, out int index)
    {
        index = -1;
        if (x < viewportRectPx.X || y < viewportRectPx.Y || x >= viewportRectPx.X + viewportRectPx.Width || y >= viewportRectPx.Y + viewportRectPx.Height)
        {
            return false;
        }

        var cellW = _lastCellW;
        var tileW = _lastTileW > 0 ? _lastTileW : ComputeTileWidth(cellW, Math.Max(4, cellW));
        var strideW = _lastStrideW > 0 ? _lastStrideW : tileW + (TileGutterCols * cellW);
        var cols = _lastCols > 0 ? _lastCols : Math.Max(1, viewportRectPx.Width / Math.Max(1, strideW));

        var localX = x - viewportRectPx.X;
        var localY = y - viewportRectPx.Y;
        var col = localX / Math.Max(1, strideW);
        if (col < 0)
        {
            return false;
        }

        var inTileX = localX - (col * strideW);
        if (inTileX < 0 || inTileX >= tileW)
        {
            return false;
        }

        var totalRows = _lastTotalRows;
        if (totalRows <= 0)
        {
            return false;
        }

        var row = FindRowAtOrBeforeY(localY, totalRows);
        if (row < 0 || row >= totalRows)
        {
            return false;
        }

        var inRowY = localY - _rowStartsPx[row];
        if (inRowY < 0 || inRowY >= _rowHeightsPx[row])
        {
            return false;
        }

        index = (row * cols) + col;
        return (uint)index < (uint)_entries.Count;
    }

    public bool SetHoveredIndex(int index)
    {
        index = (uint)index < (uint)_entries.Count ? index : -1;
        if (_hoverIndex == index)
        {
            return false;
        }

        _hoverIndex = index;
        return true;
    }

    public bool SetSelectedIndex(int index)
    {
        index = (uint)index < (uint)_entries.Count ? index : -1;
        if (_selectedIndex == index)
        {
            return false;
        }

        _selectedIndex = index;
        return true;
    }

    public bool HandleKey(in HostKeyEvent e)
    {
        if (!e.IsDown)
        {
            return false;
        }

        var cols = Math.Max(1, _lastCols);
        var count = _entries.Count;

        switch (e.Key)
        {
            case HostKey.Left:
                if (count <= 0) return true;
                if (_selectedIndex < 0) _selectedIndex = 0;
                else _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return true;
            case HostKey.Right:
                if (count <= 0) return true;
                if (_selectedIndex < 0) _selectedIndex = 0;
                else _selectedIndex = Math.Min(count - 1, _selectedIndex + 1);
                return true;
            case HostKey.Up:
                if (count <= 0) return true;
                if (_selectedIndex < 0) _selectedIndex = 0;
                else _selectedIndex = Math.Max(0, _selectedIndex - cols);
                return true;
            case HostKey.Down:
                if (count <= 0) return true;
                if (_selectedIndex < 0) _selectedIndex = 0;
                else _selectedIndex = Math.Min(count - 1, _selectedIndex + cols);
                return true;
            case HostKey.Escape:
                return SetSelectedIndex(-1);
            case HostKey.Enter:
                if ((uint)_selectedIndex >= (uint)_entries.Count)
                {
                    return true;
                }

                var entry = _entries[_selectedIndex];
                var quoted = CommandLineQuote.Quote(entry.FullPath);
                var commandText = entry.IsDirectory ? $"cd {quoted}" : $"view {quoted}";
                var refresh = entry.IsDirectory ? $"grid -s {_sizePx}" : null;
                _pendingActivation = new BlockCommandActivation(commandText, refresh);
                return true;
        }

        return false;
    }

    public bool TryDequeueActivation(out BlockCommandActivation activation)
    {
        if (_pendingActivation is { } pending)
        {
            _pendingActivation = null;
            activation = pending;
            return true;
        }

        activation = default;
        return false;
    }

    private void DrawTile(
        IRenderCanvas canvas,
        in BlockRenderContext ctx,
        in FileSystemEntry entry,
        int entryIndex,
        int x,
        int y,
        int tileW,
        int tileH,
        int rowMaxLabelLines,
        int gap,
        int cellW,
        int cellH)
    {
        var tileRect = new RectPx(x, y, tileW, tileH);
        var bg = unchecked((int)0x000000FF);
        if (entryIndex == _selectedIndex)
        {
            bg = unchecked((int)0x2A2A2AFF);
        }
        else if (entryIndex == _hoverIndex)
        {
            bg = unchecked((int)0x1C1C1CFF);
        }

        canvas.FillRect(tileRect, bg);

        var iconSquareX = x + Math.Max(0, (tileW - _sizePx) / 2);
        var iconSize = Math.Max(16, _sizePx - (cellW * 5));
        var iconInsetX = Math.Max(0, (_sizePx - iconSize) / 2);
        var iconInsetY = Math.Max(0, _sizePx - iconSize); // bottom-aligned
        var thumbRectF = new RectF(
            iconSquareX + iconInsetX,
            y + gap + iconInsetY,
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
        var nameLen = name.Length;

        var neededLines = ComputeRequiredLabelLines(name, maxChars);
        var labelAreaLines = Math.Max(1, Math.Min(MaxLabelRows, rowMaxLabelLines));
        var labelAreaHeight = labelAreaLines * cellH;
        var textAreaHeight = neededLines * cellH;

        var labelTopY = y + gap + _sizePx + IconTextGapPx;
        var textYBase = labelTopY;

        var totalCapacity = maxChars * labelAreaLines;
        var needsEllipsis = nameLen > totalCapacity;
        var usedLines = needsEllipsis ? labelAreaLines : neededLines;

        for (var line = 0; line < usedLines; line++)
        {
            var lineY = textYBase + (line * cellH);
            var start = line * maxChars;
            if ((uint)start >= (uint)nameLen)
            {
                break;
            }

            if (!needsEllipsis || line < MaxLabelRows - 1)
            {
                var len = Math.Min(maxChars, nameLen - start);
                var lineW = len * cellW;
                var lineX = x + Math.Max(0, (tileW - lineW) / 2);
                canvas.DrawText(name, start, len, lineX, lineY, ctx.Theme.ForegroundRgba);
                continue;
            }

            // Last line with ellipsis if we had to truncate.
            if (maxChars <= 3)
            {
                var len = Math.Min(maxChars, nameLen - start);
                var lineW = len * cellW;
                var lineX = x + Math.Max(0, (tileW - lineW) / 2);
                canvas.DrawText(name, start, len, lineX, lineY, ctx.Theme.ForegroundRgba);
                continue;
            }

            var prefixLen = maxChars - 3;
            var visibleLen = prefixLen + 3;
            var visibleW = visibleLen * cellW;
            var x0 = x + Math.Max(0, (tileW - visibleW) / 2);
            canvas.DrawText(name, start, Math.Min(prefixLen, nameLen - start), x0, lineY, ctx.Theme.ForegroundRgba);
            canvas.DrawText("...", 0, 3, x0 + (prefixLen * cellW), lineY, ctx.Theme.ForegroundRgba);
        }
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

    private int ComputeTileWidth(int cellW, int gap)
    {
        cellW = Math.Max(1, cellW);
        gap = Math.Max(0, gap);
        var baseW = _sizePx + (gap * 2) + (TileExtraCols * cellW);
        return Math.Max(cellW, SnapToStep(baseW, cellW));
    }

    private static int ComputeRequiredLabelLines(string? name, int maxChars)
    {
        if (maxChars <= 0)
        {
            return 1;
        }

        var len = string.IsNullOrEmpty(name) ? 0 : name.Length;
        if (len <= 0)
        {
            return 1;
        }

        var lines = (int)Math.Ceiling(len / (double)maxChars);
        return Math.Clamp(lines, 1, MaxLabelRows);
    }

    private void EnsureRowBuffers(int totalRows)
    {
        if (totalRows <= 0)
        {
            return;
        }

        if (_rowStartsPx.Length < totalRows)
        {
            _rowStartsPx = new int[totalRows];
            _rowHeightsPx = new int[totalRows];
            _rowLabelLines = new int[totalRows];
        }
    }

    private int FindRowAtOrBeforeY(int y, int totalRows)
    {
        if (totalRows <= 0)
        {
            return 0;
        }

        y = Math.Max(0, y);
        var lo = 0;
        var hi = totalRows - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var start = _rowStartsPx[mid];
            if (start == y)
            {
                return mid;
            }
            if (start < y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return Math.Clamp(hi, 0, totalRows - 1);
    }

    private static int SnapToStep(int value, int step)
    {
        step = Math.Max(1, step);
        return Math.Max(step, (int)Math.Ceiling(value / (double)step) * step);
    }
}

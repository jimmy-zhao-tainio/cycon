using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Cycon.Host.Thumbnails;

[SupportedOSPlatform("windows")]
internal sealed class ShellThumbnailService : IThumbnailService
{
    public static ShellThumbnailService Instance { get; } = new();

    private const int DefaultCacheCapacity = 256;

    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _cache = new(CacheKeyComparer.Instance);
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly int _capacity;
    private int _nextImageId = 100_000;

    private readonly Dictionary<int, int> _ownerGeneration = new();
    private readonly HashSet<CacheKey> _pending = new(CacheKeyComparer.Instance);
    private readonly ConcurrentQueue<int> _pendingReleases = new();

    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>());
    private Thread? _thread;
    private int _hasUpdates;

    private readonly Dictionary<(bool IsDir, int Size), ThumbnailImage> _fallbackIcons = new();

    private ShellThumbnailService(int capacity = DefaultCacheCapacity)
    {
        _capacity = Math.Max(16, capacity);
    }

    public bool TryGetCached(string path, int sizePx, out ThumbnailImage image)
    {
        image = default;
        if (string.IsNullOrWhiteSpace(path) || sizePx <= 0)
        {
            return false;
        }

        var key = new CacheKey(path, sizePx);
        lock (_gate)
        {
            if (!_cache.TryGetValue(key, out var node))
            {
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            image = node.Value.Image;
            return true;
        }
    }

    public void RequestThumbnail(int ownerId, int generation, string path, int sizePx)
    {
        if (string.IsNullOrWhiteSpace(path) || sizePx <= 0)
        {
            return;
        }

        path = path.Trim();

        var key = new CacheKey(path, sizePx);
        lock (_gate)
        {
            _ownerGeneration[ownerId] = generation;

            if (_cache.ContainsKey(key))
            {
                return;
            }

            if (_pending.Contains(key))
            {
                return;
            }

            _pending.Add(key);
        }

        EnsureThread();
        _queue.Add(new WorkItem(ownerId, generation, path, sizePx));
    }

    public void SetOwnerGeneration(int ownerId, int generation)
    {
        lock (_gate)
        {
            _ownerGeneration[ownerId] = generation;
        }
    }

    public bool ConsumeHasUpdates() => Interlocked.Exchange(ref _hasUpdates, 0) != 0;

    public bool TryDequeueRelease(out int imageId) => _pendingReleases.TryDequeue(out imageId);

    public ThumbnailImage GetFallbackIcon(bool isDirectory, int sizePx)
    {
        sizePx = Math.Clamp(sizePx, 16, 512);
        lock (_gate)
        {
            if (_fallbackIcons.TryGetValue((isDirectory, sizePx), out var cached))
            {
                return cached;
            }

            var rgba = isDirectory
                ? BuildFolderIconRgba(sizePx, sizePx)
                : BuildFileIconRgba(sizePx, sizePx);
            ToGrayscaleInPlace(rgba);

            var image = new ThumbnailImage(AllocateImageId(), rgba, sizePx, sizePx, UseNearest: false);
            _fallbackIcons[(isDirectory, sizePx)] = image;
            return image;
        }
    }

    private int AllocateImageId()
    {
        lock (_gate)
        {
            return _nextImageId++;
        }
    }

    private void EnsureThread()
    {
        if (_thread is not null)
        {
            return;
        }

        lock (_gate)
        {
            if (_thread is not null)
            {
                return;
            }

            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Cycon.ShellThumbnailService"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    private void WorkerLoop()
    {
        try
        {
            _ = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.COINIT_APARTMENTTHREADED);
        }
        catch
        {
        }

        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                ProcessWorkItem(item);
            }
        }
        finally
        {
            try
            {
                Ole32.CoUninitialize();
            }
            catch
            {
            }
        }
    }

    private void ProcessWorkItem(in WorkItem item)
    {
        var key = new CacheKey(item.Path, item.SizePx);
        try
        {
            lock (_gate)
            {
                if (_ownerGeneration.TryGetValue(item.OwnerId, out var current) && current != item.Generation)
                {
                    _pending.Remove(key);
                    return;
                }
            }

            if (TryGetShellThumbnail(item.Path, item.SizePx, out var rgba, out var width, out var height))
            {
                ToGrayscaleInPlace(rgba);
                var image = new ThumbnailImage(AllocateImageId(), rgba, width, height, UseNearest: false);
                InsertIntoCache(key, image);
                return;
            }

            var isDir = false;
            try
            {
                isDir = Directory.Exists(item.Path);
            }
            catch
            {
            }

            var fallback = GetFallbackIcon(isDir, item.SizePx);
            InsertIntoCache(key, fallback);
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(key);
            }
        }
    }

    private void InsertIntoCache(in CacheKey key, in ThumbnailImage image)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _cache.Remove(key);
            }

            var entry = new CacheEntry(key, image);
            var node = new LinkedListNode<CacheEntry>(entry);
            _lru.AddFirst(node);
            _cache[key] = node;

            while (_cache.Count > _capacity && _lru.Last is { } last)
            {
                _lru.RemoveLast();
                _cache.Remove(last.Value.Key);
                _pendingReleases.Enqueue(last.Value.Image.ImageId);
            }
        }

        Interlocked.Exchange(ref _hasUpdates, 1);
    }

    private static bool TryGetShellThumbnail(string path, int sizePx, out ThumbnailImage image)
    {
        image = default;
        if (!TryGetShellThumbnail(path, sizePx, out var rgba, out var width, out var height))
        {
            return false;
        }

        image = new ThumbnailImage(0, rgba, width, height, UseNearest: false);
        return true;
    }

    private static bool TryGetShellThumbnail(string path, int sizePx, out byte[] rgba, out int width, out int height)
    {
        rgba = Array.Empty<byte>();
        width = 0;
        height = 0;
        sizePx = Math.Clamp(sizePx, 16, 512);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItem).GUID;
            var hr = Shell32.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var shellItemObj);
            if (hr != 0 || shellItemObj is null)
            {
                return false;
            }

            var factory = (IShellItemImageFactory)shellItemObj;
            var size = new SIZE { cx = sizePx, cy = sizePx };
            var flags = SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_RESIZETOFIT;
            factory.GetImage(size, flags, out hBitmap);
            if (hBitmap == IntPtr.Zero)
            {
                return false;
            }

            if (!TryConvertHBitmapToRgba(hBitmap, out rgba, out width, out height))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                _ = Gdi32.DeleteObject(hBitmap);
            }
        }
    }

    private static bool TryConvertHBitmapToRgba(IntPtr hBitmap, out byte[] rgba, out int width, out int height)
    {
        rgba = Array.Empty<byte>();
        width = 0;
        height = 0;

        if (hBitmap == IntPtr.Zero)
        {
            return false;
        }

        if (Gdi32.GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var bmp) == 0)
        {
            return false;
        }

        width = bmp.bmWidth;
        height = bmp.bmHeight;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var stride = width * 4;
        var bgra = new byte[stride * height];
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height; // top-down
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = 0; // BI_RGB
        info.bmiHeader.biSizeImage = (uint)bgra.Length;

        var hdc = User32.GetDC(IntPtr.Zero);
        try
        {
            var got = Gdi32.GetDIBits(hdc, hBitmap, 0, (uint)height, bgra, ref info, 0);
            if (got == 0)
            {
                return false;
            }
        }
        finally
        {
            if (hdc != IntPtr.Zero)
            {
                _ = User32.ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        rgba = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return true;
    }

    private static void ToGrayscaleInPlace(byte[] rgba)
    {
        // RGBA bytes.
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            var r = rgba[i];
            var g = rgba[i + 1];
            var b = rgba[i + 2];

            // Approx. Rec.601 luma, integer math.
            var y = (byte)((r * 77 + g * 150 + b * 29) >> 8);
            rgba[i] = y;
            rgba[i + 1] = y;
            rgba[i + 2] = y;
        }
    }

    private static byte[] BuildFolderIconRgba(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Fill(rgba, width, height, 0, 0, width, height, r: 18, g: 18, b: 18, a: 255);
        Fill(rgba, width, height, 0, 0, width, height, r: 50, g: 50, b: 50, a: 255);

        var pad = Math.Max(1, width / 10);
        var bodyY = pad + (height / 6);
        var bodyH = Math.Max(1, height - bodyY - pad);
        Fill(rgba, width, height, pad, bodyY, width - (pad * 2), bodyH, r: 180, g: 140, b: 60, a: 255);
        Fill(rgba, width, height, pad, pad, width / 2, height / 6, r: 200, g: 160, b: 70, a: 255);
        return rgba;
    }

    private static byte[] BuildFileIconRgba(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Fill(rgba, width, height, 0, 0, width, height, r: 18, g: 18, b: 18, a: 255);
        Fill(rgba, width, height, 0, 0, width, height, r: 50, g: 50, b: 50, a: 255);

        var pad = Math.Max(1, width / 10);
        Fill(rgba, width, height, pad, pad, width - (pad * 2), height - (pad * 2), r: 170, g: 170, b: 170, a: 255);
        Fill(rgba, width, height, pad + (width / 6), pad + (height / 4), width - (pad * 2) - (width / 3), Math.Max(1, height / 12), r: 120, g: 120, b: 120, a: 255);
        Fill(rgba, width, height, pad + (width / 6), pad + (height / 4) + (height / 8), width - (pad * 2) - (width / 3), Math.Max(1, height / 12), r: 120, g: 120, b: 120, a: 255);
        return rgba;
    }

    private static void Fill(byte[] rgba, int width, int height, int x, int y, int w, int h, byte r, byte g, byte b, byte a)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var x0 = Math.Clamp(x, 0, width);
        var y0 = Math.Clamp(y, 0, height);
        var x1 = Math.Clamp(x + w, 0, width);
        var y1 = Math.Clamp(y + h, 0, height);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        for (var yy = y0; yy < y1; yy++)
        {
            var row = yy * width * 4;
            for (var xx = x0; xx < x1; xx++)
            {
                var i = row + (xx * 4);
                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = b;
                rgba[i + 3] = a;
            }
        }
    }

    private readonly record struct WorkItem(int OwnerId, int Generation, string Path, int SizePx);

    private readonly record struct CacheKey(string Path, int SizePx);

    private sealed class CacheKeyComparer : IEqualityComparer<CacheKey>
    {
        public static CacheKeyComparer Instance { get; } = new();

        public bool Equals(CacheKey x, CacheKey y) =>
            x.SizePx == y.SizePx &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Path, y.Path);

        public int GetHashCode(CacheKey obj)
        {
            unchecked
            {
                return (StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path) * 397) ^ obj.SizePx;
            }
        }
    }

    private readonly record struct CacheEntry(CacheKey Key, ThumbnailImage Image);

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    private static class Shell32
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    private static class Gdi32
    {
        [DllImport("gdi32.dll")]
        public static extern int GetObject(IntPtr hObject, int cbBuffer, out BITMAP lpvObject);

        [DllImport("gdi32.dll")]
        public static extern int DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(
            IntPtr hdc,
            IntPtr hbmp,
            uint uStartScan,
            uint cScanLines,
            [Out] byte[] lpvBits,
            ref BITMAPINFO lpbmi,
            uint uUsage);
    }

    private static class Ole32
    {
        public const int COINIT_APARTMENTTHREADED = 0x2;

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();
    }

    private static class User32
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    }
}

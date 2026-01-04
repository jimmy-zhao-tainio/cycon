using System;
using Cycon.Core.Transcript;
using Cycon.Host.Input;
using Cycon.Layout.Scrolling;
using Cycon.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Extensions.Inspect.Blocks;

public sealed class ImageBlock : IBlock, IRenderBlock, IBlockPointerHandler, IBlockWheelHandler
{
    private const float ZoomStep = 0.10f;
    private const float MinScale = 1e-4f;
    private const float DragEpsilon = 1e-3f;
    private const float ZoomTauSeconds = 0.08f;

    private bool _fitToView = true;
    private float _currentScale = 1f;
    private float _currentOffsetX;
    private float _currentOffsetY;
    private float _targetScale = 1f;
    private float _targetOffsetX;
    private float _targetOffsetY;
    private double _lastTimeSeconds;
    private bool _hasTime;
    private bool _dragging;
    private float _lastDragX;
    private float _lastDragY;

    private ImageBlock(BlockId id, string path, int width, int height, byte[] rgbaPixels)
    {
        Id = id;
        Path = path;
        ImageWidth = width;
        ImageHeight = height;
        RgbaPixels = rgbaPixels;
        _fitToView = true;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Image;
    public string Path { get; }
    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public byte[] RgbaPixels { get; }

    public static ImageBlock Load(BlockId id, string path)
    {
        using var image = Image.Load<Rgba32>(path);
        var width = image.Width;
        var height = image.Height;
        var rgba = new byte[width * height * 4];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var rowOffset = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var px = row[x];
                    var idx = rowOffset + (x * 4);
                    rgba[idx] = px.R;
                    rgba[idx + 1] = px.G;
                    rgba[idx + 2] = px.B;
                    rgba[idx + 3] = px.A;
                }
            }
        });

        return new ImageBlock(id, path, width, height, rgba);
    }

    public void Render(IRenderCanvas canvas, in BlockRenderContext ctx)
    {
        var viewport = ctx.ViewportRectPx;
        if (ImageWidth <= 0 || ImageHeight <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var viewportPx = new PxRect(viewport.X, viewport.Y, viewport.Width, viewport.Height);
        var transform = GetCurrentTransform(viewportPx, ctx.TimeSeconds);
        var rect = ComputeImageRect(viewportPx, transform);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        canvas.DrawImage2D(Id.Value, RgbaPixels, ImageWidth, ImageHeight, rect);
    }

    public bool HandleWheel(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        if (e.Kind != HostMouseEventKind.Wheel || e.WheelDelta == 0)
        {
            return false;
        }

        if (!Contains(viewportRectPx, e.X, e.Y))
        {
            return false;
        }

        var current = GetCurrentTransform(viewportRectPx, _lastTimeSeconds);
        var imgPoint = ScreenToImage(viewportRectPx, current, e.X, e.Y);
        var factor = MathF.Exp(e.WheelDelta * ZoomStep);
        var newScale = MathF.Max(MinScale, current.Scale * factor);
        var targetOffsetX = (e.X - viewportRectPx.X) - (imgPoint.X * newScale);
        var targetOffsetY = (e.Y - viewportRectPx.Y) - (imgPoint.Y * newScale);
        ClampOffset(viewportRectPx, newScale, ref targetOffsetX, ref targetOffsetY);

        _fitToView = false;
        _targetScale = newScale;
        _targetOffsetX = targetOffsetX;
        _targetOffsetY = targetOffsetY;

        return true;
    }

    public bool HandlePointer(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        if (!Contains(viewportRectPx, e.X, e.Y))
        {
            if (e.Kind == HostMouseEventKind.Up)
            {
                _dragging = false;
            }
            return false;
        }

        var current = GetCurrentTransform(viewportRectPx, _lastTimeSeconds);
        var contentRect = ComputeImageRect(viewportRectPx, current);
        var insideContent = Contains(contentRect, e.X, e.Y);

        if (e.Kind == HostMouseEventKind.Down && (e.Buttons & HostMouseButtons.Right) != 0)
        {
            SetZoomOne(viewportRectPx);
            return true;
        }

        if (e.Kind == HostMouseEventKind.Down && (e.Buttons & HostMouseButtons.Left) != 0)
        {
            if (!insideContent)
            {
                SetFit(viewportRectPx);
                _dragging = false;
                return true;
            }

            if (IsZoomed(current, viewportRectPx))
            {
                _dragging = true;
                _lastDragX = e.X;
                _lastDragY = e.Y;
                return true;
            }
        }

        if (e.Kind == HostMouseEventKind.Move && _dragging)
        {
            if ((e.Buttons & HostMouseButtons.Left) == 0)
            {
                _dragging = false;
                return true;
            }

            var dx = e.X - _lastDragX;
            var dy = e.Y - _lastDragY;
            if (MathF.Abs(dx) + MathF.Abs(dy) >= DragEpsilon)
            {
                var nextOffsetX = current.OffsetX + dx;
                var nextOffsetY = current.OffsetY + dy;
                ClampOffset(viewportRectPx, current.Scale, ref nextOffsetX, ref nextOffsetY);

                _fitToView = false;
                _currentScale = current.Scale;
                _currentOffsetX = nextOffsetX;
                _currentOffsetY = nextOffsetY;
                _targetScale = _currentScale;
                _targetOffsetX = _currentOffsetX;
                _targetOffsetY = _currentOffsetY;
                _lastDragX = e.X;
                _lastDragY = e.Y;
            }
            return true;
        }

        if (e.Kind == HostMouseEventKind.Up)
        {
            _dragging = false;
        }

        return insideContent;
    }

    private ViewTransform GetCurrentTransform(in PxRect viewport, double timeSeconds)
    {
        if (_fitToView)
        {
            var fit = ComputeFitTransform(viewport);
            _currentScale = fit.Scale;
            _currentOffsetX = fit.OffsetX;
            _currentOffsetY = fit.OffsetY;
            _targetScale = _currentScale;
            _targetOffsetX = _currentOffsetX;
            _targetOffsetY = _currentOffsetY;
            return fit;
        }

        var dt = GetDeltaSeconds(timeSeconds);
        var alpha = 1f - MathF.Exp(-(float)dt / ZoomTauSeconds);
        _currentScale = Lerp(_currentScale, _targetScale, alpha);
        _currentOffsetX = Lerp(_currentOffsetX, _targetOffsetX, alpha);
        _currentOffsetY = Lerp(_currentOffsetY, _targetOffsetY, alpha);
        ClampOffset(viewport, _currentScale, ref _currentOffsetX, ref _currentOffsetY);

        return ViewTransform.Custom(_currentScale, _currentOffsetX, _currentOffsetY);
    }

    private ViewTransform ComputeFitTransform(in PxRect viewport)
    {
        var scaleX = viewport.Width / (float)ImageWidth;
        var scaleY = viewport.Height / (float)ImageHeight;
        var scale = MathF.Min(scaleX, scaleY);
        scale = MathF.Max(MinScale, scale);

        var offsetX = (viewport.Width - (ImageWidth * scale)) * 0.5f;
        var offsetY = (viewport.Height - (ImageHeight * scale)) * 0.5f;
        return ViewTransform.Custom(scale, offsetX, offsetY);
    }

    private void SetZoomOne(in PxRect viewport)
    {
        var offsetX = (viewport.Width - ImageWidth) * 0.5f;
        var offsetY = (viewport.Height - ImageHeight) * 0.5f;
        ClampOffset(viewport, 1.0f, ref offsetX, ref offsetY);

        _fitToView = false;
        _currentScale = 1.0f;
        _currentOffsetX = offsetX;
        _currentOffsetY = offsetY;
        _targetScale = _currentScale;
        _targetOffsetX = _currentOffsetX;
        _targetOffsetY = _currentOffsetY;
    }

    private bool IsZoomed(in ViewTransform current, in PxRect viewport)
    {
        var fit = ComputeFitTransform(viewport);
        return current.Scale > fit.Scale + 1e-4f;
    }

    private void SetFit(in PxRect viewport)
    {
        _fitToView = true;
        var fit = ComputeFitTransform(viewport);
        _currentScale = fit.Scale;
        _currentOffsetX = fit.OffsetX;
        _currentOffsetY = fit.OffsetY;
        _targetScale = _currentScale;
        _targetOffsetX = _currentOffsetX;
        _targetOffsetY = _currentOffsetY;
    }

    private RectPx ComputeImageRect(in PxRect viewport, in ViewTransform transform)
    {
        var x = viewport.X + transform.OffsetX;
        var y = viewport.Y + transform.OffsetY;
        var w = ImageWidth * transform.Scale;
        var h = ImageHeight * transform.Scale;
        return new RectPx((int)MathF.Round(x), (int)MathF.Round(y), (int)MathF.Round(w), (int)MathF.Round(h));
    }

    private static bool Contains(in PxRect rect, int x, int y)
    {
        return x >= rect.X && y >= rect.Y &&
               x < rect.X + rect.Width && y < rect.Y + rect.Height;
    }

    private static bool Contains(in RectPx rect, int x, int y)
    {
        return x >= rect.X && y >= rect.Y &&
               x < rect.X + rect.Width && y < rect.Y + rect.Height;
    }

    private (float X, float Y) ScreenToImage(in PxRect viewport, in ViewTransform transform, int screenX, int screenY)
    {
        var localX = screenX - viewport.X - transform.OffsetX;
        var localY = screenY - viewport.Y - transform.OffsetY;
        return (localX / transform.Scale, localY / transform.Scale);
    }

    private void ClampOffset(in PxRect viewport, float scale, ref float offsetX, ref float offsetY)
    {
        var viewW = viewport.Width;
        var viewH = viewport.Height;
        var imgScaledW = ImageWidth * scale;
        var imgScaledH = ImageHeight * scale;

        if (imgScaledW <= viewW)
        {
            offsetX = (viewW - imgScaledW) * 0.5f;
        }
        else
        {
            var minX = viewW - imgScaledW;
            offsetX = MathF.Min(0f, MathF.Max(minX, offsetX));
        }

        if (imgScaledH <= viewH)
        {
            offsetY = (viewH - imgScaledH) * 0.5f;
        }
        else
        {
            var minY = viewH - imgScaledH;
            offsetY = MathF.Min(0f, MathF.Max(minY, offsetY));
        }
    }

    private double GetDeltaSeconds(double timeSeconds)
    {
        if (!_hasTime)
        {
            _hasTime = true;
            _lastTimeSeconds = timeSeconds;
            return 0;
        }

        var dt = timeSeconds - _lastTimeSeconds;
        _lastTimeSeconds = timeSeconds;
        if (dt < 1.0 / 500.0) return 1.0 / 500.0;
        if (dt > 1.0 / 15.0) return 1.0 / 15.0;
        return dt;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private readonly record struct ViewTransform(float Scale, float OffsetX, float OffsetY, bool IsFit)
    {
        public static ViewTransform Fit => new(1f, 0f, 0f, true);

        public static ViewTransform Custom(float scale, float offsetX, float offsetY) => new(scale, offsetX, offsetY, false);
    }
}

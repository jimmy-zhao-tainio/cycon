using System;
using System.Diagnostics;
using Cycon.Core.Transcript;
using Cycon.Host.Input;
using Cycon.Layout.Scrolling;
using Cycon.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Extensions.Inspect.Blocks;

public sealed class ImageBlock : IBlock, IRenderBlock, IBlockPointerHandler, IBlockWheelHandler
{
    private const float ZoomBase = 1.008f;
    private const float WheelZoomSpeed = 20.0f;
    private const float MinScale = 0.01f;
    private const float MaxScale = 4096f;
    private const float DragEpsilon = 1e-3f;

    private sealed class ZoomState
    {
        public float Scale = 1f;
        public float VirtualOffsetX;
        public float VirtualOffsetY;
        public float TargetScale = 1f;
        public float TargetVirtualOffsetX;
        public float TargetVirtualOffsetY;
        public double LastTimeSeconds;
    }

    private sealed class InputState
    {
        public float WheelDeltaAccum;
        public float PendingWheelAnchorX;
        public float PendingWheelAnchorY;
        public bool Dragging;
        public float LastDragX;
        public float LastDragY;
    }

    private bool _fitToView = true;
    private readonly ZoomState _zoom = new();
    private readonly InputState _input = new();

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
        var transform = GetTransformSnapshot(viewportPx, ctx.TimeSeconds);
        var rect = ComputeImageRectF(viewportPx, transform);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        canvas.DrawImage2D(Id.Value, RgbaPixels, ImageWidth, ImageHeight, rect, useNearest: true);
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

        var nowSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var current = GetTransformSnapshot(viewportRectPx, nowSeconds);
        _fitToView = false;
        
        if (!_input.Dragging)
        {
            // If we have an in-flight animation, treat target as the intent.
            // This avoids tiny “rubber band” feel under repeated wheel ticks.
            _zoom.Scale = current.Scale;
            _zoom.VirtualOffsetX = current.OffsetX;
            _zoom.VirtualOffsetY = current.OffsetY;
            SyncTargetsToCurrent();
        }

        var wheel = (float)e.WheelDelta;

        // If the backend is Windows-style (120 per notch), normalize to "notches".
        if (MathF.Abs(wheel) >= 10f)
        {
            wheel /= 120f;
        }

        _input.WheelDeltaAccum += wheel * WheelZoomSpeed;
        var minScale = MinScale; //GetFitScale(viewportRectPx);

        var ax = (float)(e.X - viewportRectPx.X);
        var ay = (float)(e.Y - viewportRectPx.Y);
        _input.PendingWheelAnchorX = ax;
        _input.PendingWheelAnchorY = ay;

        if (!TryComputeNearestWheelZoomCandidate(
                current.Scale,
                current.OffsetX,
                current.OffsetY,
                _input.PendingWheelAnchorX,
                _input.PendingWheelAnchorY,
                _input.WheelDeltaAccum,
                minScale,
                out var candidateScale,
                out var candidateVirtualOffsetX,
                out var candidateVirtualOffsetY))
        {
            return true;
        }

        if (MathF.Abs(candidateScale - current.Scale) <= 1e-6f)
        {
            _input.WheelDeltaAccum = 0f;
            return true;
        }

        _zoom.TargetScale = candidateScale;
        _zoom.TargetVirtualOffsetX = candidateVirtualOffsetX;
        _zoom.TargetVirtualOffsetY = candidateVirtualOffsetY;
        _input.WheelDeltaAccum = 0f;

        return true;
    }

    public bool HandlePointer(in HostMouseEvent e, in PxRect viewportRectPx)
    {
        var nowSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        // If we are dragging, only react to Move/Up. Ignore stray Down events.
        if (_input.Dragging)
        {
            if (e.Kind == HostMouseEventKind.Move)
            {
                if ((e.Buttons & HostMouseButtons.Left) == 0)
                {
                    _input.Dragging = false;
                    return true;
                }

                var dx = e.X - _input.LastDragX;
                var dy = e.Y - _input.LastDragY;
                if (MathF.Abs(dx) + MathF.Abs(dy) >= DragEpsilon)
                {
                    _fitToView = false;

                    // Apply delta in virtual space; keep targets in sync (prevents easing from "fighting").
                    ApplyDragDelta(viewportRectPx, _zoom.Scale, dx, dy);

                    _input.WheelDeltaAccum = 0f;
                    _input.LastDragX = e.X;
                    _input.LastDragY = e.Y;
                }

                return true;
            }

            if (e.Kind == HostMouseEventKind.Up)
            {
                _input.Dragging = false;
                return true;
            }
        }

        // Ignore events outside the block; no "click black to fit" behavior anymore.
        if (!Contains(viewportRectPx, e.X, e.Y))
        {
            return false;
        }

        // Right click toggles between Fit and 1:1 (centered) explicitly.
        if (e.Kind == HostMouseEventKind.Down && (e.Buttons & HostMouseButtons.Right) != 0)
        {
            ToggleFitOrOne(viewportRectPx, nowSeconds, e.X, e.Y);
            return true;
        }

        // Left click inside the block begins dragging (free-fly).
        if (e.Kind == HostMouseEventKind.Down && (e.Buttons & HostMouseButtons.Left) != 0)
        {
            _fitToView = false;
            SyncTargetsToCurrent(); // stop any active easing from pulling
            _input.Dragging = true;
            _input.WheelDeltaAccum = 0f;
            _input.LastDragX = e.X;
            _input.LastDragY = e.Y;
            return true;
        }

        return false;
    }

    private void SyncTargetsToCurrent()
    {
        _zoom.TargetScale = _zoom.Scale;
        _zoom.TargetVirtualOffsetX = _zoom.VirtualOffsetX;
        _zoom.TargetVirtualOffsetY = _zoom.VirtualOffsetY;
    }

    private bool IsApproximatelyFit(in PxRect viewport)
    {
        var fit = ComputeFitTransform(viewport);
        return MathF.Abs(_zoom.TargetScale - fit.Scale) <= 1e-4f;
    }

    private bool IsInsideImageAt(in PxRect viewport, double timeSeconds, int screenX, int screenY)
    {
        var t = GetTransformSnapshot(viewport, timeSeconds);
        var rect = ComputeImageRectF(viewport, t);
        return Contains(rect, screenX, screenY);
    }

    private void ToggleFitOrOne(in PxRect viewport, double timeSeconds, int screenX, int screenY)
    {
        if (IsApproximatelyFit(viewport))
        {
            if (IsInsideImageAt(viewport, timeSeconds, screenX, screenY))
            {
                SetZoomOneAnchored(viewport, timeSeconds, screenX, screenY);
            }
            else
            {
                // Clicked on bars: go to a deterministic 1:1 instead of "void".
                SetZoomOne(viewport, timeSeconds);
            }
        }
        else
        {
            SetFit(viewport, timeSeconds);
        }
    }

    private ViewTransform GetTransformSnapshot(in PxRect viewport, double timeSeconds)
    {
        if (_zoom.LastTimeSeconds == 0)
        {
            _zoom.LastTimeSeconds = timeSeconds;
        }

        var dt = (float)(timeSeconds - _zoom.LastTimeSeconds);
        _zoom.LastTimeSeconds = timeSeconds;

        if (_fitToView)
        {
            SetFit(viewport, timeSeconds);

            // Keep targets in sync with fit.
            _zoom.TargetScale = _zoom.Scale;
            SyncTargetsToCurrent();
            ComputeRenderOffsets(viewport, _zoom.Scale, _zoom.VirtualOffsetX, _zoom.VirtualOffsetY, out var rox, out var roy);
            return ViewTransform.Custom(_zoom.Scale, rox, roy);
        }

        // Clamp absolute min only.
        _zoom.TargetScale = ClampScale(_zoom.TargetScale, MinScale);

        // Exponential smoothing: higher = faster response.
        // Try 20..40 for “fast but smooth”.
        const float follow = 30f;
        var t = 1f - MathF.Exp(-follow * MathF.Max(0f, dt));

        _zoom.Scale = Lerp(_zoom.Scale, _zoom.TargetScale, t);
        _zoom.VirtualOffsetX = Lerp(_zoom.VirtualOffsetX, _zoom.TargetVirtualOffsetX, t);
        _zoom.VirtualOffsetY = Lerp(_zoom.VirtualOffsetY, _zoom.TargetVirtualOffsetY, t);

        ComputeRenderOffsets(viewport, _zoom.Scale, _zoom.VirtualOffsetX, _zoom.VirtualOffsetY, out var renderX, out var renderY);
        return ViewTransform.Custom(_zoom.Scale, renderX, renderY);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

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

    private float GetFitScale(in PxRect viewport)
    {
        var scaleX = viewport.Width / (float)ImageWidth;
        var scaleY = viewport.Height / (float)ImageHeight;
        var scale = MathF.Min(scaleX, scaleY);
        return MathF.Max(MinScale, scale);
    }

    private void SetZoomOneAnchored(in PxRect viewport, double timeSeconds, int screenX, int screenY)
    {
        // Snapshot current visible transform so we can map screen -> image.
        var current = GetTransformSnapshot(viewport, timeSeconds);

        var localX = (float)(screenX - viewport.X);
        var localY = (float)(screenY - viewport.Y);

        // Image-space coordinate currently under the cursor
        var u = (localX - current.OffsetX) / current.Scale;
        var v = (localY - current.OffsetY) / current.Scale;

        // 1:1 scale, always. (If you later want "at least fit", you can change this.)
        var scale = 1f;

        // Solve offsets so (u,v) ends up under the cursor again.
        var offsetX = localX - (u * scale);
        var offsetY = localY - (v * scale);

        _fitToView = false;
        _zoom.Scale = ClampScale(scale, MinScale);
        _zoom.VirtualOffsetX = offsetX;
        _zoom.VirtualOffsetY = offsetY;

        // Keep animation targets in sync immediately.
        _zoom.TargetScale = _zoom.Scale;
        _zoom.TargetVirtualOffsetX = _zoom.VirtualOffsetX;
        _zoom.TargetVirtualOffsetY = _zoom.VirtualOffsetY;

        _input.WheelDeltaAccum = 0f;
    }

    private void SetZoomOne(in PxRect viewport, double timeSeconds)
    {
        var fitScale = GetFitScale(viewport);
        var scale = MathF.Max(1.0f, fitScale);

        var offsetX = (viewport.Width - (ImageWidth * scale)) * 0.5f;
        var offsetY = (viewport.Height - (ImageHeight * scale)) * 0.5f;
        ClampOffset(viewport, scale, ref offsetX, ref offsetY);
        SnapOffsets(ref offsetX, ref offsetY);

        _fitToView = false;

        // 1:1 (or fitScale if larger) centered
        _zoom.Scale = ClampScale(scale, MinScale);
        _zoom.VirtualOffsetX = offsetX;
        _zoom.VirtualOffsetY = offsetY;

        // Keep animation targets in sync immediately.
        _zoom.TargetScale = _zoom.Scale;
        _zoom.TargetVirtualOffsetX = _zoom.VirtualOffsetX;
        _zoom.TargetVirtualOffsetY = _zoom.VirtualOffsetY;

        _input.WheelDeltaAccum = 0f;
    }

    private void SetFit(in PxRect viewport, double timeSeconds)
    {
        _fitToView = true;
        var fit = ComputeFitTransform(viewport);
        _zoom.Scale = fit.Scale;
        _zoom.VirtualOffsetX = fit.OffsetX;
        _zoom.VirtualOffsetY = fit.OffsetY;
        _input.WheelDeltaAccum = 0f;

        // Animated smooth zoom.
        _zoom.TargetScale = _zoom.Scale;
        _zoom.TargetVirtualOffsetX = _zoom.VirtualOffsetX;
        _zoom.TargetVirtualOffsetY = _zoom.VirtualOffsetY;
    }

    private RectPx ComputeImageRect(in PxRect viewport, in ViewTransform transform)
    {
        var x = viewport.X + transform.OffsetX;
        var y = viewport.Y + transform.OffsetY;
        var w = ImageWidth * transform.Scale;
        var h = ImageHeight * transform.Scale;
        return new RectPx((int)MathF.Round(x), (int)MathF.Round(y), (int)MathF.Round(w), (int)MathF.Round(h));
    }

    private RectF ComputeImageRectF(in PxRect viewport, in ViewTransform transform)
    {
        var x = viewport.X + transform.OffsetX;
        var y = viewport.Y + transform.OffsetY;
        var w = ImageWidth * transform.Scale;
        var h = ImageHeight * transform.Scale;
        return new RectF(x, y, w, h);
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

    private static bool Contains(in RectF rect, int x, int y)
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

    private void ComputeRenderOffsets(
        in PxRect viewport,
        float scale,
        float virtualOffsetX,
        float virtualOffsetY,
        out float renderOffsetX,
        out float renderOffsetY)
    {
        renderOffsetX = virtualOffsetX;
        renderOffsetY = virtualOffsetY;
    }

    private void ApplyDragDelta(in PxRect viewport, float scale, float dx, float dy)
    {
        _zoom.VirtualOffsetX += dx;
        _zoom.VirtualOffsetY += dy;

        _zoom.TargetVirtualOffsetX = _zoom.VirtualOffsetX;
        _zoom.TargetVirtualOffsetY = _zoom.VirtualOffsetY;
        _zoom.TargetScale = _zoom.Scale;
    }

    private readonly record struct ViewTransform(float Scale, float OffsetX, float OffsetY)
    {
        public static ViewTransform Custom(float scale, float offsetX, float offsetY) => new(scale, offsetX, offsetY);
    }

    private static float ClampScale(float scale, float minScale)
    {
        if (!float.IsFinite(minScale) || minScale <= 0f)
        {
            minScale = MinScale;
        }

        minScale = MathF.Max(MinScale, minScale);

        if (!float.IsFinite(scale) || scale <= 0f)
        {
            return minScale;
        }

        return MathF.Min(MaxScale, MathF.Max(minScale, scale));
    }

    private static void SnapOffsets(ref float offsetX, ref float offsetY)
    {
        offsetX = RoundPx(offsetX);
        offsetY = RoundPx(offsetY);
    }

    private static float RoundPx(float value) => MathF.Round(value, MidpointRounding.AwayFromZero);

    private static int RoundPxToInt(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);

    internal static bool TryComputeNearestWheelZoomCandidate(
        float scale,
        float offsetX,
        float offsetY,
        float anchorX,
        float anchorY,
        float wheelDeltaAccum,
        float minScale,
        out float scalePrime,
        out float offsetXPrime,
        out float offsetYPrime)
    {
        minScale = MathF.Max(MinScale, minScale);
        scalePrime = ClampScale(scale, minScale);
        offsetXPrime = offsetX;
        offsetYPrime = offsetY;

        if (!float.IsFinite(offsetX) || !float.IsFinite(offsetY) || wheelDeltaAccum == 0f)
        {
            return false;
        }

        var zoomBase = Math.Clamp(ZoomBase, 1.0001f, 10f);
        var candidate = scalePrime * MathF.Pow(zoomBase, wheelDeltaAccum);
        candidate = ClampScale(candidate, minScale);

        if (candidate <= 0f || !float.IsFinite(candidate))
        {
            return false;
        }

        var u = (anchorX - offsetX) / scalePrime;
        var v = (anchorY - offsetY) / scalePrime;

        offsetXPrime = anchorX - (u * candidate);
        offsetYPrime = anchorY - (v * candidate);

        // For smooth zoom: DO NOT SnapOffsets here.
        // If you want to keep panning crisp, you can snap only when scale is very close to an integer
        // or only when you explicitly switch to nearest sampling.
        scalePrime = candidate;
        return true;
    }

    private void ClampOffset(in PxRect viewport, float scale, ref float offsetX, ref float offsetY)
    {
        var viewW = viewport.Width;
        var viewH = viewport.Height;
        var imgScaledW = ImageWidth * scale;
        var imgScaledH = ImageHeight * scale;

        // X
        if (imgScaledW <= viewW)
        {
            // When letterboxed, allow offset in [0 .. view - imgScaled] instead of forcing center.
            var maxX = viewW - imgScaledW;
            offsetX = MathF.Max(0f, MathF.Min(maxX, offsetX));
        }
        else
        {
            // When larger than viewport, clamp in [view - imgScaled .. 0]
            var minX = viewW - imgScaledW;
            offsetX = MathF.Min(0f, MathF.Max(minX, offsetX));
        }

        // Y
        if (imgScaledH <= viewH)
        {
            var maxY = viewH - imgScaledH;
            offsetY = MathF.Max(0f, MathF.Min(maxY, offsetY));
        }
        else
        {
            var minY = viewH - imgScaledH;
            offsetY = MathF.Min(0f, MathF.Max(minY, offsetY));
        }
    }

}


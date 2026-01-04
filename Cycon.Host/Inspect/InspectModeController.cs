using System;
using System.IO;
using System.Numerics;
using System.Diagnostics;
using Cycon.BlockCommands;
using Cycon.Core.Settings;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Core.Metrics;
using Cycon.Layout.Scrolling;
using Cycon.Render;
using Cycon.Rendering.Renderer;
using Cycon.Host.Input;
using Cycon.Host.Hosting;

namespace Cycon.Host.Inspect;

internal sealed class InspectModeController
{
    private const float MouseDeltaClampPx = 200f;

    private readonly IInspectHost _host;
    private readonly List<InspectEntry> _inspectHistory = new();
    private int _nextInspectEntryId;
    private InspectEntry? _activeInspect;
    private Scene3DCapture? _inspectScene3DCapture;
    private BlockId? _inspectScene3DMouseFocus;
    private Scene3DNavKeys _inspectScene3DNavKeysDown;
    private int _lastScene3DMouseX;
    private int _lastScene3DMouseY;
    private bool _hasScene3DMousePos;

    public InspectModeController(IInspectHost host)
    {
        _host = host;
    }

    public bool IsActive => _activeInspect is not null;

    public void OpenInspect(InspectKind kind, string path, string title, IBlock viewBlock, string receiptLine, BlockId commandEchoId)
    {
        if (_activeInspect is not null)
        {
            WriteInspectReceiptIfNeeded(_activeInspect);
        }

        var id = ++_nextInspectEntryId;
        title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(path) : title;
        var entry = new InspectEntry(id, kind, path, title, viewBlock, commandEchoId, receiptLine ?? string.Empty);
        _inspectHistory.Add(entry);
        _activeInspect = entry;

        _inspectScene3DCapture = null;
        _inspectScene3DMouseFocus = null;
        _inspectScene3DNavKeysDown = Scene3DNavKeys.None;
        _hasScene3DMousePos = false;

        // NOTE: If drag mysteriously needs a priming click again,
        // check Windows keyboard/trackpad/trackpoint delay settings.
        // Yes, this has happened before. More than once.
        if (viewBlock is IMouseFocusableViewportBlock focusable)
        {
            focusable.HasMouseFocus = true;
        }

        if (viewBlock is IScene3DViewBlock)
        {
            _inspectScene3DMouseFocus = viewBlock.Id;
        }

        _host.RequestContentRebuild();
    }

    public void OnWindowFocusChanged(bool isFocused)
    {
        if (isFocused)
        {
            return;
        }

        if (_activeInspect?.View is IMouseFocusableViewportBlock focusable)
        {
            focusable.HasMouseFocus = false;
        }

        _inspectScene3DMouseFocus = null;
        _inspectScene3DCapture = null;
        _inspectScene3DNavKeysDown = Scene3DNavKeys.None;
    }

    public Cycon.Rendering.RenderFrame BuildInspectFrame(int framebufferWidth, int framebufferHeight, double timeSeconds)
    {
        if (_activeInspect is null)
        {
            throw new InvalidOperationException("Inspect frame requested without an active inspect entry.");
        }

        var frame = new Cycon.Rendering.RenderFrame
        {
            BuiltGrid = new GridSize(0, 0)
        };

        var meshReleases = _host.TakePendingMeshReleases();
        if (meshReleases is { Count: > 0 })
        {
            for (var i = 0; i < meshReleases.Count; i++)
            {
                frame.Add(new Cycon.Rendering.Commands.ReleaseMesh3D(meshReleases[i]));
            }
        }

        var theme = new RenderTheme(
            ForegroundRgba: _host.Document.Settings.DefaultTextStyle.ForegroundRgba,
            BackgroundRgba: _host.Document.Settings.DefaultTextStyle.BackgroundRgba);

        var fontMetrics = _host.Font.Metrics;
        var textMetrics = new TextMetrics(
            CellWidthPx: fontMetrics.CellWidthPx,
            CellHeightPx: fontMetrics.CellHeightPx,
            BaselinePx: fontMetrics.BaselinePx,
            UnderlineThicknessPx: Math.Max(1, fontMetrics.UnderlineThicknessPx),
            UnderlineTopOffsetPx: fontMetrics.UnderlineTopOffsetPx);

        var scene3D = new Scene3DRenderSettings(
            HorizontalFovDegrees: _host.Document.Settings.Scene3D.HorizontalFovDegrees,
            StlDebugMode: (int)_host.Document.Settings.Scene3D.StlDebugMode,
            SolidAmbient: _host.Document.Settings.Scene3D.SolidAmbient,
            SolidDiffuseStrength: _host.Document.Settings.Scene3D.SolidDiffuseStrength,
            ToneGamma: _host.Document.Settings.Scene3D.ToneGamma,
            ToneGain: _host.Document.Settings.Scene3D.ToneGain,
            ToneLift: _host.Document.Settings.Scene3D.ToneLift,
            VignetteStrength: _host.Document.Settings.Scene3D.VignetteStrength,
            VignetteInner: _host.Document.Settings.Scene3D.VignetteInner,
            VignetteOuter: _host.Document.Settings.Scene3D.VignetteOuter,
            ShowVertexDots: _host.Document.Settings.Scene3D.ShowVertexDots,
            VertexDotMaxVertices: _host.Document.Settings.Scene3D.VertexDotMaxVertices,
            VertexDotMaxDots: _host.Document.Settings.Scene3D.VertexDotMaxDots);

        // InspectMode is fullscreen: blocks own their own padding.
        var contentRect = new RectPx(0, 0, Math.Max(0, framebufferWidth), Math.Max(0, framebufferHeight));

        var ctx = new BlockRenderContext(contentRect, timeSeconds, theme, textMetrics, scene3D);
        if (_activeInspect.View is not IRenderBlock renderBlock)
        {
            throw new InvalidOperationException("Active inspect view does not implement IRenderBlock.");
        }

        BlockViewRenderer.RenderFullscreen(frame, _host.Font, renderBlock, ctx, framebufferWidth, framebufferHeight);

        _host.ClearPendingMeshReleases();
        return frame;
    }

    public void DrainPendingEvents(List<PendingEvent>? events, int framebufferWidth, int framebufferHeight)
    {
        if (events is null || events.Count == 0 || _activeInspect is null)
        {
            return;
        }

        var block = _activeInspect.View;
        // InspectMode is fullscreen: blocks own their own padding.
        var viewport = new PxRect(0, 0, Math.Max(0, framebufferWidth), Math.Max(0, framebufferHeight));

        for (var i = 0; i < events.Count; i++)
        {
            if (_activeInspect is null)
            {
                return;
            }

            switch (events[i])
            {
                case PendingEvent.FileDrop fileDrop:
                    // File drop should behave like typing a new `inspect "<path>"` command, even while in InspectMode.
                    _host.HandleFileDrop(fileDrop.Path);
                    break;
                case PendingEvent.Key key when key.IsDown && (key.KeyCode == HostKey.Escape || key.KeyCode == HostKey.Q):
                    ExitInspectMode();
                    return;
                case PendingEvent.Key key when key.KeyCode != HostKey.Unknown:
                    HandleInspectScene3DKey(block, key.KeyCode, key.IsDown);
                    break;
                case PendingEvent.Mouse mouse:
                    if (HandleInspectPointer(block, viewport, mouse.Event))
                    {
                        // Render-only blocks update in-place; no transcript rebuild needed.
                    }
                    break;
            }
        }
    }

    public bool TickScene3DKeys(TimeSpan dt)
    {
        if (_activeInspect is null)
        {
            return false;
        }

        if (_inspectScene3DNavKeysDown == Scene3DNavKeys.None)
        {
            return false;
        }

        if (_activeInspect.View is not IScene3DViewBlock stl)
        {
            _inspectScene3DNavKeysDown = Scene3DNavKeys.None;
            return false;
        }

        var dtSeconds = (float)dt.TotalSeconds;
        if (dtSeconds <= 0f)
        {
            return false;
        }

        var settings = _host.Document.Settings.Scene3D;

        var pan = 0f;
        if ((_inspectScene3DNavKeysDown & Scene3DNavKeys.D) != 0) pan += 1f;
        if ((_inspectScene3DNavKeysDown & Scene3DNavKeys.A) != 0) pan -= 1f;

        var dolly = 0f;
        if ((_inspectScene3DNavKeysDown & Scene3DNavKeys.W) != 0) dolly += 1f;
        if ((_inspectScene3DNavKeysDown & Scene3DNavKeys.S) != 0) dolly -= 1f;

        var didAnything = false;
        var (right, up, _) = ComputeSceneBasis(stl.CenterDir);

        if (pan != 0f)
        {
            var scale = stl.FocusDistance * settings.KeyboardPanSpeed * dtSeconds;
            var sign = settings.InvertPanX ? -1f : 1f;
            var delta = (pan * scale * sign) * right;

            if (stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit)
            {
                orbit.OrbitTarget += delta;
                stl.CameraPos += delta;
            }
            else
            {
                stl.CameraPos += delta;
            }

            didAnything = true;
        }

        if (dolly != 0f)
        {
            var exponent = dolly * settings.KeyboardDollySpeed * dtSeconds;
            var factor = MathF.Exp(-exponent);
            ApplySceneDollyFactor(stl, factor);
            didAnything = true;
        }

        return didAnything;
    }

    private void HandleInspectScene3DKey(IBlock block, HostKey key, bool isDown)
    {
        if (key is not (HostKey.W or HostKey.A or HostKey.S or HostKey.D))
        {
            return;
        }

        if (block is not IScene3DViewBlock)
        {
            return;
        }

        var mask = key switch
        {
            HostKey.W => Scene3DNavKeys.W,
            HostKey.A => Scene3DNavKeys.A,
            HostKey.S => Scene3DNavKeys.S,
            HostKey.D => Scene3DNavKeys.D,
            _ => Scene3DNavKeys.None
        };

        if (isDown)
        {
            _inspectScene3DNavKeysDown |= mask;
        }
        else
        {
            _inspectScene3DNavKeysDown &= ~mask;
        }
    }

    private bool HandleInspectPointer(IBlock block, in PxRect viewportRectPx, in HostMouseEvent e)
    {
        if (block is IBlockWheelHandler wheelHandler && e.Kind == HostMouseEventKind.Wheel)
        {
            return wheelHandler.HandleWheel(e, viewportRectPx);
        }

        if (block is IBlockPointerHandler pointerHandler &&
            e.Kind is HostMouseEventKind.Down or HostMouseEventKind.Up or HostMouseEventKind.Move)
        {
            return pointerHandler.HandlePointer(e, viewportRectPx);
        }

        if (block is IScene3DViewBlock stl)
        {
            return HandleInspectScene3DMouse(stl, viewportRectPx, e);
        }

        return false;
    }

    private bool HandleInspectScene3DMouse(IScene3DViewBlock stl, in PxRect viewportRectPx, in HostMouseEvent e)
    {
        var insideViewport =
            e.X >= viewportRectPx.X &&
            e.Y >= viewportRectPx.Y &&
            e.X < viewportRectPx.X + viewportRectPx.Width &&
            e.Y < viewportRectPx.Y + viewportRectPx.Height;

        var nowTicks = Stopwatch.GetTimestamp();

        if (_inspectScene3DCapture is { } capture)
        {
            switch (e.Kind)
            {
                case HostMouseEventKind.Move:
                {
                    var settings = _host.Document.Settings.Scene3D;
                    var dtSeconds = (float)((nowTicks - capture.LastTicks) / (double)Stopwatch.Frequency);
                    dtSeconds = Math.Clamp(dtSeconds, 1f / 500f, 1f / 15f);

                    // Orbit blocks already track yaw/pitch explicitly; keep position-based smoothing there.
                    // Freefly look uses derived yaw/pitch, so smooth the raw deltas for responsiveness (delta-EMA).
                    var isOrbitMode = stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit;
                    var isFreeflyLook = !isOrbitMode && capture.Mode == Scene3DDragMode.Orbit;

                    if (isFreeflyLook)
                    {
                        var rawDx = Math.Clamp(e.X - capture.LastX, -MouseDeltaClampPx, MouseDeltaClampPx);
                        var rawDy = Math.Clamp(e.Y - capture.LastY, -MouseDeltaClampPx, MouseDeltaClampPx);

                        var tau = Math.Clamp(settings.FreeflyLookTauSeconds, 0.001f, 0.25f);
                        var alpha = 1f - MathF.Exp(-dtSeconds / tau);

                        var smoothedDx = Lerp(capture.SmoothedDx, rawDx, alpha);
                        var smoothedDy = Lerp(capture.SmoothedDy, rawDy, alpha);

                        if (MathF.Abs(smoothedDx) + MathF.Abs(smoothedDy) <= 0.05f)
                        {
                            _inspectScene3DCapture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedDx = smoothedDx, SmoothedDy = smoothedDy };
                            return true;
                        }

                        ApplySceneDragDelta(stl, settings, capture.Mode, smoothedDx, smoothedDy);
                        _inspectScene3DCapture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedDx = smoothedDx, SmoothedDy = smoothedDy };
                        return true;
                    }

                    {
                        var tau = Math.Clamp(settings.MouseSmoothingTauSeconds, 0.001f, 0.50f);
                        var alpha = 1f - MathF.Exp(-dtSeconds / tau);

                        var rawX = (float)e.X;
                        var rawY = (float)e.Y;
                        var smoothedX = Lerp(capture.SmoothedX, rawX, alpha);
                        var smoothedY = Lerp(capture.SmoothedY, rawY, alpha);

                        var dx = Math.Clamp(smoothedX - capture.SmoothedX, -MouseDeltaClampPx, MouseDeltaClampPx);
                        var dy = Math.Clamp(smoothedY - capture.SmoothedY, -MouseDeltaClampPx, MouseDeltaClampPx);

                        if (MathF.Abs(dx) + MathF.Abs(dy) <= 0.05f)
                        {
                            _inspectScene3DCapture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedX = smoothedX, SmoothedY = smoothedY };
                            return true;
                        }

                        ApplySceneDragDelta(stl, settings, capture.Mode, dx, dy);
                        _inspectScene3DCapture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedX = smoothedX, SmoothedY = smoothedY };
                    }
                    return true;
                }
                case HostMouseEventKind.Up when (e.Buttons & capture.Button) != 0:
                    _inspectScene3DCapture = null;
                    return true;
                case HostMouseEventKind.Wheel:
                    return true;
            }

            return true;
        }

        if (!insideViewport)
        {
            if (e.Kind == HostMouseEventKind.Down)
            {
                if (stl is IMouseFocusableViewportBlock focusable)
                {
                    focusable.HasMouseFocus = false;
                }
                _inspectScene3DMouseFocus = null;
            }

            return false;
        }

        if (e.Kind == HostMouseEventKind.Wheel)
        {
            ApplySceneZoom(stl, _host.Document.Settings.Scene3D, e.WheelDelta);
            return true;
        }

        if (e.Kind == HostMouseEventKind.Move)
        {
            if (!_hasScene3DMousePos)
            {
                _lastScene3DMouseX = e.X;
                _lastScene3DMouseY = e.Y;
                _hasScene3DMousePos = true;
            }

            if ((e.Buttons & HostMouseButtons.Left) != 0)
            {
                if (stl is IMouseFocusableViewportBlock focusable)
                {
                    focusable.HasMouseFocus = true;
                }
                _inspectScene3DMouseFocus = stl.Id;
                if (stl is not IScene3DOrbitBlock { NavigationMode: Scene3DNavigationMode.Orbit })
                {
                    // Freefly: start capture without applying an unsmoothed first-frame delta.
                    _inspectScene3DCapture = new Scene3DCapture(stl.Id, HostMouseButtons.Left, Scene3DDragMode.Orbit, e.X, e.Y, nowTicks, e.X, e.Y, 0f, 0f);
                }
                else
                {
                    var rawDx = Math.Clamp(e.X - _lastScene3DMouseX, -MouseDeltaClampPx, MouseDeltaClampPx);
                    var rawDy = Math.Clamp(e.Y - _lastScene3DMouseY, -MouseDeltaClampPx, MouseDeltaClampPx);
                    ApplySceneDragDelta(stl, _host.Document.Settings.Scene3D, Scene3DDragMode.Orbit, rawDx, rawDy);
                    _inspectScene3DCapture = new Scene3DCapture(stl.Id, HostMouseButtons.Left, Scene3DDragMode.Orbit, e.X, e.Y, nowTicks, e.X, e.Y, 0f, 0f);
                }
                _lastScene3DMouseX = e.X;
                _lastScene3DMouseY = e.Y;
                return true;
            }

            if ((e.Buttons & HostMouseButtons.Right) != 0)
            {
                var rawDx = Math.Clamp(e.X - _lastScene3DMouseX, -MouseDeltaClampPx, MouseDeltaClampPx);
                var rawDy = Math.Clamp(e.Y - _lastScene3DMouseY, -MouseDeltaClampPx, MouseDeltaClampPx);
                ApplySceneDragDelta(stl, _host.Document.Settings.Scene3D, Scene3DDragMode.Pan, rawDx, rawDy);
                _inspectScene3DCapture = new Scene3DCapture(stl.Id, HostMouseButtons.Right, Scene3DDragMode.Pan, e.X, e.Y, nowTicks, e.X, e.Y, 0f, 0f);
                _lastScene3DMouseX = e.X;
                _lastScene3DMouseY = e.Y;
                return true;
            }

            _lastScene3DMouseX = e.X;
            _lastScene3DMouseY = e.Y;
        }

        if (e.Kind == HostMouseEventKind.Down)
        {
            if ((e.Buttons & HostMouseButtons.Left) != 0)
            {
                if (stl is IMouseFocusableViewportBlock focusable)
                {
                    focusable.HasMouseFocus = true;
                }
                _inspectScene3DMouseFocus = stl.Id;
                _inspectScene3DCapture = new Scene3DCapture(stl.Id, HostMouseButtons.Left, Scene3DDragMode.Orbit, e.X, e.Y, nowTicks, e.X, e.Y, 0f, 0f);
                return true;
            }

            if ((e.Buttons & HostMouseButtons.Right) != 0)
            {
                _inspectScene3DCapture = new Scene3DCapture(stl.Id, HostMouseButtons.Right, Scene3DDragMode.Pan, e.X, e.Y, nowTicks, e.X, e.Y, 0f, 0f);
                return true;
            }
        }

        return false;
    }

    public void ExitInspectMode()
    {
        if (_activeInspect is null)
        {
            return;
        }

        WriteInspectReceiptIfNeeded(_activeInspect);

        if (_activeInspect.View is IMouseFocusableViewportBlock focusable)
        {
            focusable.HasMouseFocus = false;
        }

        _inspectScene3DCapture = null;
        _inspectScene3DMouseFocus = null;
        _inspectScene3DNavKeysDown = Scene3DNavKeys.None;
        _hasScene3DMousePos = false;
        _activeInspect = null;
        _host.RequestContentRebuild();
    }

    private void WriteInspectReceiptIfNeeded(InspectEntry entry)
    {
        if (entry.ReceiptWritten)
        {
            return;
        }

        entry.ReceiptWritten = true;

        if (string.IsNullOrWhiteSpace(entry.ReceiptLine))
        {
            return;
        }

        var receiptId = _host.AllocateBlockId();
        // Insert directly after the originating command echo (even if it is the last block at the moment).
        // `InsertBlockAfter` clamps to keep the shell prompt last, but inspect temporarily removes the prompt.
        var blocks = _host.TranscriptBlocks;
        var insertAt = blocks.Count;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == entry.CommandEchoId)
            {
                insertAt = i + 1;
                break;
            }
        }

        insertAt = Math.Clamp(insertAt, 0, blocks.Count);
        _host.InsertTranscriptBlock(insertAt, new TextBlock(receiptId, entry.ReceiptLine, ConsoleTextStream.System));
    }

    private static void ApplySceneDragDelta(IScene3DViewBlock stl, Scene3DSettings settings, Scene3DDragMode mode, float dx, float dy)
    {
        if (MathF.Abs(dx) + MathF.Abs(dy) <= 1f)
        {
            return;
        }

        if (stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit)
        {
            var target = orbit.OrbitTarget;
            var distance = Math.Max(0.01f, orbit.OrbitDistance);

            var yaw = orbit.OrbitYaw;
            var pitch = orbit.OrbitPitch;

            switch (mode)
            {
                case Scene3DDragMode.Orbit:
                {
                    yaw += dx * settings.OrbitSensitivity * (settings.InvertOrbitX ? -1f : 1f);
                    pitch -= dy * settings.OrbitSensitivity * (settings.InvertOrbitY ? -1f : 1f);
                    pitch = Math.Clamp(pitch, -1.55f, 1.55f);
                    break;
                }
                case Scene3DDragMode.Pan:
                {
                    var forward = ComputeForward(yaw, pitch);
                    var (right, up, _) = ComputeSceneBasis(forward);
                    var scale = Math.Max(0.01f, distance) * settings.PanSensitivity;
                    var delta = (-dx * scale * (settings.InvertPanX ? -1f : 1f)) * right +
                                (dy * scale * (settings.InvertPanY ? -1f : 1f)) * up;
                    target += delta;
                    break;
                }
            }

            orbit.OrbitYaw = yaw;
            orbit.OrbitPitch = pitch;
            orbit.OrbitTarget = target;
            orbit.OrbitDistance = distance;

            var dir = ComputeForward(yaw, pitch);
            stl.CenterDir = dir;
            stl.FocusDistance = distance;
            stl.CameraPos = target - (dir * distance);
            return;
        }

        switch (mode)
        {
            case Scene3DDragMode.Orbit:
            {
                var yaw = MathF.Atan2(stl.CenterDir.X, stl.CenterDir.Z);
                var pitch = MathF.Asin(Math.Clamp(stl.CenterDir.Y, -1f, 1f));
                var lookSensitivity = settings.FreeflyLookSensitivity;
                yaw += dx * lookSensitivity * (settings.InvertOrbitX ? -1f : 1f);
                pitch -= dy * lookSensitivity * (settings.InvertOrbitY ? -1f : 1f);
                pitch = Math.Clamp(pitch, -1.55f, 1.55f);
                stl.CenterDir = ComputeForward(yaw, pitch);
                break;
            }
            case Scene3DDragMode.Pan:
            {
                var (right, up, _) = ComputeSceneBasis(stl.CenterDir);
                var scale = Math.Max(0.01f, stl.FocusDistance) * settings.PanSensitivity;
                stl.CameraPos += (-dx * scale * (settings.InvertPanX ? -1f : 1f)) * right;
                stl.CameraPos += (dy * scale * (settings.InvertPanY ? -1f : 1f)) * up;
                break;
            }
        }
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static void ApplySceneZoom(IScene3DViewBlock stl, Scene3DSettings settings, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var delta = settings.InvertZoom ? -wheelDelta : wheelDelta;
        var factor = MathF.Exp(-delta * settings.ZoomSensitivity);
        ApplySceneDollyFactor(stl, factor);
    }

    private static void ApplySceneDollyFactor(IScene3DViewBlock stl, float factor)
    {
        if (stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit)
        {
            var oldDistance = Math.Max(0.01f, orbit.OrbitDistance);
            var maxDistance = MathF.Max(0.01f, stl.BoundsRadius * 100f);
            var newDistance = Math.Clamp(oldDistance * factor, 0.01f, maxDistance);
            if (MathF.Abs(newDistance - oldDistance) < 1e-6f)
            {
                return;
            }

            orbit.OrbitDistance = newDistance;

            var dir = stl.CenterDir;
            if (dir.LengthSquared() < 1e-10f)
            {
                dir = new Vector3(0, 0, 1);
            }
            else
            {
                dir = Vector3.Normalize(dir);
            }

            stl.CenterDir = dir;
            stl.FocusDistance = newDistance;
            stl.CameraPos = orbit.OrbitTarget - (dir * newDistance);
            return;
        }

        var oldFocus = Math.Max(0.01f, stl.FocusDistance);
        var maxFocus = MathF.Max(0.01f, stl.BoundsRadius * 100f);
        var newFocus = Math.Clamp(oldFocus * factor, 0.01f, maxFocus);
        if (MathF.Abs(newFocus - oldFocus) < 1e-6f)
        {
            return;
        }

        var forward = stl.CenterDir;
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }
        else
        {
            forward = Vector3.Normalize(forward);
        }

        stl.CenterDir = forward;
        stl.CameraPos += forward * (oldFocus - newFocus);
        stl.FocusDistance = newFocus;
    }

    private static (Vector3 Right, Vector3 Up, Vector3 Forward) ComputeSceneBasis(Vector3 forward)
    {
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }

        forward = Vector3.Normalize(forward);

        // Project a reference "up" onto the plane perpendicular to forward.
        // This avoids discontinuous axis switching when near the poles.
        var upRef = Vector3.UnitY;
        var up = upRef - (forward * Vector3.Dot(upRef, forward));
        if (up.LengthSquared() < 1e-8f)
        {
            upRef = Vector3.UnitZ;
            up = upRef - (forward * Vector3.Dot(upRef, forward));
            if (up.LengthSquared() < 1e-8f)
            {
                upRef = Vector3.UnitX;
                up = upRef - (forward * Vector3.Dot(upRef, forward));
            }
        }

        up = up.LengthSquared() < 1e-10f ? Vector3.UnitY : Vector3.Normalize(up);

        var right = Vector3.Cross(up, forward);
        right = right.LengthSquared() < 1e-10f ? Vector3.UnitX : Vector3.Normalize(right);

        up = Vector3.Cross(forward, right);
        up = up.LengthSquared() < 1e-10f ? Vector3.UnitY : Vector3.Normalize(up);

        return (right, up, forward);
    }

    private static Vector3 ComputeForward(float yaw, float pitch)
    {
        var cy = MathF.Cos(yaw);
        var sy = MathF.Sin(yaw);
        var cp = MathF.Cos(pitch);
        var sp = MathF.Sin(pitch);
        var forward = new Vector3(sy * cp, sp, cy * cp);
        if (forward.LengthSquared() < 1e-10f)
        {
            return new Vector3(0, 0, 1);
        }

        return Vector3.Normalize(forward);
    }

    private sealed class InspectEntry
    {
        public InspectEntry(int id, InspectKind kind, string path, string title, IBlock view, BlockId commandEchoId, string receiptLine)
        {
            Id = id;
            Kind = kind;
            Path = path;
            Title = title;
            View = view;
            CommandEchoId = commandEchoId;
            ReceiptLine = receiptLine;
        }

        public int Id { get; }
        public InspectKind Kind { get; }
        public string Path { get; }
        public string Title { get; }
        public IBlock View { get; }
        public BlockId CommandEchoId { get; }
        public string ReceiptLine { get; }
        public bool ReceiptWritten { get; set; }
    }

    private enum Scene3DDragMode
    {
        Orbit,
        Pan
    }

    private readonly record struct Scene3DCapture(
        BlockId BlockId,
        HostMouseButtons Button,
        Scene3DDragMode Mode,
        int LastX,
        int LastY,
        long LastTicks,
        float SmoothedX,
        float SmoothedY,
        float SmoothedDx,
        float SmoothedDy);

    [Flags]
    private enum Scene3DNavKeys
    {
        None = 0,
        W = 1 << 0,
        A = 1 << 1,
        S = 1 << 2,
        D = 1 << 3
    }
}

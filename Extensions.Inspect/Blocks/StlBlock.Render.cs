using System;
using System.Numerics;
using Cycon.Render;
using Cycon.Core.Settings;

namespace Extensions.Inspect.Blocks;

public sealed partial class StlBlock
{
#if DEBUG
    private bool _loggedFirstRender;
    private int _loggedViewportW;
    private int _loggedViewportH;
#endif

    public void Render(IRenderCanvas canvas, in BlockRenderContext ctx)
    {
        var rect = ctx.ViewportRectPx;
        var x = rect.X;
        var y = rect.Y;
        var w = rect.Width;
        var h = rect.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var settings = ctx.Scene3D;

        // Default STL rendering is double-sided unless explicitly set otherwise (debug/compat).
        var mode = (StlDebugMode)settings.StlDebugMode;
        var cullEnabled = mode == StlDebugMode.Normal;
        canvas.SetCullState(enabled: cullEnabled, frontFaceCcw: true);

        canvas.FillRect(rect, unchecked((int)0x000000FF));

#if DEBUG
        if (!_loggedFirstRender)
        {
            _loggedFirstRender = true;
            var b = MeshBounds;
        }
#endif

        canvas.SetDepthState(enabled: true, writeEnabled: true, DepthFunc.Less);
        canvas.SetColorWrite(true);
        canvas.ClearDepth(1f);

        var viewRect = rect;
        _hasInspectViewRect = false;
        if (_inspectLayoutEnabled)
        {
            if (TryRenderInspectLayout(canvas, ctx, rect, out var layoutViewRect))
            {
                viewRect = layoutViewRect;
                _inspectViewRectPx = viewRect;
                _hasInspectViewRect = true;
            }
        }

        var aspect = viewRect.Width / (float)viewRect.Height;
        _lastProjectionAspect = aspect;
        _lastHorizontalFovDegrees = settings.HorizontalFovDegrees > 0 ? settings.HorizontalFovDegrees : 80f;
        var vfov = ComputeVerticalFovRadians(_lastHorizontalFovDegrees, aspect);

        if (float.IsFinite(_lastVerticalFovRadians))
        {
            var oldVfov = _lastVerticalFovRadians;
            if (MathF.Abs(vfov - oldVfov) > 1e-6f)
            {
                var lookAt = CameraPos + (CenterDir * FocusDistance);
                var scale = MathF.Tan(oldVfov * 0.5f) / MathF.Tan(vfov * 0.5f);
                FocusDistance *= scale;
                CameraPos = lookAt - (CenterDir * FocusDistance);
                _lastVerticalFovRadians = vfov;

                if (NavigationMode == Cycon.Core.Transcript.Scene3DNavigationMode.Orbit)
                {
                    SyncOrbitFromCurrentView();
                }
            }
        }
        else
        {
            _lastVerticalFovRadians = vfov;
        }

        var boundsRadius = MathF.Max(MeshBounds.Radius, 0.0001f);
        var near = MathF.Max(0.01f, boundsRadius * 0.005f);
        var far = MathF.Max(near + 1f, boundsRadius * 50f);

#if DEBUG
        if (_loggedViewportW != w || _loggedViewportH != h)
        {
            _loggedViewportW = w;
            _loggedViewportH = h;
        }
#endif

        var forward = CenterDir;
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }

        forward = Vector3.Normalize(forward);
        CenterDir = forward;

        if (NavigationMode == Cycon.Core.Transcript.Scene3DNavigationMode.Orbit)
        {
            SyncOrbitFromCurrentView();
        }

        var lookAtPoint = CameraPos + (forward * MathF.Max(near * 2f, FocusDistance));

        var (_, up) = ComputeStableBasis(forward);

        // Use a stable right-handed basis: right = up × forward, up = forward × right.
        var view = Matrix4x4.CreateLookAt(CameraPos, lookAtPoint, up);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(vfov, aspect, near, far);

        // View-space directional light (camera-aligned).
        var lightDirView = Vector3.Normalize(new Vector3(0.2f, 0.35f, 1.0f));
        canvas.DrawMesh3D(Id.Value, VertexData, VertexCount, viewRect, Matrix4x4.Identity, view, proj, lightDirView, settings);

        if (settings.VignetteStrength > 0f)
        {
            canvas.DrawVignette(viewRect, settings.VignetteStrength, settings.VignetteInner, settings.VignetteOuter);
        }

        canvas.SetColorWrite(true);
        canvas.SetDepthState(enabled: false, writeEnabled: false, DepthFunc.Less);
        // Restore default: no culling for 2D.
        canvas.SetCullState(enabled: false, frontFaceCcw: true);
    }

    private bool TryRenderInspectLayout(IRenderCanvas canvas, in BlockRenderContext ctx, RectPx rect, out RectPx viewRect)
    {
        viewRect = rect;

        var cellW = Math.Max(1, ctx.TextMetrics.CellWidthPx);
        var cellH = Math.Max(1, ctx.TextMetrics.CellHeightPx);
        var cols = Math.Max(0, rect.Width / cellW);
        if (cols <= 0)
        {
            return false;
        }

        const int topChromeRows = 2;
        const int bottomChromeRows = 2;
        if (rect.Height < ((topChromeRows + bottomChromeRows + 1) * cellH))
        {
            return false;
        }
        const int diagCols = 24;
        const int sepCols = 1;

        var topChromeHPx = topChromeRows * cellH;
        var bottomChromeHPx = bottomChromeRows * cellH;
        var midHPx = rect.Height - topChromeHPx - bottomChromeHPx;
        var midRows = midHPx / cellH;
        if (midRows <= 0)
        {
            return false;
        }

        var viewColStart = Math.Min(Math.Max(0, cols - 1), diagCols + sepCols);
        var viewCols = Math.Max(1, cols - viewColStart);

        var topTextYPx = rect.Y;
        var topSepYPx = rect.Y + ((topChromeRows - 1) * cellH);
        var botSepYPx = rect.Y + rect.Height - bottomChromeHPx;
        var botTextYPx = rect.Y + rect.Height - cellH;

        DrawAlignedTextAtY(canvas, rect, topTextYPx, "STL  ORB", Align.Left, cols, cellW, ctx.Theme.ForegroundRgba);
        DrawAlignedTextAtY(canvas, rect, topTextYPx, string.Empty, Align.Center, cols, cellW, ctx.Theme.ForegroundRgba);
        DrawAlignedTextAtY(canvas, rect, topTextYPx, "SRC --------", Align.Right, cols, cellW, ctx.Theme.ForegroundRgba);

        DrawChromeSeparatorAtY(canvas, rect, topSepYPx, cellH, ctx.Theme.ForegroundRgba);
        DrawChromeSeparatorAtY(canvas, rect, botSepYPx, cellH, ctx.Theme.ForegroundRgba);

        DrawAlignedTextAtY(canvas, rect, botTextYPx, "Tri -------  Vtx -------", Align.Left, cols, cellW, ctx.Theme.ForegroundRgba);
        DrawAlignedTextAtY(canvas, rect, botTextYPx, "Sel none", Align.Center, cols, cellW, ctx.Theme.ForegroundRgba);
        DrawAlignedTextAtY(canvas, rect, botTextYPx, "Q 1e-05", Align.Right, cols, cellW, ctx.Theme.ForegroundRgba);

        var diagWidth = Math.Min(diagCols, cols);
        if (diagWidth > 0)
        {
            var diagLines = GetDiagLines();
            var maxLines = Math.Min(diagLines.Length, midRows);
            for (var i = 0; i < maxLines; i++)
            {
                var line = diagLines[i];
                var yPx = rect.Y + topChromeHPx + (i * cellH);
                DrawTextRowAtY(canvas, rect, 0, yPx, line, diagWidth, cellW, ctx.Theme.ForegroundRgba);
            }
        }

        var viewYPx = rect.Y + topChromeHPx;
        var viewHPx = midHPx;
        viewRect = new RectPx(rect.X + (viewColStart * cellW), viewYPx, viewCols * cellW, viewHPx);
        return viewRect.Width > 0 && viewRect.Height > 0;
    }

    private static void DrawChromeSeparatorAtY(IRenderCanvas canvas, RectPx rect, int rowTopYPx, int cellH, int rgba)
    {
        if (cellH <= 0 || rect.Width <= 0)
        {
            return;
        }

        const int thicknessPx = 2;
        var yPx = rowTopYPx + Math.Max(0, (cellH - thicknessPx) / 2);
        canvas.FillRect(new RectPx(rect.X, yPx, rect.Width, thicknessPx), rgba);
    }

    private static void DrawTextRowAtY(IRenderCanvas canvas, RectPx rect, int col, int yPx, string text, int maxCols, int cellW, int rgba)
    {
        if (maxCols <= 0 || string.IsNullOrEmpty(text))
        {
            return;
        }

        var len = Math.Min(maxCols, text.Length);
        if (len <= 0)
        {
            return;
        }

        var xPx = rect.X + (col * cellW);
        canvas.DrawText(text, 0, len, xPx, yPx, rgba);
    }

    private static void DrawAlignedTextAtY(IRenderCanvas canvas, RectPx rect, int yPx, string text, Align align, int cols, int cellW, int rgba)
    {
        var len = text.Length;
        if (cols <= 0)
        {
            return;
        }

        var startCol = align switch
        {
            Align.Left => 0,
            Align.Center => Math.Max(0, (cols - len) / 2),
            Align.Right => Math.Max(0, cols - len),
            _ => 0
        };

        var maxLen = Math.Min(len, cols - startCol);
        if (maxLen <= 0)
        {
            return;
        }

        var xPx = rect.X + (startCol * cellW);
        canvas.DrawText(text, 0, maxLen, xPx, yPx, rgba);
    }

    private static string[] GetDiagLines() =>
        new[]
        {
            "Geometry",
            "  Tri  -------",
            "  Vtx  -------",
            "",
            "Bounds",
            "  Min  +000 +000 +000",
            "  Max  +000 +000 +000",
            "",
            "Camera",
            "  Mode ORB",
            "  FOV  45.0",
            "  Dist +0.000",
            "",
            "Selection",
            "  Kind none",
            "  Id   ------"
        };

    private enum Align
    {
        Left,
        Center,
        Right
    }

    // Center-ray camera model: view forward is tracked by CenterDir in the block.

    private static (Vector3 Right, Vector3 Up) ComputeStableBasis(Vector3 forward)
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

        return (right, up);
    }
}

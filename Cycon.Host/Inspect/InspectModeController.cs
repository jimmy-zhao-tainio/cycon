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
using Cycon.Layout.Inspect;
using Cycon.Layout.Scrolling;
using Cycon.Render;
using Cycon.Rendering.Renderer;
using Cycon.Host.Input;
using Cycon.Host.Hosting;

namespace Cycon.Host.Inspect;

internal sealed class InspectModeController
{
    private readonly IInspectHost _host;
    private readonly Scene3DPointerController _scene3DPointer = new();
    private readonly List<InspectEntry> _inspectHistory = new();
    private readonly List<InspectAction> _pendingActions = new();
    private int _nextInspectEntryId;
    private InspectEntry? _activeInspect;
    private Scene3DNavKeys _inspectScene3DNavKeysDown;
    private InspectLayoutResult _inspectLayout;
    private InspectChromeSpec _inspectChromeSpec;
    private bool _hasInspectLayout;
    private int _inspectLayoutFramebufferWidth = -1;
    private int _inspectLayoutFramebufferHeight = -1;
    private int _inspectLayoutInspectEntryId = -1;
    private InspectChromeDataBuilder _inspectChromeData = new(capacity: 128);

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

        _scene3DPointer.Reset();
        _inspectScene3DNavKeysDown = Scene3DNavKeys.None;
        _hasInspectLayout = false;

        // NOTE: If drag mysteriously needs a priming click again,
        // check Windows keyboard/trackpad/trackpoint delay settings.
        // Yes, this has happened before. More than once.
        if (viewBlock is IMouseFocusableViewportBlock focusable)
        {
            focusable.HasMouseFocus = true;
        }

        _pendingActions.Add(new InspectRequestContentRebuild());
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

        _scene3DPointer.Reset();
        _inspectScene3DNavKeysDown = Scene3DNavKeys.None;
        _hasInspectLayout = false;
    }

    public Cycon.Rendering.RenderFrame BuildInspectFrame(
        int framebufferWidth,
        int framebufferHeight,
        double timeSeconds,
        IReadOnlyList<int>? meshReleases)
    {
        if (_activeInspect is null)
        {
            throw new InvalidOperationException("Inspect frame requested without an active inspect entry.");
        }

        var frame = new Cycon.Rendering.RenderFrame
        {
            BuiltGrid = new GridSize(0, 0)
        };

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

        if (_activeInspect.View is IInspectChromeProvider inspectChromeProvider &&
            TryEnsureInspectLayout(_activeInspect, framebufferWidth, framebufferHeight, textMetrics, out var inspectLayout, out var inspectChromeSpec))
        {
            _inspectChromeData.Clear();
            inspectChromeProvider.PopulateInspectChromeData(ref _inspectChromeData);
            BlockViewRenderer.RenderFullscreenInspect(
                frame,
                _host.Font,
                renderBlock,
                ctx,
                framebufferWidth,
                framebufferHeight,
                inspectLayout,
                inspectChromeSpec,
                ref _inspectChromeData);
        }
        else
        {
            if (_activeInspect.View is IInspectLayoutBlock layoutBlock)
            {
                layoutBlock.SetInspectLayoutEnabled(true);
            }

            BlockViewRenderer.RenderFullscreen(frame, _host.Font, renderBlock, ctx, framebufferWidth, framebufferHeight);

            if (_activeInspect.View is IInspectLayoutBlock layoutBlockAfter)
            {
                layoutBlockAfter.SetInspectLayoutEnabled(false);
            }
        }

        return frame;
    }

    public IReadOnlyList<InspectAction> DrainActions()
    {
        if (_pendingActions.Count == 0)
        {
            return Array.Empty<InspectAction>();
        }

        var snapshot = _pendingActions.ToArray();
        _pendingActions.Clear();
        return snapshot;
    }

    public void DrainPendingEvents(List<PendingEvent>? events, int framebufferWidth, int framebufferHeight)
    {
        if (events is null || events.Count == 0 || _activeInspect is null)
        {
            return;
        }

        var block = _activeInspect.View;
        if (block is IInspectChromeProvider)
        {
            var fontMetrics = _host.Font.Metrics;
            var textMetrics = new TextMetrics(
                CellWidthPx: fontMetrics.CellWidthPx,
                CellHeightPx: fontMetrics.CellHeightPx,
                BaselinePx: fontMetrics.BaselinePx,
                UnderlineThicknessPx: Math.Max(1, fontMetrics.UnderlineThicknessPx),
                UnderlineTopOffsetPx: fontMetrics.UnderlineTopOffsetPx);
            TryEnsureInspectLayout(_activeInspect, framebufferWidth, framebufferHeight, textMetrics, out _, out _);
        }
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
                    _pendingActions.Add(new InspectHandleFileDrop(fileDrop.Path));
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
        var (right, up, _) = Scene3DPointerController.ComputeSceneBasis(stl.CenterDir);

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
            Scene3DPointerController.ApplySceneDollyFactor(stl, factor);
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
        var inputViewport = GetInputViewport(block, viewportRectPx);
        if (block is IBlockWheelHandler wheelHandler && e.Kind == HostMouseEventKind.Wheel)
        {
            return wheelHandler.HandleWheel(e, inputViewport);
        }

        if (block is IBlockPointerHandler pointerHandler &&
            e.Kind is HostMouseEventKind.Down or HostMouseEventKind.Up or HostMouseEventKind.Move)
        {
            return pointerHandler.HandlePointer(e, inputViewport);
        }

        if (block is IScene3DViewBlock stl)
        {
            return _scene3DPointer.Handle(stl, inputViewport, e, _host.Document.Settings.Scene3D);
        }

        return false;
    }

    private PxRect GetInputViewport(IBlock block, in PxRect viewportRectPx)
    {
        if (_activeInspect is not null &&
            _hasInspectLayout &&
            _inspectLayoutInspectEntryId == _activeInspect.Id &&
            _inspectLayoutFramebufferWidth == viewportRectPx.Width &&
            _inspectLayoutFramebufferHeight == viewportRectPx.Height &&
            ReferenceEquals(block, _activeInspect.View))
        {
            var r = _inspectLayout.ContentRect;
            return new PxRect(r.X, r.Y, r.Width, r.Height);
        }

        if (block is not IBlockChromeProvider chromeProvider)
        {
            return viewportRectPx;
        }

        var chrome = chromeProvider.ChromeSpec;
        if (!chrome.Enabled)
        {
            return viewportRectPx;
        }

        var inset = Math.Max(0, chrome.BorderPx + chrome.PaddingPx);
        if (inset <= 0)
        {
            return viewportRectPx;
        }

        var x = viewportRectPx.X + inset;
        var y = viewportRectPx.Y + inset;
        var w = Math.Max(0, viewportRectPx.Width - (inset * 2));
        var h = Math.Max(0, viewportRectPx.Height - (inset * 2));
        return new PxRect(x, y, w, h);
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

        if (_activeInspect.View is IInspectResettableBlock resettable)
        {
            resettable.ResetViewModeSize();
        }

        _scene3DPointer.Reset();
        _inspectScene3DNavKeysDown = Scene3DNavKeys.None;
        _activeInspect = null;
        _hasInspectLayout = false;
        _pendingActions.Add(new InspectRequestContentRebuild());
    }

    private bool TryEnsureInspectLayout(
        InspectEntry entry,
        int framebufferWidth,
        int framebufferHeight,
        in TextMetrics textMetrics,
        out InspectLayoutResult layout,
        out InspectChromeSpec chromeSpec)
    {
        layout = default;
        chromeSpec = default;

        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            _hasInspectLayout = false;
            return false;
        }

        if (entry.View is not IInspectChromeProvider provider)
        {
            _hasInspectLayout = false;
            return false;
        }

        chromeSpec = provider.GetInspectChromeSpec();
        if (!chromeSpec.Enabled)
        {
            _hasInspectLayout = false;
            return false;
        }

        if (_hasInspectLayout &&
            _inspectLayoutFramebufferWidth == framebufferWidth &&
            _inspectLayoutFramebufferHeight == framebufferHeight &&
            _inspectLayoutInspectEntryId == entry.Id)
        {
            layout = _inspectLayout;
            chromeSpec = _inspectChromeSpec;
            return true;
        }

        var outerRect = new RectPx(0, 0, framebufferWidth, framebufferHeight);
        var chrome = entry.View is IBlockChromeProvider chromeProvider
            ? chromeProvider.ChromeSpec
            : BlockChromeSpec.Disabled;

        var reservation = chrome.Enabled ? Math.Max(0, chrome.PaddingPx + chrome.BorderPx) : 0;
        var innerRect = reservation > 0 ? DeflateRect(outerRect, reservation) : outerRect;

        var inspectOuterRect = outerRect;
        if (chrome.Enabled && chrome.Style == BlockChromeStyle.Frame2Px)
        {
            var thickness = Math.Max(1, chrome.BorderPx);
            var inset = Math.Max(0, (reservation - thickness) / 2);
            inspectOuterRect = inset > 0 ? DeflateRect(outerRect, inset) : outerRect;
        }

        var autoInset = Math.Max(0, innerRect.X - inspectOuterRect.X);
        var outerBorderPx = autoInset + Math.Max(0, chromeSpec.OuterBorderPx);
        if (!InspectLayoutEngine.TryCompute(inspectOuterRect, textMetrics, outerBorderPx, chromeSpec.Panels, out layout))
        {
            _hasInspectLayout = false;
            return false;
        }

        _inspectLayout = layout;
        _inspectChromeSpec = chromeSpec;
        _inspectLayoutFramebufferWidth = framebufferWidth;
        _inspectLayoutFramebufferHeight = framebufferHeight;
        _inspectLayoutInspectEntryId = entry.Id;
        _hasInspectLayout = true;
        return true;
    }

    private static RectPx DeflateRect(in RectPx rect, int inset)
    {
        if (inset <= 0)
        {
            return rect;
        }

        var x = rect.X + inset;
        var y = rect.Y + inset;
        var w = Math.Max(0, rect.Width - (inset * 2));
        var h = Math.Max(0, rect.Height - (inset * 2));
        return new RectPx(x, y, w, h);
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

        _pendingActions.Add(new InspectWriteReceipt(entry.CommandEchoId, entry.ReceiptLine));
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

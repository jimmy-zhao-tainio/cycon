using Cycon.App;
using Cycon.Backends.SilkNet;
using Cycon.Backends.SilkNet.Execution;
using Cycon.Host.Hosting;
using Cycon.Host.Input;
using System.Diagnostics;
using Silk.NET.Input;

namespace Cycon.Platform.SilkNet;

public static class SilkNetCyconRunner
{
    private const int KeyRepeatInitialDelayMs = 400;
    private const int KeyRepeatIntervalMs = 33;
    private const int ActiveFps = 60;
    private const int IdleFps = 1;
    private const int IdleAfterMs = 250;

    public static void Run2D(CyconAppOptions options)
    {
        var window = SilkWindow.Create(options.Width, options.Height, options.Title);
        var swapchain = new SilkSwapchainGl(window);
        SilkGraphicsDeviceGl? device = null;
        RenderFrameExecutorGl? executor = null;
        var clipboard = new SilkClipboard();
        var session = ConsoleHostSession.CreateVga(options.InitialText, clipboard, configureBlockCommands: options.ConfigureBlockCommands);

        var ctrlDown = false;
        var shiftDown = false;
        var altDown = false;
        var buttonsDown = HostMouseButtons.None;
        var lastMouseX = 0;
        var lastMouseY = 0;

        var pressedKeys = new HashSet<Key>();
        var repeatKeys = new Dictionary<HostKey, long>();
        var lastInteractionTicks = Stopwatch.GetTimestamp();
        var lastAppliedFps = ActiveFps;
        var dirty = true;

        var debugLastReportTicks = 0L;
        var debugRenderedFrames = 0;
        var debugSkippedFrames = 0;

        window.Loaded += () =>
        {
            device = new SilkGraphicsDeviceGl(window);
            executor = new RenderFrameExecutorGl(device.Gl);

            session.Initialize(window.FramebufferWidth, window.FramebufferHeight);
            executor.Initialize(session.Atlas);
            executor.Resize(window.FramebufferWidth, window.FramebufferHeight);

            var tick = session.Tick();
            executor.Resize(tick.FramebufferWidth, tick.FramebufferHeight);
            executor.Execute(tick.Frame, session.Atlas);
            if (tick.OverlayFrame is not null)
            {
                executor.ExecuteOverlay(tick.OverlayFrame, session.Atlas);
            }
            foreach (var failure in executor.DrainRenderFailures())
            {
                session.ReportRenderFailure(failure.Key, failure.Value);
            }
            swapchain.Present();
            window.Show();

            window.FramesPerSecond = ActiveFps;
            window.UpdatesPerSecond = ActiveFps;
        };

        window.FramebufferResized += (width, height) =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            session.OnFramebufferResized(width, height);
        };

        window.TextInput += ch =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            session.OnTextInput(new HostTextInputEvent(ch));
        };

        window.KeyDown += key =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            UpdateModifiers(ref ctrlDown, ref shiftDown, ref altDown, key, isDown: true);
            pressedKeys.Add(key);

            var mapped = MapKey(key);
            var nowTicks = Stopwatch.GetTimestamp();
            if (IsRepeatable(mapped))
            {
                repeatKeys[mapped] = nowTicks + MsToTicks(KeyRepeatInitialDelayMs);
            }

            session.OnKeyEvent(new HostKeyEvent(mapped, GetModifiers(ctrlDown, shiftDown, altDown), IsDown: true));
        };

        window.KeyUp += key =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            UpdateModifiers(ref ctrlDown, ref shiftDown, ref altDown, key, isDown: false);
            pressedKeys.Remove(key);
            var mapped = MapKey(key);
            repeatKeys.Remove(mapped);
            session.OnKeyEvent(new HostKeyEvent(mapped, GetModifiers(ctrlDown, shiftDown, altDown), IsDown: false));
        };

        window.MouseDown += (x, y, button) =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            lastMouseX = x;
            lastMouseY = y;

            if (button == MouseButton.Left)
            {
                if ((buttonsDown & HostMouseButtons.Left) != 0)
                {
                    return;
                }

                buttonsDown |= HostMouseButtons.Left;
                session.OnMouseEvent(new HostMouseEvent(
                    HostMouseEventKind.Down,
                    x,
                    y,
                    buttonsDown,
                    GetModifiers(ctrlDown, shiftDown, altDown),
                    0));
            }

            if (button == MouseButton.Right)
            {
                if ((buttonsDown & HostMouseButtons.Right) != 0)
                {
                    return;
                }

                buttonsDown |= HostMouseButtons.Right;
                session.OnMouseEvent(new HostMouseEvent(
                    HostMouseEventKind.Down,
                    x,
                    y,
                    buttonsDown,
                    GetModifiers(ctrlDown, shiftDown, altDown),
                    0));
            }
        };

        window.MouseMoved += (x, y) =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            lastMouseX = x;
            lastMouseY = y;

            session.OnMouseEvent(new HostMouseEvent(
                HostMouseEventKind.Move,
                x,
                y,
                buttonsDown,
                GetModifiers(ctrlDown, shiftDown, altDown),
                0));
        };

        window.MouseUp += (x, y, button) =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            lastMouseX = x;
            lastMouseY = y;

            if (button == MouseButton.Left)
            {
                if ((buttonsDown & HostMouseButtons.Left) == 0)
                {
                    return;
                }

                session.OnMouseEvent(new HostMouseEvent(
                    HostMouseEventKind.Up,
                    x,
                    y,
                    HostMouseButtons.Left,
                    GetModifiers(ctrlDown, shiftDown, altDown),
                    0));
                buttonsDown &= ~HostMouseButtons.Left;
            }

            if (button == MouseButton.Right)
            {
                if ((buttonsDown & HostMouseButtons.Right) == 0)
                {
                    return;
                }

                session.OnMouseEvent(new HostMouseEvent(
                    HostMouseEventKind.Up,
                    x,
                    y,
                    HostMouseButtons.Right,
                    GetModifiers(ctrlDown, shiftDown, altDown),
                    0));
                buttonsDown &= ~HostMouseButtons.Right;
            }
        };

        window.MouseWheel += (x, y, delta) =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            lastMouseX = x;
            lastMouseY = y;

            session.OnMouseEvent(new HostMouseEvent(
                HostMouseEventKind.Wheel,
                x,
                y,
                buttonsDown,
                GetModifiers(ctrlDown, shiftDown, altDown),
                delta));
        };

        window.FileDropped += path =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            session.OnFileDrop(new HostFileDropEvent(path));
        };
        window.FocusChanged += isFocused =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            session.OnWindowFocusChanged(isFocused);

            if (isFocused)
            {
                return;
            }

            var mods = GetModifiers(ctrlDown, shiftDown, altDown);
            foreach (var key in pressedKeys)
            {
                var mapped = MapKey(key);
                session.OnKeyEvent(new HostKeyEvent(mapped, mods, IsDown: false));
            }

            if ((buttonsDown & HostMouseButtons.Left) != 0)
            {
                session.OnMouseEvent(new HostMouseEvent(
                    HostMouseEventKind.Up,
                    lastMouseX,
                    lastMouseY,
                    HostMouseButtons.Left,
                    mods,
                    0));
            }

            if ((buttonsDown & HostMouseButtons.Right) != 0)
            {
                session.OnMouseEvent(new HostMouseEvent(
                    HostMouseEventKind.Up,
                    lastMouseX,
                    lastMouseY,
                    HostMouseButtons.Right,
                    mods,
                    0));
            }

            ctrlDown = false;
            shiftDown = false;
            altDown = false;
            buttonsDown = HostMouseButtons.None;
            pressedKeys.Clear();
            repeatKeys.Clear();
        };
        window.PointerInWindowChanged += isInWindow =>
        {
            lastInteractionTicks = Stopwatch.GetTimestamp();
            dirty = true;
            session.OnPointerInWindowChanged(isInWindow);
        };

        window.Render += _ =>
        {
            if (executor is null)
            {
                return;
            }

            var nowTicks = Stopwatch.GetTimestamp();
            var active = pressedKeys.Count > 0 || buttonsDown != HostMouseButtons.None;
            var idle = !active && nowTicks - lastInteractionTicks >= MsToTicks(IdleAfterMs);
            var targetFps = idle ? IdleFps : ActiveFps;
            if (targetFps != lastAppliedFps)
            {
                window.FramesPerSecond = targetFps;
                window.UpdatesPerSecond = targetFps;
                lastAppliedFps = targetFps;
            }

            if (!dirty && !active && repeatKeys.Count == 0)
            {
                debugSkippedFrames++;
                ReportLoopStats(nowTicks, ref debugLastReportTicks, ref debugRenderedFrames, ref debugSkippedFrames);
                return;
            }

            if (repeatKeys.Count > 0)
            {
                var mods = GetModifiers(ctrlDown, shiftDown, altDown);
                foreach (var (hostKey, nextRepeatTicks) in repeatKeys.ToArray())
                {
                    if (nowTicks < nextRepeatTicks)
                    {
                        continue;
                    }

                    var repeatIntervalTicks = MsToTicks(KeyRepeatIntervalMs);
                    var newNextRepeat = nextRepeatTicks;
                    while (nowTicks >= newNextRepeat)
                    {
                        session.OnKeyEvent(new HostKeyEvent(hostKey, mods, IsDown: true));
                        newNextRepeat += repeatIntervalTicks;
                    }

                    repeatKeys[hostKey] = newNextRepeat;
                    lastInteractionTicks = nowTicks;
                    dirty = true;
                }
            }

            var tick = session.Tick();
            if (tick.SetVSync.HasValue)
            {
                window.VSync = tick.SetVSync.Value;
            }

            window.SetStandardCursor(MapCursor(session.CursorKind));

            executor.Resize(tick.FramebufferWidth, tick.FramebufferHeight);
            executor.Execute(tick.Frame, session.Atlas);
            if (tick.OverlayFrame is not null)
            {
                executor.ExecuteOverlay(tick.OverlayFrame, session.Atlas);
            }
            foreach (var failure in executor.DrainRenderFailures())
            {
                session.ReportRenderFailure(failure.Key, failure.Value);
            }
            swapchain.Present();
            dirty = false;
            debugRenderedFrames++;
            ReportLoopStats(nowTicks, ref debugLastReportTicks, ref debugRenderedFrames, ref debugSkippedFrames);

            if (tick.RequestExit)
            {
                window.Close();
            }
        };

        window.Run();
        window.Dispose();
    }

    private static bool IsRepeatable(HostKey key) =>
        key is HostKey.Backspace or HostKey.Left or HostKey.Right or HostKey.Up or HostKey.Down or HostKey.PageUp or HostKey.PageDown;

    private static long MsToTicks(int ms) =>
        (long)(ms * (Stopwatch.Frequency / 1000.0));

    private static void ReportLoopStats(long nowTicks, ref long lastReportTicks, ref int renderedFrames, ref int skippedFrames)
    {
        if (lastReportTicks == 0)
        {
            lastReportTicks = nowTicks;
            return;
        }

        var elapsedTicks = nowTicks - lastReportTicks;
        if (elapsedTicks < Stopwatch.Frequency)
        {
            return;
        }

        Debug.WriteLine($"Cycon loop: rendered={renderedFrames}/s skipped={skippedFrames}/s");
        renderedFrames = 0;
        skippedFrames = 0;
        lastReportTicks = nowTicks;
    }

    private static void UpdateModifiers(ref bool ctrl, ref bool shift, ref bool alt, Key key, bool isDown)
    {
        switch (key)
        {
            case Key.ControlLeft:
            case Key.ControlRight:
                ctrl = isDown;
                break;
            case Key.ShiftLeft:
            case Key.ShiftRight:
                shift = isDown;
                break;
            case Key.AltLeft:
            case Key.AltRight:
                alt = isDown;
                break;
        }
    }

    private static HostKeyModifiers GetModifiers(bool ctrl, bool shift, bool alt)
    {
        var mods = HostKeyModifiers.None;
        if (ctrl) mods |= HostKeyModifiers.Control;
        if (shift) mods |= HostKeyModifiers.Shift;
        if (alt) mods |= HostKeyModifiers.Alt;
        return mods;
    }

    private static HostKey MapKey(Key key)
    {
        return key switch
        {
            Key.Backspace => HostKey.Backspace,
            Key.Enter => HostKey.Enter,
            Key.KeypadEnter => HostKey.Enter,
            Key.Tab => HostKey.Tab,
            Key.Left => HostKey.Left,
            Key.Right => HostKey.Right,
            Key.Up => HostKey.Up,
            Key.Down => HostKey.Down,
            Key.PageUp => HostKey.PageUp,
            Key.PageDown => HostKey.PageDown,
            Key.Escape => HostKey.Escape,
            Key.C => HostKey.C,
            Key.V => HostKey.V,
            Key.W => HostKey.W,
            Key.A => HostKey.A,
            Key.S => HostKey.S,
            Key.D => HostKey.D,
            Key.Q => HostKey.Q,
            _ => HostKey.Unknown
        };
    }

    private static StandardCursor MapCursor(HostCursorKind kind)
    {
        return kind switch
        {
            HostCursorKind.Hand => StandardCursor.Hand,
            HostCursorKind.IBeam => StandardCursor.IBeam,
            _ => StandardCursor.Arrow
        };
    }
}

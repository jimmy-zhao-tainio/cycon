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

    public static void Run2D(CyconAppOptions options)
    {
        var window = SilkWindow.Create(options.Width, options.Height, options.Title);
        var swapchain = new SilkSwapchainGl(window);
        SilkGraphicsDeviceGl? device = null;
        RenderFrameExecutorGl? executor = null;
        var clipboard = new SilkClipboard();
        var session = ConsoleHostSession.CreateVga(options.InitialText, clipboard);

        var ctrlDown = false;
        var shiftDown = false;
        var altDown = false;
        var buttonsDown = HostMouseButtons.None;

        var pressedKeys = new HashSet<Key>();
        var repeatKeys = new Dictionary<HostKey, long>();

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
            swapchain.Present();
            window.Show();
        };

        window.FramebufferResized += (width, height) => session.OnFramebufferResized(width, height);

        window.TextInput += ch => session.OnTextInput(new HostTextInputEvent(ch));

        window.KeyDown += key =>
        {
            UpdateModifiers(ref ctrlDown, ref shiftDown, ref altDown, key, isDown: true);
            if (!pressedKeys.Add(key))
            {
                return;
            }

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
            UpdateModifiers(ref ctrlDown, ref shiftDown, ref altDown, key, isDown: false);
            pressedKeys.Remove(key);
            var mapped = MapKey(key);
            repeatKeys.Remove(mapped);
            session.OnKeyEvent(new HostKeyEvent(mapped, GetModifiers(ctrlDown, shiftDown, altDown), IsDown: false));
        };

        window.MouseDown += (x, y, button) =>
        {
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
        };

        window.MouseMoved += (x, y) =>
        {
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
        };

        window.MouseWheel += (x, y, delta) =>
        {
            session.OnMouseEvent(new HostMouseEvent(
                HostMouseEventKind.Wheel,
                x,
                y,
                buttonsDown,
                GetModifiers(ctrlDown, shiftDown, altDown),
                delta));
        };

        window.Render += _ =>
        {
            if (executor is null)
            {
                return;
            }

            if (repeatKeys.Count > 0)
            {
                var nowTicks = Stopwatch.GetTimestamp();
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
                }
            }

            var tick = session.Tick();
            if (tick.SetVSync.HasValue)
            {
                window.VSync = tick.SetVSync.Value;
            }

            executor.Resize(tick.FramebufferWidth, tick.FramebufferHeight);
            executor.Execute(tick.Frame, session.Atlas);
            swapchain.Present();

            if (tick.RequestExit)
            {
                window.Close();
            }
        };

        window.Run();
        window.Dispose();
    }

    private static bool IsRepeatable(HostKey key) =>
        key is HostKey.Backspace or HostKey.Left or HostKey.Right;

    private static long MsToTicks(int ms) =>
        (long)(ms * (Stopwatch.Frequency / 1000.0));

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
            Key.Left => HostKey.Left,
            Key.Right => HostKey.Right,
            Key.Up => HostKey.Up,
            Key.Down => HostKey.Down,
            Key.Escape => HostKey.Escape,
            Key.C => HostKey.C,
            Key.V => HostKey.V,
            _ => HostKey.Unknown
        };
    }
}

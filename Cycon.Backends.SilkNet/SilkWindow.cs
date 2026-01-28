using System;
using Cycon.Backends.Abstractions;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Cycon.Backends.SilkNet;

public sealed class SilkWindow : Cycon.Backends.Abstractions.IWindow, IDisposable
{
    private readonly Silk.NET.Windowing.IWindow _window;
    private IInputContext? _input;
    private IMouse? _primaryMouse;
    private bool _lastPointerInWindow = true;
    private int _lastMouseX;
    private int _lastMouseY;
    private float _wheelAccumYPx;
    private bool _disposed;
    private bool _resizingSnap;

    private SilkWindow(Silk.NET.Windowing.IWindow window)
    {
        _window = window;
        _window.Resize += OnResize;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += HandleClosing;
        _window.FileDrop += OnFileDrop;
        _window.FocusChanged += OnFocusChanged;
    }

    public event Action? Loaded;
    public event Action<double>? Render;
    public event Action<int, int>? FramebufferResized;
    public event Action<char>? TextInput;
    public event Action<Key>? KeyDown;
    public event Action<Key>? KeyUp;
    public event Action<int, int>? MouseMoved;
    public event Action<int, int, MouseButton>? MouseDown;
    public event Action<int, int, MouseButton>? MouseUp;
    public event Action<int, int, int>? MouseWheel;
    public event Action<string>? FileDropped;
    public event Action<bool>? FocusChanged;
    public event Action<bool>? PointerInWindowChanged;

    public int Width => _window.Size.X;
    public int Height => _window.Size.Y;
    public int FramebufferWidth => _window.FramebufferSize.X;
    public int FramebufferHeight => _window.FramebufferSize.Y;
    public bool VSync
    {
        get => _window.VSync;
        set => _window.VSync = value;
    }

    public double FramesPerSecond
    {
        get => _window.FramesPerSecond;
        set => _window.FramesPerSecond = value;
    }

    public double UpdatesPerSecond
    {
        get => _window.UpdatesPerSecond;
        set => _window.UpdatesPerSecond = value;
    }

    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    public void Show() => _window.IsVisible = true;

    public void Close() => _window.Close();

    public void SetSize(int width, int height)
    {
        _window.Size = new Vector2D<int>(width, height);
    }

    internal Silk.NET.Windowing.IWindow Native => _window;

    private StandardCursor? _lastStandardCursor;

    public static SilkWindow Create(int width, int height, string title)
    {
        var options = WindowOptions.Default;
        // Snap the initial size so the framebuffer grid starts aligned without requiring a manual resize.
        var snapWidth = SnapToStep(width, 8);
        var snapHeight = SnapToStep(height, 16);
        options.Size = new Vector2D<int>(snapWidth, snapHeight);
        options.Title = title;
        options.IsVisible = false;
        options.IsEventDriven = true;
        options.FramesPerSecond = 60;
        options.UpdatesPerSecond = 60;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        options.PreferredDepthBufferBits = 24;
        options.PreferredStencilBufferBits = 8;
        options.WindowBorder = WindowBorder.Resizable;
        options.ShouldSwapAutomatically = false;
        TrySetMsaaSamples(ref options, 4);

        var window = Window.Create(options);
        window.VSync = true;
        var wrapper = new SilkWindow(window);
        window.Load += wrapper.HandleLoad;
        window.Render += wrapper.HandleRender;
        return wrapper;
    }

    private static void TrySetMsaaSamples(ref WindowOptions options, int samples)
    {
        try
        {
            var prop = typeof(WindowOptions).GetProperty("Samples") ??
                       typeof(WindowOptions).GetProperty("SampleCount");
            if (prop is null || !prop.CanWrite)
            {
                return;
            }

            object boxed = options;
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object value = targetType == typeof(uint) ? (uint)samples :
                           targetType == typeof(int) ? samples :
                           targetType == typeof(ushort) ? (ushort)samples :
                           targetType == typeof(short) ? (short)samples :
                           targetType == typeof(byte) ? (byte)samples :
                           targetType == typeof(sbyte) ? (sbyte)samples :
                           samples;

            prop.SetValue(boxed, value);
            if (typeof(WindowOptions).IsValueType)
            {
                options = (WindowOptions)boxed;
            }
        }
        catch
        {
            // ignore
        }
    }

    public void Run() => _window.Run();

    internal void SwapBuffers() => _window.SwapBuffers();

    public void Wake() => _window.ContinueEvents();

    public void SetStandardCursor(StandardCursor cursor)
    {
        if (_lastStandardCursor == cursor)
        {
            return;
        }

        var mouse = _primaryMouse;
        if (mouse?.Cursor is null)
        {
            return;
        }

        try
        {
            mouse.Cursor.Type = CursorType.Standard;
            mouse.Cursor.StandardCursor = cursor;
            _lastStandardCursor = cursor;
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeInput();
        _window.Dispose();
    }

    private void HandleClosing()
    {
        DisposeInput();
    }

    private void DisposeInput()
    {
        var input = _input;
        if (input is null)
        {
            return;
        }

        _input = null;
        input.Dispose();
    }

    private void HandleLoad()
    {
        try
        {
            _input = _window.CreateInput();
            WireInput(_input);
        }
        catch
        {
            _input = null;
        }

        Loaded?.Invoke();
    }

    private void HandleRender(double deltaTime)
    {
        Render?.Invoke(deltaTime);
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        FramebufferResized?.Invoke(size.X, size.Y);
    }

    private void OnResize(Vector2D<int> size)
    {
        if (_resizingSnap)
        {
            return;
        }

        var targetWidth = SnapToStep(size.X, 8);
        var targetHeight = SnapToStep(size.Y, 16);
        if (targetWidth != size.X || targetHeight != size.Y)
        {
            _resizingSnap = true;
            _window.Size = new Vector2D<int>(targetWidth, targetHeight);
            _resizingSnap = false;
        }
    }

    private void OnFileDrop(string[] paths)
    {
        var handler = FileDropped;
        if (handler is null)
        {
            return;
        }

        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                handler.Invoke(path);
            }
        }
    }

    private void OnFocusChanged(bool isFocused)
    {
        FocusChanged?.Invoke(isFocused);
    }

    private void WireInput(IInputContext? input)
    {
        if (input is null)
        {
            return;
        }

        if (input.Mice.Count > 0)
        {
            _primaryMouse = input.Mice[0];

            var pos = _primaryMouse.Position;
            _lastMouseX = (int)pos.X;
            _lastMouseY = (int)pos.Y;
            UpdatePointerInWindow(_lastMouseX, _lastMouseY);
        }

        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyChar += (_, ch) =>
            {
                TextInput?.Invoke(ch);
            };
            keyboard.KeyDown += (_, key, _) =>
            {
                KeyDown?.Invoke(key);
            };
            keyboard.KeyUp += (_, key, _) =>
            {
                KeyUp?.Invoke(key);
            };
        }

        foreach (var mouse in input.Mice)
        {
            mouse.MouseMove += (_, pos) =>
            {
                _lastMouseX = (int)pos.X;
                _lastMouseY = (int)pos.Y;
                MouseMoved?.Invoke(_lastMouseX, _lastMouseY);
                UpdatePointerInWindow(_lastMouseX, _lastMouseY);
            };

            mouse.MouseDown += (_, button) =>
            {
                var pos = mouse.Position;
                _lastMouseX = (int)pos.X;
                _lastMouseY = (int)pos.Y;
                MouseDown?.Invoke(_lastMouseX, _lastMouseY, button);
                UpdatePointerInWindow(_lastMouseX, _lastMouseY);
            };

            mouse.MouseUp += (_, button) =>
            {
                var pos = mouse.Position;
                _lastMouseX = (int)pos.X;
                _lastMouseY = (int)pos.Y;
                MouseUp?.Invoke(_lastMouseX, _lastMouseY, button);
                UpdatePointerInWindow(_lastMouseX, _lastMouseY);
            };

            mouse.Scroll += (_, wheel) =>
            {
                var pos = mouse.Position;
                // Convert smooth (fractional) wheel input into pixel-precise deltas.
                // One "wheel unit" corresponds to three text rows (48px), matching classic terminal scroll speed.
                _wheelAccumYPx += wheel.Y * 48f;
                var delta = (int)MathF.Truncate(_wheelAccumYPx);
                if (delta != 0)
                {
                    _wheelAccumYPx -= delta;
                    MouseWheel?.Invoke((int)pos.X, (int)pos.Y, delta);
                }
            };
        }
    }

    private void UpdatePointerInWindow(int x, int y)
    {
        if (_primaryMouse is null)
        {
            return;
        }

        var inWindow =
            x >= 0 && y >= 0 &&
            x < _window.Size.X && y < _window.Size.Y;

        if (inWindow == _lastPointerInWindow)
        {
            return;
        }

        _lastPointerInWindow = inWindow;
        PointerInWindowChanged?.Invoke(inWindow);
    }

    private static int SnapToStep(int value, int step)
    {
        step = Math.Max(1, step);
        return Math.Max(step, (int)Math.Round(value / (double)step) * step);
    }
}

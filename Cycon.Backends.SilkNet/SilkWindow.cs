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
    private bool _disposed;

    private SilkWindow(Silk.NET.Windowing.IWindow window)
    {
        _window = window;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += HandleClosing;
        _window.FileDrop += OnFileDrop;
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

    public int Width => _window.Size.X;
    public int Height => _window.Size.Y;
    public int FramebufferWidth => _window.FramebufferSize.X;
    public int FramebufferHeight => _window.FramebufferSize.Y;
    public bool VSync
    {
        get => _window.VSync;
        set => _window.VSync = value;
    }

    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    public void Show() => _window.IsVisible = true;

    public void Close() => _window.Close();

    internal Silk.NET.Windowing.IWindow Native => _window;

    public static SilkWindow Create(int width, int height, string title)
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(width, height);
        options.Title = title;
        options.IsVisible = false;
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

    private void HandleRender(double deltaTime) => Render?.Invoke(deltaTime);

    private void OnFramebufferResize(Vector2D<int> size)
    {
        FramebufferResized?.Invoke(size.X, size.Y);
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

    private void WireInput(IInputContext? input)
    {
        if (input is null)
        {
            return;
        }

        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyChar += (_, ch) => TextInput?.Invoke(ch);
            keyboard.KeyDown += (_, key, _) => KeyDown?.Invoke(key);
            keyboard.KeyUp += (_, key, _) => KeyUp?.Invoke(key);
        }

        foreach (var mouse in input.Mice)
        {
            mouse.MouseDown += (_, button) =>
            {
                var pos = mouse.Position;
                MouseDown?.Invoke((int)pos.X, (int)pos.Y, button);
            };

            mouse.MouseUp += (_, button) =>
            {
                var pos = mouse.Position;
                MouseUp?.Invoke((int)pos.X, (int)pos.Y, button);
            };

            mouse.MouseMove += (_, pos) => MouseMoved?.Invoke((int)pos.X, (int)pos.Y);
            mouse.Scroll += (_, wheel) =>
            {
                var pos = mouse.Position;
                var delta = (int)MathF.Round(wheel.Y);
                MouseWheel?.Invoke((int)pos.X, (int)pos.Y, delta);
            };
        }
    }
}

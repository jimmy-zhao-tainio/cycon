using System;
using Cycon.Backends.Abstractions;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Cycon.Backends.SilkNet;

public sealed class SilkWindow : Cycon.Backends.Abstractions.IWindow
{
    private readonly Silk.NET.Windowing.IWindow _window;

    private SilkWindow(Silk.NET.Windowing.IWindow window)
    {
        _window = window;
        _window.FramebufferResize += OnFramebufferResize;
    }

    public event Action? Loaded;
    public event Action<double>? Render;
    public event Action<int, int>? FramebufferResized;

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

    internal Silk.NET.Windowing.IWindow Native => _window;

    public static SilkWindow Create(int width, int height, string title)
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(width, height);
        options.Title = title;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        options.WindowBorder = WindowBorder.Resizable;
        options.ShouldSwapAutomatically = false;

        var window = Window.Create(options);
        window.VSync = true;
        var wrapper = new SilkWindow(window);
        window.Load += wrapper.HandleLoad;
        window.Render += wrapper.HandleRender;
        return wrapper;
    }

    public void Run() => _window.Run();

    internal void SwapBuffers() => _window.SwapBuffers();

    private void HandleLoad() => Loaded?.Invoke();

    private void HandleRender(double deltaTime) => Render?.Invoke(deltaTime);

    private void OnFramebufferResize(Vector2D<int> size)
    {
        FramebufferResized?.Invoke(size.X, size.Y);
    }
}

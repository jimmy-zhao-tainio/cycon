using Cycon.Backends.Abstractions;

namespace Cycon.Backends.SilkNet;

public sealed class SilkSwapchainGl : ISwapchain
{
    private readonly SilkWindow _window;

    public SilkSwapchainGl(SilkWindow window)
    {
        _window = window;
    }

    public void Present()
    {
        _window.SwapBuffers();
    }
}

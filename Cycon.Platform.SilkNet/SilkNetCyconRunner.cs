using Cycon.Backends.SilkNet;
using Cycon.Backends.SilkNet.Execution;
using Cycon.Host.Hosting;
using Silk.NET.Input;

namespace Cycon.Platform.SilkNet;

public static class SilkNetCyconRunner
{
    public static void Run2D(CyconAppOptions options)
    {
        var window = SilkWindow.Create(options.Width, options.Height, options.Title);
        var swapchain = new SilkSwapchainGl(window);
        SilkGraphicsDeviceGl? device = null;
        RenderFrameExecutorGl? executor = null;
        var session = ConsoleHostSession.CreateVga(options.InitialText);

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

        window.TextInput += session.OnTextInput;
        window.KeyDown += key =>
        {
            switch (key)
            {
                case Key.Backspace:
                    session.OnBackspace();
                    break;
                case Key.Enter:
                case Key.KeypadEnter:
                    session.OnEnter();
                    break;
                case Key.Left:
                    session.OnMoveCaretLeft();
                    break;
                case Key.Right:
                    session.OnMoveCaretRight();
                    break;
            }
        };

        window.Render += _ =>
        {
            if (executor is null)
            {
                return;
            }

            var tick = session.Tick();
            if (tick.SetVSync.HasValue)
            {
                window.VSync = tick.SetVSync.Value;
            }

            executor.Resize(tick.FramebufferWidth, tick.FramebufferHeight);
            executor.Execute(tick.Frame, session.Atlas);
            swapchain.Present();
        };

        window.Run();
        window.Dispose();
    }
}

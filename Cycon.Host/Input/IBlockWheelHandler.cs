using Cycon.Layout.Scrolling;

namespace Cycon.Host.Input;

public interface IBlockWheelHandler
{
    /// <summary>
    /// Handles wheel events in screen (framebuffer) pixel coordinates.
    /// </summary>
    bool HandleWheel(in HostMouseEvent e, in PxRect viewportRectPx);
}


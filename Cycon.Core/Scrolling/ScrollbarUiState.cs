namespace Cycon.Core.Scrolling;

public sealed class ScrollbarUiState
{
    public float Visibility { get; set; }
    public bool IsHovering { get; set; }
    public bool IsDragging { get; set; }

    public int MsSinceInteraction { get; set; }

    public int DragGrabOffsetYPx { get; set; }
}


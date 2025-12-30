namespace Cycon.Rendering.Styling;

public readonly record struct SelectionStyle(int SelectedForegroundRgba, int? SelectedBackgroundRgba = null)
{
    public static SelectionStyle Default => new(unchecked((int)0x000000FF), unchecked((int)0xEEEEEEFF));
}

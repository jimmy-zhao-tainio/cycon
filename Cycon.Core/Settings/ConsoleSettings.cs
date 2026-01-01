using Cycon.Core.Styling;
using Cycon.Core.Scrolling;

namespace Cycon.Core.Settings;

public sealed class ConsoleSettings
{
    public int MaxHistoryEntries { get; set; } = 1024;

    public ScrollbarSettings Scrollbar { get; init; } = new();

    public ActivityIndicatorSettings Indicators { get; init; } = new();

    public Scene3DSettings Scene3D { get; init; } = new();

    public TextStyle DefaultTextStyle { get; init; } = new()
    {
        ForegroundRgba = unchecked((int)0xEEEEEEFF),
        BackgroundRgba = unchecked((int)0x000000FF)
    };

    public TextStyle StdoutTextStyle { get; init; } = new()
    {
        ForegroundRgba = unchecked((int)0xEEEEEEFF),
        BackgroundRgba = unchecked((int)0x000000FF)
    };

    public TextStyle StderrTextStyle { get; init; } = new()
    {
        ForegroundRgba = unchecked((int)0xFF7777FF),
        BackgroundRgba = unchecked((int)0x000000FF)
    };

    public TextStyle SystemTextStyle { get; init; } = new()
    {
        ForegroundRgba = unchecked((int)0xEEEEEEFF),
        BackgroundRgba = unchecked((int)0x000000FF)
    };
}

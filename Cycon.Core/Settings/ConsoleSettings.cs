using Cycon.Core.Styling;

namespace Cycon.Core.Settings;

public sealed class ConsoleSettings
{
    public int MaxHistoryEntries { get; set; } = 1024;

    public TextStyle DefaultTextStyle { get; init; } = new()
    {
        ForegroundRgba = unchecked((int)0xEEEEEEFF),
        BackgroundRgba = unchecked((int)0x000000FF)
    };
}

namespace Cycon.App;

using Cycon.BlockCommands;

public sealed class CyconAppOptions
{
    public string Title { get; set; } = "Cycon";
    public int Width { get; set; } = 960;
    public int Height { get; set; } = 540;
    public string InitialText { get; set; } = string.Empty;

    public Action<BlockCommandRegistry>? ConfigureBlockCommands { get; set; }
}

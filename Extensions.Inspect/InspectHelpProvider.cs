using Cycon.BlockCommands;

namespace Extensions.Inspect;

public sealed class InspectHelpProvider : IHelpProvider
{
    public string Name => "inspect";
    public string Summary => "Inspect files and render supported formats.";

    public void PrintHelp(IConsole console)
    {
        console.WriteLine("inspect <path>");
        console.WriteLine(string.Empty);

        console.WriteLine("Supported formats");
        console.WriteLine("  .stl    Render a 3D view panel");
        console.WriteLine("  other   Show basic file metadata");
        console.WriteLine(string.Empty);

        console.WriteLine("Tip");
        console.WriteLine("  Drag & drop a file into the window");
    }
}

namespace Cycon.BlockCommands;

public interface IHelpProvider
{
    string Name { get; }
    string Summary { get; }
    void PrintHelp(IConsole console);
}


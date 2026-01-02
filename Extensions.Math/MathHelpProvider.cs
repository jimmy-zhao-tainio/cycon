using Cycon.BlockCommands;

namespace Extensions.Math;

public sealed class MathHelpProvider : IHelpProvider
{
    public string Name => "math";
    public string Summary => "Inline math expressions and variables.";

    public void PrintHelp(IConsole console)
    {
        console.WriteLine("Math (inline)");
        console.WriteLine(string.Empty);

        console.WriteLine("Usage");
        console.WriteLine("  1 + 1");
        console.WriteLine("  x = 123");
        console.WriteLine("  x * x");
        console.WriteLine(string.Empty);

        console.WriteLine("Notes");
        console.WriteLine("  Variables are case-insensitive");
        console.WriteLine("  ans stores the last result");
        console.WriteLine(string.Empty);

        console.WriteLine("Operators");
        console.WriteLine("  +  -  *  /  %  ^");
        console.WriteLine("  ^ is right-associative");
        console.WriteLine(string.Empty);

        console.WriteLine("Constants");
        console.WriteLine("  pi  e");
        console.WriteLine(string.Empty);

        console.WriteLine("Functions");
        console.WriteLine("  sin cos tan asin acos atan atan2");
        console.WriteLine("  sqrt abs min max floor ceil round");
        console.WriteLine("  log ln exp pow");
    }
}

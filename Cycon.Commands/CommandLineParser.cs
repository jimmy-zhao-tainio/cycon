using System.Collections.Generic;

namespace Cycon.Commands;

public static class CommandLineParser
{
    public static IReadOnlyList<string> Split(string commandLine)
    {
        return string.IsNullOrWhiteSpace(commandLine)
            ? Array.Empty<string>()
            : commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

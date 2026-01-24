using System;

namespace Cycon.Host.Commands;

internal static class CommandLineQuote
{
    public static string Quote(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}

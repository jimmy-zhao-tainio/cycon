using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Host.Commands;

namespace Cycon.Host.Commands.Blocks;

public sealed class ViewFallbackBlockCommandHandler : IBlockCommandHandler
{
    private const int MaxChars = 20000;

    public CommandSpec Spec { get; } = new(
        Name: "view",
        Summary: "Opens a file in a viewer when available (native fallback).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is not IFileCommandContext fs)
        {
            ctx.InsertTextAfterCommandEcho("File system support is unavailable.", ConsoleTextStream.System);
            return true;
        }

        if (request.Args.Count == 0)
        {
            ctx.InsertTextAfterCommandEcho("Usage: view <path>", ConsoleTextStream.System);
            return true;
        }

        var rawPath = request.Args.Count == 1 ? request.Args[0] : string.Join(" ", request.Args);

        string fullPath;
        try
        {
            fullPath = fs.ResolvePath(rawPath);
        }
        catch (Exception ex)
        {
            ctx.InsertTextAfterCommandEcho($"Invalid path: {ex.Message}", ConsoleTextStream.System);
            return true;
        }

        if (!fs.FileSystem.FileExists(fullPath))
        {
            ctx.InsertTextAfterCommandEcho($"File not found: {fullPath}", ConsoleTextStream.System);
            return true;
        }

        try
        {
            var text = fs.FileSystem.ReadAllText(fullPath);
            if (text.Length > MaxChars)
            {
                ctx.InsertTextAfterCommandEcho(text.Substring(0, MaxChars) + "\n...\n", ConsoleTextStream.Stdout);
                ctx.InsertTextAfterCommandEcho($"(truncated to {MaxChars} chars; use: cat {CommandLineQuote.Quote(fullPath)})", ConsoleTextStream.System);
            }
            else
            {
                ctx.InsertTextAfterCommandEcho(text, ConsoleTextStream.Stdout);
            }
        }
        catch (Exception ex)
        {
            ctx.InsertTextAfterCommandEcho($"Read failed: {ex.Message}", ConsoleTextStream.System);
        }

        return true;
    }
}

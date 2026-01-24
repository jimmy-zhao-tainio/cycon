using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;

namespace Cycon.Host.Commands.Blocks;

public sealed class CdBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "cd",
        Summary: "Changes the current directory (native block).",
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
            ctx.InsertTextAfterCommandEcho(fs.CurrentDirectory, ConsoleTextStream.Stdout);
            return true;
        }

        var rawPath = request.Args.Count == 1 ? request.Args[0] : string.Join(" ", request.Args);
        if (!fs.TrySetCurrentDirectory(rawPath, out var error))
        {
            ctx.InsertTextAfterCommandEcho(error, ConsoleTextStream.System);
        }

        return true;
    }
}


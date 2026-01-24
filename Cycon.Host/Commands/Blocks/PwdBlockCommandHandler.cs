using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;

namespace Cycon.Host.Commands.Blocks;

public sealed class PwdBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "pwd",
        Summary: "Prints the current directory (native block).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is not IFileCommandContext fs)
        {
            ctx.InsertTextAfterCommandEcho("File system support is unavailable.", ConsoleTextStream.System);
            return true;
        }

        ctx.InsertTextAfterCommandEcho(fs.CurrentDirectory, ConsoleTextStream.Stdout);
        return true;
    }
}


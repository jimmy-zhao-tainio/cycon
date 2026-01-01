using System;
using Cycon.BlockCommands;
using Cycon.Commands;

namespace Cycon.Host.Commands.Blocks;

public sealed class ExitBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "exit",
        Summary: "Closes the app.",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (request.Args.Count != 0)
        {
            return false;
        }

        ctx.RequestExit();
        return true;
    }
}

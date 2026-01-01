using System;
using Cycon.BlockCommands;
using Cycon.Commands;

namespace Cycon.Host.Commands.Blocks;

public sealed class ClearBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "clear",
        Summary: "Clears the transcript.",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (request.Args.Count != 0)
        {
            return false;
        }

        ctx.ClearTranscript();
        return true;
    }
}

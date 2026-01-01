using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;

namespace Cycon.Host.Commands.Blocks;

public sealed class EchoBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "echo",
        Summary: "Prints text (native block).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var text = request.Args.Count == 0 ? string.Empty : string.Join(" ", request.Args);
        ctx.InsertTextAfterCommandEcho(text, ConsoleTextStream.Stdout);
        return true;
    }
}

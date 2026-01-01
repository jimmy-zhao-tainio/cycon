using System;
using Cycon.BlockCommands;
using Cycon.Commands;

namespace Cycon.Host.Commands.Blocks;

public sealed class AskBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "ask",
        Summary: "Prompts for input (native block).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.Interactive);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var prompt = request.Args.Count > 0 ? string.Join(" ", request.Args) + " " : "Input: ";
        ctx.AppendOwnedPrompt(prompt);
        return true;
    }
}

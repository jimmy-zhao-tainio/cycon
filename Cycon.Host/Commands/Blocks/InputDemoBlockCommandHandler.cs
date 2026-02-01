using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;

namespace Cycon.Host.Commands.Blocks;

public sealed class InputDemoBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "inputdemo",
        Summary: "Shows a modal overlay text input demo.",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.Interactive);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is not BlockCommandContext hostCtx)
        {
            ctx.InsertTextAfterCommandEcho("Overlay is unavailable.", ConsoleTextStream.System);
            return true;
        }

        hostCtx.ShowInputDemoOverlay();
        return true;
    }
}


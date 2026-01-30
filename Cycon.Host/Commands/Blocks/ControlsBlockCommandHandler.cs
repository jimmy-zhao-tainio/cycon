using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Host.Commands;

namespace Cycon.Host.Commands.Blocks;

public sealed class ControlsBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "controls",
        Summary: "Shows the help/controls overlay slab.",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is not IOverlayCommandContext overlay)
        {
            ctx.InsertTextAfterCommandEcho("Overlay support is unavailable.", ConsoleTextStream.System);
            return true;
        }

        overlay.ShowHelpControlsOverlay();
        return true;
    }
}


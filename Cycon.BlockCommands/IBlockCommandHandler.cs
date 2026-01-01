using Cycon.Commands;

namespace Cycon.BlockCommands;

public interface IBlockCommandHandler
{
    CommandSpec Spec { get; }
    bool TryExecute(CommandRequest request, IBlockCommandContext ctx);
}


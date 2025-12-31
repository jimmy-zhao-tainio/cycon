using Cycon.Commands;

namespace Cycon.Host.Commands.Blocks;

public interface IBlockCommandHandler
{
    CommandSpec Spec { get; }
    bool TryExecute(CommandRequest request, IBlockCommandContext ctx);
}


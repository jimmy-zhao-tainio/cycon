using Cycon.Core.Transcript;

namespace Cycon.Host.Commands;

internal interface ICommandHostView
{
    bool TryGetPromptSnapshot(BlockId promptId, out PromptSnapshot prompt);
    PromptSnapshot? GetLastPromptSnapshot();
    BlockId AllocateBlockId();
}

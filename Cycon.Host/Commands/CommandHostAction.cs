using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Commands;

namespace Cycon.Host.Commands;

internal abstract record CommandHostAction
{
    public sealed record InsertBlockBefore(BlockId BeforeId, IBlock Block) : CommandHostAction;

    public sealed record InsertBlockAfter(BlockId AfterId, IBlock Block) : CommandHostAction;

    public sealed record UpdatePrompt(BlockId PromptId, string Input, int CaretIndex) : CommandHostAction;

    public sealed record SubmitParsedCommand(
        CommandRequest Request,
        string CommandForParse,
        BlockId HeaderId,
        BlockId ShellPromptId) : CommandHostAction;

    public sealed record RequestContentRebuild : CommandHostAction;

    public sealed record SetFollowingTail(bool Enabled) : CommandHostAction;
}

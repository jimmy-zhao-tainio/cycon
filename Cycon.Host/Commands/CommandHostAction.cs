using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Commands;

namespace Cycon.Host.Commands;

internal abstract record CommandHostAction
{
    public sealed record InsertTextBlockBefore(
        BlockId BeforeId,
        BlockId NewId,
        string Text,
        ConsoleTextStream Stream) : CommandHostAction;

    public sealed record InsertTextBlockAfter(
        BlockId AfterId,
        BlockId NewId,
        string Text,
        ConsoleTextStream Stream) : CommandHostAction;

    public sealed record UpdatePrompt(BlockId PromptId, string Input, int CaretIndex) : CommandHostAction;

    public sealed record SubmitParsedCommand(
        CommandRequest Request,
        string CommandForParse,
        string RawCommand,
        string HeaderText,
        BlockId HeaderId,
        BlockId ShellPromptId) : CommandHostAction;

    public sealed record RequestContentRebuild : CommandHostAction;

    public sealed record SetFollowingTail(bool Enabled) : CommandHostAction;
}

using Cycon.Core.Transcript;
using Cycon.Host.Input;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Interaction;

public abstract record HostAction
{
    public sealed record Focus(BlockId BlockId) : HostAction;

    public sealed record ClearSelection() : HostAction;

    public sealed record SetMouseCapture(BlockId? BlockId) : HostAction;

    public sealed record InsertText(BlockId PromptId, string Text) : HostAction;

    public sealed record Backspace(BlockId PromptId) : HostAction;

    public sealed record MoveCaret(BlockId PromptId, int Delta) : HostAction;

    public sealed record SetCaret(BlockId PromptId, int Index) : HostAction;

    public sealed record SubmitPrompt(BlockId PromptId) : HostAction;

    public sealed record NavigateHistory(BlockId PromptId, int Delta) : HostAction;

    public sealed record CopySelectionToClipboard() : HostAction;

    public sealed record PasteFromClipboardIntoLastPrompt() : HostAction;

    public sealed record CancelFocusedJob() : HostAction;

    public sealed record CancelFocusedJobWithLevel(CancelLevel Level) : HostAction;

    public sealed record RequestRebuild() : HostAction;
}

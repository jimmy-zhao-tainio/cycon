using System.Threading;
using Cycon.Core;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands;

internal sealed class PromptLifecycle
{
    private readonly ConsoleDocument _document;
    private readonly string _defaultPromptText;
    private long _nextOwnedPromptId;
    private readonly Dictionary<BlockId, JobPromptRef> _jobPromptRefs = new();
    private readonly Dictionary<JobId, BlockId> _activeJobPromptBlocks = new();
    private readonly Dictionary<BlockId, OwnedPromptRef> _ownedPromptRefs = new();
    private bool _pendingShellPrompt;

    public PromptLifecycle(ConsoleDocument document, string defaultPromptText)
    {
        _document = document;
        _defaultPromptText = defaultPromptText;
    }

    public bool HasOwnedPrompts => _ownedPromptRefs.Count > 0;

    public bool HasActiveJobPrompts => _activeJobPromptBlocks.Count > 0;

    public bool PendingShellPrompt
    {
        get => _pendingShellPrompt;
        set => _pendingShellPrompt = value;
    }

    public bool TryGetOwnedPrompt(BlockId promptId, out OwnedPromptRef ownedPrompt) =>
        _ownedPromptRefs.TryGetValue(promptId, out ownedPrompt);

    public bool TryGetJobPrompt(BlockId promptId, out JobPromptRef jobPrompt) =>
        _jobPromptRefs.TryGetValue(promptId, out jobPrompt);

    public void RemoveOwnedPrompt(BlockId promptId) => _ownedPromptRefs.Remove(promptId);

    public void RemoveJobPrompt(BlockId promptId) => _jobPromptRefs.Remove(promptId);

    public bool TryGetActiveJobPrompt(JobId jobId, out BlockId promptId) =>
        _activeJobPromptBlocks.TryGetValue(jobId, out promptId);

    public void RemoveActiveJobPrompt(JobId jobId) => _activeJobPromptBlocks.Remove(jobId);

    public void AppendOwnedPrompt(
        string promptText,
        Func<int> allocateBlockId,
        Action<int> removeBlockAt,
        Action<int, IBlock> replaceBlockAt)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var lastIndex = blocks.Count - 1;
        if (blocks[lastIndex] is PromptBlock existingShellPrompt && existingShellPrompt.Owner is null)
        {
            if (string.IsNullOrEmpty(existingShellPrompt.Input))
            {
                removeBlockAt(lastIndex);
            }
            else
            {
                var archivedText = existingShellPrompt.Prompt + existingShellPrompt.Input;
                replaceBlockAt(lastIndex, new TextBlock(existingShellPrompt.Id, archivedText, ConsoleTextStream.Default));
            }
        }

        var promptId = Interlocked.Increment(ref _nextOwnedPromptId);
        var promptBlockId = new BlockId(allocateBlockId());
        var block = new PromptBlock(promptBlockId, promptText, new PromptOwner(OwnerId: 1, PromptId: promptId))
        {
            Input = string.Empty,
            CaretIndex = 0
        };

        _document.Transcript.Add(block);
        _ownedPromptRefs[promptBlockId] = new OwnedPromptRef(promptId);
        _pendingShellPrompt = true;
    }

    public void AppendJobPrompt(JobId jobId, string promptText, Func<int> allocateBlockId)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var lastIndex = blocks.Count - 1;
        if (blocks[lastIndex] is PromptBlock existingShellPrompt && existingShellPrompt.Owner is null)
        {
            if (string.IsNullOrEmpty(existingShellPrompt.Input))
            {
                _document.Transcript.RemoveAt(lastIndex);
            }
            else
            {
                var archivedText = existingShellPrompt.Prompt + existingShellPrompt.Input;
                var archived = new TextBlock(existingShellPrompt.Id, archivedText, ConsoleTextStream.Default);
                _document.Transcript.ReplaceAt(lastIndex, archived);
            }
        }

        var promptId = Interlocked.Increment(ref _nextOwnedPromptId);
        var promptBlockId = new BlockId(allocateBlockId());
        var block = new PromptBlock(promptBlockId, promptText, new PromptOwner(jobId.Value, promptId))
        {
            Input = string.Empty,
            CaretIndex = 0
        };

        _document.Transcript.Add(block);
        _jobPromptRefs[promptBlockId] = new JobPromptRef(jobId, promptId);
        _activeJobPromptBlocks[jobId] = promptBlockId;
        _pendingShellPrompt = true;
    }

    public void FinalizeActiveJobPromptIfAny(JobId jobId, Func<BlockId, PromptBlock?> getPrompt, Action<BlockId, string, ConsoleTextStream> replacePromptWithArchivedText)
    {
        if (!_activeJobPromptBlocks.TryGetValue(jobId, out var promptBlockId))
        {
            return;
        }

        var prompt = getPrompt(promptBlockId);
        if (prompt is null)
        {
            _activeJobPromptBlocks.Remove(jobId);
            _jobPromptRefs.Remove(promptBlockId);
            return;
        }

        replacePromptWithArchivedText(promptBlockId, prompt.Prompt + prompt.Input, ConsoleTextStream.Default);
        _activeJobPromptBlocks.Remove(jobId);
        _jobPromptRefs.Remove(promptBlockId);
    }

    public void EnsureShellPromptAtEndIfNeeded(Func<int> allocateBlockId)
    {
        if (_ownedPromptRefs.Count > 0 || _activeJobPromptBlocks.Count > 0)
        {
            return;
        }

        var blocks = _document.Transcript.Blocks;
        if (blocks.Count > 0 && blocks[^1] is PromptBlock prompt && prompt.Owner is null)
        {
            _pendingShellPrompt = false;
            return;
        }

        if (!_pendingShellPrompt && blocks.Count > 0)
        {
            return;
        }

        _document.Transcript.Add(new PromptBlock(new BlockId(allocateBlockId()), _defaultPromptText));
        _pendingShellPrompt = false;
    }

    public void RemoveShellPromptIfPresent(BlockId promptId, Action<int> removeBlockAt)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var lastIndex = blocks.Count - 1;
        if (blocks[lastIndex] is PromptBlock prompt && prompt.Id == promptId && prompt.Owner is null)
        {
            removeBlockAt(lastIndex);
        }
    }

    public void Clear()
    {
        _jobPromptRefs.Clear();
        _activeJobPromptBlocks.Clear();
        _ownedPromptRefs.Clear();
        _pendingShellPrompt = false;
    }

    internal readonly record struct JobPromptRef(JobId JobId, long PromptId);
    internal readonly record struct OwnedPromptRef(long PromptId);
}

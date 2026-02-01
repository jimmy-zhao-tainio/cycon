using System;
using System.Collections.Generic;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands;

internal sealed class JobEventApplier
{
    private readonly JobProjectionService _projection;
    private readonly PromptLifecycle _prompts;
    private readonly Func<int> _allocateBlockId;
    private readonly Func<BlockId, PromptBlock?> _getPrompt;
    private readonly Action<BlockId, string, ConsoleTextStream> _replacePrompt;
    private readonly Action<BlockId, string, ConsoleTextStream> _replaceTextBlock;
    private readonly Func<JobId, bool> _isForegroundJob;
    private readonly Action<BlockId, bool> _setStatusCaretEnabled;
    private readonly Action _ensureShellPrompt;
    private readonly Dictionary<BlockId, BlockTargetTextState> _blockTargetText = new();

    public JobEventApplier(
        JobProjectionService projection,
        PromptLifecycle prompts,
        Func<int> allocateBlockId,
        Func<BlockId, PromptBlock?> getPrompt,
        Action<BlockId, string, ConsoleTextStream> replacePrompt,
        Action<BlockId, string, ConsoleTextStream> replaceTextBlock,
        Func<JobId, bool> isForegroundJob,
        Action<BlockId, bool> setStatusCaretEnabled,
        Action ensureShellPrompt)
    {
        _projection = projection;
        _prompts = prompts;
        _allocateBlockId = allocateBlockId;
        _getPrompt = getPrompt;
        _replacePrompt = replacePrompt;
        _replaceTextBlock = replaceTextBlock;
        _isForegroundJob = isForegroundJob;
        _setStatusCaretEnabled = setStatusCaretEnabled;
        _ensureShellPrompt = ensureShellPrompt;
    }

    public bool Apply(JobScheduler.PublishedEvent published)
    {
        var jobId = published.JobId;
        var e = published.Event;
        switch (e)
        {
            case TextEvent text:
                _projection.AppendText(jobId, text.Stream, text.Text);
                return true;
            case ProgressEvent progress:
                var pct = progress.Fraction is { } f ? $"{Math.Round(f * 100.0)}%" : string.Empty;
                var phase = string.IsNullOrWhiteSpace(progress.Phase) ? "Progress" : progress.Phase;
                _projection.AppendText(jobId, TextStream.System, $"{phase} {pct}".Trim());
                return true;
            case PromptEvent prompt:
                _prompts.AppendJobPrompt(jobId, prompt.Prompt, _allocateBlockId);
                return true;
            case ResultEvent result:
                if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.Summary))
                {
                    var summary = string.IsNullOrWhiteSpace(result.Summary) ? string.Empty : $": {result.Summary}";
                    _projection.AppendText(jobId, TextStream.System, $"(exit {result.ExitCode}){summary}");
                }

                _prompts.FinalizeActiveJobPromptIfAny(jobId, _getPrompt, _replacePrompt);
                _projection.ClearForJob(jobId);
                _prompts.PendingShellPrompt = true;
                _ensureShellPrompt();
                return true;
            case BlockTargetStatusEvent status:
                return ApplyBlockTargetStatus(jobId, new BlockId(status.TargetBlockId), status.Status);
            case BlockTargetTextDeltaEvent delta:
                return ApplyBlockTargetDelta(jobId, new BlockId(delta.TargetBlockId), delta.Delta);
            case BlockTargetCompletedEvent completed:
                return ApplyBlockTargetCompleted(jobId, new BlockId(completed.TargetBlockId));
            case BlockTargetCancelledEvent cancelled:
                return ApplyBlockTargetCancelled(jobId, new BlockId(cancelled.TargetBlockId));
            case BlockTargetErrorEvent error:
                return ApplyBlockTargetError(jobId, new BlockId(error.TargetBlockId), error.Message);
            default:
                return false;
        }
    }

    private bool ApplyBlockTargetStatus(JobId jobId, BlockId id, string status)
    {
        if (_isForegroundJob(jobId))
        {
            _setStatusCaretEnabled(id, true);
        }

        if (!_blockTargetText.TryGetValue(id, out var state))
        {
            state = new BlockTargetTextState();
            _blockTargetText[id] = state;
        }

        state.Status = status ?? string.Empty;
        state.Error = string.Empty;
        _replaceTextBlock(id, state.ComposeText(), ConsoleTextStream.Default);
        return true;
    }

    private bool ApplyBlockTargetDelta(JobId jobId, BlockId id, string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return true;
        }

        if (_isForegroundJob(jobId))
        {
            _setStatusCaretEnabled(id, true);
        }

        if (!_blockTargetText.TryGetValue(id, out var state))
        {
            state = new BlockTargetTextState();
            _blockTargetText[id] = state;
        }

        state.Body.Append(delta);
        _replaceTextBlock(id, state.ComposeText(), ConsoleTextStream.Default);
        return true;
    }

    private bool ApplyBlockTargetCompleted(JobId jobId, BlockId id)
    {
        if (_isForegroundJob(jobId))
        {
            _setStatusCaretEnabled(id, false);
        }

        if (_blockTargetText.TryGetValue(id, out var state))
        {
            state.Status = string.Empty;
            _replaceTextBlock(id, state.ComposeText(), ConsoleTextStream.Default);
            _blockTargetText.Remove(id);
            return true;
        }

        return false;
    }

    private bool ApplyBlockTargetCancelled(JobId jobId, BlockId id)
    {
        if (_isForegroundJob(jobId))
        {
            _setStatusCaretEnabled(id, false);
        }

        if (_blockTargetText.TryGetValue(id, out var state))
        {
            state.Status = "Cancelled.";
            _replaceTextBlock(id, state.ComposeText(), ConsoleTextStream.Default);
            _blockTargetText.Remove(id);
            return true;
        }

        _replaceTextBlock(id, "Cancelled.", ConsoleTextStream.Default);
        return true;
    }

    private bool ApplyBlockTargetError(JobId jobId, BlockId id, string message)
    {
        if (_isForegroundJob(jobId))
        {
            _setStatusCaretEnabled(id, false);
        }

        if (!_blockTargetText.TryGetValue(id, out var state))
        {
            state = new BlockTargetTextState();
            _blockTargetText[id] = state;
        }

        state.Error = message ?? string.Empty;
        state.Status = string.Empty;
        _replaceTextBlock(id, state.ComposeText(), ConsoleTextStream.Default);
        _blockTargetText.Remove(id);
        return true;
    }

    private sealed class BlockTargetTextState
    {
        public string Status = string.Empty;
        public readonly System.Text.StringBuilder Body = new();
        public string Error = string.Empty;

        public string ComposeText()
        {
            if (!string.IsNullOrEmpty(Error))
            {
                return Body.Length == 0
                    ? $"Error: {Error}"
                    : $"Error: {Error}\n{Body}";
            }

            if (!string.IsNullOrEmpty(Status))
            {
                return Body.Length == 0
                    ? Status
                    : $"{Status}\n{Body}";
            }

            return Body.ToString();
        }
    }
}

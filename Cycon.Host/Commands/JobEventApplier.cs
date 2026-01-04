using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;
using Cycon.Host.Commands;

namespace Cycon.Host.Commands;

internal sealed class JobEventApplier
{
    private readonly JobProjectionService _projection;
    private readonly PromptLifecycle _prompts;
    private readonly Func<int> _allocateBlockId;
    private readonly Func<BlockId, PromptBlock?> _getPrompt;
    private readonly Action<BlockId, string, ConsoleTextStream> _replacePrompt;
    private readonly Action _ensureShellPrompt;

    public JobEventApplier(
        JobProjectionService projection,
        PromptLifecycle prompts,
        Func<int> allocateBlockId,
        Func<BlockId, PromptBlock?> getPrompt,
        Action<BlockId, string, ConsoleTextStream> replacePrompt,
        Action ensureShellPrompt)
    {
        _projection = projection;
        _prompts = prompts;
        _allocateBlockId = allocateBlockId;
        _getPrompt = getPrompt;
        _replacePrompt = replacePrompt;
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
            default:
                return false;
        }
    }
}

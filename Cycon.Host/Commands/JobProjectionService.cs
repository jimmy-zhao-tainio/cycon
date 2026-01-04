using Cycon.Core;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands;

internal sealed class JobProjectionService
{
    private readonly ConsoleDocument _document;
    private readonly Func<int> _allocateBlockId;
    private readonly Action<BlockId, IBlock> _insertBlockAfter;
    private readonly Action<BlockId, string, ConsoleTextStream> _replaceTextBlock;
    private readonly Dictionary<JobId, JobProjection> _jobProjections = new();
    private readonly Dictionary<(JobId JobId, TextStream Stream), ChunkAccumulator> _chunkAccumulators = new();

    public JobProjectionService(
        ConsoleDocument document,
        Func<int> allocateBlockId,
        Action<BlockId, IBlock> insertBlockAfter,
        Action<BlockId, string, ConsoleTextStream> replaceTextBlock)
    {
        _document = document;
        _allocateBlockId = allocateBlockId;
        _insertBlockAfter = insertBlockAfter;
        _replaceTextBlock = replaceTextBlock;
    }

    public void EnsureJobProjection(JobId jobId)
    {
        if (_jobProjections.ContainsKey(jobId))
        {
            return;
        }

        var headerId = new BlockId(_allocateBlockId());
        var insertIndex = Math.Max(0, _document.Transcript.Blocks.Count - 1);
        _document.Transcript.Insert(insertIndex, new TextBlock(headerId, $"$ [job {jobId}]"));
        _jobProjections[jobId] = new JobProjection(headerId, headerId);
    }

    public void AppendText(JobId jobId, TextStream stream, string text)
    {
        if (!_jobProjections.TryGetValue(jobId, out var proj))
        {
            EnsureJobProjection(jobId);
            proj = _jobProjections[jobId];
        }

        var mappedStream = MapTextStream(stream);

        if (stream == TextStream.System)
        {
            foreach (var line in SplitLines(text ?? string.Empty))
            {
                var id = new BlockId(_allocateBlockId());
                _insertBlockAfter(proj.LastId, new TextBlock(id, line, mappedStream));
                proj = proj with { LastId = id };
            }

            _jobProjections[jobId] = proj;
            return;
        }

        AppendChunkedJobText(jobId, stream, text ?? string.Empty, mappedStream, proj);
    }

    public void ClearForJob(JobId jobId)
    {
        if (_chunkAccumulators.Count == 0)
        {
            return;
        }

        List<(JobId, TextStream)>? toRemove = null;
        foreach (var key in _chunkAccumulators.Keys)
        {
            if (key.JobId == jobId)
            {
                toRemove ??= new List<(JobId, TextStream)>();
                toRemove.Add(key);
            }
        }

        if (toRemove is null)
        {
            return;
        }

        foreach (var key in toRemove)
        {
            _chunkAccumulators.Remove(key);
        }
    }

    public void Clear()
    {
        _jobProjections.Clear();
        _chunkAccumulators.Clear();
    }

    private void AppendChunkedJobText(
        JobId jobId,
        TextStream stream,
        string text,
        ConsoleTextStream mappedStream,
        JobProjection proj)
    {
        var key = (jobId, stream);
        if (!_chunkAccumulators.TryGetValue(key, out var acc))
        {
            acc = new ChunkAccumulator();
            _chunkAccumulators[key] = acc;
        }

        if (acc.PendingCR && text.Length > 0 && text[0] == '\n')
        {
            text = text.Substring(1);
            acc.PendingCR = false;
        }

        var combined = acc.Pending + text;
        var (lines, remainder, endsWithCR) = SplitLinesAndRemainder(combined);
        acc.PendingCR = endsWithCR;

        if (lines.Count == 0 && string.IsNullOrEmpty(remainder))
        {
            acc.Pending = string.Empty;
            _chunkAccumulators[key] = acc;
            _jobProjections[jobId] = proj;
            return;
        }

        if (lines.Count == 0)
        {
            if (acc.ActiveBlockId is { } activeId)
            {
                _replaceTextBlock(activeId, remainder, mappedStream);
                proj = proj with { LastId = activeId };
            }
            else
            {
                var id = new BlockId(_allocateBlockId());
                _insertBlockAfter(proj.LastId, new TextBlock(id, remainder, mappedStream));
                proj = proj with { LastId = id };
                acc.ActiveBlockId = id;
            }

            acc.Pending = remainder;
            _chunkAccumulators[key] = acc;
            _jobProjections[jobId] = proj;
            return;
        }

        if (acc.ActiveBlockId is { } existingActiveId)
        {
            _replaceTextBlock(existingActiveId, lines[0], mappedStream);
            proj = proj with { LastId = existingActiveId };
            acc.ActiveBlockId = null;
        }
        else
        {
            var firstId = new BlockId(_allocateBlockId());
            _insertBlockAfter(proj.LastId, new TextBlock(firstId, lines[0], mappedStream));
            proj = proj with { LastId = firstId };
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var id = new BlockId(_allocateBlockId());
            _insertBlockAfter(proj.LastId, new TextBlock(id, lines[i], mappedStream));
            proj = proj with { LastId = id };
        }

        if (!string.IsNullOrEmpty(remainder))
        {
            var id = new BlockId(_allocateBlockId());
            _insertBlockAfter(proj.LastId, new TextBlock(id, remainder, mappedStream));
            proj = proj with { LastId = id };
            acc.ActiveBlockId = id;
        }

        acc.Pending = remainder;
        _chunkAccumulators[key] = acc;
        _jobProjections[jobId] = proj;
    }

    private static ConsoleTextStream MapTextStream(TextStream stream) =>
        stream switch
        {
            TextStream.Stdout => ConsoleTextStream.Stdout,
            TextStream.Stderr => ConsoleTextStream.Stderr,
            TextStream.System => ConsoleTextStream.System,
            _ => ConsoleTextStream.Default
        };

    private static string[] SplitLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
    }

    private static (List<string> Lines, string Remainder, bool EndsWithCR) SplitLinesAndRemainder(string text)
    {
        var lines = new List<string>();
        var start = 0;
        var endsWithCR = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\r' && ch != '\n')
            {
                continue;
            }

            lines.Add(text.Substring(start, i - start));
            start = i + 1;

            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                start++;
                i++;
            }
            else if (ch == '\r' && i == text.Length - 1)
            {
                endsWithCR = true;
            }
        }

        var remainder = start >= text.Length ? string.Empty : text.Substring(start);
        return (lines, remainder, endsWithCR);
    }

    private sealed class ChunkAccumulator
    {
        public BlockId? ActiveBlockId;
        public string Pending = string.Empty;
        public bool PendingCR;
    }

    private sealed record JobProjection(BlockId HeaderId, BlockId LastId);
}

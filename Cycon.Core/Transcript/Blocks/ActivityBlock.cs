using System;
using System.Globalization;
using Cycon.Core.Styling;

namespace Cycon.Core.Transcript.Blocks;

public sealed class ActivityBlock : IBlock, ITextSelectable, IRunnableBlock, IStoppableBlock, IProgressBlock
{
    private readonly string _label;
    private readonly ActivityKind _kind;
    private readonly TimeSpan _duration;
    private TimeSpan _elapsed;
    private BlockRunState _state = BlockRunState.Running;
    private ProgressSnapshot _progress;
    private StopLevel? _stopRequested;

    public ActivityBlock(BlockId id, string label, ActivityKind kind, TimeSpan duration, ConsoleTextStream stream)
    {
        Id = id;
        _label = string.IsNullOrWhiteSpace(label) ? kind.ToString().ToLowerInvariant() : label;
        _kind = kind;
        _duration = duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : duration;
        Stream = stream;
        _progress = new ProgressSnapshot(Fraction: kind == ActivityKind.Progress ? 0 : null, Phase: null);
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Text;

    public ConsoleTextStream Stream { get; }

    public ActivityKind ActivityKind => _kind;

    public BlockRunState State => _state;

    public bool CanStop => _state == BlockRunState.Running;

    public StopLevel? StopRequestedLevel => _stopRequested;

    public ProgressSnapshot Progress => _progress;

    public bool CanSelect => true;

    public int TextLength => GetDisplayText().Length;

    public string ExportText(int start, int length)
    {
        var text = GetDisplayText();
        if (start < 0 || length < 0 || start + length > text.Length)
        {
            throw new ArgumentOutOfRangeException();
        }

        return length == 0 ? string.Empty : text.Substring(start, length);
    }

    public void Tick(TimeSpan dt)
    {
        if (_state != BlockRunState.Running)
        {
            return;
        }

        if (dt <= TimeSpan.Zero)
        {
            return;
        }

        _elapsed += dt;
        if (_elapsed >= _duration)
        {
            _elapsed = _duration;
            _state = BlockRunState.Completed;
        }

        if (_kind == ActivityKind.Progress)
        {
            var fraction = _duration.TotalSeconds <= 0 ? 1.0 : Math.Clamp(_elapsed.TotalSeconds / _duration.TotalSeconds, 0, 1);
            var phase = _state == BlockRunState.Completed ? "Done" : "Working";
            _progress = new ProgressSnapshot(fraction, phase);
        }
        else
        {
            _progress = new ProgressSnapshot(null, _state == BlockRunState.Completed ? "Done" : "Running");
        }
    }

    public void RequestStop(StopLevel level)
    {
        if (!CanStop)
        {
            return;
        }

        _stopRequested = level;
        _state = BlockRunState.Cancelled;
        _progress = new ProgressSnapshot(_kind == ActivityKind.Progress ? 0 : null, "Cancelled");
    }

    private string GetDisplayText()
    {
        var elapsedSeconds = _elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture);
        var durationSeconds = _duration.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture);

        return _kind switch
        {
            ActivityKind.Wait => _state switch
            {
                BlockRunState.Running => $"{_label}: running... {elapsedSeconds}s / {durationSeconds}s",
                BlockRunState.Completed => $"{_label}: done ({durationSeconds}s)",
                BlockRunState.Cancelled => $"{_label}: cancelled ({elapsedSeconds}s / {durationSeconds}s)",
                _ => $"{_label}: {_state.ToString().ToLowerInvariant()}"
            },
            ActivityKind.Progress => _state switch
            {
                BlockRunState.Running => $"{_label}: {FormatPercent(_progress.Fraction)} ({_progress.Phase ?? "Working"})",
                BlockRunState.Completed => $"{_label}: done",
                BlockRunState.Cancelled => $"{_label}: cancelled",
                _ => $"{_label}: {_state.ToString().ToLowerInvariant()}"
            },
            _ => $"{_label}: {_state.ToString().ToLowerInvariant()}"
        };
    }

    private static string FormatPercent(double? fraction)
    {
        if (fraction is null)
        {
            return string.Empty;
        }

        var pct = (int)Math.Round(Math.Clamp(fraction.Value, 0, 1) * 100.0);
        return $"{pct}%";
    }
}

public enum ActivityKind
{
    Wait,
    Progress
}

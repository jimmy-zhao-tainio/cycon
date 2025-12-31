namespace Cycon.Core.Transcript;

public enum BlockRunState
{
    Idle,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum StopLevel
{
    Soft,
    Terminate,
    Kill
}

public readonly record struct ProgressSnapshot(double? Fraction, string? Phase);


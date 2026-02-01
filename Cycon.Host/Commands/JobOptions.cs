namespace Cycon.Host.Commands;

public readonly record struct JobOptions(bool IsForeground)
{
    public static readonly JobOptions Default = new(IsForeground: false);
    public static readonly JobOptions Foreground = new(IsForeground: true);
}


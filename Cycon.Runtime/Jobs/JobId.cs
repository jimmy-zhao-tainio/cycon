namespace Cycon.Runtime.Jobs;

public readonly record struct JobId(long Value)
{
    public override string ToString() => Value.ToString();
}


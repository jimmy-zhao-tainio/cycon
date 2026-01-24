namespace Cycon.Host.Input;

public readonly record struct BlockCommandActivation(string CommandText, string? RefreshCommandText);

public interface IBlockCommandActivationProvider
{
    bool TryDequeueActivation(out BlockCommandActivation activation);
}


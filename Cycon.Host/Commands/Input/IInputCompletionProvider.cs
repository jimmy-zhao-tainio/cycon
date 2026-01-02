namespace Cycon.Host.Commands.Input;

public interface IInputCompletionProvider
{
    bool TryGetCandidates(in InputCompletionContext ctx, out IReadOnlyList<string> candidates);
}


using System.Collections.Generic;

namespace Cycon.Commands.Completion;

public interface ICompletionProvider
{
    IReadOnlyList<string> GetCompletions(string input);
}

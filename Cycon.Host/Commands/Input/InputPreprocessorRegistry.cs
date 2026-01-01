using System.Collections.Generic;

namespace Cycon.Host.Commands.Input;

public sealed class InputPreprocessorRegistry
{
    private readonly List<IInputPreprocessor> _preprocessors = new();

    public void Register(IInputPreprocessor preprocessor) => _preprocessors.Add(preprocessor);

    public bool TryRewrite(string rawInput, out string rewritten)
    {
        foreach (var preprocessor in _preprocessors)
        {
            if (preprocessor.TryRewrite(rawInput, out rewritten))
            {
                return true;
            }
        }

        rewritten = rawInput;
        return false;
    }
}


namespace Cycon.Host.Commands.Input;

public interface IInputPreprocessor
{
    bool TryRewrite(string rawInput, out string rewritten);
}


using Cycon.Core.Styling;

namespace Cycon.Host.Commands.Blocks;

public interface IBlockCommandContext
{
    void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream);
    void AppendOwnedPrompt(string promptText);
    void ClearTranscript();
    void RequestExit();
}


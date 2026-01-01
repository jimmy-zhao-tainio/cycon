using Cycon.Core.Styling;
using Cycon.Core.Transcript;

namespace Cycon.Host.Commands.Blocks;

public interface IBlockCommandContext
{
    BlockId AllocateBlockId();
    void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream);
    void InsertBlockAfterCommandEcho(IBlock block);
    void AppendOwnedPrompt(string promptText);
    void ClearTranscript();
    void RequestExit();
}

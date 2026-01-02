using Cycon.Core.Styling;
using Cycon.Core.Transcript;

namespace Cycon.BlockCommands;

public interface IBlockCommandContext
{
    BlockId AllocateBlockId();
    BlockId CommandEchoId { get; }
    void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream);
    void InsertBlockAfterCommandEcho(IBlock block);
    void AttachIndicator(BlockId activityBlockId);
    void AppendOwnedPrompt(string promptText);
    void ClearTranscript();
    void RequestExit();
}

namespace Cycon.Core.Transcript.Blocks;

public sealed class PromptBlock : IBlock
{
    public PromptBlock(string promptText)
    {
        PromptText = promptText;
    }

    public string PromptText { get; }
}

namespace Cycon.Core.Transcript.Blocks;

public sealed class PromptBlock : IBlock
{
    public PromptBlock(BlockId id, string prompt = "> ")
    {
        Id = id;
        Prompt = prompt;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Prompt;

    public string Prompt { get; }
    public string Input { get; set; } = string.Empty;
    public int CaretIndex { get; set; }
}

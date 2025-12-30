namespace Cycon.Core.Transcript;

public interface IBlock
{
    BlockId Id { get; }
    BlockKind Kind { get; }
}

public readonly record struct BlockId(int Value);

public enum BlockKind
{
    Text,
    Prompt,
    Image,
    Scene3D
}

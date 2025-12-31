using System.Collections.Generic;

namespace Cycon.Core.Transcript;

public sealed class Transcript
{
    private readonly List<IBlock> _blocks = new();

    public IReadOnlyList<IBlock> Blocks => _blocks;

    public void Add(IBlock block) => _blocks.Add(block);

    public void Insert(int index, IBlock block) => _blocks.Insert(index, block);

    public void RemoveAt(int index) => _blocks.RemoveAt(index);

    public void ReplaceAt(int index, IBlock block) => _blocks[index] = block;

    public void Clear() => _blocks.Clear();
}

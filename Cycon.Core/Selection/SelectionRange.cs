using Cycon.Core.Transcript;

namespace Cycon.Core.Selection;

public readonly record struct SelectionPosition(BlockId BlockId, int Index);

public readonly record struct SelectionRange(SelectionPosition Anchor, SelectionPosition Caret);

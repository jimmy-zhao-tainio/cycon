using Cycon.Core.Selection;
using Cycon.Core.Transcript;
using Cycon.Host.Input;

namespace Cycon.Host.Interaction;

public readonly record struct InteractionSnapshot(
    BlockId? Focused,
    BlockId? MouseCaptured,
    bool IsSelecting,
    SelectionRange? Selection,
    HostKeyModifiers CurrentMods,
    BlockId? LastPromptId);


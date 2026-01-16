using Cycon.Core.Selection;
using Cycon.Core.Transcript;
using Cycon.Host.Input;

namespace Cycon.Host.Interaction;

public sealed class InteractionState
{
    public BlockId? Focused { get; set; }
    public BlockId? MouseCaptured { get; set; }
    public bool IsSelecting { get; set; }
    public SelectionRange? Selection { get; set; }
    public SelectionPosition? SelectionCaret { get; set; }
    public HostKeyModifiers CurrentMods { get; set; }
    public BlockId? LastPromptId { get; set; }
}

using Cycon.Core.Scrolling;
using Cycon.Core.Selection;
using Cycon.Core.Settings;
using TranscriptModel = Cycon.Core.Transcript.Transcript;

namespace Cycon.Core;

public sealed class ConsoleDocument
{
    public ConsoleDocument(TranscriptModel transcript, InputState input, ScrollState scroll, SelectionState selection, ConsoleSettings settings)
    {
        Transcript = transcript;
        Input = input;
        Scroll = scroll;
        Selection = selection;
        Settings = settings;
    }

    public TranscriptModel Transcript { get; }
    public InputState Input { get; }
    public ScrollState Scroll { get; }
    public SelectionState Selection { get; }
    public ConsoleSettings Settings { get; }
}

public sealed class InputState
{
    public string CurrentInput { get; set; } = string.Empty;
}

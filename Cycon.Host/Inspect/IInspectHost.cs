using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Transcript;
using Cycon.Rendering.Renderer;
using Cycon.Rendering.Styling;

namespace Cycon.Host.Inspect;

internal interface IInspectHost
{
    ConsoleDocument Document { get; }
    IConsoleFont Font { get; }
    ConsoleRenderer Renderer { get; }
    SelectionStyle SelectionStyle { get; }

    IReadOnlyList<IBlock> TranscriptBlocks { get; }
    void InsertTranscriptBlock(int index, IBlock block);
    BlockId AllocateBlockId();

    void HandleFileDrop(string path);
    void RequestContentRebuild();

    IReadOnlyList<int>? TakePendingMeshReleases();
    void ClearPendingMeshReleases();
}

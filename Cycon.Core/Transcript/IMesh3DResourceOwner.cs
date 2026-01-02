using System.Collections.Generic;

namespace Cycon.Core.Transcript;

public interface IMesh3DResourceOwner
{
    int MeshId { get; }

    /// <summary>
    /// Optional additional mesh ids owned by the block (e.g. overlays).
    /// </summary>
    IReadOnlyList<int>? AdditionalMeshIds { get; }
}

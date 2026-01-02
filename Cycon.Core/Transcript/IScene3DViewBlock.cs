using System.Numerics;
using Cycon.Core.Transcript.Blocks;

namespace Cycon.Core.Transcript;

public interface IScene3DViewBlock : IBlock
{
    Vector3 CameraPos { get; set; }
    Vector3 CenterDir { get; set; }
    float FocusDistance { get; set; }
    float BoundsRadius { get; }
}

using System.Numerics;
using Cycon.Core.Transcript.Blocks;

namespace Cycon.Core.Transcript;

public interface IScene3DViewBlock : IBlock
{
    Vector3 Target { get; set; }
    float Distance { get; set; }
    float YawRadians { get; set; }
    float PitchRadians { get; set; }
    float BoundsRadius { get; }
}


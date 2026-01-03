using System.Numerics;

namespace Cycon.Core.Transcript;

public interface IScene3DOrbitBlock
{
    Scene3DNavigationMode NavigationMode { get; set; }

    Vector3 OrbitTarget { get; set; }
    float OrbitDistance { get; set; }
    float OrbitYaw { get; set; }
    float OrbitPitch { get; set; }
}


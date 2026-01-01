using Cycon.App;

namespace Cycon.Platform.SilkNet;

public sealed class SilkNetPlatformRunner : IPlatformRunner
{
    public void Run2D(Cycon.App.CyconAppOptions options)
    {
        SilkNetCyconRunner.Run2D(options);
    }

    public void Run3D(Cycon.App.CyconAppOptions options)
    {
        throw new NotSupportedException("3D runner not implemented yet.");
    }
}

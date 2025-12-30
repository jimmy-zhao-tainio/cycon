namespace Cycon.App;

public static class CyconApp
{
    private static readonly IPlatformRunner Runner = CreateDefaultRunner();

    private static IPlatformRunner CreateDefaultRunner() => new Cycon.Platform.SilkNet.SilkNetPlatformRunner();

    public static void Run2D(CyconAppOptions options)
    {
        Runner.Run2D(options);
    }

    public static void Run3D(CyconAppOptions options)
    {
        Runner.Run3D(options);
    }
}

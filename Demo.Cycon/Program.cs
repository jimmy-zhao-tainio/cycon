using Cycon.App;
using Extensions.Inspect;
using Extensions.Math;

namespace Demo.Cycon;

public static class Program
{
    public static void Main()
    {
        CyconApp.Run2D(new CyconAppOptions
        {
            Title = "Cycon",
            Width = 960,
            Height = 540,
            InitialText = BuildDemoText(),
            ConfigureBlockCommands = registry =>
            {
                InspectExtensionRegistration.Register(registry);
                MathExtensionRegistration.Register(registry);
            },
        });
    }

    private static string BuildDemoText()
    {
        return string.Join('\n',
            "Cycon [Version 0.0]",
            "(c) Cycon Corporation. No rights reserved.",
            "");
    }
}

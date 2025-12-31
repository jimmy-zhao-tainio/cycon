using Cycon.App;

namespace Demo.Cycon2D;

public static class Program
{
    public static void Main()
    {
        CyconApp.Run2D(new CyconAppOptions
        {
            Title = "Cycon 2D",
            Width = 960,
            Height = 540,
            InitialText = BuildDemoText()
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

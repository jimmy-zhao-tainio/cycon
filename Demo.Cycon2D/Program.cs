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
            "HELLO VGA",
            "Resize window; should stay pinned.",
            "0123456789 ABCDEFGHIJKLMNOPQRST",
            "abcdefghijklmnopqrstuvwxyz",
            "!\"#$%&'()*+,-./:;<=>?@[\\]^_`");
    }
}

using Cycon.BlockCommands;
using Extensions.Math.Impl;

namespace Extensions.Math;

public static class MathExtensionRegistration
{
    public static void Register(BlockCommandRegistry registry)
    {
        registry.RegisterFallback(new MathFallbackHandler());
        registry.RegisterHelpProvider(new MathHelpProvider());
    }
}

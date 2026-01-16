using Cycon.BlockCommands;
using Extensions.Inspect.Commands;

namespace Extensions.Inspect;

public static class InspectExtensionRegistration
{
    public static void Register(BlockCommandRegistry registry)
    {
        registry.Register(new ViewBlockCommandHandler());
    }
}

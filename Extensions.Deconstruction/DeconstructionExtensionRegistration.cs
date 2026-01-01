using Cycon.BlockCommands;
using Extensions.Deconstruction.Commands;

namespace Extensions.Deconstruction;

public static class DeconstructionExtensionRegistration
{
    public static void Register(BlockCommandRegistry registry)
    {
        registry.Register(new DeconstructBlockCommandHandler());
    }
}


namespace Cycon.BlockCommands;

public interface IBlockCommandFallbackHandler
{
    bool TryHandle(string rawInput, IBlockCommandContext ctx);
}


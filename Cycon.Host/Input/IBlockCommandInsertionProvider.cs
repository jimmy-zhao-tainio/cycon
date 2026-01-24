using Cycon.Layout.Scrolling;

namespace Cycon.Host.Input;

public interface IBlockCommandInsertionProvider
{
    bool TryGetInsertionCommand(int x, int y, in PxRect viewportRectPx, out string commandText);
}


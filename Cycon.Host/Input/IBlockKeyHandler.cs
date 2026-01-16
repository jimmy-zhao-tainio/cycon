namespace Cycon.Host.Input;

public interface IBlockKeyHandler
{
    bool HandleKey(in HostKeyEvent e);
}


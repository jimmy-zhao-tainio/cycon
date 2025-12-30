using System.Collections.Generic;

namespace Cycon.Commands;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, object> _commands = new();

    public void Register(string name, object command)
    {
        _commands[name] = command;
    }
}

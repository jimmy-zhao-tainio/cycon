using System;
using Cycon.Backends.Abstractions.Input;

namespace Cycon.Backends.Abstractions;

public interface IInputSource
{
    event Action<KeyEvent>? KeyChanged;
    event Action<MouseEvent>? MouseChanged;
}

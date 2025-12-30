using System;
using Cycon.Backends.Abstractions;
using Cycon.Backends.Abstractions.Input;

namespace Cycon.Backends.SilkNet;

public sealed class SilkInputSource : IInputSource
{
    public event Action<KeyEvent>? KeyChanged;
    public event Action<MouseEvent>? MouseChanged;
}

using Cycon.Core;
using Cycon.Core.Fonts;

namespace Cycon.Host.Inspect;

internal interface IInspectHost
{
    ConsoleDocument Document { get; }
    IConsoleFont Font { get; }
}

using System.Collections.Generic;
using Cycon.Core.Metrics;

namespace Cycon.Backends.Abstractions.Rendering;

public sealed class RenderFrame
{
    private readonly List<DrawCommand> _commands = new();

    public GridSize BuiltGrid { get; set; }

    public IReadOnlyList<DrawCommand> Commands => _commands;

    public void Add(DrawCommand command) => _commands.Add(command);
}

using System;
using System.Collections.Generic;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Commands;

[Flags]
public enum CommandCapabilities
{
    None = 0,
    Interactive = 1 << 0,
    SupportsProgress = 1 << 1,
    ExternalProcess = 1 << 2,
    Cancellable = 1 << 3
}

public sealed record CommandSpec(
    string Name,
    string Summary,
    IReadOnlyList<string> Aliases,
    CommandCapabilities Capabilities);

public sealed record CommandRequest(string Name, IReadOnlyList<string> Args, string RawText);

public sealed record CommandStartContext(
    JobId JobId,
    IServiceProvider Services,
    IEventSink Events,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

public interface ICommandHandler
{
    CommandSpec Spec { get; }
    IJob Start(CommandRequest request, CommandStartContext context);
}


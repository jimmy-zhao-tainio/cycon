using System;
using System.Collections.Generic;
using Cycon.Runtime.Jobs;
using Cycon.Runtime.Runtime;

namespace Cycon.Commands;

public sealed class CommandDispatcher
{
    private readonly CommandRegistry _registry;
    private readonly IJobRuntime _runtime;
    private readonly IServiceProvider _services;
    private readonly Func<string> _cwdProvider;
    private readonly Func<IReadOnlyDictionary<string, string>> _envProvider;

    public CommandDispatcher(
        CommandRegistry registry,
        IJobRuntime runtime,
        IServiceProvider services,
        Func<string> cwdProvider,
        Func<IReadOnlyDictionary<string, string>> envProvider)
    {
        _registry = registry;
        _runtime = runtime;
        _services = services;
        _cwdProvider = cwdProvider;
        _envProvider = envProvider;
    }

    public CommandDispatchResult Dispatch(string rawLine)
    {
        var request = CommandLineParser.Parse(rawLine);
        if (request is null)
        {
            return CommandDispatchResult.NoOp();
        }

        var handler = _registry.Resolve(request.Name);
        if (handler is null)
        {
            return CommandDispatchResult.Unknown(request.Name);
        }

        var jobId = _runtime.AllocateJobId();
        var sink = _runtime.CreateEventSink(jobId);
        var ctx = new CommandStartContext(
            jobId,
            _services,
            sink,
            _cwdProvider(),
            _envProvider());

        var job = handler.Start(request, ctx);
        _runtime.StartJob(job);
        return CommandDispatchResult.Started(jobId, handler.Spec.Name);
    }
}

public readonly record struct CommandDispatchResult(
    bool IsNoOp,
    bool IsUnknown,
    JobId? JobId,
    string? CommandName,
    string? UnknownName)
{
    public static CommandDispatchResult NoOp() => new(true, false, null, null, null);
    public static CommandDispatchResult Unknown(string name) => new(false, true, null, null, name);
    public static CommandDispatchResult Started(JobId jobId, string commandName) => new(false, false, jobId, commandName, null);
}


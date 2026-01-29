using System;
using System.Collections.Generic;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Commands.Blocks;
using Cycon.Host.Commands.Handlers;
using Cycon.Host.Commands.Input;
using Cycon.Host.Interaction;

namespace Cycon.Host.Commands;

internal sealed class CommandHost
{
    private readonly BlockCommandRegistry _blockCommands;
    private readonly InputPreprocessorRegistry _inputPreprocessors = new();
    private readonly InputHistory _history;
    private readonly InputCompletionController _completion;

    public CommandHost(Action<BlockCommandRegistry>? configureBlockCommands)
    {
        _history = InputHistory.LoadDefault();

        _blockCommands = new BlockCommandRegistry();
        _blockCommands.RegisterCore(new HelpBlockCommandHandler(_blockCommands));
        _blockCommands.RegisterCore(new EchoBlockCommandHandler());
        _blockCommands.RegisterCore(new AskBlockCommandHandler());
        _blockCommands.RegisterCore(new ClearBlockCommandHandler());
        _blockCommands.RegisterCore(new ExitBlockCommandHandler());
        _blockCommands.RegisterCore(new WaitBlockCommandHandler());
        _blockCommands.RegisterCore(new ProgressBlockCommandHandler());
        _blockCommands.RegisterCore(new PwdBlockCommandHandler());
        _blockCommands.RegisterCore(new CdBlockCommandHandler());
        _blockCommands.RegisterCore(new LsBlockCommandHandler());
        _blockCommands.RegisterCore(new CatBlockCommandHandler());
        _blockCommands.RegisterCore(new ViewFallbackBlockCommandHandler());
        _blockCommands.RegisterCore(new CaretBlockCommandHandler());
        configureBlockCommands?.Invoke(_blockCommands);

        _completion = new InputCompletionController(
            new CommandCompletionProvider(_blockCommands),
            new FilePathCompletionProvider());
    }

    public BlockCommandRegistry BlockCommands => _blockCommands;
    public InputPreprocessorRegistry InputPreprocessors => _inputPreprocessors;

    public void ResetOnClear()
    {
        _completion.Reset();
        _history.ResetNavigation();
    }

    public void NotifyHostActionApplied(HostAction action)
    {
        if (ShouldResetCompletion(action))
        {
            _completion.Reset();
        }
    }

    public IReadOnlyList<CommandHostAction> HandleAutocomplete(BlockId promptId, int delta, ICommandHostView view)
    {
        if (!view.TryGetPromptSnapshot(promptId, out var prompt))
        {
            return Array.Empty<CommandHostAction>();
        }

        if (prompt.Owner is not null)
        {
            return Array.Empty<CommandHostAction>();
        }

        var reverse = delta < 0;
        if (!_completion.TryHandleTab(prompt.Input, prompt.CaretIndex, reverse, out var newInput, out var newCaret, out var matchesLine))
        {
            return Array.Empty<CommandHostAction>();
        }

        var actions = new List<CommandHostAction>(3);
        actions.Add(new CommandHostAction.UpdatePrompt(promptId, newInput, Math.Clamp(newCaret, 0, newInput.Length)));
        actions.Add(new CommandHostAction.RequestContentRebuild());
        return actions;
    }

    public IReadOnlyList<CommandHostAction> HandleNavigateHistory(BlockId promptId, int delta, ICommandHostView view)
    {
        if (delta == 0)
        {
            return Array.Empty<CommandHostAction>();
        }

        if (!view.TryGetPromptSnapshot(promptId, out var prompt))
        {
            return Array.Empty<CommandHostAction>();
        }

        if (prompt.Owner is not null)
        {
            return Array.Empty<CommandHostAction>();
        }

        if (!_history.TryNavigate(prompt.Input, delta, out var updated))
        {
            return Array.Empty<CommandHostAction>();
        }

        return new CommandHostAction[]
        {
            new CommandHostAction.UpdatePrompt(promptId, updated, updated.Length),
            new CommandHostAction.RequestContentRebuild()
        };
    }

    public IReadOnlyList<CommandHostAction> HandleSubmitPrompt(BlockId promptId, ICommandHostView view)
    {
        if (!view.TryGetPromptSnapshot(promptId, out var prompt))
        {
            return Array.Empty<CommandHostAction>();
        }

        if (prompt.Owner is not null)
        {
            return Array.Empty<CommandHostAction>();
        }

        return BuildSubmissionActions(prompt, promptId, view);
    }

    public IReadOnlyList<CommandHostAction> HandleFileDrop(string path, ICommandHostView view)
    {
        var lastPrompt = view.GetLastPromptSnapshot();
        if (lastPrompt is null)
        {
            return Array.Empty<CommandHostAction>();
        }

        var commandText = $"view {QuoteForCommandLineParser(path)}";
        var prompt = lastPrompt.Value with { Input = commandText, CaretIndex = commandText.Length };
        return BuildSubmissionActions(prompt, lastPrompt.Value.Id, view);
    }

    private IReadOnlyList<CommandHostAction> BuildSubmissionActions(
        PromptSnapshot prompt,
        BlockId promptId,
        ICommandHostView view)
    {
        var command = prompt.Input;
        var headerText = prompt.Prompt + command;

        _history.RecordSubmitted(command);

        var commandForParse = command;
        if (_inputPreprocessors.TryRewrite(command, out var rewritten))
        {
            commandForParse = rewritten;
        }

        var actions = new List<CommandHostAction>(4);
        var headerId = view.AllocateBlockId();
        actions.Add(new CommandHostAction.InsertTextBlockBefore(
            promptId,
            headerId,
            headerText,
            ConsoleTextStream.Default));

        var request = TryRewriteDriveSwitch(commandForParse, out var driveSwitchRequest)
            ? driveSwitchRequest
            : CommandLineParser.Parse(commandForParse);
        if (request is null)
        {
            actions.Add(new CommandHostAction.UpdatePrompt(promptId, string.Empty, 0));
            actions.Add(new CommandHostAction.RequestContentRebuild());
            _history.ResetNavigation();
            return actions;
        }

        actions.Add(new CommandHostAction.SubmitParsedCommand(
            request,
            commandForParse,
            command,
            headerText,
            headerId,
            promptId));
        actions.Add(new CommandHostAction.UpdatePrompt(promptId, string.Empty, 0));
        actions.Add(new CommandHostAction.RequestContentRebuild());
        _history.ResetNavigation();
        return actions;
    }


    private static bool ShouldResetCompletion(HostAction action) =>
        action is HostAction.InsertText or
        HostAction.SetPromptInput or
        HostAction.Backspace or
        HostAction.MoveCaret or
        HostAction.SetCaret or
        HostAction.NavigateHistory or
        HostAction.SubmitPrompt;

    private static string QuoteForCommandLineParser(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static bool TryRewriteDriveSwitch(string command, out CommandRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.Trim();
        if (trimmed.Length != 2 || trimmed[1] != ':' || !char.IsLetter(trimmed[0]))
        {
            return false;
        }

        request = new CommandRequest("cd", new[] { trimmed }, command);
        return true;
    }

}

internal readonly record struct PromptSnapshot(
    BlockId Id,
    string Prompt,
    string Input,
    int CaretIndex,
    PromptOwner? Owner);

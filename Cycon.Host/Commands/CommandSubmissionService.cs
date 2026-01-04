using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Transcript;
using Cycon.Host.Commands.Input;

namespace Cycon.Host.Commands;

internal sealed class CommandSubmissionService
{
    private readonly BlockCommandRegistry _blockCommands;
    private readonly InputPreprocessorRegistry _inputPreprocessors;

    public CommandSubmissionService(
        BlockCommandRegistry blockCommands,
        InputPreprocessorRegistry inputPreprocessors)
    {
        _blockCommands = blockCommands;
        _inputPreprocessors = inputPreprocessors;
    }

    public CommandSubmissionResult Submit(
        string rawCommand,
        BlockId headerId,
        BlockId shellPromptId,
        IBlockCommandSession session)
    {
        var commandForParse = rawCommand;
        if (_inputPreprocessors.TryRewrite(rawCommand, out var rewritten))
        {
            commandForParse = rewritten;
        }

        var request = CommandLineParser.Parse(commandForParse);
        if (request is null)
        {
            return CommandSubmissionResult.ParseFailed;
        }

        var ctx = new BlockCommandContext(session, headerId, shellPromptId);
        var handled = _blockCommands.TryExecuteOrFallback(request, commandForParse, ctx);
        return new CommandSubmissionResult(handled, ctx.StartedBlockingActivity, false);
    }
}

internal readonly record struct CommandSubmissionResult(
    bool Handled,
    bool StartedBlockingActivity,
    bool IsParseFailed)
{
    public static CommandSubmissionResult ParseFailed => new(false, false, true);
}

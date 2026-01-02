using System;
using Cycon.BlockCommands;
using Cycon.Core.Styling;

namespace Extensions.Math.Impl;

internal sealed class MathFallbackHandler : IBlockCommandFallbackHandler
{
    private readonly MathEnv _env = new();
    private readonly IMathEngine _engine = new NCalcMathEngine();

    public bool TryHandle(string rawInput, IBlockCommandContext ctx)
    {
        var outcome = _engine.TryEvaluate(rawInput, _env, out var result, out var failure);
        if (outcome == MathTryResult.NotMath)
        {
            return false;
        }

        if (outcome == MathTryResult.Error)
        {
            ctx.InsertTextAfterCommandEcho(failure.Message, ConsoleTextStream.Default);
            return true;
        }

        ctx.InsertTextAfterCommandEcho(result.DisplayText, ConsoleTextStream.Stdout);
        return true;
    }
}

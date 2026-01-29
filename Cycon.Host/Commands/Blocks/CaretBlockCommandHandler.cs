using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Host.Hosting;

namespace Cycon.Host.Commands.Blocks;

public sealed class CaretBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "caret",
        Summary: "Shows or updates prompt caret pulse settings (native block).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is not BlockCommandContext hostCtx)
        {
            ctx.InsertTextAfterCommandEcho("Caret settings are unavailable.", ConsoleTextStream.System);
            return true;
        }

        var current = hostCtx.GetPromptCaretSettings();
        if (request.Args.Count == 0)
        {
            Print(ctx, current);
            return true;
        }

        var args = request.Args;
        var verb = args[0];
        if (string.Equals(verb, "default", StringComparison.OrdinalIgnoreCase))
        {
            var defaults = PromptCaretSettings.Default;
            hostCtx.SetPromptCaretSettings(defaults);
            Print(ctx, defaults);
            return true;
        }

        var updated = current;
        try
        {
            if (string.Equals(verb, "park", StringComparison.OrdinalIgnoreCase))
            {
                RequireArgCount(args, 2);
                var parked = ParseByte(args[1], "parkedAlpha");
                updated = updated with { Focus = updated.Focus with { ParkedAlpha = parked } };
            }
            else if (string.Equals(verb, "solid", StringComparison.OrdinalIgnoreCase))
            {
                RequireArgCount(args, 2);
                var solid = ParseByte(args[1], "solidAlpha");
                updated = updated with { Typing = updated.Typing with { SolidAlpha = solid } };
            }
            else if (string.Equals(verb, "grace", StringComparison.OrdinalIgnoreCase))
            {
                RequireArgCount(args, 2);
                var ms = ParseInt(args[1], "typingGraceMs");
                updated = updated with { Typing = updated.Typing with { TypingGraceMs = ms } };
            }
            else if (string.Equals(verb, "hz", StringComparison.OrdinalIgnoreCase))
            {
                RequireArgCount(args, 2);
                var hz = ParseInt(args[1], "animHzDuringFade");
                updated = updated with { Pulse = updated.Pulse with { AnimHzDuringFade = hz } };
            }
            else if (string.Equals(verb, "pulse", StringComparison.OrdinalIgnoreCase))
            {
                // pulse <periodMs> <fadeInMs> <onHoldMs> <fadeOutMs> <offHoldMs> [risePow] [fallPow]
                if (args.Count < 6 || args.Count > 8)
                {
                    throw new ArgumentException("Usage: caret pulse <periodMs> <fadeInMs> <onHoldMs> <fadeOutMs> <offHoldMs> [risePow] [fallPow]");
                }

                var periodMs = ParseInt(args[1], "periodMs");
                var fadeInMs = ParseInt(args[2], "fadeInMs");
                var onHoldMs = ParseInt(args[3], "onHoldMs");
                var fadeOutMs = ParseInt(args[4], "fadeOutMs");
                var offHoldMs = ParseInt(args[5], "offHoldMs");
                var risePow = args.Count >= 7 ? ParseDouble(args[6], "risePow") : updated.Pulse.RisePow;
                var fallPow = args.Count >= 8 ? ParseDouble(args[7], "fallPow") : updated.Pulse.FallPow;

                updated = updated with
                {
                    Pulse = updated.Pulse with
                    {
                        PeriodMs = periodMs,
                        FadeInMs = fadeInMs,
                        OnHoldMs = onHoldMs,
                        FadeOutMs = fadeOutMs,
                        OffHoldMs = offHoldMs,
                        RisePow = risePow,
                        FallPow = fallPow,
                    }
                };
            }
            else
            {
                throw new ArgumentException("Unknown caret command. Try: caret, caret default, caret park <0-255>, caret grace <ms>, caret hz <n>, caret pulse ...");
            }

            updated.Validate();
            hostCtx.SetPromptCaretSettings(updated);
            Print(ctx, updated);
            return true;
        }
        catch (Exception ex)
        {
            ctx.InsertTextAfterCommandEcho(ex.Message, ConsoleTextStream.System);
            return true;
        }
    }

    private static void Print(IBlockCommandContext ctx, in PromptCaretSettings s)
    {
        var pulseSum = s.Pulse.FadeInMs + s.Pulse.OnHoldMs + s.Pulse.FadeOutMs + s.Pulse.OffHoldMs;
        var sumOk = pulseSum == s.Pulse.PeriodMs;
        ctx.InsertTextAfterCommandEcho(
            $"Pulse: period={s.Pulse.PeriodMs}ms fadeIn={s.Pulse.FadeInMs}ms on={s.Pulse.OnHoldMs}ms fadeOut={s.Pulse.FadeOutMs}ms off={s.Pulse.OffHoldMs}ms sum={pulseSum}ms ok={sumOk} risePow={s.Pulse.RisePow:0.###} fallPow={s.Pulse.FallPow:0.###} max={s.Pulse.MaxAlpha} hz={s.Pulse.AnimHzDuringFade}",
            ConsoleTextStream.Stdout);
        ctx.InsertTextAfterCommandEcho(
            $"Focus: parked={s.Focus.ParkedAlpha} blurFade={s.Focus.BlurFadeMs}ms wakeFade={s.Focus.FocusWakeFadeMs}ms wakeHold={s.Focus.FocusWakeHoldMs}ms",
            ConsoleTextStream.Stdout);
        ctx.InsertTextAfterCommandEcho(
            $"Typing: grace={s.Typing.TypingGraceMs}ms solid={s.Typing.SolidAlpha}",
            ConsoleTextStream.Stdout);
    }

    private static void RequireArgCount(System.Collections.Generic.IReadOnlyList<string> args, int count)
    {
        if (args.Count != count)
        {
            throw new ArgumentException("Invalid arguments.");
        }
    }

    private static int ParseInt(string s, string name)
    {
        if (!int.TryParse(s, out var v))
        {
            throw new ArgumentException($"Invalid {name}: {s}");
        }

        return v;
    }

    private static double ParseDouble(string s, string name)
    {
        if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            throw new ArgumentException($"Invalid {name}: {s}");
        }

        return v;
    }

    private static byte ParseByte(string s, string name)
    {
        var v = ParseInt(s, name);
        if (v < 0 || v > 255)
        {
            throw new ArgumentOutOfRangeException(name, "Value must be between 0 and 255.");
        }

        return (byte)v;
    }
}

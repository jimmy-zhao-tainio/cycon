using System;
using System.Globalization;
using System.Text.RegularExpressions;
using NCalc;

namespace Extensions.Math.Impl;

internal sealed class NCalcMathEngine : IMathEngine
{
    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public MathTryResult TryEvaluate(string input, MathEnv env, out MathValueResult result, out MathFailure failure)
    {
        result = default;
        failure = default;

        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return MathTryResult.NotMath;
        }

        if (!ShouldAttemptMath(trimmed, env))
        {
            return MathTryResult.NotMath;
        }

        try
        {
            if (TrySplitAssignment(trimmed, out var left, out var right))
            {
                left = left.Trim();
                right = right.Trim();
                if (!IdentifierRegex.IsMatch(left))
                {
                    failure = MathFailure.Invalid("Syntax error.");
                    return MathTryResult.Error;
                }

                if (IsReservedName(left))
                {
                    failure = MathFailure.Invalid("Syntax error.");
                    return MathTryResult.Error;
                }

                var value = EvaluateDouble(right, env);
                if (!TryValidateNumber(value, right, out failure))
                {
                    return MathTryResult.Error;
                }
                value = NormalizeZero(value);

                env.Variables[left] = value;
                env.Ans = value;

                var formatted = Format(value);
                result = new MathValueResult(
                    IsAssignment: true,
                    AssignedName: left,
                    Value: value,
                    DisplayText: $"{left} = {formatted}");
                return MathTryResult.Success;
            }

            {
                var value = EvaluateDouble(trimmed, env);
                if (!TryValidateNumber(value, trimmed, out failure))
                {
                    return MathTryResult.Error;
                }
                value = NormalizeZero(value);
                env.Ans = value;

                result = new MathValueResult(
                    IsAssignment: false,
                    AssignedName: null,
                    Value: value,
                    DisplayText: Format(value));
                return MathTryResult.Success;
            }
        }
        catch (MathEvalException ex)
        {
            failure = MathFailure.Invalid(ex.Message);
            return MathTryResult.Error;
        }
        catch (EvaluationException)
        {
            failure = MathFailure.Invalid("Syntax error.");
            return MathTryResult.Error;
        }
        catch (DivideByZeroException)
        {
            failure = MathFailure.FromKind(MathFailureKind.DivideByZero);
            return MathTryResult.Error;
        }
        catch (OverflowException)
        {
            failure = MathFailure.FromKind(MathFailureKind.Overflow);
            return MathTryResult.Error;
        }
        catch
        {
            failure = MathFailure.Invalid("Syntax error.");
            return MathTryResult.Error;
        }
    }

    private static bool ShouldAttemptMath(string input, MathEnv env)
    {
        if (TrySplitAssignment(input, out _, out _))
        {
            return true;
        }

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsDigit(c) || c == '.' || c == '(' || c == ')' || c == '+' || c == '-' || c == '*' || c == '/' || c == '%' || c == '^')
            {
                return true;
            }
        }

        // Bare identifiers are math only if they are known constants/variables.
        if (IdentifierRegex.IsMatch(input))
        {
            if (string.Equals(input, "ans", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "pi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "e", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return env.Variables.ContainsKey(input);
        }

        return false;
    }

    private static bool TrySplitAssignment(string input, out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;

        var idx = -1;
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != '=')
            {
                continue;
            }

            var prev = i > 0 ? input[i - 1] : '\0';
            var next = i + 1 < input.Length ? input[i + 1] : '\0';

            // Reject ==, !=, <=, >=
            if (prev == '=' || next == '=' || prev == '<' || prev == '>' || prev == '!')
            {
                continue;
            }

            if (idx != -1)
            {
                return false;
            }

            idx = i;
        }

        if (idx == -1)
        {
            return false;
        }

        left = input[..idx];
        right = input[(idx + 1)..];
        return true;
    }

    private static bool IsReservedName(string name) =>
        string.Equals(name, "ans", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "e", StringComparison.OrdinalIgnoreCase);

    private static double EvaluateDouble(string exprText, MathEnv env)
    {
        var finalText = exprText;
        if (finalText.IndexOf('^', StringComparison.Ordinal) >= 0)
        {
            if (!PowerToPowRewriter.TryRewrite(finalText, out finalText))
            {
                throw new MathEvalException("Syntax error.");
            }
        }

        var expr = new Expression(finalText, EvaluateOptions.IgnoreCase);

        expr.EvaluateParameter += (name, args) =>
        {
            if (string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase))
            {
                args.Result = global::System.Math.PI;
                return;
            }

            if (string.Equals(name, "e", StringComparison.OrdinalIgnoreCase))
            {
                args.Result = global::System.Math.E;
                return;
            }

            if (string.Equals(name, "ans", StringComparison.OrdinalIgnoreCase))
            {
                args.Result = env.Ans;
                return;
            }

            if (env.Variables.TryGetValue(name, out var value))
            {
                args.Result = value;
                return;
            }

            throw new MathEvalException($"Unknown variable: {name}");
        };

        expr.EvaluateFunction += (name, args) =>
        {
            var p = args.Parameters;
            double Get(int i) => Convert.ToDouble(p[i].Evaluate(), CultureInfo.InvariantCulture);

            switch (name.ToLowerInvariant())
            {
                case "sin": args.Result = global::System.Math.Sin(Get(0)); return;
                case "cos": args.Result = global::System.Math.Cos(Get(0)); return;
                case "tan": args.Result = global::System.Math.Tan(Get(0)); return;
                case "asin": args.Result = global::System.Math.Asin(Get(0)); return;
                case "acos": args.Result = global::System.Math.Acos(Get(0)); return;
                case "atan":
                    args.Result = p.Length == 2 ? global::System.Math.Atan2(Get(0), Get(1)) : global::System.Math.Atan(Get(0));
                    return;
                case "atan2": args.Result = global::System.Math.Atan2(Get(0), Get(1)); return;

                case "sqrt": args.Result = global::System.Math.Sqrt(Get(0)); return;
                case "abs": args.Result = global::System.Math.Abs(Get(0)); return;
                case "min": args.Result = global::System.Math.Min(Get(0), Get(1)); return;
                case "max": args.Result = global::System.Math.Max(Get(0), Get(1)); return;

                case "floor": args.Result = global::System.Math.Floor(Get(0)); return;
                case "ceil": args.Result = global::System.Math.Ceiling(Get(0)); return;
                case "round":
                    args.Result = p.Length == 2 ? global::System.Math.Round(Get(0), (int)Get(1)) : global::System.Math.Round(Get(0));
                    return;

                case "ln": args.Result = global::System.Math.Log(Get(0)); return;
                case "log":
                    args.Result = p.Length == 2 ? global::System.Math.Log(Get(0), Get(1)) : global::System.Math.Log10(Get(0));
                    return;
                case "exp": args.Result = global::System.Math.Exp(Get(0)); return;
                case "pow": args.Result = global::System.Math.Pow(Get(0), Get(1)); return;
                default:
                    throw new MathEvalException($"Unknown function: {name}");
            }
        };

        var raw = expr.Evaluate();
        if (raw is null)
        {
            throw new MathEvalException("Syntax error.");
        }

        if (raw is bool)
        {
            throw new MathEvalException("Syntax error.");
        }

        var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        return value;
    }

    private static bool TryValidateNumber(double value, string exprText, out MathFailure failure)
    {
        if (double.IsNaN(value))
        {
            failure = MathFailure.FromKind(MathFailureKind.Invalid);
            return false;
        }

        if (double.IsInfinity(value))
        {
            // NCalc often returns +/-Infinity for division by zero instead of throwing.
            if (LooksLikeDivideByZero(exprText))
            {
                failure = MathFailure.FromKind(MathFailureKind.DivideByZero);
                return false;
            }

            failure = MathFailure.FromKind(MathFailureKind.Overflow);
            return false;
        }

        failure = default;
        return true;
    }

    private static bool LooksLikeDivideByZero(string exprText)
    {
        // Heuristic: if the expression contains division/modulo and produced Infinity, report divide-by-zero.
        // This matches typical user intent for inputs like "23 / 0" or "23 / (1-1)".
        for (var i = 0; i < exprText.Length; i++)
        {
            var c = exprText[i];
            if (c is '/' or '%')
            {
                return true;
            }
        }

        return false;
    }

    private static string Format(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static double NormalizeZero(double value) =>
        value == 0d ? 0d : value;

    private sealed class MathEvalException : Exception
    {
        public MathEvalException(string message) : base(message) { }
    }
}

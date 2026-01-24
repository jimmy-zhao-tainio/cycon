using System;
using System.Collections.Generic;

namespace Cycon.Host.Commands.Input;

public sealed class InputCompletionController
{
    private readonly List<IInputCompletionProvider> _providers;
    private CompletionState _state = new();

    public InputCompletionController(params IInputCompletionProvider[] providers)
    {
        _providers = new List<IInputCompletionProvider>(providers ?? Array.Empty<IInputCompletionProvider>());
    }

    public void Reset() => _state = new CompletionState();

    public bool TryHandleTab(
        string input,
        int caretIndex,
        bool reverseCycle,
        out string newInput,
        out int newCaretIndex,
        out string? matchesLine)
    {
        matchesLine = null;
        newInput = input ?? string.Empty;
        newCaretIndex = Math.Clamp(caretIndex, 0, newInput.Length);

        if (!TryGetCompletionTarget(newInput, newCaretIndex, out var target))
        {
            Reset();
            return false;
        }

        if (!_state.IsCompatibleWith(target))
        {
            _state = new CompletionState
            {
                TargetStart = target.ReplaceStart,
                Mode = target.Mode
            };
        }

        if (_state.Candidates is null)
        {
            if (!TryGetCandidates(target.Mode, target.Prefix, out var candidates) || candidates.Count == 0)
            {
                Reset();
                return false;
            }

            _state.Candidates = candidates;
            _state.TabCount = 0;
            _state.CycleIndex = -1;
        }

        var candidatesList = _state.Candidates;
        if (candidatesList is null || candidatesList.Count == 0)
        {
            Reset();
            return false;
        }

        if (candidatesList.Count == 1)
        {
            ApplyReplacement(ref newInput, ref newCaretIndex, target.ReplaceStart, target.ReplaceLength, candidatesList[0]);
            Reset();
            return true;
        }

        if (_state.TabCount == 0)
        {
            var lcp = LongestCommonPrefix(candidatesList);
            if (lcp.Length > target.Prefix.Length)
            {
                ApplyReplacement(ref newInput, ref newCaretIndex, target.ReplaceStart, target.ReplaceLength, lcp);
            }

            _state.TabCount = 1;
            return true;
        }

        if (_state.TabCount == 1)
        {
            matchesLine = "Matches: " + string.Join(" ", candidatesList);
            _state.TabCount = 2;
            return true;
        }

        var delta = reverseCycle ? -1 : 1;
        if (_state.CycleIndex < 0 && reverseCycle)
        {
            _state.CycleIndex = 0;
        }
        _state.CycleIndex = Mod(_state.CycleIndex + delta, candidatesList.Count);
        ApplyReplacement(ref newInput, ref newCaretIndex, target.ReplaceStart, target.ReplaceLength, candidatesList[_state.CycleIndex]);
        _state.TabCount++;
        return true;
    }

    private bool TryGetCandidates(CompletionMode mode, string prefix, out IReadOnlyList<string> candidates)
    {
        var ctx = new InputCompletionContext(mode, prefix);
        for (var i = 0; i < _providers.Count; i++)
        {
            if (_providers[i].TryGetCandidates(in ctx, out candidates) && candidates.Count > 0)
            {
                return true;
            }
        }

        candidates = Array.Empty<string>();
        return false;
    }

    private static void ApplyReplacement(ref string input, ref int caret, int start, int length, string replacement)
    {
        start = Math.Clamp(start, 0, input.Length);
        length = Math.Clamp(length, 0, input.Length - start);
        input = input.Remove(start, length).Insert(start, replacement);
        caret = start + replacement.Length;
    }

    private static int Mod(int x, int m)
    {
        var r = x % m;
        return r < 0 ? r + m : r;
    }

    private static string LongestCommonPrefix(IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        var prefix = candidates[0] ?? string.Empty;
        for (var i = 1; i < candidates.Count; i++)
        {
            prefix = CommonPrefix(prefix, candidates[i] ?? string.Empty);
            if (prefix.Length == 0)
            {
                break;
            }
        }

        return prefix;
    }

    private static string CommonPrefix(string a, string b)
    {
        var len = Math.Min(a.Length, b.Length);
        var i = 0;
        for (; i < len; i++)
        {
            if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i]))
            {
                break;
            }
        }

        return i == 0 ? string.Empty : a.Substring(0, i);
    }

    private static bool TryGetCompletionTarget(string input, int caret, out Target target)
    {
        target = default;

        var tokens = TokenizeWithRanges(input);
        if (tokens.Count == 0)
        {
            target = new Target(CompletionMode.CommandName, Prefix: string.Empty, ReplaceStart: 0, ReplaceLength: 0);
            return true;
        }

        var tokenIndex = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (caret >= tokens[i].Start && caret <= tokens[i].End)
            {
                tokenIndex = i;
                break;
            }
        }

        if (tokenIndex == -1)
        {
            return false;
        }

        var token = tokens[tokenIndex];
        if (token.IsQuoted)
        {
            return false;
        }

        if (tokenIndex == 0)
        {
            var prefixLen = Math.Clamp(caret - token.Start, 0, token.End - token.Start);
            var prefix = input.Substring(token.Start, prefixLen);
            target = new Target(CompletionMode.CommandName, prefix, token.Start, token.End - token.Start);
            return true;
        }

        var firstTokenText = input.Substring(tokens[0].Start, tokens[0].End - tokens[0].Start);
        if (IsHelpToken(firstTokenText) && tokenIndex == 1)
        {
            var prefixLen = Math.Clamp(caret - token.Start, 0, token.End - token.Start);
            var prefix = input.Substring(token.Start, prefixLen);
            target = new Target(CompletionMode.HelpTarget, prefix, token.Start, token.End - token.Start);
            return true;
        }

        return false;
    }

    private static bool IsHelpToken(string token) =>
        string.Equals(token, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(token, "?", StringComparison.OrdinalIgnoreCase);

    private static List<TokenRange> TokenizeWithRanges(string input)
    {
        var tokens = new List<TokenRange>();
        if (string.IsNullOrEmpty(input))
        {
            return tokens;
        }

        var i = 0;
        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
            {
                i++;
            }

            if (i >= input.Length)
            {
                break;
            }

            var start = i;
            var isQuoted = false;
            var quote = '\0';
            if (input[i] is '"' or '\'')
            {
                isQuoted = true;
                quote = input[i];
                i++;
            }

            while (i < input.Length)
            {
                var ch = input[i];
                if (isQuoted)
                {
                    if (ch == quote)
                    {
                        i++;
                        break;
                    }

                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    break;
                }

                i++;
            }

            var end = i;
            if (end > input.Length)
            {
                end = input.Length;
            }

            if (start < end)
            {
                tokens.Add(new TokenRange(start, end, isQuoted));
            }

            while (i < input.Length && !isQuoted && char.IsWhiteSpace(input[i]))
            {
                i++;
            }
        }

        return tokens;
    }

    private readonly record struct TokenRange(int Start, int End, bool IsQuoted);

    private readonly record struct Target(CompletionMode Mode, string Prefix, int ReplaceStart, int ReplaceLength);

    private sealed class CompletionState
    {
        public int TargetStart { get; init; } = -1;
        public CompletionMode Mode { get; init; } = CompletionMode.CommandName;
        public IReadOnlyList<string>? Candidates { get; set; }
        public int TabCount { get; set; }
        public int CycleIndex { get; set; }

        public bool IsCompatibleWith(Target target) =>
            Candidates is not null &&
            TargetStart == target.ReplaceStart &&
            Mode == target.Mode;
    }
}

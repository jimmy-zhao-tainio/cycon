using System;
using System.Globalization;
using System.Text;

namespace Extensions.Math.Impl;

internal static class PowerToPowRewriter
{
    public static bool TryRewrite(string input, out string rewritten)
    {
        rewritten = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var parser = new Parser(input);
        if (!parser.TryParseExpression(out var expr) || !parser.IsEof)
        {
            return false;
        }

        rewritten = expr.ToText();
        return true;
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s;

        public bool IsEof
        {
            get
            {
                SkipWs();
                return _i >= _s.Length;
            }
        }

        public bool TryParseExpression(out Expr expr) => TryParseAddSub(out expr);

        private bool TryParseAddSub(out Expr expr)
        {
            if (!TryParseMulDivMod(out expr))
            {
                return false;
            }

            while (true)
            {
                SkipWs();
                if (!TryPeek(out var c) || (c != '+' && c != '-'))
                {
                    return true;
                }

                _i++;
                if (!TryParseMulDivMod(out var right))
                {
                    return false;
                }

                expr = new Binary(expr, c, right);
            }
        }

        private bool TryParseMulDivMod(out Expr expr)
        {
            if (!TryParsePower(out expr))
            {
                return false;
            }

            while (true)
            {
                SkipWs();
                if (!TryPeek(out var c) || (c != '*' && c != '/' && c != '%'))
                {
                    return true;
                }

                _i++;
                if (!TryParsePower(out var right))
                {
                    return false;
                }

                expr = new Binary(expr, c, right);
            }
        }

        // Right-associative: a ^ b ^ c == a ^ (b ^ c)
        private bool TryParsePower(out Expr expr)
        {
            if (!TryParseUnary(out expr))
            {
                return false;
            }

            SkipWs();
            if (!TryPeek(out var c) || c != '^')
            {
                return true;
            }

            _i++;
            if (!TryParsePower(out var right))
            {
                return false;
            }

            expr = new Pow(expr, right);
            return true;
        }

        private bool TryParseUnary(out Expr expr)
        {
            SkipWs();
            if (TryPeek(out var c) && (c == '+' || c == '-'))
            {
                _i++;
                if (!TryParseUnary(out var inner))
                {
                    expr = null!;
                    return false;
                }

                expr = c == '-' ? new UnaryMinus(inner) : inner;
                return true;
            }

            return TryParsePrimary(out expr);
        }

        private bool TryParsePrimary(out Expr expr)
        {
            SkipWs();
            if (!TryPeek(out var c))
            {
                expr = null!;
                return false;
            }

            if (c == '(')
            {
                _i++;
                if (!TryParseExpression(out expr))
                {
                    return false;
                }

                SkipWs();
                if (!TryConsume(')'))
                {
                    return false;
                }

                return true;
            }

            if (IsIdentStart(c))
            {
                if (!TryReadIdentifier(out var ident))
                {
                    expr = null!;
                    return false;
                }

                SkipWs();
                if (TryConsume('('))
                {
                    var args = new System.Collections.Generic.List<Expr>();
                    SkipWs();
                    if (!TryConsume(')'))
                    {
                        while (true)
                        {
                            if (!TryParseExpression(out var arg))
                            {
                                expr = null!;
                                return false;
                            }

                            args.Add(arg);
                            SkipWs();
                            if (TryConsume(')'))
                            {
                                break;
                            }

                            if (!TryConsume(','))
                            {
                                expr = null!;
                                return false;
                            }
                        }
                    }

                    expr = new Call(ident, args.ToArray());
                    return true;
                }

                expr = new Identifier(ident);
                return true;
            }

            if (char.IsDigit(c) || c == '.')
            {
                if (!TryReadNumber(out var literal))
                {
                    expr = null!;
                    return false;
                }

                expr = new Number(literal);
                return true;
            }

            expr = null!;
            return false;
        }

        private bool TryReadIdentifier(out string ident)
        {
            ident = string.Empty;
            SkipWs();
            var start = _i;
            if (!TryPeek(out var c) || !IsIdentStart(c))
            {
                return false;
            }

            _i++;
            while (_i < _s.Length)
            {
                c = _s[_i];
                if (!IsIdentPart(c))
                {
                    break;
                }

                _i++;
            }

            ident = _s[start.._i];
            return true;
        }

        private bool TryReadNumber(out string literal)
        {
            literal = string.Empty;
            SkipWs();
            var start = _i;

            var sawDigit = false;
            while (_i < _s.Length && char.IsDigit(_s[_i]))
            {
                sawDigit = true;
                _i++;
            }

            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i]))
                {
                    sawDigit = true;
                    _i++;
                }
            }

            if (!sawDigit)
            {
                return false;
            }

            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                var expStart = _i;
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                {
                    _i++;
                }

                var expDigits = 0;
                while (_i < _s.Length && char.IsDigit(_s[_i]))
                {
                    expDigits++;
                    _i++;
                }

                if (expDigits == 0)
                {
                    _i = expStart;
                }
            }

            literal = _s[start.._i];
            return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private bool TryConsume(char ch)
        {
            SkipWs();
            if (_i < _s.Length && _s[_i] == ch)
            {
                _i++;
                return true;
            }

            return false;
        }

        private bool TryPeek(out char c)
        {
            if (_i >= _s.Length)
            {
                c = '\0';
                return false;
            }

            c = _s[_i];
            return true;
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
            {
                _i++;
            }
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    private abstract record Expr
    {
        public abstract string ToText();
    }

    private sealed record Number(string Literal) : Expr
    {
        public override string ToText() => Literal;
    }

    private sealed record Identifier(string Name) : Expr
    {
        public override string ToText() => Name;
    }

    private sealed record UnaryMinus(Expr Inner) : Expr
    {
        public override string ToText() => "-(" + Inner.ToText() + ")";
    }

    private sealed record Binary(Expr Left, char Op, Expr Right) : Expr
    {
        public override string ToText() => "(" + Left.ToText() + " " + Op + " " + Right.ToText() + ")";
    }

    private sealed record Pow(Expr Left, Expr Right) : Expr
    {
        public override string ToText() => "pow(" + Left.ToText() + "," + Right.ToText() + ")";
    }

    private sealed record Call(string Name, Expr[] Args) : Expr
    {
        public override string ToText()
        {
            var sb = new StringBuilder();
            sb.Append(Name);
            sb.Append('(');
            for (var i = 0; i < Args.Length; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append(Args[i].ToText());
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}


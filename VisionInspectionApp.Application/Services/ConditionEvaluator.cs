using System;
using System.Collections.Generic;
using System.Globalization;

namespace VisionInspectionApp.Application;

public static partial class ConditionEvaluator
{
    internal readonly record struct ConditionValue(bool IsBool, bool Bool, double Number, string? Text)
    {
        public static ConditionValue FromBool(bool v) => new(true, v, 0.0, null);
        public static ConditionValue FromNumber(double v) => new(false, false, v, null);
        public static ConditionValue FromString(string v) => new(false, false, 0.0, v);
    }

    public static bool Evaluate(string expression, Dictionary<string, Variable> vars)
    {
        var p = new Parser(expression, vars);
        var v = p.ParseExpression();
        p.Expect(TokenKind.Eof);
        return ToBool(v);
    }

    private static bool ToBool(ConditionValue v)
    {
        if (v.IsBool) return v.Bool;
        throw new InvalidOperationException("Expression did not evaluate to boolean");
    }

    private enum TokenKind
    {
        Eof,
        Identifier,
        Number,
        String,
        True,
        False,
        And,
        Or,
        Not,
        LParen,
        RParen,
        Dot,
        Eq,
        Ne,
        Gt,
        Ge,
        Lt,
        Le
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Number);

    private sealed class Lexer
    {
        private readonly string _text;
        private int _pos;

        public Lexer(string text)
        {
            _text = text ?? string.Empty;
            _pos = 0;
        }

        public Token Next()
        {
            SkipWs();
            if (_pos >= _text.Length)
            {
                return new Token(TokenKind.Eof, string.Empty, 0);
            }

            char c = _text[_pos];
            if (c == '(') { _pos++; return new Token(TokenKind.LParen, "(", 0); }
            if (c == ')') { _pos++; return new Token(TokenKind.RParen, ")", 0); }
            if (c == '.') { _pos++; return new Token(TokenKind.Dot, ".", 0); }

            if (c == '=')
            {
                _pos++;
                if (Peek('=')) { _pos++; }
                return new Token(TokenKind.Eq, "==", 0);
            }

            if (c == '!')
            {
                _pos++;
                if (Peek('=')) { _pos++; return new Token(TokenKind.Ne, "!=", 0); }
                return new Token(TokenKind.Not, "!", 0);
            }

            if (c == '>')
            {
                _pos++;
                if (Peek('=')) { _pos++; return new Token(TokenKind.Ge, ">=", 0); }
                return new Token(TokenKind.Gt, ">", 0);
            }

            if (c == '<')
            {
                _pos++;
                if (Peek('>')) { _pos++; return new Token(TokenKind.Ne, "<>", 0); }
                if (Peek('=')) { _pos++; return new Token(TokenKind.Le, "<=", 0); }
                return new Token(TokenKind.Lt, "<", 0);
            }

            if (c == '&' && Peek('&'))
            {
                _pos += 2;
                return new Token(TokenKind.And, "&&", 0);
            }

            if (c == '|' && Peek('|'))
            {
                _pos += 2;
                return new Token(TokenKind.Or, "||", 0);
            }

            if (c == '\'' || c == '"')
            {
                char quote = c;
                _pos++;
                int start = _pos;
                while (_pos < _text.Length && _text[_pos] != quote)
                {
                    _pos++;
                }
                string str = _text.Substring(start, _pos - start);
                if (_pos < _text.Length && _text[_pos] == quote) _pos++;
                return new Token(TokenKind.String, str, 0);
            }

            if (char.IsDigit(c) || (c == '-' && _pos + 1 < _text.Length && char.IsDigit(_text[_pos + 1])))
            {
                int start = _pos;
                _pos++;
                while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                {
                    _pos++;
                }
                string numStr = _text.Substring(start, _pos - start);
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    return new Token(TokenKind.Number, numStr, val);
                }
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                int start = _pos;
                _pos++;
                while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_' || _text[_pos] == '$'))
                {
                    _pos++;
                }
                string id = _text.Substring(start, _pos - start);
                if (string.Equals(id, "true", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.True, id, 0);
                if (string.Equals(id, "false", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.False, id, 0);
                if (string.Equals(id, "and", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.And, id, 0);
                if (string.Equals(id, "or", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Or, id, 0);
                if (string.Equals(id, "not", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Not, id, 0);
                return new Token(TokenKind.Identifier, id, 0);
            }

            throw new InvalidOperationException($"Unexpected character '{c}' at position {_pos}");
        }

        private bool Peek(char expected)
        {
            return _pos < _text.Length && _text[_pos] == expected;
        }

        private void SkipWs()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }
    }

    private sealed class Parser
    {
        private readonly Lexer _lexer;
        private readonly Dictionary<string, Variable> _vars;
        private Token _current;

        public Parser(string expression, Dictionary<string, Variable> vars)
        {
            _lexer = new Lexer(expression);
            _vars = vars ?? new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);
            _current = _lexer.Next();
        }

        public void Expect(TokenKind kind)
        {
            if (_current.Kind != kind)
            {
                throw new InvalidOperationException($"Expected token '{kind}', got '{_current.Kind}'");
            }
        }

        public ConditionValue ParseExpression()
        {
            return ParseOr();
        }

        private ConditionValue ParseOr()
        {
            var left = ParseAnd();
            while (_current.Kind == TokenKind.Or)
            {
                _current = _lexer.Next();
                var right = ParseAnd();
                left = ConditionValue.FromBool(ToBool(left) || ToBool(right));
            }
            return left;
        }

        private ConditionValue ParseAnd()
        {
            var left = ParseUnary();
            while (_current.Kind == TokenKind.And)
            {
                _current = _lexer.Next();
                var right = ParseUnary();
                left = ConditionValue.FromBool(ToBool(left) && ToBool(right));
            }
            return left;
        }

        private ConditionValue ParseUnary()
        {
            if (_current.Kind == TokenKind.Not)
            {
                _current = _lexer.Next();
                var expr = ParseUnary();
                return ConditionValue.FromBool(!ToBool(expr));
            }
            return ParsePrimary();
        }

        private ConditionValue ParsePrimary()
        {
            var left = ParseValue();
            if (IsCompare(_current.Kind))
            {
                var op = _current.Kind;
                _current = _lexer.Next();
                var right = ParseValue();
                return ConditionValue.FromBool(Compare(op, left, right));
            }
            return left;
        }

        private static bool IsCompare(TokenKind k) => k is TokenKind.Eq or TokenKind.Ne or TokenKind.Gt or TokenKind.Ge or TokenKind.Lt or TokenKind.Le;

        private static bool Compare(TokenKind op, ConditionValue a, ConditionValue b)
        {
            if (a.IsBool || b.IsBool)
            {
                bool ab = ToBool(a);
                bool bb = ToBool(b);
                return op switch
                {
                    TokenKind.Eq => ab == bb,
                    TokenKind.Ne => ab != bb,
                    _ => throw new InvalidOperationException($"Operator '{op}' not supported for boolean")
                };
            }

            if (a.Text is not null || b.Text is not null)
            {
                string sa = a.Text ?? a.Number.ToString(CultureInfo.InvariantCulture);
                string sb = b.Text ?? b.Number.ToString(CultureInfo.InvariantCulture);
                int compStr = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                return op switch
                {
                    TokenKind.Eq => compStr == 0,
                    TokenKind.Ne => compStr != 0,
                    TokenKind.Gt => compStr > 0,
                    TokenKind.Ge => compStr >= 0,
                    TokenKind.Lt => compStr < 0,
                    TokenKind.Le => compStr <= 0,
                    _ => false
                };
            }

            int comp = a.Number.CompareTo(b.Number);
            return op switch
            {
                TokenKind.Eq => comp == 0,
                TokenKind.Ne => comp != 0,
                TokenKind.Gt => comp > 0,
                TokenKind.Ge => comp >= 0,
                TokenKind.Lt => comp < 0,
                TokenKind.Le => comp <= 0,
                _ => false
            };
        }

        private ConditionValue ParseValue()
        {
            if (_current.Kind == TokenKind.True)
            {
                _current = _lexer.Next();
                return ConditionValue.FromBool(true);
            }
            if (_current.Kind == TokenKind.False)
            {
                _current = _lexer.Next();
                return ConditionValue.FromBool(false);
            }
            if (_current.Kind == TokenKind.Number)
            {
                double num = _current.Number;
                _current = _lexer.Next();
                return ConditionValue.FromNumber(num);
            }
            if (_current.Kind == TokenKind.String)
            {
                string str = _current.Text;
                _current = _lexer.Next();
                return ConditionValue.FromString(str);
            }
            if (_current.Kind == TokenKind.LParen)
            {
                _current = _lexer.Next();
                var expr = ParseExpression();
                Expect(TokenKind.RParen);
                _current = _lexer.Next();
                return expr;
            }
            if (_current.Kind == TokenKind.Identifier)
            {
                string id = _current.Text;
                _current = _lexer.Next();

                string? member = null;
                if (_current.Kind == TokenKind.Dot)
                {
                    _current = _lexer.Next();
                    Expect(TokenKind.Identifier);
                    member = _current.Text;
                    _current = _lexer.Next();
                }

                return Resolve(id, member);
            }

            throw new InvalidOperationException($"Unexpected token '{_current.Kind}'");
        }

        private ConditionValue Resolve(string name, string? member)
        {
            Variable? v = null;
            if (!_vars.TryGetValue(name, out v) || v is null)
            {
                // Thử alternate aliases (ví dụ Origin -> Origin1, Point1 -> Point)
                var alternates = GetAlternateVarNames(name);
                foreach (var alt in alternates)
                {
                    if (_vars.TryGetValue(alt, out var vAlt) && vAlt != null)
                    {
                        v = vAlt;
                        break;
                    }
                }

                // Thử tra cứu phẳng name.member nếu có
                if (!string.IsNullOrEmpty(member) && _vars.TryGetValue($"{name}.{member}", out var vFlat) && vFlat != null)
                {
                    if (vFlat.Text != null) return ConditionValue.FromString(vFlat.Text);
                    if (vFlat.Value.HasValue) return ConditionValue.FromNumber(vFlat.Value.Value);
                    return ConditionValue.FromBool(vFlat.Pass);
                }

                if (v is null)
                {
                    throw new InvalidOperationException($"Unknown identifier '{name}'");
                }
            }

            if (string.IsNullOrWhiteSpace(member))
            {
                if (v.Text is not null) return ConditionValue.FromString(v.Text);
                if (v.Value is not null) return ConditionValue.FromNumber(v.Value.Value);
                if (v.Score is not null) return ConditionValue.FromNumber(v.Score.Value);
                return ConditionValue.FromBool(v.Pass);
            }

            // 1. Kiểm tra trực tiếp trong v.Members
            if (v.TryGetMember(member, out var mVal) && mVal != null)
            {
                if (mVal is bool b) return ConditionValue.FromBool(b);
                if (mVal is double d) return ConditionValue.FromNumber(d);
                if (mVal is int i) return ConditionValue.FromNumber(i);
                if (mVal is long l) return ConditionValue.FromNumber(l);
                if (mVal is float f) return ConditionValue.FromNumber(f);
                if (double.TryParse(mVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                {
                    return ConditionValue.FromNumber(parsed);
                }
                return ConditionValue.FromString(mVal.ToString() ?? string.Empty);
            }

            // 2. Tra cứu aliases chuẩn
            if (string.Equals(member, "PASS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "OK", StringComparison.OrdinalIgnoreCase))
            {
                return ConditionValue.FromBool(v.Pass);
            }

            if (string.Equals(member, "FOUND", StringComparison.OrdinalIgnoreCase))
            {
                return ConditionValue.FromBool(v.Found ?? v.Pass);
            }

            if (string.Equals(member, "VALUE", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(member, "COUNT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "ROWCOUNT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "COLUMNCOUNT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "ROWSAFFECTED", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Value.HasValue) return ConditionValue.FromNumber(v.Value.Value);
                if (v.Score.HasValue) return ConditionValue.FromNumber(v.Score.Value);
                throw new InvalidOperationException($"{name}.Value is not available");
            }

            if (string.Equals(member, "SCORE", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(member, "MAXAREA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "AREA", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Score.HasValue) return ConditionValue.FromNumber(v.Score.Value);
                if (v.Value.HasValue) return ConditionValue.FromNumber(v.Value.Value);
                throw new InvalidOperationException($"{name}.Score is not available");
            }

            if (string.Equals(member, "ANGLE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "ANGLEDEG", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "ROTATION", StringComparison.OrdinalIgnoreCase))
            {
                if (v.TryGetMember("Angle", out var a) && a is double da) return ConditionValue.FromNumber(da);
                if (v.TryGetMember("AngleDeg", out var ad) && ad is double dad) return ConditionValue.FromNumber(dad);
                if (v.Value.HasValue) return ConditionValue.FromNumber(v.Value.Value);
            }

            if (string.Equals(member, "X", StringComparison.OrdinalIgnoreCase))
            {
                if (v.TryGetMember("X", out var x) && x is double dx) return ConditionValue.FromNumber(dx);
                if (v.TryGetMember("CenterX", out var cx) && cx is double dcx) return ConditionValue.FromNumber(dcx);
            }

            if (string.Equals(member, "Y", StringComparison.OrdinalIgnoreCase))
            {
                if (v.TryGetMember("Y", out var y) && y is double dy) return ConditionValue.FromNumber(dy);
                if (v.TryGetMember("CenterY", out var cy) && cy is double dcy) return ConditionValue.FromNumber(dcy);
            }

            if (string.Equals(member, "TEXT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "STRING", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Text is not null) return ConditionValue.FromString(v.Text);
                if (v.TryGetMember("Text", out var t) && t != null) return ConditionValue.FromString(t.ToString() ?? string.Empty);
                throw new InvalidOperationException($"{name}.Text is not available");
            }

            throw new InvalidOperationException($"Unknown member '{member}' on '{name}'");
        }
    }
}

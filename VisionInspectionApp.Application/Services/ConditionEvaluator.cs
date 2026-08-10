using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Application;

public static class ConditionEvaluator
{
    internal readonly record struct ConditionValue(bool IsBool, bool Bool, double Number, string? Text)
    {
        public static ConditionValue FromBool(bool v) => new(true, v, 0.0, null);
        public static ConditionValue FromNumber(double v) => new(false, false, v, null);
        public static ConditionValue FromString(string v) => new(false, false, 0.0, v);
    }

    public sealed class Variable
    {
        public Variable(bool pass, double? value = null, double? score = null, bool? found = null, string? text = null)
        {
            Pass = pass;
            Value = value;
            Score = score;
            Found = found;
            Text = text;
        }

        public bool Pass { get; }
        public double? Value { get; }
        public double? Score { get; }
        public bool? Found { get; }
        public string? Text { get; }
    }

    public static Dictionary<string, Variable> BuildVariableMap(InspectionResult result)
    {
        var vars = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);

        if (result.Origin is not null && !string.IsNullOrWhiteSpace(result.Origin.Name))
        {
            vars[result.Origin.Name] = new Variable(result.Origin.Pass, score: result.Origin.Score);
            vars[$"{result.Origin.Name}.X"] = new Variable(result.Origin.Pass, value: result.Origin.Position.X);
            vars[$"{result.Origin.Name}.Y"] = new Variable(result.Origin.Pass, value: result.Origin.Position.Y);
            vars[$"{result.Origin.Name}.Score"] = new Variable(result.Origin.Pass, value: result.Origin.Score);
            vars[$"{result.Origin.Name}.Pass"] = new Variable(result.Origin.Pass);
            vars[$"{result.Origin.Name}.Angle"] = new Variable(result.Origin.Pass, value: result.Origin.AngleDeg);
        }

        foreach (var p in result.Points)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            vars[p.Name] = new Variable(p.Pass, score: p.Score);
            vars[$"{p.Name}.X"] = new Variable(p.Pass, value: p.Position.X);
            vars[$"{p.Name}.Y"] = new Variable(p.Pass, value: p.Position.Y);
            vars[$"{p.Name}.Score"] = new Variable(p.Pass, value: p.Score);
            vars[$"{p.Name}.Pass"] = new Variable(p.Pass);
        }

        foreach (var l in result.Lines)
        {
            if (string.IsNullOrWhiteSpace(l.Name)) continue;
            vars[l.Name] = new Variable(l.Found, found: l.Found);
            vars[$"{l.Name}.Found"] = new Variable(l.Found, found: l.Found);
            vars[$"{l.Name}.Pass"] = new Variable(l.Found);
            vars[$"{l.Name}.Length"] = new Variable(l.Found, value: l.LengthPx);
        }

        foreach (var d in result.Distances)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            vars[d.Name] = new Variable(d.Pass, value: d.Value);
            vars[$"{d.Name}.Value"] = new Variable(d.Pass, value: d.Value);
            vars[$"{d.Name}.Pass"] = new Variable(d.Pass);
        }

        foreach (var dd in result.LineToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name)) continue;
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Value"] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Pass"] = new Variable(dd.Pass);
        }

        foreach (var dd in result.PointToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name)) continue;
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Value"] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Pass"] = new Variable(dd.Pass);
        }

        foreach (var sld in result.SegmentLineDistances)
        {
            if (string.IsNullOrWhiteSpace(sld.Name)) continue;
            vars[sld.Name] = new Variable(sld.Pass, value: sld.Value);
            vars[$"{sld.Name}.Value"] = new Variable(sld.Pass, value: sld.Value);
            vars[$"{sld.Name}.Pass"] = new Variable(sld.Pass);
        }

        foreach (var lpd in result.LinePairDetections)
        {
            if (string.IsNullOrWhiteSpace(lpd.Name)) continue;
            vars[lpd.Name] = new Variable(lpd.Pass, value: lpd.Value, found: lpd.Found);
            vars[$"{lpd.Name}.Value"] = new Variable(lpd.Pass, value: lpd.Value);
            vars[$"{lpd.Name}.Pass"] = new Variable(lpd.Pass);
            vars[$"{lpd.Name}.Found"] = new Variable(lpd.Pass, found: lpd.Found);
            vars[$"LPD.{lpd.Name}"] = new Variable(lpd.Pass, value: lpd.Value, found: lpd.Found);
        }

        foreach (var cf in result.CircleFinders)
        {
            if (string.IsNullOrWhiteSpace(cf.Name)) continue;
            vars[cf.Name] = new Variable(cf.Found, value: cf.RadiusPx, found: cf.Found, score: cf.Score);
            vars[$"{cf.Name}.Value"] = new Variable(cf.Found, value: cf.RadiusPx);
            vars[$"{cf.Name}.RadiusPx"] = new Variable(cf.Found, value: cf.RadiusPx);
            vars[$"{cf.Name}.CenterX"] = new Variable(cf.Found, value: cf.Center.X);
            vars[$"{cf.Name}.CenterY"] = new Variable(cf.Found, value: cf.Center.Y);
            vars[$"{cf.Name}.Found"] = new Variable(cf.Found, found: cf.Found);
            vars[$"{cf.Name}.Pass"] = new Variable(cf.Found);
            vars[$"{cf.Name}.Score"] = new Variable(cf.Found, value: cf.Score);
            vars[$"CIR.{cf.Name}"] = new Variable(cf.Found, value: cf.RadiusPx, found: cf.Found, score: cf.Score);
        }

        foreach (var a in result.Angles)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) continue;
            vars[a.Name] = new Variable(a.Pass, value: a.ValueDeg);
            vars[$"{a.Name}.Value"] = new Variable(a.Pass, value: a.ValueDeg);
            vars[$"{a.Name}.Pass"] = new Variable(a.Pass);
        }

        foreach (var ep in result.EdgePairs)
        {
            if (string.IsNullOrWhiteSpace(ep.Name)) continue;
            vars[ep.Name] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
            vars[$"{ep.Name}.Value"] = new Variable(ep.Pass, value: ep.Value);
            vars[$"{ep.Name}.Pass"] = new Variable(ep.Pass);
            vars[$"{ep.Name}.Found"] = new Variable(ep.Pass, found: ep.Found);
            vars[$"EP.{ep.Name}"] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
            vars[$"EdgePair.{ep.Name}"] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
        }

        foreach (var epd in result.EdgePairDetections)
        {
            if (string.IsNullOrWhiteSpace(epd.Name)) continue;
            vars[epd.Name] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
            vars[$"{epd.Name}.Value"] = new Variable(epd.Pass, value: epd.Value);
            vars[$"{epd.Name}.Pass"] = new Variable(epd.Pass);
            vars[$"{epd.Name}.Found"] = new Variable(epd.Pass, found: epd.Found);
            vars[$"EPD.{epd.Name}"] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
            vars[$"EdgePairDetect.{epd.Name}"] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
        }

        foreach (var c in result.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            vars[c.Name] = new Variable(c.Pass);
            vars[$"{c.Name}.Pass"] = new Variable(c.Pass);
        }

        foreach (var b in result.BlobDetections)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) continue;
            vars[b.Name] = new Variable(true, value: b.Count);
            vars[$"{b.Name}.Count"] = new Variable(true, value: b.Count);
            vars[$"{b.Name}.Value"] = new Variable(true, value: b.Count);
        }

        foreach (var sc in result.SurfaceCompares)
        {
            if (string.IsNullOrWhiteSpace(sc.Name)) continue;
            vars[sc.Name] = new Variable(sc.Pass, value: sc.Count, score: sc.MaxArea);
            vars[$"{sc.Name}.Count"] = new Variable(sc.Pass, value: sc.Count);
            vars[$"{sc.Name}.MaxArea"] = new Variable(sc.Pass, value: sc.MaxArea);
            vars[$"{sc.Name}.Pass"] = new Variable(sc.Pass);
            vars[$"SC.{sc.Name}"] = new Variable(sc.Pass, value: sc.Count, score: sc.MaxArea);
            vars[$"SurfaceCompare.{sc.Name}"] = new Variable(sc.Pass, value: sc.Count, score: sc.MaxArea);
            vars[$"SC.{sc.Name}.MaxArea"] = new Variable(sc.Pass, value: sc.MaxArea);
            vars[$"SurfaceCompare.{sc.Name}.MaxArea"] = new Variable(sc.Pass, value: sc.MaxArea);
        }

        foreach (var c in result.Calipers)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            vars[c.Name] = new Variable(c.Found, value: c.AvgStrength, found: c.Found);
            vars[$"{c.Name}.Value"] = new Variable(c.Found, value: c.AvgStrength);
            vars[$"{c.Name}.Found"] = new Variable(c.Found, found: c.Found);
            vars[$"{c.Name}.Pass"] = new Variable(c.Found);
            vars[$"CAL.{c.Name}"] = new Variable(c.Found, value: c.AvgStrength, found: c.Found);
            vars[$"Caliper.{c.Name}"] = new Variable(c.Found, value: c.AvgStrength, found: c.Found);
        }

        foreach (var cdt in result.CodeDetections)
        {
            if (string.IsNullOrWhiteSpace(cdt.Name)) continue;
            vars[cdt.Name] = new Variable(cdt.Found, found: cdt.Found, text: cdt.Text);
            vars[$"{cdt.Name}.Text"] = new Variable(cdt.Found, text: cdt.Text);
            vars[$"{cdt.Name}.Found"] = new Variable(cdt.Found, found: cdt.Found);
            vars[$"{cdt.Name}.Pass"] = new Variable(cdt.Found);
        }

        foreach (var d in result.Diameters)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            vars[d.Name] = new Variable(d.Pass, value: d.Value, found: d.Found);
            vars[$"{d.Name}.Value"] = new Variable(d.Pass, value: d.Value);
            vars[$"{d.Name}.Pass"] = new Variable(d.Pass);
            vars[$"{d.Name}.Found"] = new Variable(d.Pass, found: d.Found);
            vars[$"CIR.{d.Name}"] = new Variable(d.Pass, value: d.Value, found: d.Found);
            vars[$"Diameter.{d.Name}"] = new Variable(d.Pass, value: d.Value, found: d.Found);
        }

        foreach (var io in result.ImageOutputs)
        {
            if (string.IsNullOrWhiteSpace(io.Name)) continue;
            vars[io.Name] = new Variable(io.Saved, found: io.Saved, text: io.SavedFilePath);
            vars[$"{io.Name}.Saved"] = new Variable(io.Saved, found: io.Saved);
            vars[$"{io.Name}.SavedFilePath"] = new Variable(io.Saved, text: io.SavedFilePath);
            vars[$"Saved.{io.Name}"] = new Variable(io.Saved, found: io.Saved, text: io.SavedFilePath);
        }

        if (result.DbResults is not null)
        {
            foreach (var db in result.DbResults)
            {
                if (string.IsNullOrWhiteSpace(db.NodeName)) continue;

                void AddDbAlias(string aliasName)
                {
                    if (string.IsNullOrWhiteSpace(aliasName)) return;

                    double valNum = 0;
                    string textVal = db.Text ?? string.Empty;
                    if (db.Value != null && double.TryParse(db.Value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
                    {
                        valNum = parsedVal;
                    }

                    var dbVar = new Variable(db.Success, value: valNum, score: db.RowCount, found: db.Success, text: textVal);

                    vars[aliasName] = dbVar;
                    vars[$"{aliasName}.Value"] = new Variable(db.Success, value: valNum, text: db.Value?.ToString() ?? textVal);
                    vars[$"{aliasName}.Text"] = new Variable(db.Success, text: textVal);
                    vars[$"{aliasName}.Pass"] = new Variable(db.Success);
                    vars[$"{aliasName}.Success"] = new Variable(db.Success, value: db.Success ? 1.0 : 0.0);
                    vars[$"{aliasName}.RowCount"] = new Variable(db.Success, value: db.RowCount);
                    vars[$"{aliasName}.ColumnCount"] = new Variable(db.Success, value: db.ColumnCount);
                    vars[$"{aliasName}.RowsAffected"] = new Variable(db.Success, value: db.RowsAffected);

                    foreach (var kvp in db.ColumnMap)
                    {
                        if (string.IsNullOrWhiteSpace(kvp.Key)) continue;

                        double colNum = 0;
                        string colStr = kvp.Value?.ToString() ?? string.Empty;
                        if (kvp.Value != null && double.TryParse(colStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pCol))
                        {
                            colNum = pCol;
                        }

                        vars[$"{aliasName}.{kvp.Key}"] = new Variable(db.Success, value: colNum, text: colStr);
                    }
                }

                AddDbAlias(db.NodeName);
                if (db.NodeName.Contains("Node", StringComparison.OrdinalIgnoreCase))
                {
                    AddDbAlias(db.NodeName.Replace("Node", "", StringComparison.OrdinalIgnoreCase));
                }
                else if (db.NodeName.StartsWith("DB", StringComparison.OrdinalIgnoreCase) && db.NodeName.Length > 2 && char.IsDigit(db.NodeName[2]))
                {
                    AddDbAlias($"DbNode{db.NodeName[2..]}");
                }
                AddDbAlias("DB");
            }
        }

        return vars;
    }

    public static string EvaluateTextTemplate(string text, Dictionary<string, Variable>? vars)
    {
        if (string.IsNullOrEmpty(text) || vars is null || vars.Count == 0)
        {
            return text ?? string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(text, @"\{([^}]+)\}", m =>
        {
            var inner = m.Groups[1].Value?.Trim() ?? string.Empty;
            if (inner.Length == 0)
                return string.Empty;

            var fmt = string.Empty;
            var colonIdx = inner.IndexOf(':');
            if (colonIdx >= 0)
            {
                fmt = inner[(colonIdx + 1)..].Trim();
                inner = inner[..colonIdx].Trim();
            }

            if (vars.TryGetValue(inner, out var vDirect) && vDirect is not null)
            {
                object? directVal = vDirect.Text ?? (object?)vDirect.Value ?? vDirect.Found ?? vDirect.Pass;
                if (directVal is double dD)
                {
                    return string.IsNullOrWhiteSpace(fmt) ? dD.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : dD.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                }
                if (directVal is bool bD)
                {
                    return bD ? "True" : "False";
                }
                return directVal?.ToString() ?? string.Empty;
            }

            var varName = inner;
            var prop = string.Empty;
            var dotIdx = inner.IndexOf('.');
            if (dotIdx >= 0)
            {
                varName = inner[..dotIdx].Trim();
                prop = inner[(dotIdx + 1)..].Trim();
            }

            if (string.IsNullOrWhiteSpace(varName) || !vars.TryGetValue(varName, out var v) || v is null)
            {
                if (inner.StartsWith("DB", StringComparison.OrdinalIgnoreCase) || inner.StartsWith("DbNode", StringComparison.OrdinalIgnoreCase))
                {
                    string altKey = inner.Contains("Node", StringComparison.OrdinalIgnoreCase)
                        ? inner.Replace("Node", "", StringComparison.OrdinalIgnoreCase)
                        : inner;

                    if (vars.TryGetValue(altKey, out var vAlt) && vAlt is not null)
                    {
                        object? altVal = vAlt.Text ?? (object?)vAlt.Value ?? vAlt.Found ?? vAlt.Pass;
                        return altVal?.ToString() ?? string.Empty;
                    }

                    return string.Empty;
                }
                return m.Value;
            }

            object? valueObj = null;
            if (string.IsNullOrWhiteSpace(prop))
            {
                valueObj = v.Text ?? (object?)v.Value ?? v.Found ?? v.Pass;
            }
            else if (string.Equals(prop, "Pass", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Pass;
            }
            else if (string.Equals(prop, "Value", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Value ?? (object?)v.Score ?? v.Found ?? v.Pass;
            }
            else if (string.Equals(prop, "Score", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Score ?? v.Value;
            }
            else if (string.Equals(prop, "Found", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Found ?? v.Pass;
            }
            else if (string.Equals(prop, "Text", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Text ?? v.Value?.ToString() ?? v.Pass.ToString();
            }
            else if (string.Equals(prop, "Count", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Value;
            }
            else if (string.Equals(prop, "MaxArea", StringComparison.OrdinalIgnoreCase) || string.Equals(prop, "Area", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Score;
            }

            if (valueObj is null)
            {
                return string.Empty;
            }

            if (valueObj is double d)
            {
                return string.IsNullOrWhiteSpace(fmt) ? d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : d.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (valueObj is bool b)
            {
                return b ? "True" : "False";
            }

            return valueObj.ToString() ?? string.Empty;
        });
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
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
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
            _vars = vars;
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
                string sa = a.Text ?? a.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string sb = b.Text ?? b.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            if (!_vars.TryGetValue(name, out var v))
            {
                throw new InvalidOperationException($"Unknown identifier '{name}'");
            }

            if (string.IsNullOrWhiteSpace(member))
            {
                if (v.Text is not null) return ConditionValue.FromString(v.Text);
                if (v.Value is not null) return ConditionValue.FromNumber(v.Value.Value);
                return ConditionValue.FromBool(v.Pass);
            }

            if (string.Equals(member, "PASS", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(v.Pass);
            if (string.Equals(member, "SUCCESS", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(v.Pass);
            if (string.Equals(member, "VALUE", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(member, "COUNT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "ROWCOUNT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "COLUMNCOUNT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "ROWSAFFECTED", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Value is null) throw new InvalidOperationException($"{name}.Value is not available");
                return ConditionValue.FromNumber(v.Value.Value);
            }
            if (string.Equals(member, "SCORE", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(member, "MAXAREA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "AREA", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Score is null) throw new InvalidOperationException($"{name}.Score is not available");
                return ConditionValue.FromNumber(v.Score.Value);
            }
            if (string.Equals(member, "FOUND", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Found is null) throw new InvalidOperationException($"{name}.Found is not available");
                return ConditionValue.FromBool(v.Found.Value);
            }

            if (string.Equals(member, "TEXT", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Text is null) throw new InvalidOperationException($"{name}.Text is not available");
                return ConditionValue.FromString(v.Text);
            }

            throw new InvalidOperationException($"Unknown member '{member}' on '{name}'");
        }

        private static bool ToBool(ConditionValue v)
        {
            if (v.IsBool) return v.Bool;
            throw new InvalidOperationException("Expected boolean");
        }
    }
}

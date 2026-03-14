using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public interface IConfigService
{
    VisionConfig LoadConfig(string productCode);

    void SaveConfig(VisionConfig config);
}

public sealed class ConfigStoreOptions
{
    public string ConfigRootDirectory { get; set; } = "configs";
}

public sealed record PointMatchResult(string Name, Point2d Position, Rect MatchRect, double Score, double Threshold, bool Pass, double AngleDeg);

public sealed class InspectionResult
{
    public bool Pass { get; set; }

    public PointMatchResult? Origin { get; set; }

    public List<PointMatchResult> Points { get; } = new();

    public List<LineDetectResult> Lines { get; } = new();

    public List<DistanceCheckResult> Distances { get; } = new();

    public List<SegmentDistanceResult> LineToLineDistances { get; } = new();

    public List<SegmentDistanceResult> PointToLineDistances { get; } = new();

    public List<ConditionResult> Conditions { get; } = new();

    public DefectDetectionResult? Defects { get; set; }
}

public sealed record ConditionResult(string Name, string Expression, bool Pass, string? Error);

public interface IInspectionService
{
    InspectionResult Inspect(Mat image, VisionConfig config);
}

public sealed class InspectionService : IInspectionService
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly PatternMatcher _matcher;
    private readonly DistanceCalculator _distanceCalculator;
    private readonly LineDetector _lineDetector;
    private readonly IDefectDetector _defectDetector;

    public InspectionService(
        ImagePreprocessor preprocessor,
        PatternMatcher matcher,
        DistanceCalculator distanceCalculator,
        LineDetector lineDetector,
        IDefectDetector defectDetector)
    {
        _preprocessor = preprocessor;
        _matcher = matcher;
        _distanceCalculator = distanceCalculator;
        _lineDetector = lineDetector;
        _defectDetector = defectDetector;
    }

    public InspectionResult Inspect(Mat image, VisionConfig config)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        using var processed = _preprocessor.Run(image, config.Preprocess);

        var result = new InspectionResult();

        var originMatch = _matcher.MatchWithRotation(processed, config.Origin, config.Preprocess, -60.0, 60.0, 2.0);
        var templateAngleDeg = originMatch.AngleDeg;
        var poseAngleDeg = -templateAngleDeg;
        var originPass = originMatch.Score >= config.Origin.MatchScoreThreshold;
        result.Origin = new PointMatchResult(
            config.Origin.Name,
            originMatch.Position,
            originMatch.MatchRect,
            originMatch.Score,
            config.Origin.MatchScoreThreshold,
            originPass,
            poseAngleDeg);

        var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
        var originFound = originMatch.Position;
        var angleDeg = poseAngleDeg;

        var foundPoints = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in config.Points)
        {
            var def = TransformPointDefinition(p, originTeach, originFound, angleDeg);
            var m = _matcher.MatchWithFixedRotation(processed, def, templateAngleDeg, config.Preprocess);
            var pass = m.Score >= p.MatchScoreThreshold;

            result.Points.Add(new PointMatchResult(p.Name, m.Position, m.MatchRect, m.Score, p.MatchScoreThreshold, pass, 0.0));
            foundPoints[p.Name] = m.Position;
        }

        var foundLines = new Dictionary<string, LineDetectResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in config.Lines)
        {
            var roi = TransformRoiKeepSize(l.SearchRoi, originTeach, originFound, angleDeg);
            var det = _lineDetector.DetectLongestLine(processed, roi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
            var named = det with { Name = l.Name };
            result.Lines.Add(named);
            foundLines[l.Name] = named;
        }

        foreach (var d in config.Distances)
        {
            if (!foundPoints.TryGetValue(d.PointA, out var a) || !foundPoints.TryGetValue(d.PointB, out var b))
            {
                result.Distances.Add(new DistanceCheckResult(d.Name, d.PointA, d.PointB, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, false));
                continue;
            }

            result.Distances.Add(_distanceCalculator.CheckDistance(d, a, b, config.PixelsPerMm));
        }

        foreach (var dd in config.LineToLineDistances)
        {
            if (!foundLines.TryGetValue(dd.LineA, out var la) || !foundLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
            {
                result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                continue;
            }

            var (distPx, ca, cb) = CalculateLineLineDistance(la, lb, dd.Mode);
            var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
            var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
            result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, ca, cb));
        }

        foreach (var dd in config.PointToLineDistances)
        {
            if (!foundPoints.TryGetValue(dd.Point, out var p) || !foundLines.TryGetValue(dd.Line, out var l) || !l.Found)
            {
                result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                continue;
            }

            var (distPx, closest) = CalculatePointLineDistance(p, l, dd.Mode);
            var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
            var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
            result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, p, closest));
        }

        EvaluateConditions(config, result);

        var defectConfig = TransformDefectConfig(config.DefectConfig, originTeach, originFound, angleDeg);
        result.Defects = _defectDetector.Detect(processed, defectConfig);

        result.Pass = originPass
            && result.Points.All(x => x.Pass)
            && result.Distances.All(x => x.Pass)
            && result.LineToLineDistances.All(x => x.Pass)
            && result.PointToLineDistances.All(x => x.Pass)
            && result.Conditions.All(x => x.Pass)
            && (result.Defects.Defects.Count == 0);

        return result;
    }

    private static void EvaluateConditions(VisionConfig config, InspectionResult result)
    {
        if (config.Conditions is null || config.Conditions.Count == 0)
        {
            return;
        }

        var vars = ConditionEvaluator.BuildVariableMap(result);
        foreach (var c in config.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                continue;
            }

            var expr = c.Expression ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expr))
            {
                result.Conditions.Add(new ConditionResult(c.Name, expr, false, "Empty expression"));
                continue;
            }

            try
            {
                var ok = ConditionEvaluator.Evaluate(expr, vars);
                result.Conditions.Add(new ConditionResult(c.Name, expr, ok, null));
            }
            catch (Exception ex)
            {
                result.Conditions.Add(new ConditionResult(c.Name, expr, false, ex.Message));
            }
        }
    }

    private static (double DistPx, Point2d A, Point2d B) CalculateLineLineDistance(LineDetectResult la, LineDetectResult lb, LineLineDistanceMode mode)
    {
        // Default / legacy
        if (mode == LineLineDistanceMode.ClosestPointsOnSegments)
        {
            return Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
        }

        if (mode == LineLineDistanceMode.MidpointToMidpoint)
        {
            var ma = new Point2d((la.P1.X + la.P2.X) * 0.5, (la.P1.Y + la.P2.Y) * 0.5);
            var mb = new Point2d((lb.P1.X + lb.P2.X) * 0.5, (lb.P1.Y + lb.P2.Y) * 0.5);
            return (Geometry2D.Distance(ma, mb), ma, mb);
        }

        // Endpoints based
        var aEnds = new[] { la.P1, la.P2 };
        var bEnds = new[] { lb.P1, lb.P2 };

        var bestDist = mode == LineLineDistanceMode.FarthestEndpoints ? double.NegativeInfinity : double.PositiveInfinity;
        var bestA = la.P1;
        var bestB = lb.P1;

        foreach (var a in aEnds)
        {
            foreach (var b in bEnds)
            {
                var d = Geometry2D.Distance(a, b);
                if (mode == LineLineDistanceMode.NearestEndpoints)
                {
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestA = a;
                        bestB = b;
                    }
                }
                else if (mode == LineLineDistanceMode.FarthestEndpoints)
                {
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestA = a;
                        bestB = b;
                    }
                }
            }
        }

        return (bestDist, bestA, bestB);
    }

    private static (double DistPx, Point2d Closest) CalculatePointLineDistance(Point2d p, LineDetectResult l, PointLineDistanceMode mode)
    {
        if (mode == PointLineDistanceMode.PointToInfiniteLine)
        {
            var a = l.P1;
            var b = l.P2;
            var ab = b - a;
            var ap = p - a;
            var ab2 = ab.X * ab.X + ab.Y * ab.Y;
            if (ab2 <= 1e-12)
            {
                return (Geometry2D.Distance(p, a), a);
            }

            var t = (ap.X * ab.X + ap.Y * ab.Y) / ab2;
            var proj = new Point2d(a.X + t * ab.X, a.Y + t * ab.Y);
            return (Geometry2D.Distance(p, proj), proj);
        }

        // Default / legacy
        return Geometry2D.PointToSegmentDistance(p, l.P1, l.P2);
    }

    private static Point2d Rotate(Point2d p, Point2d origin, double angleDeg)
    {
        if (Math.Abs(angleDeg) < 0.000001)
        {
            return p;
        }

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);

        var dx = p.X - origin.X;
        var dy = p.Y - origin.Y;
        var x = dx * cos - dy * sin;
        var y = dx * sin + dy * cos;
        return new Point2d(x + origin.X, y + origin.Y);
    }

    private static Roi TransformRoi(Roi roi, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new Roi();
        }

        var p1 = new Point2d(roi.X, roi.Y);
        var p2 = new Point2d(roi.X + roi.Width, roi.Y);
        var p3 = new Point2d(roi.X + roi.Width, roi.Y + roi.Height);
        var p4 = new Point2d(roi.X, roi.Y + roi.Height);

        p1 = Rotate(p1, originTeach, angleDeg);
        p2 = Rotate(p2, originTeach, angleDeg);
        p3 = Rotate(p3, originTeach, angleDeg);
        p4 = Rotate(p4, originTeach, angleDeg);

        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;

        p1 = new Point2d(p1.X + dx, p1.Y + dy);
        p2 = new Point2d(p2.X + dx, p2.Y + dy);
        p3 = new Point2d(p3.X + dx, p3.Y + dy);
        p4 = new Point2d(p4.X + dx, p4.Y + dy);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new Roi
        {
            X = (int)Math.Round(minX),
            Y = (int)Math.Round(minY),
            Width = (int)Math.Round(maxX - minX),
            Height = (int)Math.Round(maxY - minY)
        };
    }

    private static Roi TransformRoiKeepSize(Roi roi, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new Roi();
        }

        var centerTeach = new Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);
        var centerRot = Rotate(centerTeach, originTeach, angleDeg);

        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        var centerFound = new Point2d(centerRot.X + dx, centerRot.Y + dy);

        return new Roi
        {
            X = (int)Math.Round(centerFound.X - roi.Width / 2.0),
            Y = (int)Math.Round(centerFound.Y - roi.Height / 2.0),
            Width = roi.Width,
            Height = roi.Height
        };
    }

    private static PointDefinition TransformPointDefinition(PointDefinition p, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        return new PointDefinition
        {
            Name = p.Name,
            MatchScoreThreshold = p.MatchScoreThreshold,
            TemplateImageFile = p.TemplateImageFile,
            TemplateRoi = p.TemplateRoi,
            SearchRoi = TransformRoiKeepSize(p.SearchRoi, originTeach, originFound, angleDeg),
            WorldPosition = p.WorldPosition
        };
    }

    private static DefectInspectionConfig TransformDefectConfig(DefectInspectionConfig cfg, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        return new DefectInspectionConfig
        {
            InspectRoi = TransformRoi(cfg.InspectRoi, originTeach, originFound, angleDeg),
            ThresholdWhite = cfg.ThresholdWhite,
            ThresholdBlack = cfg.ThresholdBlack,
            MinBlobSize = cfg.MinBlobSize,
            MaxBlobSize = cfg.MaxBlobSize
        };
    }
}

internal static class ConditionEvaluator
{
    internal readonly record struct ConditionValue(bool IsBool, bool Bool, double Number)
    {
        public static ConditionValue FromBool(bool v) => new(true, v, 0.0);
        public static ConditionValue FromNumber(double v) => new(false, false, v);
    }

    internal sealed class Variable
    {
        public Variable(bool pass, double? value = null, double? score = null, bool? found = null)
        {
            Pass = pass;
            Value = value;
            Score = score;
            Found = found;
        }

        public bool Pass { get; }
        public double? Value { get; }
        public double? Score { get; }
        public bool? Found { get; }
    }

    public static Dictionary<string, Variable> BuildVariableMap(InspectionResult result)
    {
        var vars = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);

        if (result.Origin is not null)
        {
            vars[result.Origin.Name] = new Variable(result.Origin.Pass, score: result.Origin.Score);
        }

        foreach (var p in result.Points)
        {
            vars[p.Name] = new Variable(p.Pass, score: p.Score);
        }

        foreach (var l in result.Lines)
        {
            vars[l.Name] = new Variable(l.Found, found: l.Found);
        }

        foreach (var d in result.Distances)
        {
            vars[d.Name] = new Variable(d.Pass, value: d.Value);
        }

        foreach (var dd in result.LineToLineDistances)
        {
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
        }

        foreach (var dd in result.PointToLineDistances)
        {
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
        }

        foreach (var c in result.Conditions)
        {
            vars[c.Name] = new Variable(c.Pass);
        }

        return vars;
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
        LParen,
        RParen,
        Dot,
        And,
        Or,
        Not,
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
        private readonly string _s;
        private int _i;

        public Lexer(string s) => _s = s ?? string.Empty;

        public Token Next()
        {
            SkipWs();
            if (_i >= _s.Length) return new Token(TokenKind.Eof, string.Empty, 0);

            var ch = _s[_i];
            if (char.IsLetter(ch) || ch == '_')
            {
                var start = _i++;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '-')) _i++;
                var t = _s.Substring(start, _i - start);
                if (string.Equals(t, "AND", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.And, t, 0);
                if (string.Equals(t, "OR", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Or, t, 0);
                if (string.Equals(t, "NOT", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Not, t, 0);
                if (string.Equals(t, "TRUE", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Number, t, 1);
                if (string.Equals(t, "FALSE", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Number, t, 0);
                return new Token(TokenKind.Identifier, t, 0);
            }

            if (char.IsDigit(ch) || (ch == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
            {
                var start = _i;
                var hasDot = false;
                if (ch == '.') { hasDot = true; _i++; }
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (char.IsDigit(c)) { _i++; continue; }
                    if (c == '.' && !hasDot) { hasDot = true; _i++; continue; }
                    break;
                }

                var t = _s.Substring(start, _i - start);
                if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    throw new InvalidOperationException($"Invalid number '{t}'");
                }
                return new Token(TokenKind.Number, t, n);
            }

            _i++;
            return ch switch
            {
                '(' => new Token(TokenKind.LParen, "(", 0),
                ')' => new Token(TokenKind.RParen, ")", 0),
                '.' => new Token(TokenKind.Dot, ".", 0),
                '>' => Peek('=') ? new Token(TokenKind.Ge, ">=", 0) : new Token(TokenKind.Gt, ">", 0),
                '<' => Peek('=') ? new Token(TokenKind.Le, "<=", 0) : new Token(TokenKind.Lt, "<", 0),
                '=' => Peek('=') ? new Token(TokenKind.Eq, "==", 0) : throw new InvalidOperationException("Use '==' for equality"),
                '!' => Peek('=') ? new Token(TokenKind.Ne, "!=", 0) : throw new InvalidOperationException("Use '!=' for not-equal"),
                _ => throw new InvalidOperationException($"Unexpected character '{ch}'")
            };
        }

        private bool Peek(char expected)
        {
            if (_i < _s.Length && _s[_i] == expected)
            {
                _i++;
                return true;
            }
            return false;
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }

    private sealed class Parser
    {
        private readonly Lexer _lex;
        private readonly Dictionary<string, Variable> _vars;
        private Token _t;

        public Parser(string s, Dictionary<string, Variable> vars)
        {
            _lex = new Lexer(s);
            _vars = vars;
            _t = _lex.Next();
        }

        public ConditionValue ParseExpression() => ParseOr();

        public void Expect(TokenKind kind)
        {
            if (_t.Kind != kind)
            {
                throw new InvalidOperationException($"Expected {kind} but got '{_t.Text}'");
            }
            _t = _lex.Next();
        }

        private ConditionValue ParseOr()
        {
            var left = ParseAnd();
            while (_t.Kind == TokenKind.Or)
            {
                Expect(TokenKind.Or);
                var right = ParseAnd();
                left = ConditionValue.FromBool(ToBool(left) || ToBool(right));
            }
            return left;
        }

        private ConditionValue ParseAnd()
        {
            var left = ParseUnary();
            while (_t.Kind == TokenKind.And)
            {
                Expect(TokenKind.And);
                var right = ParseUnary();
                left = ConditionValue.FromBool(ToBool(left) && ToBool(right));
            }
            return left;
        }

        private ConditionValue ParseUnary()
        {
            if (_t.Kind == TokenKind.Not)
            {
                Expect(TokenKind.Not);
                var v = ParseUnary();
                return ConditionValue.FromBool(!ToBool(v));
            }
            return ParsePrimary();
        }

        private ConditionValue ParsePrimary()
        {
            if (_t.Kind == TokenKind.LParen)
            {
                Expect(TokenKind.LParen);
                var v = ParseExpression();
                Expect(TokenKind.RParen);
                return v;
            }

            var left = ParseValue();
            if (IsCompare(_t.Kind))
            {
                var op = _t.Kind;
                _t = _lex.Next();
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
                var ba = a.IsBool ? a.Bool : throw new InvalidOperationException("Left side is not boolean");
                var bb = b.IsBool ? b.Bool : throw new InvalidOperationException("Right side is not boolean");
                return op switch
                {
                    TokenKind.Eq => ba == bb,
                    TokenKind.Ne => ba != bb,
                    _ => throw new InvalidOperationException("Only == and != are allowed for booleans")
                };
            }

            var na = a.Number;
            var nb = b.Number;
            return op switch
            {
                TokenKind.Eq => Math.Abs(na - nb) < 0.0000001,
                TokenKind.Ne => Math.Abs(na - nb) >= 0.0000001,
                TokenKind.Gt => na > nb,
                TokenKind.Ge => na >= nb,
                TokenKind.Lt => na < nb,
                TokenKind.Le => na <= nb,
                _ => false
            };
        }

        private ConditionValue ParseValue()
        {
            if (_t.Kind == TokenKind.Number)
            {
                var n = _t.Number;
                var txt = _t.Text;
                Expect(TokenKind.Number);

                if (string.Equals(txt, "TRUE", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(true);
                if (string.Equals(txt, "FALSE", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(false);
                return ConditionValue.FromNumber(n);
            }

            if (_t.Kind == TokenKind.Identifier)
            {
                var name = _t.Text;
                Expect(TokenKind.Identifier);

                string? member = null;
                if (_t.Kind == TokenKind.Dot)
                {
                    Expect(TokenKind.Dot);
                    if (_t.Kind != TokenKind.Identifier)
                    {
                        throw new InvalidOperationException("Expected member after '.'");
                    }
                    member = _t.Text;
                    Expect(TokenKind.Identifier);
                }

                return Resolve(name, member);
            }

            throw new InvalidOperationException($"Unexpected token '{_t.Text}'");
        }

        private ConditionValue Resolve(string name, string? member)
        {
            if (!_vars.TryGetValue(name, out var v))
            {
                throw new InvalidOperationException($"Unknown identifier '{name}'");
            }

            if (string.IsNullOrWhiteSpace(member))
            {
                return ConditionValue.FromBool(v.Pass);
            }

            if (string.Equals(member, "PASS", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(v.Pass);
            if (string.Equals(member, "VALUE", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Value is null) throw new InvalidOperationException($"{name}.Value is not available");
                return ConditionValue.FromNumber(v.Value.Value);
            }
            if (string.Equals(member, "SCORE", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Score is null) throw new InvalidOperationException($"{name}.Score is not available");
                return ConditionValue.FromNumber(v.Score.Value);
            }
            if (string.Equals(member, "FOUND", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Found is null) throw new InvalidOperationException($"{name}.Found is not available");
                return ConditionValue.FromBool(v.Found.Value);
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

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public static partial class ConditionEvaluator
{
    public sealed class Variable
    {
        public Variable(
            bool pass, 
            double? value = null, 
            double? score = null, 
            bool? found = null, 
            string? text = null, 
            object? rawObject = null, 
            IDictionary<string, object?>? members = null)
        {
            Pass = pass;
            Value = value;
            Score = score;
            Found = found;
            Text = text;
            RawObject = rawObject;

            if (members != null && members.Count > 0)
            {
                Members = new Dictionary<string, object?>(members, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Members = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public bool Pass { get; }
        public double? Value { get; }
        public double? Score { get; }
        public bool? Found { get; }
        public string? Text { get; }
        public object? RawObject { get; }
        public Dictionary<string, object?> Members { get; }

        public void SetMember(string name, object? val)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Members[name] = val;
            }
        }

        public bool TryGetMember(string name, out object? val)
        {
            if (Members.TryGetValue(name, out val))
            {
                return true;
            }
            val = null;
            return false;
        }
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> CachedTypeProperties = new();

    private static PropertyInfo[] GetCachedProperties(Type type)
    {
        return CachedTypeProperties.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>
    /// Xây dựng bản đồ biến (Variable Map) toàn diện và đa định danh (Multi-Alias)
    /// cho toàn bộ kết quả của mọi Tool trong InspectionResult.
    /// </summary>
    public static Dictionary<string, Variable> BuildVariableMap(InspectionResult result, VisionConfig? config = null)
    {
        var vars = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);
        if (result is null) return vars;

        var scale = config?.PixelsPerMm ?? 1.0;
        var hasScale = scale > 0 && Math.Abs(scale - 1.0) > 1e-6;

        // Trích xuất danh sách node trên đồ thị để ánh xạ RefName <-> Name <-> Type
        var nodeRefToType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var typeToNodeRefs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (config?.ToolGraph?.Nodes != null)
        {
            foreach (var node in config.ToolGraph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.RefName)) continue;
                nodeRefToType[node.RefName] = node.Type ?? string.Empty;

                var tKey = node.Type ?? string.Empty;
                if (!typeToNodeRefs.TryGetValue(tKey, out var list))
                {
                    list = new List<string>();
                    typeToNodeRefs[tKey] = list;
                }
                list.Add(node.RefName);
            }
        }

        void RegisterToolVariable(string primaryName, IEnumerable<string>? aliases, Variable variable)
        {
            if (string.IsNullOrWhiteSpace(primaryName) || variable is null) return;

            var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryName };
            if (aliases != null)
            {
                foreach (var a in aliases)
                {
                    if (!string.IsNullOrWhiteSpace(a)) allNames.Add(a);
                }
            }

            foreach (var name in allNames)
            {
                vars[name] = variable;

                // Đăng ký tất cả các thành viên dạng phẳng: name.Prop
                foreach (var kvp in variable.Members)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                    var fullKey = $"{name}.{kvp.Key}";

                    double? numVal = null;
                    bool? boolVal = null;
                    string? strVal = kvp.Value?.ToString();

                    if (kvp.Value is double dVal) numVal = dVal;
                    else if (kvp.Value is float fVal) numVal = fVal;
                    else if (kvp.Value is int iVal) numVal = iVal;
                    else if (kvp.Value is long lVal) numVal = lVal;
                    else if (kvp.Value is bool bVal) boolVal = bVal;

                    vars[fullKey] = new Variable(
                        boolVal ?? variable.Pass,
                        value: numVal,
                        score: numVal,
                        found: boolVal ?? variable.Found,
                        text: strVal,
                        rawObject: kvp.Value);
                }

                // Thuộc tính mặc định nếu chưa có
                if (!variable.Members.ContainsKey("Pass")) vars[$"{name}.Pass"] = new Variable(variable.Pass, found: variable.Pass);
                if (!variable.Members.ContainsKey("Status")) vars[$"{name}.Status"] = new Variable(variable.Pass, text: variable.Pass ? "OK" : "NG");
                if (!variable.Members.ContainsKey("PassBit")) vars[$"{name}.PassBit"] = new Variable(variable.Pass, value: variable.Pass ? 1.0 : 0.0, text: variable.Pass ? "1" : "0");
                if (!variable.Members.ContainsKey("FailBit")) vars[$"{name}.FailBit"] = new Variable(!variable.Pass, value: !variable.Pass ? 1.0 : 0.0, text: !variable.Pass ? "1" : "0");

                if (variable.Value.HasValue && !variable.Members.ContainsKey("Value"))
                {
                    vars[$"{name}.Value"] = new Variable(variable.Pass, value: variable.Value.Value);
                }
                if (variable.Score.HasValue && !variable.Members.ContainsKey("Score"))
                {
                    vars[$"{name}.Score"] = new Variable(variable.Pass, value: variable.Score.Value, score: variable.Score.Value);
                }
                if (variable.Found.HasValue && !variable.Members.ContainsKey("Found"))
                {
                    vars[$"{name}.Found"] = new Variable(variable.Found.Value, found: variable.Found.Value);
                }
                if (variable.Text != null && !variable.Members.ContainsKey("Text"))
                {
                    vars[$"{name}.Text"] = new Variable(variable.Pass, text: variable.Text);
                }
            }
        }

        // ==========================================
        // 1. ORIGIN TOOL
        // ==========================================
        if (result.Origin != null)
        {
            var o = result.Origin;
            var oName = string.IsNullOrWhiteSpace(o.Name) ? "Origin" : o.Name;
            var oAliases = new List<string> { "Origin", "Origin1", "Origin_1", "Pattern", "Pattern1" };
            if (typeToNodeRefs.TryGetValue("Origin", out var originNodes))
            {
                oAliases.AddRange(originNodes);
            }

            var oMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["X"] = o.Position.X,
                ["Y"] = o.Position.Y,
                ["X_px"] = o.Position.X,
                ["Y_px"] = o.Position.Y,
                ["X_mm"] = hasScale ? o.Position.X / scale : o.Position.X,
                ["Y_mm"] = hasScale ? o.Position.Y / scale : o.Position.Y,
                ["Angle"] = o.AngleDeg,
                ["AngleDeg"] = o.AngleDeg,
                ["Rotation"] = o.AngleDeg,
                ["PoseAngle"] = o.AngleDeg,
                ["Score"] = o.Score,
                ["Threshold"] = o.Threshold,
                ["MinScore"] = o.Threshold,
                ["Pass"] = o.Pass,
                ["Status"] = o.Pass ? "OK" : "NG",
                ["PassBit"] = o.Pass ? 1 : 0,
                ["FailBit"] = o.Pass ? 0 : 1,
                ["Width"] = (double)o.MatchRect.Width,
                ["Height"] = (double)o.MatchRect.Height,
                ["FeatureCount"] = (double)(o.FeaturePoints?.Count ?? 0),
                ["Time"] = result.Timings.OriginMs > 0 ? (double)result.Timings.OriginMs : (result.Timings.NodeTimings.TryGetValue(oName, out var tMs) ? (double)tMs : 0.0),
                ["ExecutionTime"] = result.Timings.OriginMs > 0 ? (double)result.Timings.OriginMs : (result.Timings.NodeTimings.TryGetValue(oName, out var tMs2) ? (double)tMs2 : 0.0)
            };

            var oVar = new Variable(o.Pass, value: o.Score, score: o.Score, found: o.Pass, text: o.Pass ? "OK" : "NG", rawObject: o, members: oMembers);
            RegisterToolVariable(oName, oAliases, oVar);
        }

        // ==========================================
        // 2. POINTS
        // ==========================================
        for (int i = 0; i < result.Points.Count; i++)
        {
            var p = result.Points[i];
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            var pAliases = new List<string> { p.Name, $"Point{i + 1}", $"P{i + 1}", $"Point.{p.Name}" };
            var pMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["X"] = p.Position.X,
                ["Y"] = p.Position.Y,
                ["X_px"] = p.Position.X,
                ["Y_px"] = p.Position.Y,
                ["X_mm"] = hasScale ? p.Position.X / scale : p.Position.X,
                ["Y_mm"] = hasScale ? p.Position.Y / scale : p.Position.Y,
                ["Angle"] = p.AngleDeg,
                ["AngleDeg"] = p.AngleDeg,
                ["Score"] = p.Score,
                ["Threshold"] = p.Threshold,
                ["Pass"] = p.Pass,
                ["Status"] = p.Pass ? "OK" : "NG",
                ["PassBit"] = p.Pass ? 1 : 0,
                ["Width"] = (double)p.MatchRect.Width,
                ["Height"] = (double)p.MatchRect.Height,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(p.Name, out var ptMs) ? (double)ptMs : 0.0
            };

            var pVar = new Variable(p.Pass, value: p.Score, score: p.Score, found: p.Pass, text: p.Pass ? "OK" : "NG", rawObject: p, members: pMembers);
            RegisterToolVariable(p.Name, pAliases, pVar);
        }

        // ==========================================
        // 3. LINES
        // ==========================================
        for (int i = 0; i < result.Lines.Count; i++)
        {
            var l = result.Lines[i];
            if (string.IsNullOrWhiteSpace(l.Name)) continue;

            var dx = l.P2.X - l.P1.X;
            var dy = l.P2.Y - l.P1.Y;
            var angleDeg = Math.Atan2(dy, dx) * (180.0 / Math.PI);

            var lAliases = new List<string> { l.Name, $"Line{i + 1}", $"L{i + 1}" };
            var lMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Found"] = l.Found,
                ["Pass"] = l.Found,
                ["Status"] = l.Found ? "OK" : "NG",
                ["PassBit"] = l.Found ? 1 : 0,
                ["Length"] = l.LengthPx,
                ["LengthPx"] = l.LengthPx,
                ["Length_px"] = l.LengthPx,
                ["Length_mm"] = hasScale ? l.LengthPx / scale : l.LengthPx,
                ["Value"] = l.LengthPx,
                ["X1"] = l.P1.X,
                ["Y1"] = l.P1.Y,
                ["X2"] = l.P2.X,
                ["Y2"] = l.P2.Y,
                ["Angle"] = angleDeg,
                ["AngleDeg"] = angleDeg,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(l.Name, out var ltMs) ? (double)ltMs : 0.0
            };

            var lVar = new Variable(l.Found, value: l.LengthPx, found: l.Found, text: l.Found ? "OK" : "NG", rawObject: l, members: lMembers);
            RegisterToolVariable(l.Name, lAliases, lVar);
        }

        // ==========================================
        // 4. DISTANCES (LineDistance)
        // ==========================================
        for (int i = 0; i < result.Distances.Count; i++)
        {
            var d = result.Distances[i];
            if (string.IsNullOrWhiteSpace(d.Name)) continue;

            var dAliases = new List<string> { d.Name, $"Distance{i + 1}", $"Dist{i + 1}", $"Dist.{d.Name}" };
            var dMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = d.Value,
                ["Distance"] = d.Value,
                ["Value_px"] = d.Value,
                ["Value_mm"] = hasScale ? d.Value / scale : d.Value,
                ["Nominal"] = d.Nominal,
                ["TolPlus"] = d.TolPlus,
                ["TolMinus"] = d.TolMinus,
                ["Diff"] = d.Value - d.Nominal,
                ["Dev"] = d.Value - d.Nominal,
                ["Pass"] = d.Pass,
                ["Status"] = d.Pass ? "OK" : "NG",
                ["PassBit"] = d.Pass ? 1 : 0,
                ["PointA"] = d.PointA,
                ["PointB"] = d.PointB,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(d.Name, out var dtMs) ? (double)dtMs : 0.0
            };

            var dVar = new Variable(d.Pass, value: d.Value, found: d.Pass, text: d.Value.ToString("0.###", CultureInfo.InvariantCulture), rawObject: d, members: dMembers);
            RegisterToolVariable(d.Name, dAliases, dVar);
        }

        // ==========================================
        // 5. SEGMENT DISTANCES (LineLine, PointLine, SegmentLine)
        // ==========================================
        void RegisterSegmentDistances(IEnumerable<SegmentDistanceResult> segList, string prefix)
        {
            int idx = 1;
            foreach (var sd in segList)
            {
                if (string.IsNullOrWhiteSpace(sd.Name)) continue;

                var sdAliases = new List<string> { sd.Name, $"{prefix}{idx}", $"{prefix}.{sd.Name}" };
                var sdMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Value"] = sd.Value,
                    ["Distance"] = sd.Value,
                    ["Value_px"] = sd.Value,
                    ["Value_mm"] = hasScale ? sd.Value / scale : sd.Value,
                    ["Nominal"] = sd.Nominal,
                    ["TolPlus"] = sd.TolPlus,
                    ["TolMinus"] = sd.TolMinus,
                    ["Diff"] = sd.Value - sd.Nominal,
                    ["Dev"] = sd.Value - sd.Nominal,
                    ["Pass"] = sd.Pass,
                    ["Status"] = sd.Pass ? "OK" : "NG",
                    ["PassBit"] = sd.Pass ? 1 : 0,
                    ["RefA"] = sd.RefA,
                    ["RefB"] = sd.RefB,
                    ["ClosestAX"] = sd.ClosestA.X,
                    ["ClosestAY"] = sd.ClosestA.Y,
                    ["ClosestBX"] = sd.ClosestB.X,
                    ["ClosestBY"] = sd.ClosestB.Y,
                    ["Time"] = result.Timings.NodeTimings.TryGetValue(sd.Name, out var sdtMs) ? (double)sdtMs : 0.0
                };

                var sdVar = new Variable(sd.Pass, value: sd.Value, found: sd.Pass, text: sd.Value.ToString("0.###", CultureInfo.InvariantCulture), rawObject: sd, members: sdMembers);
                RegisterToolVariable(sd.Name, sdAliases, sdVar);
                idx++;
            }
        }

        RegisterSegmentDistances(result.LineToLineDistances, "LineLineDist");
        RegisterSegmentDistances(result.PointToLineDistances, "PointLineDist");
        RegisterSegmentDistances(result.SegmentLineDistances, "SegmentLineDist");

        // ==========================================
        // 6. ANGLES
        // ==========================================
        for (int i = 0; i < result.Angles.Count; i++)
        {
            var a = result.Angles[i];
            if (string.IsNullOrWhiteSpace(a.Name)) continue;

            var aAliases = new List<string> { a.Name, $"Angle{i + 1}", $"Ang{i + 1}", $"Angle.{a.Name}" };
            var aMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = a.ValueDeg,
                ["ValueDeg"] = a.ValueDeg,
                ["Angle"] = a.ValueDeg,
                ["AngleDeg"] = a.ValueDeg,
                ["Nominal"] = a.Nominal,
                ["TolPlus"] = a.TolPlus,
                ["TolMinus"] = a.TolMinus,
                ["Diff"] = a.ValueDeg - a.Nominal,
                ["Dev"] = a.ValueDeg - a.Nominal,
                ["Pass"] = a.Pass,
                ["Found"] = a.Found,
                ["Status"] = a.Pass ? "OK" : "NG",
                ["PassBit"] = a.Pass ? 1 : 0,
                ["LineA"] = a.LineA,
                ["LineB"] = a.LineB,
                ["IntersectionX"] = a.Intersection.X,
                ["IntersectionY"] = a.Intersection.Y,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(a.Name, out var atMs) ? (double)atMs : 0.0
            };

            var aVar = new Variable(a.Pass, value: a.ValueDeg, found: a.Found, text: a.ValueDeg.ToString("0.###", CultureInfo.InvariantCulture), rawObject: a, members: aMembers);
            RegisterToolVariable(a.Name, aAliases, aVar);
        }

        // ==========================================
        // 7. CIRCLE FINDER
        // ==========================================
        for (int i = 0; i < result.CircleFinders.Count; i++)
        {
            var cf = result.CircleFinders[i];
            if (string.IsNullOrWhiteSpace(cf.Name)) continue;

            var diameterPx = cf.RadiusPx * 2.0;
            var cfAliases = new List<string> { cf.Name, $"CircleFinder{i + 1}", $"Circle{i + 1}", $"CIR{i + 1}", $"CIR.{cf.Name}" };
            var cfMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = cf.RadiusPx,
                ["Radius"] = cf.RadiusPx,
                ["RadiusPx"] = cf.RadiusPx,
                ["Radius_px"] = cf.RadiusPx,
                ["Radius_mm"] = hasScale ? cf.RadiusPx / scale : cf.RadiusPx,
                ["Diameter"] = diameterPx,
                ["DiameterPx"] = diameterPx,
                ["Diameter_px"] = diameterPx,
                ["Diameter_mm"] = hasScale ? diameterPx / scale : diameterPx,
                ["CenterX"] = cf.Center.X,
                ["CenterY"] = cf.Center.Y,
                ["X"] = cf.Center.X,
                ["Y"] = cf.Center.Y,
                ["X_mm"] = hasScale ? cf.Center.X / scale : cf.Center.X,
                ["Y_mm"] = hasScale ? cf.Center.Y / scale : cf.Center.Y,
                ["Score"] = cf.Score,
                ["Found"] = cf.Found,
                ["Pass"] = cf.Found,
                ["Status"] = cf.Found ? "OK" : "NG",
                ["PassBit"] = cf.Found ? 1 : 0,
                ["EdgePointCount"] = (double)(cf.EdgePoints?.Count ?? 0),
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cf.Name, out var cftMs) ? (double)cftMs : 0.0
            };

            var cfVar = new Variable(cf.Found, value: cf.RadiusPx, score: cf.Score, found: cf.Found, text: cf.Found ? "OK" : "NG", rawObject: cf, members: cfMembers);
            RegisterToolVariable(cf.Name, cfAliases, cfVar);
        }

        // ==========================================
        // 8. DIAMETER
        // ==========================================
        for (int i = 0; i < result.Diameters.Count; i++)
        {
            var d = result.Diameters[i];
            if (string.IsNullOrWhiteSpace(d.Name)) continue;

            var dAliases = new List<string> { d.Name, $"Diameter{i + 1}", $"Dia{i + 1}", $"CIR.{d.Name}", $"Diameter.{d.Name}" };
            var dMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = d.Value,
                ["Diameter"] = d.Value,
                ["Value_px"] = d.Value,
                ["Value_mm"] = hasScale ? d.Value / scale : d.Value,
                ["Radius"] = d.RadiusPx,
                ["RadiusPx"] = d.RadiusPx,
                ["Radius_mm"] = hasScale ? d.RadiusPx / scale : d.RadiusPx,
                ["CenterX"] = d.Center.X,
                ["CenterY"] = d.Center.Y,
                ["X"] = d.Center.X,
                ["Y"] = d.Center.Y,
                ["Nominal"] = d.Nominal,
                ["TolPlus"] = d.TolPlus,
                ["TolMinus"] = d.TolMinus,
                ["Diff"] = d.Value - d.Nominal,
                ["Dev"] = d.Value - d.Nominal,
                ["Pass"] = d.Pass,
                ["Found"] = d.Found,
                ["Status"] = d.Pass ? "OK" : "NG",
                ["PassBit"] = d.Pass ? 1 : 0,
                ["CircleRef"] = d.CircleRef,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(d.Name, out var dtMs) ? (double)dtMs : 0.0
            };

            var dVar = new Variable(d.Pass, value: d.Value, found: d.Found, text: d.Value.ToString("0.###", CultureInfo.InvariantCulture), rawObject: d, members: dMembers);
            RegisterToolVariable(d.Name, dAliases, dVar);
        }

        // ==========================================
        // 9. EDGE PAIRS & EDGE PAIR DETECT & LINE PAIR DETECT
        // ==========================================
        for (int i = 0; i < result.EdgePairs.Count; i++)
        {
            var ep = result.EdgePairs[i];
            if (string.IsNullOrWhiteSpace(ep.Name)) continue;

            var epAliases = new List<string> { ep.Name, $"EdgePair{i + 1}", $"EP{i + 1}", $"EP.{ep.Name}", $"EdgePair.{ep.Name}" };
            var epMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = ep.Value,
                ["Value_px"] = ep.Value,
                ["Value_mm"] = hasScale ? ep.Value / scale : ep.Value,
                ["Nominal"] = ep.Nominal,
                ["TolPlus"] = ep.TolPlus,
                ["TolMinus"] = ep.TolMinus,
                ["Diff"] = ep.Value - ep.Nominal,
                ["Dev"] = ep.Value - ep.Nominal,
                ["Pass"] = ep.Pass,
                ["Found"] = ep.Found,
                ["Status"] = ep.Pass ? "OK" : "NG",
                ["PassBit"] = ep.Pass ? 1 : 0,
                ["RefA"] = ep.RefA,
                ["RefB"] = ep.RefB,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(ep.Name, out var eptMs) ? (double)eptMs : 0.0
            };

            var epVar = new Variable(ep.Pass, value: ep.Value, found: ep.Found, text: ep.Value.ToString("0.###", CultureInfo.InvariantCulture), rawObject: ep, members: epMembers);
            RegisterToolVariable(ep.Name, epAliases, epVar);
        }

        for (int i = 0; i < result.EdgePairDetections.Count; i++)
        {
            var epd = result.EdgePairDetections[i];
            if (string.IsNullOrWhiteSpace(epd.Name)) continue;

            var epdAliases = new List<string> { epd.Name, $"EdgePairDetect{i + 1}", $"EPD{i + 1}", $"EPD.{epd.Name}", $"EdgePairDetect.{epd.Name}" };
            var epdMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = epd.Value,
                ["Value_px"] = epd.Value,
                ["Value_mm"] = hasScale ? epd.Value / scale : epd.Value,
                ["Nominal"] = epd.Nominal,
                ["TolPlus"] = epd.TolPlus,
                ["TolMinus"] = epd.TolMinus,
                ["Diff"] = epd.Value - epd.Nominal,
                ["Dev"] = epd.Value - epd.Nominal,
                ["Pass"] = epd.Pass,
                ["Found"] = epd.Found,
                ["Status"] = epd.Pass ? "OK" : "NG",
                ["PassBit"] = epd.Pass ? 1 : 0,
                ["Edge1Count"] = (double)(epd.Edge1Points?.Count ?? 0),
                ["Edge2Count"] = (double)(epd.Edge2Points?.Count ?? 0),
                ["Time"] = result.Timings.NodeTimings.TryGetValue(epd.Name, out var epdtMs) ? (double)epdtMs : 0.0
            };

            var epdVar = new Variable(epd.Pass, value: epd.Value, found: epd.Found, text: epd.Value.ToString("0.###", CultureInfo.InvariantCulture), rawObject: epd, members: epdMembers);
            RegisterToolVariable(epd.Name, epdAliases, epdVar);
        }

        for (int i = 0; i < result.LinePairDetections.Count; i++)
        {
            var lpd = result.LinePairDetections[i];
            if (string.IsNullOrWhiteSpace(lpd.Name)) continue;

            var lpdAliases = new List<string> { lpd.Name, $"LinePairDetect{i + 1}", $"LPD{i + 1}", $"LPD.{lpd.Name}", $"LinePair.{lpd.Name}" };
            var lpdMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = lpd.Value,
                ["Value_px"] = lpd.Value,
                ["Value_mm"] = hasScale ? lpd.Value / scale : lpd.Value,
                ["Nominal"] = lpd.Nominal,
                ["TolPlus"] = lpd.TolPlus,
                ["TolMinus"] = lpd.TolMinus,
                ["Diff"] = lpd.Value - lpd.Nominal,
                ["Dev"] = lpd.Value - lpd.Nominal,
                ["Pass"] = lpd.Pass,
                ["Found"] = lpd.Found,
                ["Status"] = lpd.Pass ? "OK" : "NG",
                ["PassBit"] = lpd.Pass ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(lpd.Name, out var lpdtMs) ? (double)lpdtMs : 0.0
            };

            var lpdVar = new Variable(lpd.Pass, value: lpd.Value, found: lpd.Found, text: lpd.Value.ToString("0.###", CultureInfo.InvariantCulture), rawObject: lpd, members: lpdMembers);
            RegisterToolVariable(lpd.Name, lpdAliases, lpdVar);
        }

        // ==========================================
        // 10. CALIPER
        // ==========================================
        for (int i = 0; i < result.Calipers.Count; i++)
        {
            var c = result.Calipers[i];
            if (string.IsNullOrWhiteSpace(c.Name)) continue;

            var cAliases = new List<string> { c.Name, $"Caliper{i + 1}", $"CAL{i + 1}", $"CAL.{c.Name}", $"Caliper.{c.Name}" };
            var cMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = c.AvgStrength,
                ["Strength"] = c.AvgStrength,
                ["AvgStrength"] = c.AvgStrength,
                ["Found"] = c.Found,
                ["Pass"] = c.Found,
                ["Status"] = c.Found ? "OK" : "NG",
                ["PassBit"] = c.Found ? 1 : 0,
                ["PointCount"] = (double)(c.Points?.Count ?? 0),
                ["P1X"] = c.LineP1.X,
                ["P1Y"] = c.LineP1.Y,
                ["P2X"] = c.LineP2.X,
                ["P2Y"] = c.LineP2.Y,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(c.Name, out var ctMs) ? (double)ctMs : 0.0
            };

            var cVar = new Variable(c.Found, value: c.AvgStrength, found: c.Found, text: c.Found ? "OK" : "NG", rawObject: c, members: cMembers);
            RegisterToolVariable(c.Name, cAliases, cVar);
        }

        // ==========================================
        // 11. CODE DETECTION (Barcode / QR)
        // ==========================================
        for (int i = 0; i < result.CodeDetections.Count; i++)
        {
            var cd = result.CodeDetections[i];
            if (string.IsNullOrWhiteSpace(cd.Name)) continue;

            var cdAliases = new List<string> { cd.Name, $"Code{i + 1}", $"Barcode{i + 1}", $"QR{i + 1}", $"CDT{i + 1}", $"CodeDetection{i + 1}" };
            var cdMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Text"] = cd.Text ?? string.Empty,
                ["Value"] = cd.Text ?? string.Empty,
                ["Code"] = cd.Text ?? string.Empty,
                ["Found"] = cd.Found,
                ["Pass"] = cd.Found,
                ["Status"] = cd.Found ? "OK" : "NG",
                ["PassBit"] = cd.Found ? 1 : 0,
                ["Angle"] = cd.Angle,
                ["AngleDeg"] = cd.Angle,
                ["X"] = (double)cd.BoundingBox.X,
                ["Y"] = (double)cd.BoundingBox.Y,
                ["Width"] = (double)cd.BoundingBox.Width,
                ["Height"] = (double)cd.BoundingBox.Height,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cd.Name, out var cdtMs) ? (double)cdtMs : 0.0
            };

            var cdVar = new Variable(cd.Found, found: cd.Found, text: cd.Text ?? string.Empty, rawObject: cd, members: cdMembers);
            RegisterToolVariable(cd.Name, cdAliases, cdVar);
        }

        // ==========================================
        // 12. SURFACE COMPARE & CONTOUR COMPARE & BLOB DETECTION
        // ==========================================
        for (int i = 0; i < result.SurfaceCompares.Count; i++)
        {
            var sc = result.SurfaceCompares[i];
            if (string.IsNullOrWhiteSpace(sc.Name)) continue;

            var scAliases = new List<string> { sc.Name, $"SurfaceCompare{i + 1}", $"SC{i + 1}", $"SC.{sc.Name}", $"SurfaceCompare.{sc.Name}" };
            var scMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Count"] = (double)sc.Count,
                ["DefectCount"] = (double)sc.Count,
                ["MaxArea"] = sc.MaxArea,
                ["Area"] = sc.MaxArea,
                ["Score"] = sc.MaxArea,
                ["Pass"] = sc.Pass,
                ["Status"] = sc.Pass ? "OK" : "NG",
                ["PassBit"] = sc.Pass ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(sc.Name, out var sctMs) ? (double)sctMs : 0.0
            };

            var scVar = new Variable(sc.Pass, value: sc.Count, score: sc.MaxArea, found: sc.Pass, text: sc.Pass ? "OK" : "NG", rawObject: sc, members: scMembers);
            RegisterToolVariable(sc.Name, scAliases, scVar);
        }

        for (int i = 0; i < result.ContourCompares.Count; i++)
        {
            var cc = result.ContourCompares[i];
            if (string.IsNullOrWhiteSpace(cc.Name)) continue;

            var ccAliases = new List<string> { cc.Name, $"ContourCompare{i + 1}", $"CC{i + 1}", $"CC.{cc.Name}", $"ContourCompare.{cc.Name}" };
            var ccMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["MatchScore"] = cc.MatchScore,
                ["Score"] = cc.MatchScore,
                ["MaxDistancePx"] = cc.MaxDistancePx,
                ["MaxDistance_px"] = cc.MaxDistancePx,
                ["MaxDistance_mm"] = hasScale ? cc.MaxDistancePx / scale : cc.MaxDistancePx,
                ["AreaDiffPercent"] = cc.AreaDiffPercent,
                ["PerimeterDiffPercent"] = cc.PerimeterDiffPercent,
                ["Pass"] = cc.Pass,
                ["Found"] = cc.Found,
                ["Status"] = cc.Pass ? "OK" : "NG",
                ["PassBit"] = cc.Pass ? 1 : 0,
                ["PassSegmentsCount"] = (double)(cc.PassSegments?.Count ?? 0),
                ["FailSegmentsCount"] = (double)(cc.FailSegments?.Count ?? 0),
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cc.Name, out var cctMs) ? (double)cctMs : 0.0
            };

            var ccVar = new Variable(cc.Pass, value: cc.MatchScore, score: cc.MatchScore, found: cc.Found, text: cc.Pass ? "OK" : "NG", rawObject: cc, members: ccMembers);
            RegisterToolVariable(cc.Name, ccAliases, ccVar);
        }

        for (int i = 0; i < result.BlobDetections.Count; i++)
        {
            var b = result.BlobDetections[i];
            if (string.IsNullOrWhiteSpace(b.Name)) continue;

            var bAliases = new List<string> { b.Name, $"BlobDetection{i + 1}", $"Blob{i + 1}", $"Blob.{b.Name}" };
            var bMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Count"] = (double)b.Count,
                ["Value"] = (double)b.Count,
                ["BlobCount"] = (double)b.Count,
                ["Pass"] = b.Count > 0,
                ["Status"] = b.Count > 0 ? "OK" : "NG",
                ["PassBit"] = b.Count > 0 ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(b.Name, out var btMs) ? (double)btMs : 0.0
            };

            if (b.Blobs != null && b.Blobs.Count > 0)
            {
                bMembers["FirstBlobArea"] = b.Blobs[0].Area;
                bMembers["FirstBlobX"] = b.Blobs[0].Centroid.X;
                bMembers["FirstBlobY"] = b.Blobs[0].Centroid.Y;
            }

            var bVar = new Variable(b.Count > 0, value: b.Count, found: b.Count > 0, text: b.Count.ToString(), rawObject: b, members: bMembers);
            RegisterToolVariable(b.Name, bAliases, bVar);
        }

        // ==========================================
        // 13. COLOR DIFF & CROP & ARITHMETIC
        // ==========================================
        for (int i = 0; i < result.ColorDiffs.Count; i++)
        {
            var cd = result.ColorDiffs[i];
            if (string.IsNullOrWhiteSpace(cd.Name)) continue;

            var cdAliases = new List<string> { cd.Name, $"ColorDiff{i + 1}", $"CD{i + 1}", $"Color{i + 1}", $"ColorDiff.{cd.Name}" };
            var cdMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DeltaE"] = cd.DeltaE,
                ["dE"] = cd.DeltaE,
                ["Value"] = cd.DeltaE,
                ["MaxDeltaE"] = cd.MaxDeltaE,
                ["L"] = cd.MeasuredL,
                ["A"] = cd.MeasuredA,
                ["B"] = cd.MeasuredB,
                ["MeasuredL"] = cd.MeasuredL,
                ["MeasuredA"] = cd.MeasuredA,
                ["MeasuredB"] = cd.MeasuredB,
                ["SampleL"] = cd.MeasuredL,
                ["SampleA"] = cd.MeasuredA,
                ["SampleB"] = cd.MeasuredB,
                ["RefL"] = cd.RefL,
                ["RefA"] = cd.RefA,
                ["RefB"] = cd.RefB,
                ["Pass"] = cd.Pass,
                ["Status"] = cd.Pass ? "OK" : "NG",
                ["PassBit"] = cd.Pass ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cd.Name, out var cdtMs) ? (double)cdtMs : 0.0
            };

            var cdVar = new Variable(cd.Pass, value: cd.DeltaE, found: cd.Pass, text: cd.DeltaE.ToString("0.##", CultureInfo.InvariantCulture), rawObject: cd, members: cdMembers);
            RegisterToolVariable(cd.Name, cdAliases, cdVar);
        }

        for (int i = 0; i < result.Crops.Count; i++)
        {
            var cr = result.Crops[i];
            if (string.IsNullOrWhiteSpace(cr.Name)) continue;

            var crAliases = new List<string> { cr.Name, $"Crop{i + 1}" };
            var crMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Success"] = cr.Success,
                ["Pass"] = cr.Success,
                ["Status"] = cr.Success ? "OK" : "NG",
                ["PassBit"] = cr.Success ? 1 : 0,
                ["Width"] = (double)cr.Width,
                ["Height"] = (double)cr.Height,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cr.Name, out var crtMs) ? (double)crtMs : 0.0
            };

            var crVar = new Variable(cr.Success, found: cr.Success, text: cr.Success ? "OK" : "NG", rawObject: cr, members: crMembers);
            RegisterToolVariable(cr.Name, crAliases, crVar);
        }

        for (int i = 0; i < result.ImgArithmetics.Count; i++)
        {
            var ia = result.ImgArithmetics[i];
            if (string.IsNullOrWhiteSpace(ia.Name)) continue;

            var iaAliases = new List<string> { ia.Name, $"ImgArithmetic{i + 1}", $"Math{i + 1}" };
            var iaMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Success"] = ia.Success,
                ["Pass"] = ia.Success,
                ["Status"] = ia.Success ? "OK" : "NG",
                ["PassBit"] = ia.Success ? 1 : 0,
                ["Op"] = ia.Op.ToString(),
                ["Width"] = (double)ia.Width,
                ["Height"] = (double)ia.Height,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(ia.Name, out var iatMs) ? (double)iatMs : 0.0
            };

            var iaVar = new Variable(ia.Success, found: ia.Success, text: ia.Success ? "OK" : "NG", rawObject: ia, members: iaMembers);
            RegisterToolVariable(ia.Name, iaAliases, iaVar);
        }

        // ==========================================
        // 14. CREATE GEOMETRY TOOLS (CreatePoint, CreateLine, CreateRect, CreateCircle)
        // ==========================================
        for (int i = 0; i < result.CreatePoints.Count; i++)
        {
            var cp = result.CreatePoints[i];
            if (string.IsNullOrWhiteSpace(cp.Name)) continue;

            var cpAliases = new List<string> { cp.Name, $"CreatePoint{i + 1}", $"CP{i + 1}" };
            var cpMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["X"] = cp.X,
                ["Y"] = cp.Y,
                ["X_px"] = cp.X,
                ["Y_px"] = cp.Y,
                ["X_mm"] = hasScale ? cp.X / scale : cp.X,
                ["Y_mm"] = hasScale ? cp.Y / scale : cp.Y,
                ["Success"] = cp.Success,
                ["Pass"] = cp.Success,
                ["Status"] = cp.Success ? "OK" : "NG",
                ["PassBit"] = cp.Success ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cp.Name, out var cptMs) ? (double)cptMs : 0.0
            };

            var cpVar = new Variable(cp.Success, found: cp.Success, text: $"({cp.X:0.##}, {cp.Y:0.##})", rawObject: cp, members: cpMembers);
            RegisterToolVariable(cp.Name, cpAliases, cpVar);
        }

        for (int i = 0; i < result.CreateLines.Count; i++)
        {
            var cl = result.CreateLines[i];
            if (string.IsNullOrWhiteSpace(cl.Name)) continue;

            var clAliases = new List<string> { cl.Name, $"CreateLine{i + 1}", $"CL{i + 1}" };
            var clMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["X1"] = cl.X1,
                ["Y1"] = cl.Y1,
                ["X2"] = cl.X2,
                ["Y2"] = cl.Y2,
                ["Angle"] = cl.Angle,
                ["AngleDeg"] = cl.Angle,
                ["Length"] = cl.Length,
                ["LengthPx"] = cl.Length,
                ["Length_px"] = cl.Length,
                ["Length_mm"] = hasScale ? cl.Length / scale : cl.Length,
                ["Value"] = cl.Length,
                ["Success"] = cl.Success,
                ["Pass"] = cl.Success,
                ["Status"] = cl.Success ? "OK" : "NG",
                ["PassBit"] = cl.Success ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cl.Name, out var cltMs) ? (double)cltMs : 0.0
            };

            var clVar = new Variable(cl.Success, value: cl.Length, found: cl.Success, text: cl.Length.ToString("0.###", CultureInfo.InvariantCulture), rawObject: cl, members: clMembers);
            RegisterToolVariable(cl.Name, clAliases, clVar);
        }

        for (int i = 0; i < result.CreateRects.Count; i++)
        {
            var cr = result.CreateRects[i];
            if (string.IsNullOrWhiteSpace(cr.Name)) continue;

            var crAliases = new List<string> { cr.Name, $"CreateRect{i + 1}", $"CR{i + 1}" };
            var crMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["X"] = cr.X,
                ["Y"] = cr.Y,
                ["Width"] = cr.Width,
                ["Height"] = cr.Height,
                ["Width_mm"] = hasScale ? cr.Width / scale : cr.Width,
                ["Height_mm"] = hasScale ? cr.Height / scale : cr.Height,
                ["Angle"] = cr.Angle,
                ["AngleDeg"] = cr.Angle,
                ["Anchor"] = cr.Anchor.ToString(),
                ["TopLeftX"] = cr.TopLeftX,
                ["TopLeftY"] = cr.TopLeftY,
                ["Success"] = cr.Success,
                ["Pass"] = cr.Success,
                ["Status"] = cr.Success ? "OK" : "NG",
                ["PassBit"] = cr.Success ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(cr.Name, out var crtMs) ? (double)crtMs : 0.0
            };

            var crVar = new Variable(cr.Success, found: cr.Success, text: cr.Success ? "OK" : "NG", rawObject: cr, members: crMembers);
            RegisterToolVariable(cr.Name, crAliases, crVar);
        }

        for (int i = 0; i < result.CreateCircles.Count; i++)
        {
            var ccir = result.CreateCircles[i];
            if (string.IsNullOrWhiteSpace(ccir.Name)) continue;

            var dia = ccir.Radius * 2.0;
            var ccirAliases = new List<string> { ccir.Name, $"CreateCircle{i + 1}", $"CCIR{i + 1}" };
            var ccirMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["CenterX"] = ccir.CenterX,
                ["CenterY"] = ccir.CenterY,
                ["X"] = ccir.CenterX,
                ["Y"] = ccir.CenterY,
                ["X_mm"] = hasScale ? ccir.CenterX / scale : ccir.CenterX,
                ["Y_mm"] = hasScale ? ccir.CenterY / scale : ccir.CenterY,
                ["Radius"] = ccir.Radius,
                ["RadiusPx"] = ccir.Radius,
                ["Radius_px"] = ccir.Radius,
                ["Radius_mm"] = hasScale ? ccir.Radius / scale : ccir.Radius,
                ["Diameter"] = dia,
                ["DiameterPx"] = dia,
                ["Diameter_px"] = dia,
                ["Diameter_mm"] = hasScale ? dia / scale : dia,
                ["Value"] = ccir.Radius,
                ["Success"] = ccir.Success,
                ["Pass"] = ccir.Success,
                ["Status"] = ccir.Success ? "OK" : "NG",
                ["PassBit"] = ccir.Success ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(ccir.Name, out var ccirtMs) ? (double)ccirtMs : 0.0
            };

            var ccirVar = new Variable(ccir.Success, value: ccir.Radius, found: ccir.Success, text: ccir.Radius.ToString("0.###", CultureInfo.InvariantCulture), rawObject: ccir, members: ccirMembers);
            RegisterToolVariable(ccir.Name, ccirAliases, ccirVar);
        }

        // ==========================================
        // 15. CONDITIONS
        // ==========================================
        for (int i = 0; i < result.Conditions.Count; i++)
        {
            var c = result.Conditions[i];
            if (string.IsNullOrWhiteSpace(c.Name)) continue;

            var cAliases = new List<string> { c.Name, $"Condition{i + 1}", $"Cond{i + 1}" };
            var cMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pass"] = c.Pass,
                ["Status"] = c.Pass ? "OK" : "NG",
                ["PassBit"] = c.Pass ? 1 : 0,
                ["Expression"] = c.Expression ?? string.Empty,
                ["Error"] = c.Error ?? string.Empty,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(c.Name, out var ctMs) ? (double)ctMs : 0.0
            };

            var cVar = new Variable(c.Pass, found: c.Pass, text: c.Pass ? "OK" : "NG", rawObject: c, members: cMembers);
            RegisterToolVariable(c.Name, cAliases, cVar);
        }

        // ==========================================
        // 16. IMAGE OUTPUTS
        // ==========================================
        for (int i = 0; i < result.ImageOutputs.Count; i++)
        {
            var io = result.ImageOutputs[i];
            if (string.IsNullOrWhiteSpace(io.Name)) continue;

            var ioAliases = new List<string> { io.Name, $"ImageOutput{i + 1}", $"OutputImage{i + 1}", $"Saved.{io.Name}" };
            var ioMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Saved"] = io.Saved,
                ["Pass"] = io.Saved,
                ["Found"] = io.Saved,
                ["Status"] = io.Saved ? "OK" : "NG",
                ["PassBit"] = io.Saved ? 1 : 0,
                ["SavedFilePath"] = io.SavedFilePath ?? string.Empty,
                ["Text"] = io.SavedFilePath ?? string.Empty,
                ["Error"] = io.Error ?? string.Empty,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(io.Name, out var iotMs) ? (double)iotMs : 0.0
            };

            var ioVar = new Variable(io.Saved, found: io.Saved, text: io.SavedFilePath ?? string.Empty, rawObject: io, members: ioMembers);
            RegisterToolVariable(io.Name, ioAliases, ioVar);
        }

        // ==========================================
        // 17. DATABASE RESULTS (DbResult)
        // ==========================================
        if (result.DbResults != null)
        {
            for (int i = 0; i < result.DbResults.Count; i++)
            {
                var db = result.DbResults[i];
                if (string.IsNullOrWhiteSpace(db.NodeName)) continue;

                double valNum = 0.0;
                string textVal = db.Text ?? string.Empty;
                if (db.Value != null && double.TryParse(db.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pVal))
                {
                    valNum = pVal;
                }

                var dbAliases = new List<string> { db.NodeName, $"DbNode{i + 1}", $"DB{i + 1}", "DB" };
                if (db.NodeName.Contains("Node", StringComparison.OrdinalIgnoreCase))
                {
                    dbAliases.Add(db.NodeName.Replace("Node", "", StringComparison.OrdinalIgnoreCase));
                }

                var dbMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Value"] = db.Value ?? textVal ?? (object)valNum,
                    ["Text"] = textVal,
                    ["Pass"] = db.Success,
                    ["Success"] = db.Success,
                    ["Status"] = db.Success ? "OK" : "NG",
                    ["PassBit"] = db.Success ? 1 : 0,
                    ["RowCount"] = (double)db.RowCount,
                    ["ColumnCount"] = (double)db.ColumnCount,
                    ["RowsAffected"] = (double)db.RowsAffected,
                    ["Error"] = db.ErrorMessage ?? string.Empty,
                    ["Time"] = result.Timings.NodeTimings.TryGetValue(db.NodeName, out var dbtMs) ? (double)dbtMs : 0.0
                };

                if (db.ColumnMap != null)
                {
                    foreach (var colKvp in db.ColumnMap)
                    {
                        if (!string.IsNullOrWhiteSpace(colKvp.Key))
                        {
                            dbMembers[colKvp.Key] = colKvp.Value;
                        }
                    }
                }

                var dbVar = new Variable(db.Success, value: valNum, score: db.RowCount, found: db.Success, text: textVal, rawObject: db, members: dbMembers);
                RegisterToolVariable(db.NodeName, dbAliases, dbVar);
            }
        }

        // ==========================================
        // 18. PLC RESULTS (PlcRead, PlcWrite, PlcWait, PlcTrigger, PlcBatchRead, PlcBatchWrite)
        // ==========================================
        for (int i = 0; i < result.PlcReads.Count; i++)
        {
            var pr = result.PlcReads[i];
            if (string.IsNullOrWhiteSpace(pr.Name)) continue;

            var prAliases = new List<string> { pr.Name, $"PlcRead{i + 1}", $"PLC_Read{i + 1}", "PLC" };
            double valNum = 0.0;
            if (pr.Value != null && double.TryParse(pr.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)) valNum = parsed;

            var prMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Value"] = valNum,
                ["Text"] = pr.Value?.ToString() ?? string.Empty,
                ["PlcId"] = pr.PlcId,
                ["TagName"] = pr.TagName,
                ["Found"] = pr.Found,
                ["Pass"] = pr.Found,
                ["Status"] = pr.Found ? "OK" : "NG",
                ["PassBit"] = pr.Found ? 1 : 0,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(pr.Name, out var prtMs) ? (double)prtMs : 0.0
            };

            var prVar = new Variable(pr.Found, value: valNum, found: pr.Found, text: pr.Value?.ToString() ?? string.Empty, rawObject: pr, members: prMembers);
            RegisterToolVariable(pr.Name, prAliases, prVar);
        }

        for (int i = 0; i < result.PlcWrites.Count; i++)
        {
            var pw = result.PlcWrites[i];
            if (string.IsNullOrWhiteSpace(pw.Name)) continue;

            var pwAliases = new List<string> { pw.Name, $"PlcWrite{i + 1}" };
            var pwMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Success"] = pw.Success,
                ["Pass"] = pw.Success,
                ["Status"] = pw.Success ? "OK" : "NG",
                ["PassBit"] = pw.Success ? 1 : 0,
                ["PlcId"] = pw.PlcId,
                ["TagName"] = pw.TagName,
                ["Value"] = pw.WrittenValue?.ToString() ?? string.Empty,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(pw.Name, out var pwtMs) ? (double)pwtMs : 0.0
            };

            var pwVar = new Variable(pw.Success, found: pw.Success, text: pw.Success ? "OK" : "NG", rawObject: pw, members: pwMembers);
            RegisterToolVariable(pw.Name, pwAliases, pwVar);
        }

        for (int i = 0; i < result.PlcWaits.Count; i++)
        {
            var pw = result.PlcWaits[i];
            if (string.IsNullOrWhiteSpace(pw.Name)) continue;

            var pwAliases = new List<string> { pw.Name, $"PlcWait{i + 1}" };
            var pwMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Success"] = pw.Success,
                ["Pass"] = pw.Success,
                ["Status"] = pw.Success ? "OK" : "NG",
                ["PassBit"] = pw.Success ? 1 : 0,
                ["ElapsedMs"] = pw.ElapsedMs,
                ["TargetValue"] = pw.TargetValue,
                ["PlcId"] = pw.PlcId,
                ["TagName"] = pw.TagName,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(pw.Name, out var pwtMs) ? (double)pwtMs : 0.0
            };

            var pwVar = new Variable(pw.Success, value: pw.ElapsedMs, found: pw.Success, text: pw.Success ? "OK" : "NG", rawObject: pw, members: pwMembers);
            RegisterToolVariable(pw.Name, pwAliases, pwVar);
        }

        for (int i = 0; i < result.PlcTriggers.Count; i++)
        {
            var pt = result.PlcTriggers[i];
            if (string.IsNullOrWhiteSpace(pt.Name)) continue;

            var ptAliases = new List<string> { pt.Name, $"PlcTrigger{i + 1}" };
            var ptMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Triggered"] = pt.Triggered,
                ["Pass"] = pt.Triggered,
                ["Status"] = pt.Triggered ? "OK" : "NG",
                ["PassBit"] = pt.Triggered ? 1 : 0,
                ["PlcId"] = pt.PlcId,
                ["TagName"] = pt.TagName,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(pt.Name, out var pttMs) ? (double)pttMs : 0.0
            };

            var ptVar = new Variable(pt.Triggered, found: pt.Triggered, text: pt.Triggered ? "OK" : "NG", rawObject: pt, members: ptMembers);
            RegisterToolVariable(pt.Name, ptAliases, ptVar);
        }

        for (int i = 0; i < result.PlcBatchReads.Count; i++)
        {
            var pbr = result.PlcBatchReads[i];
            if (string.IsNullOrWhiteSpace(pbr.Name)) continue;

            var pbrAliases = new List<string> { pbr.Name, $"PlcBatchRead{i + 1}" };
            var pbrMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PlcId"] = pbr.PlcId,
                ["Count"] = (double)(pbr.TagValues?.Count ?? 0),
                ["Pass"] = true,
                ["Status"] = "OK",
                ["PassBit"] = 1,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(pbr.Name, out var pbrtMs) ? (double)pbrtMs : 0.0
            };

            if (pbr.TagValues != null)
            {
                foreach (var tagKvp in pbr.TagValues)
                {
                    if (!string.IsNullOrWhiteSpace(tagKvp.Key))
                    {
                        pbrMembers[tagKvp.Key] = tagKvp.Value;
                    }
                }
            }

            var pbrVar = new Variable(true, found: true, text: "OK", rawObject: pbr, members: pbrMembers);
            RegisterToolVariable(pbr.Name, pbrAliases, pbrVar);
        }

        for (int i = 0; i < result.PlcBatchWrites.Count; i++)
        {
            var pbw = result.PlcBatchWrites[i];
            if (string.IsNullOrWhiteSpace(pbw.Name)) continue;

            var pbwAliases = new List<string> { pbw.Name, $"PlcBatchWrite{i + 1}" };
            var pbwMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Success"] = pbw.Success,
                ["Pass"] = pbw.Success,
                ["Status"] = pbw.Success ? "OK" : "NG",
                ["PassBit"] = pbw.Success ? 1 : 0,
                ["PlcId"] = pbw.PlcId,
                ["Time"] = result.Timings.NodeTimings.TryGetValue(pbw.Name, out var pbwtMs) ? (double)pbwtMs : 0.0
            };

            var pbwVar = new Variable(pbw.Success, found: pbw.Success, text: pbw.Success ? "OK" : "NG", rawObject: pbw, members: pbwMembers);
            RegisterToolVariable(pbw.Name, pbwAliases, pbwVar);
        }

        // ==========================================
        // 19. DEFECTS
        // ==========================================
        if (result.Defects != null)
        {
            var dCount = result.Defects.Defects?.Count ?? 0;
            var dPass = dCount == 0;
            var dMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Count"] = (double)dCount,
                ["DefectCount"] = (double)dCount,
                ["Pass"] = dPass,
                ["Status"] = dPass ? "OK" : "NG",
                ["PassBit"] = dPass ? 1 : 0,
                ["Time"] = (double)result.Timings.DefectsMs
            };

            var dVar = new Variable(dPass, value: dCount, found: dPass, text: dPass ? "OK" : "NG", rawObject: result.Defects, members: dMembers);
            RegisterToolVariable("Defect", new[] { "Defects", "DefectDetection", "DefectInspection" }, dVar);
        }

        // ==========================================
        // 20. UNIVERSAL DYNAMIC REFLECTION FALLBACK
        // Quét tự động tất cả các property trong InspectionResult để bắt các tool mới
        // ==========================================
        try
        {
            var resProps = GetCachedProperties(typeof(InspectionResult));
            foreach (var prop in resProps)
            {
                var val = prop.GetValue(result);
                if (val is null) continue;

                // Nếu là danh sách IEnumerable (List<CustomToolResult>)
                if (val is IEnumerable list && !(val is string))
                {
                    int itemIdx = 1;
                    foreach (var item in list)
                    {
                        if (item is null) continue;
                        var itemType = item.GetType();
                        var itemProps = GetCachedProperties(itemType);

                        var nameProp = itemProps.FirstOrDefault(p => string.Equals(p.Name, "Name", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, "NodeName", StringComparison.OrdinalIgnoreCase));
                        var itemName = nameProp?.GetValue(item)?.ToString();
                        if (string.IsNullOrWhiteSpace(itemName))
                        {
                            itemName = $"{itemType.Name.Replace("Result", "")}{itemIdx}";
                        }

                        // Nếu chưa được đăng ký trong vars, trích xuất tự động qua Reflection
                        if (!vars.ContainsKey(itemName))
                        {
                            var autoMembers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                            bool itemPass = true;
                            double? itemVal = null;
                            string? itemText = null;

                            foreach (var p in itemProps)
                            {
                                var pVal = p.GetValue(item);
                                autoMembers[p.Name] = pVal;

                                if (string.Equals(p.Name, "Pass", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, "Success", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, "Found", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (pVal is bool b) itemPass = b;
                                }
                                else if (string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, "Score", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (pVal is double d) itemVal = d;
                                    else if (pVal is int i) itemVal = i;
                                }
                                else if (string.Equals(p.Name, "Text", StringComparison.OrdinalIgnoreCase))
                                {
                                    itemText = pVal?.ToString();
                                }
                            }

                            var autoVar = new Variable(itemPass, value: itemVal, found: itemPass, text: itemText ?? (itemPass ? "OK" : "NG"), rawObject: item, members: autoMembers);
                            RegisterToolVariable(itemName, new[] { $"{itemType.Name.Replace("Result", "")}{itemIdx}" }, autoVar);
                        }
                        itemIdx++;
                    }
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi reflection nếu có
        }

        // ==========================================
        // 21. GLOBAL SYSTEM VARIABLES
        // ==========================================
        var now = DateTime.Now;
        var totalMs = result.Timings.TotalMs > 0 ? result.Timings.TotalMs : result.Timings.NodeTimings.Values.Sum();

        vars["TotalPass"] = new Variable(result.Pass, found: result.Pass, text: result.Pass ? "PASS" : "FAIL");
        vars["TotalFail"] = new Variable(!result.Pass, found: !result.Pass, text: !result.Pass ? "FAIL" : "PASS");
        vars["Pass"] = new Variable(result.Pass, found: result.Pass, text: result.Pass ? "PASS" : "FAIL");
        vars["Status"] = new Variable(result.Pass, found: result.Pass, text: result.Pass ? "PASS" : "FAIL");
        vars["Result"] = new Variable(result.Pass, found: result.Pass, text: result.Pass ? "PASS" : "FAIL");
        vars["TotalPassBit"] = new Variable(result.Pass, value: result.Pass ? 1.0 : 0.0, text: result.Pass ? "1" : "0");
        vars["TotalFailBit"] = new Variable(!result.Pass, value: !result.Pass ? 1.0 : 0.0, text: !result.Pass ? "1" : "0");
        vars["PassBit"] = new Variable(result.Pass, value: result.Pass ? 1.0 : 0.0, text: result.Pass ? "1" : "0");
        vars["FailBit"] = new Variable(!result.Pass, value: !result.Pass ? 1.0 : 0.0, text: !result.Pass ? "1" : "0");

        vars["TotalMs"] = new Variable(true, value: totalMs, text: totalMs.ToString());
        vars["TotalTime"] = new Variable(true, value: totalMs, text: $"{totalMs} ms");
        vars["ExecutionTime"] = new Variable(true, value: totalMs, text: $"{totalMs} ms");

        vars["ProductCode"] = new Variable(true, text: config?.ProductCode ?? string.Empty);
        vars["ProductName"] = new Variable(true, text: config?.ProductName ?? string.Empty);

        vars["DateTime"] = new Variable(true, text: now.ToString("yyyy-MM-dd HH:mm:ss"));
        vars["Date"] = new Variable(true, text: now.ToString("yyyy-MM-dd"));
        vars["Time"] = new Variable(true, text: now.ToString("HH:mm:ss"));
        vars["Year"] = new Variable(true, value: now.Year, text: now.Year.ToString("0000"));
        vars["YYYY"] = new Variable(true, value: now.Year, text: now.Year.ToString("0000"));
        vars["YY"] = new Variable(true, value: now.Year % 100, text: (now.Year % 100).ToString("00"));
        vars["Month"] = new Variable(true, value: now.Month, text: now.Month.ToString("00"));
        vars["MM"] = new Variable(true, value: now.Month, text: now.Month.ToString("00"));
        vars["Day"] = new Variable(true, value: now.Day, text: now.Day.ToString("00"));
        vars["DD"] = new Variable(true, value: now.Day, text: now.Day.ToString("00"));
        vars["Hour"] = new Variable(true, value: now.Hour, text: now.Hour.ToString("00"));
        vars["HH"] = new Variable(true, value: now.Hour, text: now.Hour.ToString("00"));
        vars["Minute"] = new Variable(true, value: now.Minute, text: now.Minute.ToString("00"));
        vars["mm"] = new Variable(true, value: now.Minute, text: now.Minute.ToString("00"));
        vars["Second"] = new Variable(true, value: now.Second, text: now.Second.ToString("00"));
        vars["ss"] = new Variable(true, value: now.Second, text: now.Second.ToString("00"));
        vars["Timestamp"] = new Variable(true, text: now.ToString("yyyyMMdd_HHmmss"));

        return vars;
    }

    /// <summary>
    /// Đánh giá và thay thế toàn bộ các token/biến trong chuỗi bản mẫu văn bản (Text Template)
    /// Hỗ trợ format số, boolean, tọa độ, thuộc tính lồng nhau và tự động dò tìm alias thông minh.
    /// </summary>
    public static string EvaluateTextTemplate(string text, Dictionary<string, Variable>? vars)
    {
        if (string.IsNullOrEmpty(text) || vars is null || vars.Count == 0)
        {
            return text ?? string.Empty;
        }

        return Regex.Replace(text, @"\{([^}]+)\}", m =>
        {
            var rawInner = m.Groups[1].Value?.Trim() ?? string.Empty;
            if (rawInner.Length == 0) return string.Empty;

            var fmt = string.Empty;
            var inner = rawInner;
            var colonIdx = inner.IndexOf(':');
            if (colonIdx >= 0)
            {
                fmt = inner[(colonIdx + 1)..].Trim();
                inner = inner[..colonIdx].Trim();
            }

            // Tầng 1: Tra cứu trực tiếp chính xác
            if (vars.TryGetValue(inner, out var vDirect) && vDirect != null)
            {
                return FormatVariableValue(vDirect, fmt);
            }

            // Tầng 2: Tra cứu cú pháp Dot-Notation: varName.propName
            var varName = inner;
            var propName = string.Empty;
            var dotIdx = inner.IndexOf('.');
            if (dotIdx >= 0)
            {
                varName = inner[..dotIdx].Trim();
                propName = inner[(dotIdx + 1)..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(varName))
            {
                // Thử tìm biến theo varName chính xác
                if (vars.TryGetValue(varName, out var vObj) && vObj != null)
                {
                    var resolved = ResolveMemberValue(vObj, propName, fmt);
                    if (resolved != null) return resolved;
                }

                // Tầng 3: Fuzzy Alias Lookup (Thử bỏ/thêm số thứ tự 1 nếu có)
                var alternateNames = GetAlternateVarNames(varName);
                foreach (var altName in alternateNames)
                {
                    // Thử tra cứu phẳng altName.propName
                    if (!string.IsNullOrEmpty(propName) && vars.TryGetValue($"{altName}.{propName}", out var vAltFlat) && vAltFlat != null)
                    {
                        return FormatVariableValue(vAltFlat, fmt);
                    }

                    // Thử tra cứu object altName
                    if (vars.TryGetValue(altName, out var vAltObj) && vAltObj != null)
                    {
                        var resolved = ResolveMemberValue(vAltObj, propName, fmt);
                        if (resolved != null) return resolved;
                    }
                }
            }

            // Fallback: giữ nguyên chuỗi token nếu không tìm thấy biến
            return m.Value;
        });
    }

    private static List<string> GetAlternateVarNames(string name)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(name)) return list;

        // Nếu là Origin / Origin1
        if (name.StartsWith("Origin", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("Origin");
            list.Add("Origin1");
            list.Add("Origin_1");
            list.Add("Pattern");
            list.Add("Pattern1");
        }
        else if (name.EndsWith("1", StringComparison.OrdinalIgnoreCase) && name.Length > 1)
        {
            // Bỏ đuôi 1 (ví dụ Point1 -> Point, Caliper1 -> Caliper)
            list.Add(name[..^1]);
        }
        else
        {
            // Thêm đuôi 1 (ví dụ Point -> Point1, Caliper -> Caliper1)
            list.Add(name + "1");
        }

        // DB Aliases
        if (name.StartsWith("DB", StringComparison.OrdinalIgnoreCase) || name.StartsWith("DbNode", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("DB");
            list.Add("DbNode1");
            list.Add("DB1");
            list.Add("ReadDB1");
        }

        // PLC Aliases
        if (name.StartsWith("PLC", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("PLC");
            list.Add("PLC1");
            list.Add("PlcRead1");
        }

        return list;
    }

    private static string? ResolveMemberValue(Variable v, string propName, string fmt)
    {
        if (string.IsNullOrWhiteSpace(propName))
        {
            return FormatVariableValue(v, fmt);
        }

        // 1. Kiểm tra trực tiếp trong Dictionary Members
        if (v.TryGetMember(propName, out var mVal) && mVal != null)
        {
            return FormatObjectValue(mVal, fmt);
        }

        // 2. Tra cứu theo từ đồng nghĩa thông minh (Property Aliases)
        if (string.Equals(propName, "Angle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propName, "AngleDeg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propName, "Rotation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propName, "PoseAngle", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("Angle", out var a) && a != null) return FormatObjectValue(a, fmt);
            if (v.TryGetMember("AngleDeg", out var ad) && ad != null) return FormatObjectValue(ad, fmt);
            if (v.TryGetMember("ValueDeg", out var vd) && vd != null) return FormatObjectValue(vd, fmt);
            if (v.Value.HasValue) return FormatObjectValue(v.Value.Value, fmt);
        }
        else if (string.Equals(propName, "X", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "PosX", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("X", out var x) && x != null) return FormatObjectValue(x, fmt);
            if (v.TryGetMember("CenterX", out var cx) && cx != null) return FormatObjectValue(cx, fmt);
            if (v.TryGetMember("X1", out var x1) && x1 != null) return FormatObjectValue(x1, fmt);
        }
        else if (string.Equals(propName, "Y", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "PosY", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("Y", out var y) && y != null) return FormatObjectValue(y, fmt);
            if (v.TryGetMember("CenterY", out var cy) && cy != null) return FormatObjectValue(cy, fmt);
            if (v.TryGetMember("Y1", out var y1) && y1 != null) return FormatObjectValue(y1, fmt);
        }
        else if (string.Equals(propName, "Score", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "Confidence", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "MatchScore", StringComparison.OrdinalIgnoreCase))
        {
            if (v.Score.HasValue) return FormatObjectValue(v.Score.Value, fmt);
            if (v.TryGetMember("Score", out var sc) && sc != null) return FormatObjectValue(sc, fmt);
            if (v.TryGetMember("MatchScore", out var ms) && ms != null) return FormatObjectValue(ms, fmt);
        }
        else if (string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase))
        {
            if (v.Value.HasValue) return FormatObjectValue(v.Value.Value, fmt);
            if (v.Score.HasValue) return FormatObjectValue(v.Score.Value, fmt);
            if (v.Text != null) return v.Text;
            if (v.Found.HasValue) return v.Found.Value ? "True" : "False";
            return v.Pass ? "True" : "False";
        }
        else if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "OK", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "Success", StringComparison.OrdinalIgnoreCase))
        {
            return v.Pass ? "True" : "False";
        }
        else if (string.Equals(propName, "Status", StringComparison.OrdinalIgnoreCase))
        {
            return v.Pass ? "OK" : "NG";
        }
        else if (string.Equals(propName, "Found", StringComparison.OrdinalIgnoreCase))
        {
            return (v.Found ?? v.Pass) ? "True" : "False";
        }
        else if (string.Equals(propName, "Text", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "String", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "Code", StringComparison.OrdinalIgnoreCase))
        {
            if (v.Text != null) return v.Text;
            if (v.TryGetMember("Text", out var t) && t != null) return t.ToString() ?? string.Empty;
        }
        else if (string.Equals(propName, "Radius", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "RadiusPx", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("Radius", out var r) && r != null) return FormatObjectValue(r, fmt);
            if (v.TryGetMember("RadiusPx", out var rpx) && rpx != null) return FormatObjectValue(rpx, fmt);
            if (v.Value.HasValue) return FormatObjectValue(v.Value.Value, fmt);
        }
        else if (string.Equals(propName, "Diameter", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "DiameterPx", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("Diameter", out var d) && d != null) return FormatObjectValue(d, fmt);
            if (v.TryGetMember("RadiusPx", out var rpx) && rpx is double dr) return FormatObjectValue(dr * 2.0, fmt);
        }
        else if (string.Equals(propName, "Count", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "BlobCount", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "DefectCount", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("Count", out var c) && c != null) return FormatObjectValue(c, fmt);
            if (v.Value.HasValue) return FormatObjectValue(v.Value.Value, fmt);
        }
        else if (string.Equals(propName, "MaxArea", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "Area", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("MaxArea", out var ma) && ma != null) return FormatObjectValue(ma, fmt);
            if (v.Score.HasValue) return FormatObjectValue(v.Score.Value, fmt);
        }
        else if (string.Equals(propName, "Time", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "ExecutionTime", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propName, "ElapsedMs", StringComparison.OrdinalIgnoreCase))
        {
            if (v.TryGetMember("Time", out var tm) && tm != null) return FormatObjectValue(tm, fmt);
            if (v.TryGetMember("ExecutionTime", out var et) && et != null) return FormatObjectValue(et, fmt);
            if (v.TryGetMember("ElapsedMs", out var em) && em != null) return FormatObjectValue(em, fmt);
        }

        // 3. Tra cứu qua Reflection trên RawObject (nếu có)
        if (v.RawObject != null)
        {
            var rawType = v.RawObject.GetType();
            var props = GetCachedProperties(rawType);
            var matchedProp = props.FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase));
            if (matchedProp != null)
            {
                var val = matchedProp.GetValue(v.RawObject);
                if (val != null) return FormatObjectValue(val, fmt);
            }
        }

        return null;
    }

    private static string FormatVariableValue(Variable v, string fmt)
    {
        if (!string.IsNullOrWhiteSpace(fmt))
        {
            if (v.Value.HasValue) return FormatObjectValue(v.Value.Value, fmt);
            if (v.Score.HasValue) return FormatObjectValue(v.Score.Value, fmt);
            if (v.Text != null && double.TryParse(v.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedD))
            {
                return FormatObjectValue(parsedD, fmt);
            }
        }

        object? directVal = v.Text ?? (object?)v.Value ?? (object?)v.Score ?? (object?)v.Found ?? (v.Pass ? "True" : "False");
        return FormatObjectValue(directVal, fmt);
    }

    private static string FormatObjectValue(object? val, string fmt)
    {
        if (val is null) return string.Empty;

        if (val is double d)
        {
            return string.IsNullOrWhiteSpace(fmt)
                ? d.ToString("0.###", CultureInfo.InvariantCulture)
                : d.ToString(fmt, CultureInfo.InvariantCulture);
        }
        if (val is float f)
        {
            return string.IsNullOrWhiteSpace(fmt)
                ? f.ToString("0.###", CultureInfo.InvariantCulture)
                : f.ToString(fmt, CultureInfo.InvariantCulture);
        }
        if (val is int i)
        {
            return string.IsNullOrWhiteSpace(fmt)
                ? i.ToString(CultureInfo.InvariantCulture)
                : i.ToString(fmt, CultureInfo.InvariantCulture);
        }
        if (val is long l)
        {
            return string.IsNullOrWhiteSpace(fmt)
                ? l.ToString(CultureInfo.InvariantCulture)
                : l.ToString(fmt, CultureInfo.InvariantCulture);
        }
        if (val is bool b)
        {
            return b ? "True" : "False";
        }
        if (val is Point2d pt)
        {
            return $"({pt.X.ToString("0.##", CultureInfo.InvariantCulture)}, {pt.Y.ToString("0.##", CultureInfo.InvariantCulture)})";
        }
        if (val is Rect r)
        {
            return $"[{r.X}, {r.Y}, {r.Width}, {r.Height}]";
        }

        if (!string.IsNullOrWhiteSpace(fmt) && val is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVal))
        {
            return parsedVal.ToString(fmt, CultureInfo.InvariantCulture);
        }

        return val.ToString() ?? string.Empty;
    }
}

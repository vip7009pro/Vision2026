using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

public static class PlcResultTransferRunner
{
    public static async Task ExecuteResultTransfersAsync(VisionConfig config, InspectionResult result, IPlcManagerService plcManager)
    {
        if (config == null || config.ResultTransfers == null || config.ResultTransfers.Count == 0 || result == null || plcManager == null)
        {
            return;
        }

        foreach (var nodeDef in config.ResultTransfers)
        {
            if (nodeDef.Items == null || nodeDef.Items.Count == 0)
                continue;

            foreach (var item in nodeDef.Items)
            {
                if (string.IsNullOrWhiteSpace(item.TagName))
                    continue;

                // Kiểm tra điều kiện gửi
                if (item.Condition == ImageOutputCondition.OnPass && !result.Pass)
                    continue;
                if (item.Condition == ImageOutputCondition.OnFail && result.Pass)
                    continue;

                // Tính toán biểu thức giá trị
                object writeVal = EvaluateExpression(item.ValueExpression, result, config);

                // Gửi xuống PLC
                string targetPlcId = item.PlcId;
                System.Diagnostics.Debug.WriteLine($"[PLC RESULT TRANSFER] Tag='{item.TagName}' (PLC: {targetPlcId}) | Expr='{item.ValueExpression}' => Output: {writeVal} ({writeVal?.GetType().Name})");
                Console.WriteLine($"[PLC RESULT TRANSFER] Tag='{item.TagName}' (PLC: {targetPlcId}) | Expr='{item.ValueExpression}' => Output: {writeVal} ({writeVal?.GetType().Name})");

                await plcManager.WriteTagValueAsync(targetPlcId, item.TagName, writeVal);
            }
        }
    }

    public static object EvaluateExpression(string rawExpr, InspectionResult result, VisionConfig? config = null)
    {
        if (string.IsNullOrWhiteSpace(rawExpr))
            return result.Pass;

        string expr = rawExpr.Trim();

        if (string.Equals(expr, "TotalPass", StringComparison.OrdinalIgnoreCase) || string.Equals(expr, "Pass", StringComparison.OrdinalIgnoreCase))
        {
            return result.Pass;
        }

        if (string.Equals(expr, "TotalFail", StringComparison.OrdinalIgnoreCase) || string.Equals(expr, "Fail", StringComparison.OrdinalIgnoreCase) || string.Equals(expr, "NG", StringComparison.OrdinalIgnoreCase))
        {
            return !result.Pass;
        }

        if (string.Equals(expr, "TotalPassBit", StringComparison.OrdinalIgnoreCase))
        {
            return result.Pass ? 1 : 0;
        }

        if (string.Equals(expr, "TotalFailBit", StringComparison.OrdinalIgnoreCase))
        {
            return result.Pass ? 0 : 1;
        }

        if (string.Equals(expr, "PassCount", StringComparison.OrdinalIgnoreCase))
        {
            int passCount = result.Points.Count(p => p.Pass) +
                            result.Distances.Count(d => d.Pass) +
                            result.Angles.Count(a => a.Pass) +
                            result.Conditions.Count(c => c.Pass);
            return passCount;
        }

        if (string.Equals(expr, "FailCount", StringComparison.OrdinalIgnoreCase))
        {
            int failCount = result.Points.Count(p => !p.Pass) +
                            result.Distances.Count(d => !d.Pass) +
                            result.Angles.Count(a => !a.Pass) +
                            result.Conditions.Count(c => !c.Pass);
            return failCount;
        }

        // Thay thế biểu thức mẫu {ToolName.Property}
        bool hasReplacement = false;
        string replaced = Regex.Replace(expr, @"\{([^}]+)\}", match =>
        {
            string token = match.Groups[1].Value.Trim();
            var parts = token.Split('.');
            if (parts.Length != 2) return match.Value;

            string toolName = parts[0].Trim();
            string propName = parts[1].Trim();

            string? valStr = ResolveToolPropertyValue(toolName, propName, result, config);
            if (valStr != null)
            {
                hasReplacement = true;
                return valStr;
            }
            return match.Value;
        });

        // Nếu biểu thức là mẫu {Tool.Prop} nhưng không thể resolve, trả về 0.0 thay vì chuỗi raw để tránh lỗi ghi 0 không mong muốn
        if (!hasReplacement && expr.StartsWith("{") && expr.EndsWith("}"))
        {
            return 0.0;
        }

        // Thử parse double
        if (double.TryParse(replaced, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal))
        {
            return dVal;
        }

        // Thử parse bool
        if (bool.TryParse(replaced, out bool bVal))
        {
            return bVal;
        }

        return replaced;
    }

    private static string? ResolveToolPropertyValue(string toolName, string propName, InspectionResult result, VisionConfig? config)
    {
        // Hệ số Calibration (px -> mm)
        double scale = (config != null && config.PixelsPerMm > 0) ? config.PixelsPerMm : 1.0;
        bool isCalibrated = config != null && config.PixelsPerMm > 0 && Math.Abs(config.PixelsPerMm - 1.0) > 1e-6;

        // 0. Origin
        if (result.Origin != null && (string.Equals(toolName, "Origin", StringComparison.OrdinalIgnoreCase) 
            || string.Equals(result.Origin.Name, toolName, StringComparison.OrdinalIgnoreCase) 
            || toolName.StartsWith("Origin", StringComparison.OrdinalIgnoreCase)))
        {
            double posX_mm = isCalibrated ? result.Origin.Position.X / scale : result.Origin.Position.X;
            double posY_mm = isCalibrated ? result.Origin.Position.Y / scale : result.Origin.Position.Y;

            if (string.Equals(propName, "X", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "X_mm", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "PosX", StringComparison.OrdinalIgnoreCase))
                return posX_mm.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "X_px", StringComparison.OrdinalIgnoreCase))
                return result.Origin.Position.X.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Y", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Y_mm", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "PosY", StringComparison.OrdinalIgnoreCase))
                return posY_mm.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Y_px", StringComparison.OrdinalIgnoreCase))
                return result.Origin.Position.Y.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Angle", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "AngleDeg", StringComparison.OrdinalIgnoreCase))
                return result.Origin.AngleDeg.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return result.Origin.Pass ? "1" : "0";

            if (string.Equals(propName, "Score", StringComparison.OrdinalIgnoreCase))
                return result.Origin.Score.ToString("F3", CultureInfo.InvariantCulture);
        }

        // 1. Points
        var pt = result.Points.FirstOrDefault(p => string.Equals(p.Name, toolName, StringComparison.OrdinalIgnoreCase) || toolName.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase));
        if (pt != null)
        {
            double posX_mm = isCalibrated ? pt.Position.X / scale : pt.Position.X;
            double posY_mm = isCalibrated ? pt.Position.Y / scale : pt.Position.Y;

            if (string.Equals(propName, "X", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "X_mm", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "PosX", StringComparison.OrdinalIgnoreCase))
                return posX_mm.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "X_px", StringComparison.OrdinalIgnoreCase))
                return pt.Position.X.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Y", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Y_mm", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "PosY", StringComparison.OrdinalIgnoreCase))
                return posY_mm.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Y_px", StringComparison.OrdinalIgnoreCase))
                return pt.Position.Y.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Angle", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "AngleDeg", StringComparison.OrdinalIgnoreCase))
                return pt.AngleDeg.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return pt.Pass ? "1" : "0";

            if (string.Equals(propName, "Score", StringComparison.OrdinalIgnoreCase))
                return pt.Score.ToString("F3", CultureInfo.InvariantCulture);
        }

        // 2. Distances (Distances, LineToLineDistances, PointToLineDistances, SegmentLineDistances)
        var dist = result.Distances.FirstOrDefault(d => string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (dist != null)
        {
            if (string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Distance", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Value_mm", StringComparison.OrdinalIgnoreCase))
                return dist.Value.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Value_px", StringComparison.OrdinalIgnoreCase))
                return (dist.Value * scale).ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return dist.Pass ? "1" : "0";
        }

        var lineLineDist = result.LineToLineDistances.FirstOrDefault(d => string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (lineLineDist != null)
        {
            if (string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Distance", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Value_mm", StringComparison.OrdinalIgnoreCase))
                return lineLineDist.Value.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Value_px", StringComparison.OrdinalIgnoreCase))
                return (lineLineDist.Value * scale).ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return lineLineDist.Pass ? "1" : "0";
        }

        var ptLineDist = result.PointToLineDistances.FirstOrDefault(d => string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (ptLineDist != null)
        {
            if (string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Distance", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Value_mm", StringComparison.OrdinalIgnoreCase))
                return ptLineDist.Value.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Value_px", StringComparison.OrdinalIgnoreCase))
                return (ptLineDist.Value * scale).ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return ptLineDist.Pass ? "1" : "0";
        }

        var segLineDist = result.SegmentLineDistances.FirstOrDefault(d => string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (segLineDist != null)
        {
            if (string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Distance", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Value_mm", StringComparison.OrdinalIgnoreCase))
                return segLineDist.Value.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Value_px", StringComparison.OrdinalIgnoreCase))
                return (segLineDist.Value * scale).ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return segLineDist.Pass ? "1" : "0";
        }

        // 3. Angles
        var ang = result.Angles.FirstOrDefault(a => string.Equals(a.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (ang != null)
        {
            if (string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Angle", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "AngleDeg", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "ValueDeg", StringComparison.OrdinalIgnoreCase))
                return ang.ValueDeg.ToString("F3", CultureInfo.InvariantCulture);

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return ang.Pass ? "1" : "0";
        }

        // 4. Lines
        var line = result.Lines.FirstOrDefault(l => string.Equals(l.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (line != null)
        {
            if (string.Equals(propName, "Found", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return line.Found ? "1" : "0";
        }

        // 5. Code Detections
        var code = result.CodeDetections.FirstOrDefault(c => string.Equals(c.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (code != null)
        {
            if (string.Equals(propName, "Text", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase))
                return code.Text;

            if (string.Equals(propName, "Found", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase))
                return code.Found ? "1" : "0";
        }

        // 6. Blob Detections
        var blob = result.BlobDetections.FirstOrDefault(b => string.Equals(b.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (blob != null)
        {
            if (string.Equals(propName, "Count", StringComparison.OrdinalIgnoreCase))
                return blob.Count.ToString();

            if (string.Equals(propName, "Area", StringComparison.OrdinalIgnoreCase))
            {
                double areaVal = blob.Blobs.Sum(bItem => bItem.Area);
                double areaMm2 = isCalibrated ? areaVal / (scale * scale) : areaVal;
                return areaMm2.ToString("F1", CultureInfo.InvariantCulture);
            }

            if (string.Equals(propName, "Pass", StringComparison.OrdinalIgnoreCase) || string.Equals(propName, "Found", StringComparison.OrdinalIgnoreCase))
                return blob.Count > 0 ? "1" : "0";
        }

        return null;
    }
}

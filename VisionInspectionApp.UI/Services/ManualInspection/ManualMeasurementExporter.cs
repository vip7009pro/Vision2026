using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VisionInspectionApp.UI.Models.ManualInspection;

namespace VisionInspectionApp.UI.Services.ManualInspection;

public static class ManualMeasurementExporter
{
    public static void ExportToCsv(string filePath, IEnumerable<ManualMeasurementRecord> records, double pixelsPerMm)
    {
        var sb = new StringBuilder();
        // UTF-8 BOM for Excel Vietnamese compatibility
        sb.AppendLine("ID,Tool,Value (mm / °),Value (px),Unit,Nominal,Upper Tol (+),Lower Tol (-),Status,Details");

        foreach (var r in records)
        {
            string nomStr = r.Nominal.HasValue ? r.Nominal.Value.ToString("F3") : "";
            string upStr = r.UpperTolerance.HasValue ? r.UpperTolerance.Value.ToString("F3") : "";
            string lowStr = r.LowerTolerance.HasValue ? r.LowerTolerance.Value.ToString("F3") : "";
            string statusStr = r.Status.ToString();

            string safeDetails = $"\"{r.Details.Replace("\"", "\"\"")}\"";
            sb.AppendLine($"{r.Id},\"{r.ToolName}\",{r.ValueMm:F4},{r.ValuePx:F2},{r.Unit},{nomStr},{upStr},{lowStr},{statusStr},{safeDetails}");
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }
}

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Dịch vụ xuất báo cáo chất lượng cuộn sản phẩm (Roll Quality Certificate, Cut List CSV, JSON Data)
/// </summary>
public static class RollReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Xuất dữ liệu cuộn đầy đủ sang định dạng JSON
    /// </summary>
    public static void ExportToJson(RollSession session, string filePath)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Xuất danh sách vết lỗi và bảng vị trí cắt (Cut List) sang định dạng CSV
    /// </summary>
    public static void ExportToCsv(RollSession session, string filePath)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Index,DefectType,Severity,WebPosition_Meter,WebPosition_Mm,CrossWidth_Mm,Width_Mm,Length_Mm,Area_Mm2,RejectStatus,Timestamp");

        int idx = 1;
        foreach (var defect in session.Defects)
        {
            double meterPos = defect.WebY_Mm / 1000.0;
            sb.AppendLine($"{idx}," +
                          $"\"{defect.DefectType}\"," +
                          $"\"{defect.Severity}\"," +
                          $"{meterPos:F3}," +
                          $"{defect.WebY_Mm:F1}," +
                          $"{defect.WebX_Mm:F1}," +
                          $"{defect.Width_Mm:F2}," +
                          $"{defect.Length_Mm:F2}," +
                          $"{defect.Area_Mm2:F2}," +
                          $"\"{(defect.RejectTriggered ? "REJECTED" : "PASS")}\"," +
                          $"\"{defect.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\"");
            idx++;
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Xuất Giấy Chứng Nhận Chất Lượng Cuộn (Roll Quality Certificate) định dạng HTML tiêu chuẩn in ấn
    /// </summary>
    public static void ExportToHtmlCertificate(RollSession session, string filePath)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"vi\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <title>Roll Quality Certificate - " + session.SessionId + "</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #0f172a; color: #f8fafc; margin: 0; padding: 24px; }");
        sb.AppendLine("    .container { max-width: 1000px; margin: 0 auto; background: #1e293b; border-radius: 12px; padding: 32px; box-shadow: 0 10px 25px rgba(0,0,0,0.5); border: 1px solid #334155; }");
        sb.AppendLine("    .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #3b82f6; padding-bottom: 16px; margin-bottom: 24px; }");
        sb.AppendLine("    h1 { color: #38bdf8; margin: 0; font-size: 24px; }");
        sb.AppendLine("    .badge { background: #10b981; color: white; padding: 6px 16px; border-radius: 20px; font-weight: bold; }");
        sb.AppendLine("    .badge-ng { background: #ef4444; }");
        sb.AppendLine("    .grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px; }");
        sb.AppendLine("    .card { background: #0f172a; padding: 16px; border-radius: 8px; border: 1px solid #334155; }");
        sb.AppendLine("    .card-title { font-size: 12px; color: #94a3b8; text-transform: uppercase; margin-bottom: 4px; }");
        sb.AppendLine("    .card-value { font-size: 20px; font-weight: bold; color: #f8fafc; }");
        sb.AppendLine("    table { width: 100%; border-collapse: collapse; margin-top: 16px; background: #0f172a; border-radius: 8px; overflow: hidden; }");
        sb.AppendLine("    th, td { padding: 12px 16px; text-align: left; border-bottom: 1px solid #334155; font-size: 13px; }");
        sb.AppendLine("    th { background: #1e293b; color: #38bdf8; font-weight: 600; }");
        sb.AppendLine("    tr:hover { background: #1e293b; }");
        sb.AppendLine("    .tag-reject { color: #ef4444; font-weight: bold; }");
        sb.AppendLine("    .tag-warn { color: #f59e0b; font-weight: bold; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class=\"container\">");
        sb.AppendLine("    <div class=\"header\">");
        sb.AppendLine("      <div>");
        sb.AppendLine("        <h1>📜 ROLL QUALITY CERTIFICATE</h1>");
        sb.AppendLine($"        <p style=\"color: #94a3b8; margin: 4px 0 0 0;\">Phiên kiểm tra cuộn: {session.SessionId} | Lô: {session.LotNumber}</p>");
        sb.AppendLine("      </div>");
        
        bool isPass = session.QualityYieldPercentage >= 95.0 && session.RejectCount == 0;
        sb.AppendLine($"      <div class=\"badge {(isPass ? "" : "badge-ng")}\">{(isPass ? "GRADE A - PASSED" : "GRADE B / REJECT")}</div>");
        sb.AppendLine("    </div>");

        sb.AppendLine("    <div class=\"grid\">");
        sb.AppendLine($"      <div class=\"card\"><div class=\"card-title\">Tổng Chiều Dài</div><div class=\"card-value\">{session.TotalLengthMeters:F2} m</div></div>");
        sb.AppendLine($"      <div class=\"card\"><div class=\"card-title\">Tỷ Lệ Đạt (Yield)</div><div class=\"card-value\" style=\"color: #10b981;\">{session.QualityYieldPercentage:F1}%</div></div>");
        sb.AppendLine($"      <div class=\"card\"><div class=\"card-title\">Tổng Số Vết Lỗi</div><div class=\"card-value\" style=\"color: {(session.TotalDefectsCount > 0 ? "#ef4444" : "#10b981")};\">{session.TotalDefectsCount}</div></div>");
        sb.AppendLine($"      <div class=\"card\"><div class=\"card-title\">Số Lượng Reject</div><div class=\"card-value\" style=\"color: #ef4444;\">{session.RejectCount}</div></div>");
        sb.AppendLine("    </div>");

        sb.AppendLine("    <h3 style=\"color: #38bdf8; margin-top: 24px; margin-bottom: 8px;\">📍 Danh Sách Khuyết Tật Chi Tiết (Defect Map & Cut List)</h3>");
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead>");
        sb.AppendLine("        <tr><th>#</th><th>Loại Lỗi</th><th>Mức Độ</th><th>Vị Trí Dọc (m)</th><th>Vị Trí Ngang (mm)</th><th>Kích Thước WxL (mm)</th><th>Trạng Thái Loại Bỏ</th><th>Thời Gian</th></tr>");
        sb.AppendLine("      </thead>");
        sb.AppendLine("      <tbody>");

        int count = 1;
        foreach (var d in session.Defects)
        {
            double mPos = d.WebY_Mm / 1000.0;
            string sevClass = d.Severity >= DefectSeverity.Reject ? "tag-reject" : "tag-warn";
            sb.AppendLine("        <tr>");
            sb.AppendLine($"          <td>{count++}</td>");
            sb.AppendLine($"          <td><b>{d.DefectType}</b></td>");
            sb.AppendLine($"          <td><span class=\"{sevClass}\">{d.Severity}</span></td>");
            sb.AppendLine($"          <td><b>{mPos:F3} m</b> ({d.WebY_Mm:F1} mm)</td>");
            sb.AppendLine($"          <td>{d.WebX_Mm:F1} mm</td>");
            sb.AppendLine($"          <td>{d.Width_Mm:F2} x {d.Length_Mm:F2}</td>");
            sb.AppendLine($"          <td>{(d.RejectTriggered ? "🔴 ĐÃ KÍCH HOẠT REJECT" : "🟢 Không loại bỏ")}</td>");
            sb.AppendLine($"          <td>{d.Timestamp:HH:mm:ss.fff}</td>");
            sb.AppendLine("        </tr>");
        }

        if (session.Defects.Count == 0)
        {
            sb.AppendLine("        <tr><td colspan=\"8\" style=\"text-align: center; color: #10b981; padding: 24px;\">✨ Không phát hiện bất kỳ khuyết tật nào trên cuộn sản phẩm. Cuộn hoàn hảo 100%!</td></tr>");
        }

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }
}

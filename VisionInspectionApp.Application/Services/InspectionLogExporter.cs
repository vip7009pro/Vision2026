using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Dịch vụ xuất báo cáo Lịch sử kiểm tra và Phân tích SPC sang định dạng Excel XML, CSV, JSON
/// </summary>
public static class InspectionLogExporter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Xuất dữ liệu ra file JSON
    /// </summary>
    public static void ExportToJson(
        InspectionSessionRecord session,
        IReadOnlyList<InspectionPartRecord> parts,
        SpcAnalysisResult? spc,
        string filePath)
    {
        var payload = new
        {
            Session = session,
            TotalPartsCount = parts?.Count ?? 0,
            SpcSummary = spc,
            Parts = parts
        };

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Xuất dữ liệu ra file CSV
    /// </summary>
    public static void ExportToCsv(
        InspectionSessionRecord session,
        IReadOnlyList<InspectionPartRecord> parts,
        string filePath)
    {
        var sb = new StringBuilder();

        // Header thông tin Session
        sb.AppendLine($"Session ID,{session.Id}");
        sb.AppendLine($"Product Name,{EscapeCsv(session.ProductName)}");
        sb.AppendLine($"Material,{EscapeCsv(session.Material)}");
        sb.AppendLine($"Start Time,{session.FormattedStartTime}");
        sb.AppendLine($"End Time,{session.FormattedEndTime}");
        sb.AppendLine($"Total Parts,{session.TotalParts}");
        sb.AppendLine($"Pass Parts,{session.PassParts}");
        sb.AppendLine($"Fail Parts,{session.FailParts}");
        sb.AppendLine($"Yield Rate,{session.FormattedYield}");
        sb.AppendLine();

        // Bảng chi tiết từng hạng mục đo của từng con hàng
        sb.AppendLine("Part Index,Timestamp,Part Judge,Tool Name,Tool Type,Target (Nominal),Min (LSL),Max (USL),Measured Value,Unit,Item Judge,Reason");

        if (parts != null)
        {
            foreach (var part in parts)
            {
                if (part.Measurements == null || part.Measurements.Count == 0)
                {
                    sb.AppendLine($"{part.PartIndex},{part.FormattedTimestamp},{part.StatusText},-,-,-,-,-,-,-,{part.StatusText},{EscapeCsv(part.DetailedReason)}");
                }
                else
                {
                    foreach (var m in part.Measurements)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4},{5:F3},{6:F3},{7:F3},{8:F3},{9},{10},{11}",
                            part.PartIndex,
                            part.FormattedTimestamp,
                            part.StatusText,
                            EscapeCsv(m.ItemName),
                            EscapeCsv(m.ToolType),
                            m.Nominal,
                            m.Lsl,
                            m.Usl,
                            m.MeasuredValue,
                            EscapeCsv(m.Unit),
                            m.Judge,
                            EscapeCsv(part.DetailedReason)));
                    }
                }
            }
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    /// Xuất báo cáo Excel định dạng XML Spreadsheet 2003 tương thích 100% mọi phiên bản Microsoft Excel
    /// </summary>
    public static void ExportToExcel(
        InspectionSessionRecord session,
        IReadOnlyList<InspectionPartRecord> parts,
        SpcAnalysisResult? spc,
        string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

        // Styles
        sb.AppendLine(" <Styles>");
        sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\"><Alignment ss:Vertical=\"Center\"/><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\"/></Style>");
        sb.AppendLine("  <Style ss:ID=\"Title\"><Font ss:FontName=\"Segoe UI\" ss:Size=\"14\" ss:Bold=\"1\" ss:Color=\"#1E293B\"/></Style>");
        sb.AppendLine("  <Style ss:ID=\"Header\"><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#0F766E\" ss:Pattern=\"Solid\"/><Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/></Style>");
        sb.AppendLine("  <Style ss:ID=\"SubHeader\"><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#0F766E\"/><Interior ss:Color=\"#E6FFFA\" ss:Pattern=\"Solid\"/></Style>");
        sb.AppendLine("  <Style ss:ID=\"CellPass\"><Font ss:FontName=\"Segoe UI\" ss:Color=\"#047857\" ss:Bold=\"1\"/><Interior ss:Color=\"#D1FAE5\" ss:Pattern=\"Solid\"/><Alignment ss:Horizontal=\"Center\"/></Style>");
        sb.AppendLine("  <Style ss:ID=\"CellFail\"><Font ss:FontName=\"Segoe UI\" ss:Color=\"#B91C1C\" ss:Bold=\"1\"/><Interior ss:Color=\"#FEE2E2\" ss:Pattern=\"Solid\"/><Alignment ss:Horizontal=\"Center\"/></Style>");
        sb.AppendLine("  <Style ss:ID=\"Number\"><Alignment ss:Horizontal=\"Right\"/><NumberFormat ss:Format=\"0.000\"/></Style>");
        sb.AppendLine("  <Style ss:ID=\"Center\"><Alignment ss:Horizontal=\"Center\"/></Style>");
        sb.AppendLine(" </Styles>");

        // ═══ SHEET 1: SUMMARY & SPC ═══
        sb.AppendLine(" <Worksheet ss:Name=\"Tóm Tắt & SPC\">");
        sb.AppendLine("  <Table ss:DefaultRowHeight=\"20\">");
        sb.AppendLine("   <Column ss:Width=\"160\"/>");
        sb.AppendLine("   <Column ss:Width=\"180\"/>");
        sb.AppendLine("   <Column ss:Width=\"140\"/>");
        sb.AppendLine("   <Column ss:Width=\"140\"/>");

        // Tiêu đề
        sb.AppendLine("   <Row ss:Height=\"28\"><Cell ss:StyleID=\"Title\" ss:MergeAcross=\"3\"><Data ss:Type=\"String\">BÁO CÁO LỊCH SỬ KIỂM TRA &amp; PHÂN TÍCH NĂNG LỰC SPC</Data></Cell></Row>");
        sb.AppendLine("   <Row ss:Height=\"8\"></Row>");

        // Thông tin chung
        sb.AppendLine($"   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Mã Phiên:</Data></Cell><Cell><Data ss:Type=\"String\">{session.Id}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Sản Phẩm:</Data></Cell><Cell><Data ss:Type=\"String\">{EscapeXml(session.ProductName)}</Data></Cell></Row>");
        sb.AppendLine($"   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Thời Gian Bắt Đầu:</Data></Cell><Cell><Data ss:Type=\"String\">{session.FormattedStartTime}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Vật Liệu:</Data></Cell><Cell><Data ss:Type=\"String\">{EscapeXml(session.Material)}</Data></Cell></Row>");
        sb.AppendLine($"   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Thời Gian Kết Thúc:</Data></Cell><Cell><Data ss:Type=\"String\">{session.FormattedEndTime}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Thời Lượng:</Data></Cell><Cell><Data ss:Type=\"String\">{session.FormattedDuration}</Data></Cell></Row>");
        sb.AppendLine($"   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Tổng Số Con Hàng:</Data></Cell><Cell ss:StyleID=\"Center\"><Data ss:Type=\"Number\">{session.TotalParts}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Tỉ Lệ Đạt (Yield):</Data></Cell><Cell ss:StyleID=\"CellPass\"><Data ss:Type=\"String\">{session.FormattedYield}</Data></Cell></Row>");
        sb.AppendLine($"   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Số Lượng Đạt (OK):</Data></Cell><Cell ss:StyleID=\"CellPass\"><Data ss:Type=\"Number\">{session.PassParts}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Số Lượng Lỗi (NG):</Data></Cell><Cell ss:StyleID=\"CellFail\"><Data ss:Type=\"Number\">{session.FailParts}</Data></Cell></Row>");

        if (spc != null && spc.TotalSamples > 0)
        {
            sb.AppendLine("   <Row ss:Height=\"12\"></Row>");
            sb.AppendLine($"   <Row ss:Height=\"24\"><Cell ss:StyleID=\"Title\" ss:MergeAcross=\"3\"><Data ss:Type=\"String\">CHỈ SỐ NĂNG LỰC QUÁ TRÌNH SPC / CPK ({EscapeXml(spc.ItemName)})</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Hạng Mục Phân Tích:</Data></Cell><Cell><Data ss:Type=\"String\">{EscapeXml(spc.ItemName)} ({spc.Unit})</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Đánh Giá Quá Trình:</Data></Cell><Cell ss:StyleID=\"CellPass\"><Data ss:Type=\"String\">{EscapeXml(spc.Assessment)}</Data></Cell></Row>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Chỉ Số Cpk:</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{0:F3}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Chỉ Số Cp:</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{1:F3}</Data></Cell></Row>", spc.Cpk, spc.Cp));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Giá Trị Trung Bình (Mean):</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{0:F3}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Độ Lệch Chuẩn (Sigma):</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{1:F4}</Data></Cell></Row>", spc.OverallMean, spc.OverallSigma));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Giới Hạn Dưới (LSL):</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{0:F3}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Giới Hạn Trên (USL):</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{1:F3}</Data></Cell></Row>", spc.Lsl, spc.Usl));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">X-bar Giới Hạn (LCL..UCL):</Data></Cell><Cell><Data ss:Type=\"String\">{0:F3} .. {1:F3}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">R Giới Hạn (LCL..UCL):</Data></Cell><Cell><Data ss:Type=\"String\">{2:F3} .. {3:F3}</Data></Cell></Row>", spc.Xbar_LCL, spc.Xbar_UCL, spc.R_LCL, spc.R_UCL));
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Cỡ Mẫu Nhóm (n):</Data></Cell><Cell ss:StyleID=\"Center\"><Data ss:Type=\"Number\">{spc.SubgroupSizeN}</Data></Cell><Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Số Nhóm (k):</Data></Cell><Cell ss:StyleID=\"Center\"><Data ss:Type=\"Number\">{spc.SubgroupCountK}</Data></Cell></Row>");
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");

        // ═══ SHEET 2: DETAILED PARTS ═══
        sb.AppendLine(" <Worksheet ss:Name=\"Chi Tiết Từng Con Hàng\">");
        sb.AppendLine("  <Table ss:DefaultRowHeight=\"18\">");
        sb.AppendLine("   <Column ss:Width=\"50\"/>");
        sb.AppendLine("   <Column ss:Width=\"90\"/>");
        sb.AppendLine("   <Column ss:Width=\"60\"/>");
        sb.AppendLine("   <Column ss:Width=\"130\"/>");
        sb.AppendLine("   <Column ss:Width=\"100\"/>");
        sb.AppendLine("   <Column ss:Width=\"80\"/>");
        sb.AppendLine("   <Column ss:Width=\"80\"/>");
        sb.AppendLine("   <Column ss:Width=\"80\"/>");
        sb.AppendLine("   <Column ss:Width=\"90\"/>");
        sb.AppendLine("   <Column ss:Width=\"50\"/>");
        sb.AppendLine("   <Column ss:Width=\"60\"/>");
        sb.AppendLine("   <Column ss:Width=\"150\"/>");

        // Headers
        sb.AppendLine("   <Row ss:Height=\"22\">");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">STT</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Thời Gian</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Tổng Thể</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Tên Phép Đo</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Loại Công Cụ</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Spec (Target)</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Min (LSL)</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Max (USL)</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Giá Trị Đo</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Đơn Vị</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Đánh Giá</Data></Cell>");
        sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Ghi Chú / Lý Do</Data></Cell>");
        sb.AppendLine("   </Row>");

        if (parts != null)
        {
            foreach (var part in parts)
            {
                string partJudgeStyle = part.Pass ? "CellPass" : "CellFail";
                if (part.Measurements == null || part.Measurements.Count == 0)
                {
                    sb.AppendLine($"   <Row><Cell ss:StyleID=\"Center\"><Data ss:Type=\"Number\">{part.PartIndex}</Data></Cell><Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{part.FormattedTimestamp}</Data></Cell><Cell ss:StyleID=\"{partJudgeStyle}\"><Data ss:Type=\"String\">{part.StatusText}</Data></Cell><Cell><Data ss:Type=\"String\">-</Data></Cell><Cell><Data ss:Type=\"String\">-</Data></Cell><Cell><Data ss:Type=\"String\">-</Data></Cell><Cell><Data ss:Type=\"String\">-</Data></Cell><Cell><Data ss:Type=\"String\">-</Data></Cell><Cell><Data ss:Type=\"String\">-</Data></Cell><Cell><Data ss:Type=\"String\">-</Data></Cell><Cell ss:StyleID=\"{partJudgeStyle}\"><Data ss:Type=\"String\">{part.StatusText}</Data></Cell><Cell><Data ss:Type=\"String\">{EscapeXml(part.DetailedReason)}</Data></Cell></Row>");
                }
                else
                {
                    foreach (var m in part.Measurements)
                    {
                        string itemJudgeStyle = m.Pass ? "CellPass" : "CellFail";
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "   <Row><Cell ss:StyleID=\"Center\"><Data ss:Type=\"Number\">{0}</Data></Cell><Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{1}</Data></Cell><Cell ss:StyleID=\"{2}\"><Data ss:Type=\"String\">{3}</Data></Cell><Cell><Data ss:Type=\"String\">{4}</Data></Cell><Cell><Data ss:Type=\"String\">{5}</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{6:F3}</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{7:F3}</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{8:F3}</Data></Cell><Cell ss:StyleID=\"Number\"><Data ss:Type=\"Number\">{9:F3}</Data></Cell><Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{10}</Data></Cell><Cell ss:StyleID=\"{11}\"><Data ss:Type=\"String\">{12}</Data></Cell><Cell><Data ss:Type=\"String\">{13}</Data></Cell></Row>",
                            part.PartIndex,
                            part.FormattedTimestamp,
                            partJudgeStyle,
                            part.StatusText,
                            EscapeXml(m.ItemName),
                            EscapeXml(m.ToolType),
                            m.Nominal,
                            m.Lsl,
                            m.Usl,
                            m.MeasuredValue,
                            EscapeXml(m.Unit),
                            itemJudgeStyle,
                            m.Judge,
                            EscapeXml(part.DetailedReason)));
                    }
                }
            }
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");

        sb.AppendLine("</Workbook>");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsv(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        if (input.Contains(',') || input.Contains('"') || input.Contains('\n') || input.Contains('\r'))
        {
            return $"\"{input.Replace("\"", "\"\"")}\"";
        }
        return input;
    }

    private static string EscapeXml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

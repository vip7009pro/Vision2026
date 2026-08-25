using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

public enum PlcTagCsvFormat
{
    AutoDetect,
    GxWorks3GlobalLabels,
    GxWorksDeviceComments,
    StandardCsv
}

public static class PlcTagCsvService
{
    /// <summary>
    /// Tự động nhận diện định dạng CSV dựa trên Header
    /// </summary>
    public static PlcTagCsvFormat DetectCsvFormat(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent)) return PlcTagCsvFormat.StandardCsv;

        using var reader = new StringReader(csvContent);
        string? headerLine = reader.ReadLine();
        while (headerLine != null && string.IsNullOrWhiteSpace(headerLine))
        {
            headerLine = reader.ReadLine();
        }

        if (string.IsNullOrWhiteSpace(headerLine)) return PlcTagCsvFormat.StandardCsv;

        var tokens = ParseCsvLine(headerLine);
        var normalizedTokens = tokens.Select(t => t.Trim().Trim('"').ToLowerInvariant()).ToList();

        if (normalizedTokens.Contains("label name") || normalizedTokens.Contains("class") || 
            (normalizedTokens.Contains("data type") && normalizedTokens.Contains("device")))
        {
            return PlcTagCsvFormat.GxWorks3GlobalLabels;
        }

        if (normalizedTokens.Count == 2 && normalizedTokens.Contains("device") && normalizedTokens.Contains("comment"))
        {
            return PlcTagCsvFormat.GxWorksDeviceComments;
        }

        return PlcTagCsvFormat.StandardCsv;
    }

    /// <summary>
    /// Phân tích nội dung CSV thành danh sách PlcTag
    /// </summary>
    public static List<PlcTag> ParseCsv(string csvContent, string plcId = "PLC1", PlcTagCsvFormat format = PlcTagCsvFormat.AutoDetect)
    {
        var result = new List<PlcTag>();
        if (string.IsNullOrWhiteSpace(csvContent)) return result;

        if (format == PlcTagCsvFormat.AutoDetect)
        {
            format = DetectCsvFormat(csvContent);
        }

        var lines = SplitCsvLines(csvContent);
        if (lines.Count == 0) return result;

        switch (format)
        {
            case PlcTagCsvFormat.GxWorks3GlobalLabels:
                ParseGxWorks3GlobalLabels(lines, plcId, result);
                break;
            case PlcTagCsvFormat.GxWorksDeviceComments:
                ParseGxWorksDeviceComments(lines, plcId, result);
                break;
            case PlcTagCsvFormat.StandardCsv:
            default:
                ParseStandardCsv(lines, plcId, result);
                break;
        }

        return result;
    }

    private static void ParseGxWorks3GlobalLabels(List<string> lines, string plcId, List<PlcTag> result)
    {
        if (lines.Count == 0) return;

        // Bỏ qua dòng Header
        int startIndex = 0;
        var firstTokens = ParseCsvLine(lines[0]);
        if (firstTokens.Any(t => t.Trim().Trim('"').Equals("Label Name", StringComparison.OrdinalIgnoreCase) ||
                                 t.Trim().Trim('"').Equals("Class", StringComparison.OrdinalIgnoreCase)))
        {
            startIndex = 1;
        }

        for (int i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var tokens = ParseCsvLine(line);
            if (tokens.Count < 3) continue;

            // Format: "Class","Label Name","Data Type","Constant","Device","Address","Comment"
            // Indices:   0          1            2          3         4        5         6
            string labelName = tokens.Count > 1 ? tokens[1].Trim() : string.Empty;
            string rawDataType = tokens.Count > 2 ? tokens[2].Trim() : string.Empty;
            string device = tokens.Count > 4 ? tokens[4].Trim() : string.Empty;
            string address = tokens.Count > 5 ? tokens[5].Trim() : string.Empty;
            string comment = tokens.Count > 6 ? tokens[6].Trim() : string.Empty;

            // Nếu device rỗng, thử lấy address
            string finalAddress = !string.IsNullOrWhiteSpace(device) ? device : address;
            if (string.IsNullOrWhiteSpace(finalAddress) && string.IsNullOrWhiteSpace(labelName)) continue;

            // Nếu address rỗng nhưng labelName trông giống Address (ví dụ X0, Y1), dùng labelName làm address
            if (string.IsNullOrWhiteSpace(finalAddress))
            {
                finalAddress = labelName;
            }

            // Tên Tag
            string finalName = !string.IsNullOrWhiteSpace(labelName) ? labelName : $"Tag_{finalAddress}";

            // Map kiểu dữ liệu
            PlcDataType dataType = MapGxWorksDataType(rawDataType, finalAddress, finalName);

            var tag = new PlcTag
            {
                PlcId = plcId,
                Name = finalName,
                Address = finalAddress,
                DataType = dataType,
                Description = comment,
                Category = "GXWorks_Global"
            };

            result.Add(tag);
        }
    }

    private static void ParseGxWorksDeviceComments(List<string> lines, string plcId, List<PlcTag> result)
    {
        if (lines.Count == 0) return;

        int startIndex = 0;
        var firstTokens = ParseCsvLine(lines[0]);
        if (firstTokens.Any(t => t.Trim().Trim('"').Equals("Device", StringComparison.OrdinalIgnoreCase)))
        {
            startIndex = 1;
        }

        for (int i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var tokens = ParseCsvLine(line);
            if (tokens.Count < 2) continue;

            string device = tokens[0].Trim();
            string comment = tokens[1].Trim();

            if (string.IsNullOrWhiteSpace(device)) continue;

            // Trích xuất Tag Name từ Comment nếu có (ví dụ: "Vision_Ready (Vision sẵn sàng...)")
            string tagName = device;
            string description = comment;

            if (!string.IsNullOrWhiteSpace(comment))
            {
                int parenIdx = comment.IndexOfAny(new[] { '(', '[', '-', ':' });
                if (parenIdx > 0)
                {
                    string candidateName = comment.Substring(0, parenIdx).Trim();
                    if (IsValidIdentifier(candidateName))
                    {
                        tagName = candidateName;
                    }
                }
                else if (IsValidIdentifier(comment))
                {
                    tagName = comment;
                }
            }

            PlcDataType dataType = PlcManagerService.InferDataTypeFromAddress(device);

            var tag = new PlcTag
            {
                PlcId = plcId,
                Name = tagName,
                Address = device,
                DataType = dataType,
                Description = description,
                Category = "GXWorks_Device"
            };

            result.Add(tag);
        }
    }

    private static void ParseStandardCsv(List<string> lines, string plcId, List<PlcTag> result)
    {
        if (lines.Count == 0) return;

        int startIndex = 0;
        int nameCol = 0, addrCol = 1, typeCol = 2, readOnlyCol = 3, descCol = 4, plcIdCol = -1;

        // Kiểm tra dòng Header
        var headerTokens = ParseCsvLine(lines[0]);
        bool hasHeader = false;
        for (int c = 0; c < headerTokens.Count; c++)
        {
            string col = headerTokens[c].Trim().Trim('"').ToLowerInvariant();
            if (col.Contains("name") || col.Contains("tag")) { nameCol = c; hasHeader = true; }
            else if (col.Contains("address") || col.Contains("addr")) { addrCol = c; hasHeader = true; }
            else if (col.Contains("type") || col.Contains("data")) { typeCol = c; hasHeader = true; }
            else if (col.Contains("readonly") || col.Contains("read")) { readOnlyCol = c; hasHeader = true; }
            else if (col.Contains("desc") || col.Contains("comment") || col.Contains("mô tả")) { descCol = c; hasHeader = true; }
            else if (col.Contains("plc") || col.Contains("station")) { plcIdCol = c; hasHeader = true; }
        }

        if (hasHeader)
        {
            startIndex = 1;
        }

        for (int i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var tokens = ParseCsvLine(line);
            if (tokens.Count == 0) continue;

            string name = tokens.Count > nameCol ? tokens[nameCol].Trim() : string.Empty;
            string address = tokens.Count > addrCol ? tokens[addrCol].Trim() : string.Empty;
            string rawType = tokens.Count > typeCol ? tokens[typeCol].Trim() : string.Empty;
            string rawReadOnly = tokens.Count > readOnlyCol ? tokens[readOnlyCol].Trim() : string.Empty;
            string description = tokens.Count > descCol ? tokens[descCol].Trim() : string.Empty;
            string rowPlcId = (plcIdCol >= 0 && tokens.Count > plcIdCol) ? tokens[plcIdCol].Trim() : plcId;

            if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(name)) continue;
            if (string.IsNullOrWhiteSpace(address)) address = name;
            if (string.IsNullOrWhiteSpace(name)) name = $"Tag_{address}";
            if (string.IsNullOrWhiteSpace(rowPlcId)) rowPlcId = plcId;

            PlcDataType dataType = MapGxWorksDataType(rawType, address, name);
            bool isReadOnly = false;
            if (!string.IsNullOrWhiteSpace(rawReadOnly))
            {
                bool.TryParse(rawReadOnly, out isReadOnly);
            }

            var tag = new PlcTag
            {
                PlcId = rowPlcId,
                Name = name,
                Address = address,
                DataType = dataType,
                ReadOnly = isReadOnly,
                Description = description,
                Category = "Standard"
            };

            result.Add(tag);
        }
    }

    /// <summary>
    /// Chuyển đổi chuỗi Data Type của GX Works / IEC / C# sang enum PlcDataType
    /// </summary>
    public static PlcDataType MapGxWorksDataType(string rawType, string address, string tagName = "")
    {
        string norm = (rawType ?? string.Empty).Trim().ToLowerInvariant();

        if (norm.Contains("bit") || norm.Equals("bool") || norm.Equals("boolean"))
        {
            return PlcDataType.Bool;
        }
        if (norm.Contains("single-precision real") || norm.Equals("real") || norm.Equals("float") || norm.Equals("single"))
        {
            return PlcDataType.Float;
        }
        if (norm.Contains("double-precision real") || norm.Equals("lreal") || norm.Equals("double"))
        {
            return PlcDataType.Double;
        }
        if (norm.Contains("double word [signed]") || norm.Equals("dint") || norm.Equals("int32") || norm.Equals("integer"))
        {
            return PlcDataType.Int32;
        }
        if (norm.Contains("double word [unsigned]") || norm.Contains("32-bit") || norm.Equals("udint") || norm.Equals("uint32") || norm.Equals("dword"))
        {
            return PlcDataType.UInt32;
        }
        if (norm.Contains("word [signed]") || norm.Equals("int") || norm.Equals("int16") || norm.Equals("short"))
        {
            return PlcDataType.Int16;
        }
        if (norm.Contains("word [unsigned]") || norm.Contains("16-bit") || norm.Equals("uint") || norm.Equals("uint16") || norm.Equals("word"))
        {
            return PlcDataType.UInt16;
        }
        if (norm.Contains("string"))
        {
            return PlcDataType.String;
        }
        if (norm.Contains("byte") || norm.Equals("sint"))
        {
            return PlcDataType.UInt16;
        }

        // Nếu chuỗi rỗng hoặc không khớp, kiểm tra gợi ý từ TagName
        string nameUpper = (tagName ?? string.Empty).ToUpperInvariant();
        if (nameUpper.Contains("COUNT") || nameUpper.Contains("PULSE") || nameUpper.Contains("TOTAL"))
        {
            return PlcDataType.Int32;
        }
        if (nameUpper.Contains("POS") || nameUpper.Contains("MM") || nameUpper.Contains("COORD") || 
            nameUpper.Contains("ANGLE") || nameUpper.Contains("DIST") || nameUpper.Contains("SCORE"))
        {
            return PlcDataType.Float;
        }

        // Tự động suy luận từ địa chỉ thiết bị
        return PlcManagerService.InferDataTypeFromAddress(address);
    }

    /// <summary>
    /// Xuất danh bạ Tags sang định dạng Standard CSV
    /// </summary>
    public static string ExportToStandardCsv(IEnumerable<PlcTag> tags)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tag Name,Address,Data Type,Read Only,Description,PLC ID");

        foreach (var tag in tags)
        {
            sb.AppendLine($"{EscapeCsv(tag.Name)},{EscapeCsv(tag.Address)},{EscapeCsv(tag.DataType.ToString())},{tag.ReadOnly},{EscapeCsv(tag.Description)},{EscapeCsv(tag.PlcId)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Xuất danh bạ Tags sang định dạng Mitsubishi GX Works 3 Global Labels CSV
    /// </summary>
    public static string ExportToGxWorksGlobalLabelsCsv(IEnumerable<PlcTag> tags)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"Class\",\"Label Name\",\"Data Type\",\"Constant\",\"Device\",\"Address\",\"Comment\"");

        foreach (var tag in tags)
        {
            string gxDataType = FormatGxWorksDataType(tag.DataType);
            sb.AppendLine($"\"VAR_GLOBAL\",{EscapeCsvQuoted(tag.Name)},{EscapeCsvQuoted(gxDataType)},\"\",{EscapeCsvQuoted(tag.Address)},\"\",{EscapeCsvQuoted(tag.Description)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Xuất danh bạ Tags sang định dạng Mitsubishi GX Works Device Comments CSV
    /// </summary>
    public static string ExportToGxWorksDeviceCommentsCsv(IEnumerable<PlcTag> tags)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"Device\",\"Comment\"");

        foreach (var tag in tags)
        {
            string comment = !string.IsNullOrWhiteSpace(tag.Description) 
                ? $"{tag.Name} ({tag.Description})" 
                : tag.Name;

            sb.AppendLine($"{EscapeCsvQuoted(tag.Address)},{EscapeCsvQuoted(comment)}");
        }

        return sb.ToString();
    }

    private static string FormatGxWorksDataType(PlcDataType dataType)
    {
        return dataType switch
        {
            PlcDataType.Bool => "Bit",
            PlcDataType.Int16 => "Word [Signed]",
            PlcDataType.UInt16 => "Word [Unsigned]/Bit String [16-bit]",
            PlcDataType.Int32 => "Double Word [Signed]",
            PlcDataType.UInt32 => "Double Word [Unsigned]/Bit String [32-bit]",
            PlcDataType.Float => "Single-precision real",
            PlcDataType.Double => "Double-precision real",
            PlcDataType.String => "String",
            _ => "Word [Signed]"
        };
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value.Contains(';'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static string EscapeCsvQuoted(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains(' ') || name.Contains(',') || name.Contains('\t')) return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-');
    }

    private static List<string> SplitCsvLines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    /// <summary>
    /// Phân tích một dòng CSV có xử lý dấu ngoặc kép RFC 4180
    /// </summary>
    public static List<string> ParseCsvLine(string line)
    {
        var tokens = new List<string>();
        if (line == null) return tokens;

        var sb = new StringBuilder();
        bool inQuotes = false;
        char delimiter = line.Contains(';') && !line.Contains(',') ? ';' : ',';

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++; // Bỏ qua dấu ngoặc kép kép
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                tokens.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        tokens.Add(sb.ToString().Trim());
        return tokens;
    }
}

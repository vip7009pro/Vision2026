using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.OQC;

public sealed class OqcScannerService : IOqcScannerService
{
    private readonly string _configFilePath;
    public OqcScannerConfig Config { get; private set; } = new();

    public OqcScannerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Vision2026");
        Directory.CreateDirectory(dir);
        _configFilePath = Path.Combine(dir, "oqc_scanner_config.json");

        LoadConfig();
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var loaded = JsonSerializer.Deserialize<OqcScannerConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (loaded != null)
                {
                    Config = loaded;
                    return;
                }
            }
        }
        catch
        {
            // Fallback to default config on error
        }

        Config = new OqcScannerConfig();
    }

    public void SaveConfig(OqcScannerConfig config)
    {
        if (config == null) return;
        Config = config;

        try
        {
            var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save OQC config: {ex.Message}");
        }
    }

    public async Task<(bool Found, string JobFilePath, string ErrorMessage)> LookupJobAsync(
        string scannedCode, IDbManagerService dbManager)
    {
        if (string.IsNullOrWhiteSpace(scannedCode))
        {
            return (false, string.Empty, "Mã scan rỗng.");
        }

        if (dbManager == null)
        {
            return (false, string.Empty, "Dịch vụ DB Manager chưa được khởi tạo.");
        }

        if (string.IsNullOrWhiteSpace(Config.LookupQuery))
        {
            return (false, string.Empty, "Chưa cấu hình truy vấn tra cứu Job (Lookup Query).");
        }

        string safeCode = EscapeSqlValue(scannedCode.Trim());
        string query = Config.LookupQuery.Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase);

        // Validate safety (Read mode)
        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Read, allowUpdateDelete: false);
        if (!isSafe)
        {
            return (false, string.Empty, safetyError);
        }

        var (success, table, error) = await dbManager.ExecuteQueryAsync(Config.LookupDbId, query);
        if (!success || table == null || table.Rows.Count == 0)
        {
            return (false, string.Empty, string.IsNullOrWhiteSpace(error) ? $"Không tìm thấy Job cho mã '{scannedCode}' trong cơ sở dữ liệu." : error);
        }

        // Extract column
        string colName = Config.JobFilePathColumn?.Trim() ?? "";
        object? rawVal = null;

        if (!string.IsNullOrEmpty(colName) && table.Columns.Contains(colName))
        {
            rawVal = table.Rows[0][colName];
        }
        else if (table.Columns.Count > 0)
        {
            rawVal = table.Rows[0][0];
        }

        if (rawVal == null || rawVal == DBNull.Value)
        {
            return (false, string.Empty, $"Kết quả DB trả về ô rỗng cho mã '{scannedCode}'.");
        }

        string rawPath = rawVal.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return (false, string.Empty, $"Đường dẫn Job từ DB rỗng cho mã '{scannedCode}'.");
        }

        // Check file existence
        string resolvedPath = rawPath;

        // 1. Direct existence check
        if (File.Exists(resolvedPath))
        {
            return (true, resolvedPath, string.Empty);
        }

        // 2. Combine with JobRootDirectory if relative path or filename
        if (!string.IsNullOrWhiteSpace(Config.JobRootDirectory))
        {
            string fileNameOnly = Path.GetFileName(rawPath);
            string combinedPath = Path.Combine(Config.JobRootDirectory, fileNameOnly);
            if (File.Exists(combinedPath))
            {
                return (true, combinedPath, string.Empty);
            }

            string combinedRelative = Path.Combine(Config.JobRootDirectory, rawPath.TrimStart('\\', '/'));
            if (File.Exists(combinedRelative))
            {
                return (true, combinedRelative, string.Empty);
            }
        }

        return (false, rawPath, $"Không tìm thấy tệp Job tại đường dẫn: '{rawPath}'" +
            (!string.IsNullOrWhiteSpace(Config.JobRootDirectory) ? $" hoặc trong thư mục gốc '{Config.JobRootDirectory}'." : "."));
    }

    public async Task<(bool Found, string ProductName, string ErrorMessage)> LookupProductNameAsync(
        string scannedCode, IDbManagerService dbManager)
    {
        if (string.IsNullOrWhiteSpace(scannedCode))
        {
            return (false, string.Empty, "Mã scan rỗng.");
        }

        if (!Config.EnableProductNameLookup || string.IsNullOrWhiteSpace(Config.ProductNameQuery))
        {
            return (false, scannedCode, "Chưa bật hoặc chưa cấu hình truy vấn Tên sản phẩm.");
        }

        if (dbManager == null)
        {
            return (false, scannedCode, "Dịch vụ DB Manager chưa được khởi tạo.");
        }

        string safeCode = EscapeSqlValue(scannedCode.Trim());
        string query = Config.ProductNameQuery.Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Read, allowUpdateDelete: false);
        if (!isSafe)
        {
            return (false, scannedCode, safetyError);
        }

        var (success, table, error) = await dbManager.ExecuteQueryAsync(Config.ProductNameDbId, query);
        if (!success || table == null || table.Rows.Count == 0)
        {
            return (false, scannedCode, string.IsNullOrWhiteSpace(error) ? $"Không tìm thấy Tên sản phẩm cho mã '{scannedCode}' trong cơ sở dữ liệu." : error);
        }

        string colName = Config.ProductNameColumn?.Trim() ?? "";
        object? rawVal = null;

        if (!string.IsNullOrEmpty(colName) && table.Columns.Contains(colName))
        {
            rawVal = table.Rows[0][colName];
        }
        else if (table.Columns.Count > 0)
        {
            rawVal = table.Rows[0][0];
        }

        if (rawVal == null || rawVal == DBNull.Value)
        {
            return (false, scannedCode, "Kết quả DB trả về ô tên sản phẩm rỗng.");
        }

        string name = rawVal.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, scannedCode, "Tên sản phẩm trả về rỗng.");
        }

        return (true, name, string.Empty);
    }

    public async Task<(bool Success, DataTable? Table, string ErrorMessage)> GetProductListAsync(
        string searchText, int pageIndex, IDbManagerService dbManager)
    {
        if (dbManager == null)
        {
            return (false, null, "DB Manager service not available.");
        }

        if (string.IsNullOrWhiteSpace(Config.ProductListQuery))
        {
            return (false, null, "Chưa cấu hình truy vấn danh sách sản phẩm.");
        }

        int pageSize = Math.Max(1, Config.ProductListPageSize);
        int offset = Math.Max(0, pageIndex * pageSize);
        string safeSearch = EscapeSqlValue((searchText ?? "").Trim());

        string query = Config.ProductListQuery
            .Replace("{SearchText}", safeSearch, StringComparison.OrdinalIgnoreCase)
            .Replace("{Offset}", offset.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{PageSize}", pageSize.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Read, allowUpdateDelete: false);
        if (!isSafe)
        {
            return (false, null, safetyError);
        }

        var (success, table, error) = await dbManager.ExecuteQueryAsync(Config.ProductListDbId, query);
        return (success, table, error);
    }

    public async Task<(bool Success, string Message)> AssignProductJobAsync(
        string productCode, string jobFilePath, IDbManagerService dbManager)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return (false, "Mã sản phẩm rỗng.");
        }

        if (string.IsNullOrWhiteSpace(jobFilePath))
        {
            return (false, "Đường dẫn tệp Job rỗng.");
        }

        if (dbManager == null)
        {
            return (false, "DB Manager service not available.");
        }

        if (string.IsNullOrWhiteSpace(Config.AssignQuery))
        {
            return (false, "Chưa cấu hình truy vấn Gán sản phẩm (Assign Query).");
        }

        string safeCode = EscapeSqlValue(productCode.Trim());
        string safePath = EscapeSqlValue(jobFilePath.Trim());

        string query = Config.AssignQuery
            .Replace("{ProductCode}", safeCode, StringComparison.OrdinalIgnoreCase)
            .Replace("{JobFilePath}", safePath, StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Write, allowUpdateDelete: true);
        if (!isSafe)
        {
            return (false, safetyError);
        }

        var (success, rows, error) = await dbManager.ExecuteNonQueryAsync(Config.AssignDbId, query);
        if (success)
        {
            return (true, $"✅ Gán sản phẩm '{productCode}' với Job '{Path.GetFileName(jobFilePath)}' thành công! (Số dòng tác động: {rows})");
        }
        else
        {
            return (false, $"Lỗi DB: {error}");
        }
    }

    public async Task<(bool Success, string Message)> LogInspectionResultAsync(
        string scannedCode, string jobFilePath, InspectionResult result, VisionConfig config, IDbManagerService dbManager)
    {
        if (!Config.LogResultToDb || string.IsNullOrWhiteSpace(Config.LogResultQuery))
        {
            return (true, "Ghi log DB bị tắt.");
        }

        if (dbManager == null)
        {
            return (false, "DB Manager service not available.");
        }

        string safeCode = EscapeSqlValue((scannedCode ?? "").Trim());
        string safeProductName = EscapeSqlValue((config?.ProductName ?? "").Trim());
        string safePath = EscapeSqlValue((jobFilePath ?? "").Trim());
        string passBit = (result != null && result.Pass) ? "1" : "0";
        string inspectResultText = (result != null && result.Pass) ? "PASS" : "NG";
        string ngReasons = result != null ? EscapeSqlValue(ExtractNgReasons(result)) : "";

        string query = Config.LogResultQuery
            .Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase)
            .Replace("{ProductName}", safeProductName, StringComparison.OrdinalIgnoreCase)
            .Replace("{JobFilePath}", safePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{PassBit}", passBit, StringComparison.OrdinalIgnoreCase)
            .Replace("{Pass}", passBit, StringComparison.OrdinalIgnoreCase)
            .Replace("{InspectResult}", inspectResultText, StringComparison.OrdinalIgnoreCase)
            .Replace("{NgReasons}", ngReasons, StringComparison.OrdinalIgnoreCase);

        if (result != null && config != null)
        {
            query = DbNodeRunner.InterpolateSqlQuery(query, result, config);
        }

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Write, allowUpdateDelete: true);
        if (!isSafe)
        {
            return (false, safetyError);
        }

        var (success, rows, error) = await dbManager.ExecuteNonQueryAsync(Config.LogResultDbId, query);
        if (success)
        {
            return (true, $"✅ Đã lưu kết quả kiểm tra lên DB! (Số dòng: {rows})");
        }
        else
        {
            return (false, $"Lỗi ghi log DB: {error}");
        }
    }

    public static string ExtractNgReasons(InspectionResult result)
    {
        if (result == null) return "Chưa có kết quả";
        if (result.Pass) return "Tất cả công cụ kiểm tra đạt yêu cầu (PASS).";

        var reasons = new System.Collections.Generic.List<string>();

        if (result.Origin != null && !result.Origin.Pass)
        {
            reasons.Add($"Origin NG (Score: {result.Origin.Score:F3})");
        }

        foreach (var d in result.Distances)
        {
            if (!d.Pass)
            {
                reasons.Add($"Distance [{d.Name}] NG: {d.Value:F3}mm (Tiêu chuẩn: {d.Nominal:F3}, Dung sai: +{d.TolPlus}/-{d.TolMinus})");
            }
        }

        foreach (var l2l in result.LineToLineDistances)
        {
            if (!l2l.Pass)
            {
                reasons.Add($"LineToLine [{l2l.Name}] NG: {l2l.Value:F3}mm (Tiêu chuẩn: {l2l.Nominal:F3})");
            }
        }

        foreach (var p2l in result.PointToLineDistances)
        {
            if (!p2l.Pass)
            {
                reasons.Add($"PointToLine [{p2l.Name}] NG: {p2l.Value:F3}mm (Tiêu chuẩn: {p2l.Nominal:F3})");
            }
        }

        foreach (var seg in result.SegmentLineDistances)
        {
            if (!seg.Pass)
            {
                reasons.Add($"SegmentLine [{seg.Name}] NG: {seg.Value:F3}mm");
            }
        }

        foreach (var ang in result.Angles)
        {
            if (!ang.Pass)
            {
                reasons.Add($"Angle [{ang.Name}] NG: {ang.ValueDeg:F2}° (Tiêu chuẩn: {ang.Nominal:F2}°)");
            }
        }

        foreach (var ep in result.EdgePairs)
        {
            if (!ep.Pass)
            {
                reasons.Add($"EdgePair [{ep.Name}] NG: {ep.Value:F3}mm");
            }
        }

        foreach (var epd in result.EdgePairDetections)
        {
            if (!epd.Pass)
            {
                reasons.Add($"EdgePairDetect [{epd.Name}] NG: {epd.Value:F3}mm");
            }
        }

        foreach (var dia in result.Diameters)
        {
            if (!dia.Pass)
            {
                reasons.Add($"Diameter [{dia.Name}] NG: {dia.Value:F3}mm");
            }
        }

        foreach (var c in result.Conditions)
        {
            if (!c.Pass)
            {
                reasons.Add($"Condition [{c.Name}] NG ({c.Expression})");
            }
        }

        foreach (var sc in result.SurfaceCompares)
        {
            if (!sc.Pass)
            {
                reasons.Add($"Ngoại quan [{sc.Name}] NG ({sc.Count} vết lỗi)");
            }
        }

        foreach (var cc in result.ContourCompares)
        {
            if (!cc.Pass)
            {
                reasons.Add($"ContourCompare [{cc.Name}] NG (Score: {cc.MatchScore:F3}, MaxDist: {cc.MaxDistancePx:F1}px)");
            }
        }

        foreach (var cd in result.CodeDetections)
        {
            if (!cd.Found)
            {
                reasons.Add($"CodeDetect [{cd.Name}] NG (Không đọc được mã)");
            }
        }

        if (reasons.Count == 0) return "NG (Không đạt tiêu chí kiểm tra chung)";

        return string.Join("; ", reasons);
    }

    private static string EscapeSqlValue(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw.Replace("'", "''");
    }
}

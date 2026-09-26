using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public class SystemConfigBackupService : ISystemConfigBackupService
{
    private readonly IDbManagerService _dbManager;
    private readonly IPlcManagerService _plcManager;
    private readonly IOqcScannerService _oqcScanner;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public event EventHandler? SystemConfigRestored;

    public SystemConfigBackupService(
        IDbManagerService dbManager,
        IPlcManagerService plcManager,
        IOqcScannerService oqcScanner)
    {
        _dbManager = dbManager;
        _plcManager = plcManager;
        _oqcScanner = oqcScanner;
    }

    private static string AppSettingsPath => AppStoragePaths.GlobalSettingsFilePath;
    private static string Vision2026Dir => AppStoragePaths.StandardConfigDirectory;

    private static string CameraSettingsPath => AppStoragePaths.CameraAdjustSettingsFilePath;
    private static string CalibrationMatrixPath => AppStoragePaths.ChessboardCalibrationFilePath;
    private static string CalibrationSettingsPath => AppStoragePaths.ChessboardSettingsFilePath;

    public async Task<SystemConfigPackage> CreateBackupPackageAsync(SystemConfigBackupOptions? options = null)
    {
        options ??= new SystemConfigBackupOptions();

        var package = new SystemConfigPackage
        {
            Header = SystemConfigPackage.CurrentPackageHeader,
            Version = SystemConfigPackage.CurrentPackageVersion,
            ExportedAt = DateTime.Now,
            ExportedFromMachine = Environment.MachineName,
            AppVersion = "2026.9",
            Description = $"Bản sao lưu cấu hình toàn bộ hệ thống từ máy {Environment.MachineName} ({DateTime.Now:dd/MM/yyyy HH:mm:ss})"
        };

        // 1. Cài đặt Ứng dụng & Chiếu sáng
        if (options.IncludeAppSettings)
        {
            try
            {
                if (File.Exists(AppSettingsPath))
                {
                    string json = await File.ReadAllTextAsync(AppSettingsPath);
                    package.AppSettings = JsonSerializer.Deserialize<GlobalAppSettings>(json, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Lỗi nạp AppSettings: {ex.Message}");
            }
        }

        // 2. Cấu hình PLC & Motion
        if (options.IncludePlcConfig)
        {
            try
            {
                package.PlcConfig = new PlcConfigContainer
                {
                    Plcs = _plcManager.Plcs.ToList(),
                    Tags = _plcManager.Tags.ToList(),
                    IndustrialConfig = _plcManager.IndustrialConfig
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Lỗi nạp PlcConfig: {ex.Message}");
            }
        }

        // 3. Cấu hình Cơ sở dữ liệu
        if (options.IncludeDatabaseConfig)
        {
            try
            {
                package.DatabaseConfig = _dbManager.Databases.ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Lỗi nạp DatabaseConfig: {ex.Message}");
            }
        }

        // 4. Cấu hình OQC Scanner & SQL Queries
        if (options.IncludeOqcConfig)
        {
            try
            {
                var oqcCfg = _oqcScanner.Config;
                // Tạo bản sao và bổ sung DbName cho từng vị trí DB
                var cloned = JsonSerializer.Deserialize<OqcScannerConfig>(
                    JsonSerializer.Serialize(oqcCfg, JsonOptions), JsonOptions) ?? new OqcScannerConfig();

                cloned.LookupDbName = _dbManager.GetDatabase(cloned.LookupDbId)?.Name ?? "";
                cloned.ProductNameDbName = _dbManager.GetDatabase(cloned.ProductNameDbId)?.Name ?? "";
                cloned.ProductListDbName = _dbManager.GetDatabase(cloned.ProductListDbId)?.Name ?? "";
                cloned.AssignDbName = _dbManager.GetDatabase(cloned.AssignDbId)?.Name ?? "";
                cloned.UpdateTeachImageDbName = _dbManager.GetDatabase(cloned.UpdateTeachImageDbId)?.Name ?? "";
                cloned.JobManagerDbName = _dbManager.GetDatabase(cloned.JobManagerDbId)?.Name ?? "";
                cloned.LogResultDbName = _dbManager.GetDatabase(cloned.LogResultDbId)?.Name ?? "";
                cloned.LogDetailResultDbName = _dbManager.GetDatabase(cloned.LogDetailResultDbId)?.Name ?? "";

                package.OqcConfig = cloned;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Lỗi nạp OqcConfig: {ex.Message}");
            }
        }

        // 5. Cấu hình Camera điều chỉnh
        if (options.IncludeCameraConfig)
        {
            try
            {
                string camPath = File.Exists(CameraSettingsPath)
                    ? CameraSettingsPath
                    : Path.Combine(AppStoragePaths.AppBaseDirectory, "camera_adjust_settings.json");
                if (File.Exists(camPath))
                {
                    package.CameraSettingsJson = await File.ReadAllTextAsync(camPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Lỗi nạp CameraSettings: {ex.Message}");
            }
        }

        // 6. Cấu hình Hiệu chuẩn thấu kính Chessboard
        if (options.IncludeCalibrationConfig)
        {
            try
            {
                if (File.Exists(CalibrationMatrixPath))
                {
                    package.ChessboardCalibrationJson = await File.ReadAllTextAsync(CalibrationMatrixPath);
                }
                if (File.Exists(CalibrationSettingsPath))
                {
                    package.ChessboardSettingsJson = await File.ReadAllTextAsync(CalibrationSettingsPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Lỗi nạp Calibration: {ex.Message}");
            }
        }

        return package;
    }

    public async Task<(bool Success, string FilePath, string ErrorMessage)> ExportBackupPackageToFileAsync(string filePath, SystemConfigBackupOptions? options = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return (false, string.Empty, "Đường dẫn tệp xuất không hợp lệ.");

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var package = await CreateBackupPackageAsync(options);
            string json = JsonSerializer.Serialize(package, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);

            return (true, filePath, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"Lỗi xuất tệp sao lưu: {ex.Message}");
        }
    }

    public SystemConfigPackageInspection InspectBackupPackage(string filePath)
    {
        var result = new SystemConfigPackageInspection();

        try
        {
            if (!File.Exists(filePath))
            {
                result.IsValid = false;
                result.ErrorMessage = "Tệp không tồn tại.";
                return result;
            }

            string json = File.ReadAllText(filePath);
            var package = JsonSerializer.Deserialize<SystemConfigPackage>(json, JsonOptions);

            if (package == null)
            {
                result.IsValid = false;
                result.ErrorMessage = "Không thể giải mã dữ liệu tệp sao lưu.";
                return result;
            }

            result.IsValid = true;
            result.Header = package.Header ?? "N/A";
            result.Version = package.Version ?? "1.0";
            result.ExportedAt = package.ExportedAt;
            result.ExportedFromMachine = package.ExportedFromMachine ?? "Không rõ";
            result.Description = package.Description ?? "";

            result.HasAppSettings = package.AppSettings != null;
            result.HasPlcConfig = package.PlcConfig != null;
            result.PlcCount = package.PlcConfig?.Plcs?.Count ?? 0;
            result.TagCount = package.PlcConfig?.Tags?.Count ?? 0;

            result.HasDatabaseConfig = package.DatabaseConfig != null && package.DatabaseConfig.Count > 0;
            result.DatabaseCount = package.DatabaseConfig?.Count ?? 0;
            if (package.DatabaseConfig != null)
            {
                result.DatabaseNames = package.DatabaseConfig.Select(d => d.Name).ToList();
            }

            result.HasOqcConfig = package.OqcConfig != null;
            result.HasCameraConfig = !string.IsNullOrWhiteSpace(package.CameraSettingsJson);
            result.HasCalibrationConfig = !string.IsNullOrWhiteSpace(package.ChessboardCalibrationJson);

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"Lỗi đọc tệp sao lưu: {ex.Message}";
            return result;
        }
    }

    public async Task<SystemConfigRestoreResult> RestoreBackupPackageFromFileAsync(string filePath, SystemConfigRestoreOptions? options = null)
    {
        options ??= new SystemConfigRestoreOptions();
        var result = new SystemConfigRestoreResult();

        try
        {
            if (!File.Exists(filePath))
            {
                result.Success = false;
                result.Message = "Tệp sao lưu không tồn tại trên đĩa.";
                result.Logs.Add($"[LỖI] Tệp không tồn tại: {filePath}");
                return result;
            }

            result.Logs.Add($"[Khởi động] Đọc tệp cấu hình: {Path.GetFileName(filePath)}...");
            string json = await File.ReadAllTextAsync(filePath);
            var package = JsonSerializer.Deserialize<SystemConfigPackage>(json, JsonOptions);

            if (package == null)
            {
                result.Success = false;
                result.Message = "Dữ liệu cấu hình không hợp lệ.";
                result.Logs.Add("[LỖI] Giải mã JSON thất bại.");
                return result;
            }

            result.Logs.Add($"[Thông tin gói] Máy xuất: {package.ExportedFromMachine}, Ngày tạo: {package.ExportedAt:dd/MM/yyyy HH:mm:ss}");

            // 1. Phục hồi Cơ sở dữ liệu (Database Config)
            if (options.RestoreDatabaseConfig && package.DatabaseConfig != null && package.DatabaseConfig.Count > 0)
            {
                try
                {
                    _dbManager.LoadDatabases(package.DatabaseConfig);
                    result.DatabasesRestored = package.DatabaseConfig.Count;
                    result.Logs.Add($"[Cơ sở dữ liệu] ✅ Đã nạp {package.DatabaseConfig.Count} kết nối Database ({string.Join(", ", package.DatabaseConfig.Select(d => d.Name))}).");
                }
                catch (Exception ex)
                {
                    result.Logs.Add($"[Cơ sở dữ liệu] ⚠️ Lỗi nạp Database: {ex.Message}");
                }
            }

            // 2. Phục hồi PLC & Motion
            if (options.RestorePlcConfig && package.PlcConfig != null)
            {
                try
                {
                    var plcs = package.PlcConfig.Plcs ?? new List<PlcModel>();
                    var tags = package.PlcConfig.Tags ?? new List<PlcTag>();
                    _plcManager.LoadConfig(plcs, tags);
                    if (package.PlcConfig.IndustrialConfig != null)
                    {
                        _plcManager.IndustrialConfig = package.PlcConfig.IndustrialConfig;
                    }
                    _plcManager.SaveGlobalConfig();
                    result.PlcsRestored = plcs.Count;
                    result.TagsRestored = tags.Count;
                    result.Logs.Add($"[PLC & Motion] ✅ Đã nạp {plcs.Count} trạm PLC và {tags.Count} biến Tags.");
                }
                catch (Exception ex)
                {
                    result.Logs.Add($"[PLC & Motion] ⚠️ Lỗi nạp PLC: {ex.Message}");
                }
            }

            // 3. Phục hồi OQC Scanner Config
            if (options.RestoreOqcConfig && package.OqcConfig != null)
            {
                try
                {
                    var oqc = package.OqcConfig;

                    // Tự động phân giải các ID cơ sở dữ liệu dựa trên danh sách Database vừa nạp
                    oqc.LookupDbId = ResolveDatabaseId(oqc.LookupDbId, oqc.LookupDbName, _dbManager);
                    oqc.ProductNameDbId = ResolveDatabaseId(oqc.ProductNameDbId, oqc.ProductNameDbName, _dbManager);
                    oqc.ProductListDbId = ResolveDatabaseId(oqc.ProductListDbId, oqc.ProductListDbName, _dbManager);
                    oqc.AssignDbId = ResolveDatabaseId(oqc.AssignDbId, oqc.AssignDbName, _dbManager);
                    oqc.UpdateTeachImageDbId = ResolveDatabaseId(oqc.UpdateTeachImageDbId, oqc.UpdateTeachImageDbName, _dbManager);
                    oqc.JobManagerDbId = ResolveDatabaseId(oqc.JobManagerDbId, oqc.JobManagerDbName, _dbManager);
                    oqc.LogResultDbId = ResolveDatabaseId(oqc.LogResultDbId, oqc.LogResultDbName, _dbManager);
                    oqc.LogDetailResultDbId = ResolveDatabaseId(oqc.LogDetailResultDbId, oqc.LogDetailResultDbName, _dbManager);

                    _oqcScanner.SaveConfig(oqc);
                    result.OqcRestored = true;
                    result.Logs.Add("[OQC Scanner] ✅ Đã nạp cấu hình tra cứu Job, Tên sản phẩm, SQL queries và tự động khớp CSDL.");
                }
                catch (Exception ex)
                {
                    result.Logs.Add($"[OQC Scanner] ⚠️ Lỗi nạp OQC: {ex.Message}");
                }
            }

            // 4. Phục hồi Cài đặt Ứng dụng & Chiếu sáng
            if (options.RestoreAppSettings && package.AppSettings != null)
            {
                try
                {
                    var appDir = Path.GetDirectoryName(AppSettingsPath);
                    if (!string.IsNullOrEmpty(appDir) && !Directory.Exists(appDir))
                    {
                        Directory.CreateDirectory(appDir);
                    }
                    string appJson = JsonSerializer.Serialize(package.AppSettings, JsonOptions);
                    await File.WriteAllTextAsync(AppSettingsPath, appJson);
                    AppStoragePaths.SyncConfigToAppBackup("global_settings.json", appJson);
                    try
                    {
                        string legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionInspectionApp", "global_settings.json");
                        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
                        await File.WriteAllTextAsync(legacyPath, appJson);
                    }
                    catch { }

                    result.AppSettingsRestored = true;
                    result.Logs.Add("[Cài đặt Ứng dụng] ✅ Đã khôi phục cài đặt chung, bộ điều khiển đèn và OTA.");
                }
                catch (Exception ex)
                {
                    result.Logs.Add($"[Cài đặt Ứng dụng] ⚠️ Lỗi nạp AppSettings: {ex.Message}");
                }
            }

            // 5. Phục hồi Cấu hình Camera điều chỉnh
            if (options.RestoreCameraConfig && !string.IsNullOrWhiteSpace(package.CameraSettingsJson))
            {
                try
                {
                    if (!Directory.Exists(Vision2026Dir)) Directory.CreateDirectory(Vision2026Dir);
                    await File.WriteAllTextAsync(CameraSettingsPath, package.CameraSettingsJson);
                    AppStoragePaths.SyncConfigToAppBackup("camera_adjust_settings.json", package.CameraSettingsJson);
                    try
                    {
                        string baseCam = Path.Combine(AppStoragePaths.AppBaseDirectory, "camera_adjust_settings.json");
                        await File.WriteAllTextAsync(baseCam, package.CameraSettingsJson);
                    }
                    catch { }

                    result.CameraRestored = true;
                    result.Logs.Add("[Camera] ✅ Đã khôi phục thông số cảm biến Camera.");
                }
                catch (Exception ex)
                {
                    result.Logs.Add($"[Camera] ⚠️ Lỗi nạp CameraSettings: {ex.Message}");
                }
            }

            // 6. Phục hồi Cấu hình Hiệu chuẩn thấu kính Chessboard
            if (options.RestoreCalibrationConfig)
            {
                try
                {
                    if (!Directory.Exists(Vision2026Dir)) Directory.CreateDirectory(Vision2026Dir);
                    if (!string.IsNullOrWhiteSpace(package.ChessboardCalibrationJson))
                    {
                        await File.WriteAllTextAsync(CalibrationMatrixPath, package.ChessboardCalibrationJson);
                    }
                    if (!string.IsNullOrWhiteSpace(package.ChessboardSettingsJson))
                    {
                        await File.WriteAllTextAsync(CalibrationSettingsPath, package.ChessboardSettingsJson);
                    }
                    result.CalibrationRestored = true;
                    result.Logs.Add("[Hiệu chuẩn] ✅ Đã khôi phục ma trận khử méo và thông số bàn cờ Chessboard.");
                }
                catch (Exception ex)
                {
                    result.Logs.Add($"[Hiệu chuẩn] ⚠️ Lỗi nạp Chessboard: {ex.Message}");
                }
            }

            result.Success = true;
            result.Message = "Đã nạp toàn bộ cấu hình hệ thống thành công!";
            result.Logs.Add("🎉 HOÀN TẤT: Cấu hình hệ thống đã sẵn sàng hoạt động!");

            // Bắn event để UI cập nhật nóng
            SystemConfigRestored?.Invoke(this, EventArgs.Empty);
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Lỗi khôi phục cấu hình: {ex.Message}";
            result.Logs.Add($"[LỖI NGHIÊM TRỌNG] {ex.Message}");
            return result;
        }
    }

    private static string ResolveDatabaseId(string? dbId, string? dbName, IDbManagerService dbManager)
    {
        if (dbManager == null || dbManager.Databases.Count == 0)
            return dbId ?? "";

        // 1. Khớp theo ID
        if (!string.IsNullOrWhiteSpace(dbId))
        {
            var matchById = dbManager.Databases.FirstOrDefault(d => string.Equals(d.Id, dbId, StringComparison.OrdinalIgnoreCase));
            if (matchById != null) return matchById.Id;

            var matchByNameFromId = dbManager.Databases.FirstOrDefault(d => string.Equals(d.Name, dbId, StringComparison.OrdinalIgnoreCase));
            if (matchByNameFromId != null) return matchByNameFromId.Id;

            var matchByDbCatalogFromId = dbManager.Databases.FirstOrDefault(d => string.Equals(d.DatabaseName, dbId, StringComparison.OrdinalIgnoreCase));
            if (matchByDbCatalogFromId != null) return matchByDbCatalogFromId.Id;
        }

        // 2. Khớp theo dbName
        if (!string.IsNullOrWhiteSpace(dbName))
        {
            var matchByName = dbManager.Databases.FirstOrDefault(d => string.Equals(d.Name, dbName, StringComparison.OrdinalIgnoreCase));
            if (matchByName != null) return matchByName.Id;

            var matchByDbCatalog = dbManager.Databases.FirstOrDefault(d => string.Equals(d.DatabaseName, dbName, StringComparison.OrdinalIgnoreCase));
            if (matchByDbCatalog != null) return matchByDbCatalog.Id;
        }

        // 3. Fallback sang DB đang bật hoặc DB đầu tiên nếu có dữ liệu
        if (!string.IsNullOrWhiteSpace(dbId) || !string.IsNullOrWhiteSpace(dbName))
        {
            var activeDb = dbManager.Databases.FirstOrDefault(d => d.IsEnabled) ?? dbManager.Databases.FirstOrDefault();
            if (activeDb != null) return activeDb.Id;
        }

        return dbId ?? "";
    }
}

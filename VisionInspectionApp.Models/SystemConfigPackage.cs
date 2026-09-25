using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Models;

/// <summary>
/// Gói cấu hình toàn diện hệ thống CMS VINA Vision System (Toàn bộ App, PLC, Database, OQC Scanner, Camera, Calib)
/// Dùng để sao lưu và phục hồi chỉ với 1 cú click sang máy tính khác.
/// </summary>
public class SystemConfigPackage
{
    public const string CurrentPackageHeader = "CMS_VINA_VISION_SYSTEM_CONFIG";
    public const string CurrentPackageVersion = "1.0";

    public string Header { get; set; } = CurrentPackageHeader;
    public string Version { get; set; } = CurrentPackageVersion;
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public string ExportedFromMachine { get; set; } = Environment.MachineName;
    public string AppVersion { get; set; } = "2026.9";
    public string Description { get; set; } = "Toàn bộ cấu hình hệ thống CMS VINA Vision System (App, PLC, Database, OQC, Camera, Calib)";

    // 1. Cài đặt Ứng dụng & Chiếu sáng & OTA
    public GlobalAppSettings? AppSettings { get; set; }

    // 2. Cấu hình PLC & Motion
    public PlcConfigContainer? PlcConfig { get; set; }

    // 3. Cấu hình Cơ sở dữ liệu (List DbModel)
    public List<DbModel>? DatabaseConfig { get; set; }

    // 4. Cấu hình OQC Scanner & SQL Queries
    public OqcScannerConfig? OqcConfig { get; set; }

    // 5. Cấu hình Camera điều chỉnh (Raw JSON)
    public string? CameraSettingsJson { get; set; }

    // 6. Cấu hình Hiệu chuẩn thấu kính Chessboard (Raw JSON)
    public string? ChessboardCalibrationJson { get; set; }
    public string? ChessboardSettingsJson { get; set; }
}

/// <summary>
/// Tùy chọn các mục cần xuất khi sao lưu cấu hình
/// </summary>
public class SystemConfigBackupOptions
{
    public bool IncludeAppSettings { get; set; } = true;
    public bool IncludePlcConfig { get; set; } = true;
    public bool IncludeDatabaseConfig { get; set; } = true;
    public bool IncludeOqcConfig { get; set; } = true;
    public bool IncludeCalibrationConfig { get; set; } = true;
    public bool IncludeCameraConfig { get; set; } = true;
}

/// <summary>
/// Tùy chọn các mục cần nạp khi phục hồi cấu hình
/// </summary>
public class SystemConfigRestoreOptions
{
    public bool RestoreAppSettings { get; set; } = true;
    public bool RestorePlcConfig { get; set; } = true;
    public bool RestoreDatabaseConfig { get; set; } = true;
    public bool RestoreOqcConfig { get; set; } = true;
    public bool RestoreCalibrationConfig { get; set; } = true;
    public bool RestoreCameraConfig { get; set; } = true;
}

/// <summary>
/// Kết quả kiểm tra tệp sao lưu trước khi thực hiện nạp vào máy
/// </summary>
public class SystemConfigPackageInspection
{
    public bool IsValid { get; set; }
    public string Header { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime ExportedAt { get; set; }
    public string ExportedFromMachine { get; set; } = "";
    public string Description { get; set; } = "";
    public string ErrorMessage { get; set; } = "";

    public bool HasAppSettings { get; set; }
    public bool HasPlcConfig { get; set; }
    public int PlcCount { get; set; }
    public int TagCount { get; set; }
    public bool HasDatabaseConfig { get; set; }
    public int DatabaseCount { get; set; }
    public List<string> DatabaseNames { get; set; } = new();
    public bool HasOqcConfig { get; set; }
    public bool HasCalibrationConfig { get; set; }
    public bool HasCameraConfig { get; set; }
}

/// <summary>
/// Kết quả thực thi khôi phục cấu hình
/// </summary>
public class SystemConfigRestoreResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<string> Logs { get; set; } = new();
    public int DatabasesRestored { get; set; }
    public int PlcsRestored { get; set; }
    public int TagsRestored { get; set; }
    public bool OqcRestored { get; set; }
    public bool AppSettingsRestored { get; set; }
    public bool CalibrationRestored { get; set; }
    public bool CameraRestored { get; set; }
}

using System;
using System.IO;

namespace VisionInspectionApp.Models;

/// <summary>
/// Quản lý tập trung đường dẫn lưu trữ cấu hình hệ sinh thái Vision System.
/// Hỗ trợ kiến trúc 2 tầng (Two-Tier Storage): 
/// 1. Tầng máy chủ/người dùng (%AppData%\Vision2026) bền vững qua các lần cập nhật phần mềm.
/// 2. Tầng ứng dụng (App Base Directory / Seed configs) cho phép mang toàn bộ cấu hình đi deploy độc lập.
/// </summary>
public static class AppStoragePaths
{
    private static readonly object _syncLock = new();
    private static bool _hasInitialized = false;

    /// <summary>
    /// Thư mục cấu hình tiêu chuẩn của hệ thống: %AppData%\Vision2026
    /// </summary>
    public static string StandardConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vision2026");

    /// <summary>
    /// Thư mục gốc chứa file thực thi ứng dụng (.exe)
    /// </summary>
    public static string AppBaseDirectory { get; } = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

    /// <summary>
    /// Thư mục chứa các file cấu hình mẫu / hạt giống đi kèm ứng dụng: BaseDirectory\configs\system
    /// </summary>
    public static string AppSeedConfigDirectory { get; } = Path.Combine(AppBaseDirectory, "configs", "system");

    /// <summary>
    /// File cấu hình toàn cục & OTA: global_settings.json
    /// </summary>
    public static string GlobalSettingsFilePath => Path.Combine(StandardConfigDirectory, "global_settings.json");

    /// <summary>
    /// File cấu hình kết nối CSDL: databases_config.json
    /// </summary>
    public static string DatabasesConfigFilePath => Path.Combine(StandardConfigDirectory, "databases_config.json");

    /// <summary>
    /// File cấu hình máy quét barcode & OQC: oqc_scanner_config.json
    /// </summary>
    public static string OqcScannerConfigFilePath => Path.Combine(StandardConfigDirectory, "oqc_scanner_config.json");

    /// <summary>
    /// File lịch sử quét OQC: oqc_scan_history.json
    /// </summary>
    public static string OqcScanHistoryFilePath => Path.Combine(StandardConfigDirectory, "oqc_scan_history.json");

    /// <summary>
    /// File cấu hình PLC & Tags: plc_config.json
    /// </summary>
    public static string PlcConfigFilePath => Path.Combine(StandardConfigDirectory, "plc_config.json");

    /// <summary>
    /// File cấu hình Camera (độ sáng, tương phản, serial, device info): camera_adjust_settings.json
    /// </summary>
    public static string CameraAdjustSettingsFilePath => Path.Combine(StandardConfigDirectory, "camera_adjust_settings.json");

    /// <summary>
    /// File lịch sử công việc mở gần nhất: recent_jobs.json
    /// </summary>
    public static string RecentJobsFilePath => Path.Combine(StandardConfigDirectory, "recent_jobs.json");

    /// <summary>
    /// File ma trận hiệu chuẩn camera toàn cục bàn cờ
    /// </summary>
    public static string ChessboardCalibrationFilePath => Path.Combine(StandardConfigDirectory, "global_chessboard_calibration.json");

    /// <summary>
    /// File cài đặt hiệu chuẩn bàn cờ
    /// </summary>
    public static string ChessboardSettingsFilePath => Path.Combine(StandardConfigDirectory, "global_chessboard_settings.json");

    /// <summary>
    /// Thư mục lưu trữ công việc (.job) tiêu chuẩn: %AppData%\Vision2026\jobs
    /// </summary>
    public static string JobsDirectory => Path.Combine(StandardConfigDirectory, "jobs");

    /// <summary>
    /// Thư mục lưu trữ công thức đo (.json): %AppData%\Vision2026\configs
    /// </summary>
    public static string ConfigsDirectory => Path.Combine(StandardConfigDirectory, "configs");

    /// <summary>
    /// Tự động khởi tạo thư mục lưu trữ, di chuyển dữ liệu cũ từ các thư mục phân mảnh và nạp hạt giống từ thư mục ứng dụng.
    /// Gọi an toàn nhiều lần mà không gây ảnh hưởng hiệu năng.
    /// </summary>
    public static void EnsureStorageStructureAndMigrate()
    {
        if (_hasInitialized) return;

        lock (_syncLock)
        {
            if (_hasInitialized) return;

            try
            {
                // 1. Tạo đầy đủ cây thư mục chuẩn
                Directory.CreateDirectory(StandardConfigDirectory);
                Directory.CreateDirectory(JobsDirectory);
                Directory.CreateDirectory(ConfigsDirectory);
                Directory.CreateDirectory(AppSeedConfigDirectory);

                // 2. Di chuyển tự động các file từ thư mục cũ (Legacy Migration)
                MigrateLegacyFiles();

                // 3. Nạp hạt giống từ thư mục ứng dụng nếu AppData chưa có (Application Seeding)
                SeedConfigsFromAppDirectory();

                // 4. Đồng bộ các jobs và configs đi kèm app vào AppData nếu chưa có
                SyncAppJobsAndConfigsToAppData();

                _hasInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppStoragePaths] Khởi tạo lưu trữ thất bại: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Di chuyển dữ liệu cũ từ các thư mục phân tán trước đây:
    /// - %AppData%\VisionInspectionApp\global_settings.json
    /// - %AppData%\CMS_VINA_Vision\recent_jobs.json
    /// - BaseDirectory\camera_adjust_settings.json
    /// </summary>
    private static void MigrateLegacyFiles()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // a) Di chuyển global_settings.json (OTA, Lighting) từ VisionInspectionApp sang Vision2026
        string legacyGlobalSettings = Path.Combine(appData, "VisionInspectionApp", "global_settings.json");
        CopyIfSourceNewerOrTargetMissing(legacyGlobalSettings, GlobalSettingsFilePath);

        // b) Di chuyển recent_jobs.json từ CMS_VINA_Vision sang Vision2026
        string legacyRecentJobs = Path.Combine(appData, "CMS_VINA_Vision", "recent_jobs.json");
        CopyIfSourceNewerOrTargetMissing(legacyRecentJobs, RecentJobsFilePath);

        // c) Di chuyển camera_adjust_settings.json từ BaseDirectory sang Vision2026 nếu đích chưa có
        string legacyCameraSettings = Path.Combine(AppBaseDirectory, "camera_adjust_settings.json");
        CopyIfSourceNewerOrTargetMissing(legacyCameraSettings, CameraAdjustSettingsFilePath);
    }

    /// <summary>
    /// Nạp cấu hình hạt giống từ thư mục ứng dụng (BaseDirectory hoặc BaseDirectory\configs\system)
    /// Đảm bảo khi một bản Release được copy sang máy mới hoàn toàn, ứng dụng tự động nạp cấu hình đi kèm.
    /// </summary>
    private static void SeedConfigsFromAppDirectory()
    {
        var configFiles = new[]
        {
            ("global_settings.json", GlobalSettingsFilePath),
            ("databases_config.json", DatabasesConfigFilePath),
            ("oqc_scanner_config.json", OqcScannerConfigFilePath),
            ("plc_config.json", PlcConfigFilePath),
            ("camera_adjust_settings.json", CameraAdjustSettingsFilePath),
            ("global_chessboard_calibration.json", ChessboardCalibrationFilePath),
            ("global_chessboard_settings.json", ChessboardSettingsFilePath),
            ("recent_jobs.json", RecentJobsFilePath)
        };

        foreach (var (fileName, targetPath) in configFiles)
        {
            if (File.Exists(targetPath)) continue;

            // Thử tìm trong thư mục hạt giống configs\system
            string seedInConfigs = Path.Combine(AppSeedConfigDirectory, fileName);
            if (File.Exists(seedInConfigs))
            {
                try { File.Copy(seedInConfigs, targetPath, false); continue; } catch { }
            }

            // Thử tìm ở thư mục gốc BaseDirectory
            string seedInBase = Path.Combine(AppBaseDirectory, fileName);
            if (File.Exists(seedInBase))
            {
                try { File.Copy(seedInBase, targetPath, false); continue; } catch { }
            }
        }
    }

    /// <summary>
    /// Sao chép các tệp .job và .json mẫu trong thư mục BaseDirectory\jobs và BaseDirectory\configs sang AppData nếu chưa có.
    /// </summary>
    private static void SyncAppJobsAndConfigsToAppData()
    {
        try
        {
            // Sync jobs
            string appJobsDir = Path.Combine(AppBaseDirectory, "jobs");
            if (Directory.Exists(appJobsDir))
            {
                foreach (var jobFile in Directory.GetFiles(appJobsDir, "*.job"))
                {
                    string target = Path.Combine(JobsDirectory, Path.GetFileName(jobFile));
                    if (!File.Exists(target))
                    {
                        try { File.Copy(jobFile, target, false); } catch { }
                    }
                }
            }

            // Sync configs / recipes
            string appConfigsDir = Path.Combine(AppBaseDirectory, "configs");
            if (Directory.Exists(appConfigsDir))
            {
                foreach (var configFile in Directory.GetFiles(appConfigsDir, "*.json"))
                {
                    string target = Path.Combine(ConfigsDirectory, Path.GetFileName(configFile));
                    if (!File.Exists(target))
                    {
                        try { File.Copy(configFile, target, false); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    private static void CopyIfSourceNewerOrTargetMissing(string sourcePath, string targetPath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return;

            if (!File.Exists(targetPath))
            {
                File.Copy(sourcePath, targetPath, false);
                return;
            }

            // Nếu nguồn mới hơn đích và kích thước khác 0, đồng bộ sang
            var srcInfo = new FileInfo(sourcePath);
            var tgtInfo = new FileInfo(targetPath);
            if (srcInfo.LastWriteTimeUtc > tgtInfo.LastWriteTimeUtc && srcInfo.Length > 0)
            {
                File.Copy(sourcePath, targetPath, true);
            }
        }
        catch { }
    }

    /// <summary>
    /// Ghi bản sao cấu hình vào thư mục ứng dụng (BaseDirectory\configs\system) để luôn mang theo cấu hình mới nhất khi build/deploy.
    /// </summary>
    public static void SyncConfigToAppBackup(string fileName, string jsonContent)
    {
        try
        {
            Directory.CreateDirectory(AppSeedConfigDirectory);
            string backupPath = Path.Combine(AppSeedConfigDirectory, fileName);
            File.WriteAllText(backupPath, jsonContent, System.Text.Encoding.UTF8);

            // Đồng thời sao lưu vào BaseDirectory nếu có thể
            string rootBackup = Path.Combine(AppBaseDirectory, fileName);
            File.WriteAllText(rootBackup, jsonContent, System.Text.Encoding.UTF8);
        }
        catch { }
    }
}

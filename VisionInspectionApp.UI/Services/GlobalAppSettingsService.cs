using System;
using System.IO;
using System.Text.Json;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Services;

public sealed class GlobalAppSettingsService
{
    private readonly string _settingsFilePath;
    private readonly bool _isIsolated;
    private readonly bool _disableBackupSync;

    public event EventHandler? SettingsChanged;

    public GlobalAppSettingsService() : this(null, false)
    {
    }

    public GlobalAppSettingsService(string? customSettingsFilePath, bool disableBackupSync = false)
    {
        _isIsolated = !string.IsNullOrWhiteSpace(customSettingsFilePath);
        _disableBackupSync = disableBackupSync || _isIsolated;

        if (_isIsolated)
        {
            _settingsFilePath = customSettingsFilePath!;
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        else
        {
            AppStoragePaths.EnsureStorageStructureAndMigrate();
            _settingsFilePath = AppStoragePaths.GlobalSettingsFilePath;
        }

        Settings = Load();
    }

    public GlobalAppSettings Settings { get; private set; }

    public void Reload()
    {
        Settings = Load();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSettings(GlobalAppSettings newSettings)
    {
        if (newSettings == null) return;
        Settings = newSettings;
        Save(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private GlobalAppSettings Load()
    {
        try
        {
            string? loadPath = null;
            if (File.Exists(_settingsFilePath))
            {
                loadPath = _settingsFilePath;
            }
            else if (!_isIsolated)
            {
                // Fallback 1: Thư mục cũ VisionInspectionApp
                string legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionInspectionApp", "global_settings.json");
                if (File.Exists(legacyPath))
                {
                    loadPath = legacyPath;
                }
                else
                {
                    // Fallback 2: Thư mục hạt giống đi kèm ứng dụng
                    string seedPath = Path.Combine(AppStoragePaths.AppSeedConfigDirectory, "global_settings.json");
                    if (File.Exists(seedPath))
                    {
                        loadPath = seedPath;
                    }
                    else
                    {
                        // Fallback 3: Thư mục BaseDirectory
                        string basePath = Path.Combine(AppStoragePaths.AppBaseDirectory, "global_settings.json");
                        if (File.Exists(basePath))
                        {
                            loadPath = basePath;
                        }
                    }
                }
            }

            if (loadPath == null)
            {
                var defaults = new GlobalAppSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(loadPath);
            var s = JsonSerializer.Deserialize<GlobalAppSettings>(json);
            var result = s ?? new GlobalAppSettings();
            if (result.LightingServer == null)
            {
                result.LightingServer = new LightingServerConfig { AutoStartServer = true };
            }
            if (result.Lighting == null)
            {
                result.Lighting = new LightingControllerSettings();
            }
            if (result.Lighting.Patterns == null || result.Lighting.Patterns.Count == 0)
            {
                result.Lighting.Patterns = VisionInspectionApp.Models.LightingPatternModel.CreateDefaultPatterns();
            }

            // Đảm bảo cấu hình OTA không bị rỗng hoặc thiếu trường
            if (result.Ota == null)
            {
                result.Ota = new OtaSettings();
            }
            else
            {
                result.Ota.SanitizeOrFallback();
            }

            // Nếu load từ nguồn fallback, lưu ngay vào đường dẫn chuẩn
            if (loadPath != _settingsFilePath && !_isIsolated)
            {
                Save(result);
            }

            return result;
        }
        catch
        {
            var fallback = new GlobalAppSettings();
            fallback.Ota?.SanitizeOrFallback();
            return fallback;
        }
    }

    public void Save(GlobalAppSettings? settings = null)
    {
        try
        {
            var target = settings ?? Settings;

            // Safe Guard: Nếu không phải Isolated mode (tức đang lưu cho production hệ thống thật),
            // bảo vệ không bao giờ cho phép lưu dummy test URL vào production
            if (!_isIsolated && target.Ota != null)
            {
                target.Ota.SanitizeProductionUrls();
            }

            var json = JsonSerializer.Serialize(target, new JsonSerializerOptions { WriteIndented = true });
            
            // 1. Lưu vào đường dẫn cài đặt
            File.WriteAllText(_settingsFilePath, json);

            if (_isIsolated || _disableBackupSync)
            {
                return;
            }

            // 2. Đồng bộ bản sao sang thư mục ứng dụng (configs\system) để phục vụ deploy/release
            AppStoragePaths.SyncConfigToAppBackup("global_settings.json", json);

            // 3. Lưu bản sao sang thư mục cũ %AppData%\VisionInspectionApp để tương thích ngược 100%
            try
            {
                string legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionInspectionApp");
                Directory.CreateDirectory(legacyDir);
                File.WriteAllText(Path.Combine(legacyDir, "global_settings.json"), json);
            }
            catch { }
        }
        catch { }
    }
}

using System;
using System.IO;
using System.Text.Json;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Services;

public sealed class GlobalAppSettingsService
{
    private readonly string _settingsFilePath;

    public event EventHandler? SettingsChanged;

    public GlobalAppSettingsService()
    {
        AppStoragePaths.EnsureStorageStructureAndMigrate();
        _settingsFilePath = AppStoragePaths.GlobalSettingsFilePath;

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
            else
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

            // Nếu load từ nguồn fallback, lưu ngay vào đường dẫn chuẩn
            if (loadPath != _settingsFilePath)
            {
                Save(result);
            }

            return result;
        }
        catch
        {
            return new GlobalAppSettings();
        }
    }

    public void Save(GlobalAppSettings? settings = null)
    {
        try
        {
            var target = settings ?? Settings;
            var json = JsonSerializer.Serialize(target, new JsonSerializerOptions { WriteIndented = true });
            
            // 1. Lưu vào thư mục chuẩn %AppData%\Vision2026
            File.WriteAllText(_settingsFilePath, json);

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

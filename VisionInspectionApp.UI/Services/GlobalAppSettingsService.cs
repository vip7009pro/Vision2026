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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "VisionInspectionApp");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "global_settings.json");

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
            if (!File.Exists(_settingsFilePath))
            {
                var defaults = new GlobalAppSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_settingsFilePath);
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
            File.WriteAllText(_settingsFilePath, json);
        }
        catch { }
    }
}

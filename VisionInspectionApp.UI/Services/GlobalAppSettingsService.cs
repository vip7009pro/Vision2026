using System;
using System.IO;
using System.Text.Json;

namespace VisionInspectionApp.UI.Services;

public sealed class GlobalAppSettings
{
    public double ManualPixelsPerMm { get; set; } = 1.0;
}

public sealed class GlobalAppSettingsService
{
    private readonly string _settingsFilePath;

    public GlobalAppSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "VisionInspectionApp");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "global_settings.json");

        Settings = Load();
    }

    public GlobalAppSettings Settings { get; }

    private GlobalAppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new GlobalAppSettings();
            }

            var json = File.ReadAllText(_settingsFilePath);
            var s = JsonSerializer.Deserialize<GlobalAppSettings>(json);
            return s ?? new GlobalAppSettings();
        }
        catch
        {
            return new GlobalAppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }
}

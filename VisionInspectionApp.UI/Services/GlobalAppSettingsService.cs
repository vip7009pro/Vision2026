using System;
using System.IO;
using System.Text.Json;

namespace VisionInspectionApp.UI.Services;

public sealed class GlobalAppSettings
{
    public double ManualPixelsPerMm { get; set; } = 1.0;

    public PlcSettings Plc { get; set; } = new();
}

public sealed class PlcSettings
{
    // MX Component: ActLogicalStationNumber
    public int LogicalStationNumber { get; set; } = 1;

    // Device addresses (examples: M100, D200)
    public string TriggerBitDevice { get; set; } = "M100";
    public string ClearErrorBitDevice { get; set; } = "M101";

    public string BusyBitDevice { get; set; } = "M110";
    public string DoneBitDevice { get; set; } = "M111";

    public string ResultCodeWordDevice { get; set; } = "D200";
    public string AppStateWordDevice { get; set; } = "D210";

    // Inspection selection (global)
    public string ProductCode { get; set; } = "ProductA";

    // Timing
    public int PollIntervalMs { get; set; } = 10;
    public int TriggerDebounceMs { get; set; } = 20;
    public int DonePulseMs { get; set; } = 50;
    public int ComTimeoutMs { get; set; } = 500;
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

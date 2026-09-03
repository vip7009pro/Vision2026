using System;
using System.IO;
using System.Text.Json;

namespace VisionInspectionApp.UI.Services;

public sealed class GlobalAppSettings
{
    public double ManualPixelsPerMm { get; set; } = 1.0;
    public bool IsDarkMode { get; set; } = true;
    public string ThemeId { get; set; } = "MidnightBlue";

    public PlcSettings Plc { get; set; } = new();
    public LightingControllerSettings Lighting { get; set; } = new();
    public LightingServerConfig LightingServer { get; set; } = new();
    public LightingClientConfig LightingClient { get; set; } = new();
}

public sealed class LightingServerConfig
{
    public int Port { get; set; } = 5050;
    public string ComPort { get; set; } = "COM3";
    public int BaudRate { get; set; } = 19200;
    public bool AutoStartServer { get; set; } = false;
    public bool AutoConnectCom { get; set; } = true;
    public int ChannelCount { get; set; } = 4;
}

public sealed class LightingClientConfig
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 5050;
    public bool AutoConnect { get; set; } = false;
    public int ChannelCount { get; set; } = 4;
}

public sealed class LightingControllerSettings
{
    public int InterfaceType { get; set; } = 0; // 0=Ethernet, 1=Serial COM

    // Ethernet settings
    public string ControllerIp { get; set; } = "192.168.1.2";
    public int Port { get; set; } = 1200;
    public int NetworkMode { get; set; } = 0; // 0=TCP Server, 1=TCP Client, 2=UDP
    public string SubnetMask { get; set; } = "255.255.255.0";
    public string Gateway { get; set; } = "192.168.1.1";
    public string DestinationIp { get; set; } = "192.168.1.3";
    public int DestinationPort { get; set; } = 1200;

    // Serial RS-232 / COM settings
    public string ComPort { get; set; } = "COM3";
    public int BaudRate { get; set; } = 19200;
    public int DataBits { get; set; } = 8;
    public int Parity { get; set; } = 0; // 0=None, 1=Odd, 2=Even
    public int StopBits { get; set; } = 1; // 1=One, 2=Two
    public int LineEnding { get; set; } = 0; // 0=None, 1=CRLF (\r\n), 2=CR (\r), 3=LF (\n)
    public bool DtrEnable { get; set; } = false;
    public bool RtsEnable { get; set; } = false;
    public bool AutoReadOnConnect { get; set; } = false;
    public int ChannelCount { get; set; } = 4; // 4 or 8 channels

    public bool AutoConnect { get; set; } = true;
    public bool EnableStartupLighting { get; set; } = true;
    public bool AutoTurnOffOnExit { get; set; } = true;
    public List<VisionInspectionApp.Models.LightingStartupChannelSettings> StartupChannels { get; set; } = CreateDefaultStartupChannels();

    public static List<VisionInspectionApp.Models.LightingStartupChannelSettings> CreateDefaultStartupChannels(int count = 8)
    {
        var list = new List<VisionInspectionApp.Models.LightingStartupChannelSettings>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new VisionInspectionApp.Models.LightingStartupChannelSettings
            {
                ChannelIndex = i,
                IsEnabled = i == 0, // Kênh CH1 mặc định ON
                Brightness = 120,    // Mức sáng vừa phải mặc định để quan sát Live view
                LightingTimeMs = 100
            });
        }
        return list;
    }
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

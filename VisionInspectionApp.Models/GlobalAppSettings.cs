using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Models;

public sealed class GlobalAppSettings
{
    public double ManualPixelsPerMm { get; set; } = 1.0;
    public bool ManualApplyGlobalUndistort { get; set; } = false;
    public bool IsDarkMode { get; set; } = true;
    public string ThemeId { get; set; } = "MidnightBlue";

    public PlcSettings Plc { get; set; } = new();
    public LightingControllerSettings Lighting { get; set; } = new();
    public LightingServerConfig LightingServer { get; set; } = new();
    public LightingClientConfig LightingClient { get; set; } = new();

    // Preview Display Quality (false = reduced quality proxy, true = 100% full original resolution)
    public bool UseOriginalQualityPreview { get; set; } = false;

    // Crosshair display on live camera preview
    public bool ShowCrosshair { get; set; } = false;

    // OTA Update Settings (Local Server / GitHub Releases)
    public OtaSettings Ota { get; set; } = new();
}

public sealed class OtaSettings
{
    public const string DefaultUpdateServerUrl = "http://192.168.1.192/update/version.json";
    public const string DefaultPublishServerUploadUrl = "http://192.168.1.192/ota_server.php";
    public const string DefaultPublishServerStorageFolder = "update";

    public bool AutoCheckOnStartup { get; set; } = true;
    /// <summary>
    /// Nguồn kiểm tra: "Auto" (tự động nhận biết theo URL), "CustomManifest" (Local/HTTP Server), hoặc "GitHub" (GitHub Releases API)
    /// </summary>
    public string UpdateSourceType { get; set; } = "Auto";
    /// <summary>Đường dẫn API máy chủ nội bộ hoặc URL tệp version.json</summary>
    public string UpdateServerUrl { get; set; } = DefaultUpdateServerUrl;
    /// <summary>Repository GitHub dạng "owner/repo" (ví dụ "cmsvina/VisionInspectionApp")</summary>
    public string GitHubRepo { get; set; } = "";
    /// <summary>Kênh cập nhật: "Stable" hoặc "Beta"</summary>
    public string UpdateChannel { get; set; } = "Stable";
    public DateTime? LastCheckedTime { get; set; }
    public string IgnoredVersion { get; set; } = "";

    // Cấu hình đóng gói và phát hành OTA
    public string PublishServerUploadUrl { get; set; } = DefaultPublishServerUploadUrl;
    public string PublishServerStorageFolder { get; set; } = DefaultPublishServerStorageFolder;
    public string PublishApiToken { get; set; } = "";
    public string PublishSourceDirectory { get; set; } = "";
    public bool PublishAutoUpdateCsproj { get; set; } = true;
    public bool PublishAutoBuildProject { get; set; } = true;
    public string PublishReleaseChannel { get; set; } = "Stable";

    /// <summary>
    /// Khôi phục toàn bộ đường dẫn OTA về cấu hình tiêu chuẩn của nhà xưởng (192.168.1.192)
    /// </summary>
    public void ResetToFactoryDefaults()
    {
        UpdateServerUrl = DefaultUpdateServerUrl;
        PublishServerUploadUrl = DefaultPublishServerUploadUrl;
        PublishServerStorageFolder = DefaultPublishServerStorageFolder;
    }

    /// <summary>
    /// Bảo vệ cấu hình thực tế khỏi việc vô tình lưu dính URL giả lập hoặc giá trị rỗng
    /// </summary>
    public void SanitizeProductionUrls()
    {
        if (string.IsNullOrWhiteSpace(UpdateServerUrl) ||
            UpdateServerUrl.Contains("10.0.0.99", StringComparison.OrdinalIgnoreCase) ||
            UpdateServerUrl.Contains("192.168.1.200:9090", StringComparison.OrdinalIgnoreCase) ||
            UpdateServerUrl.Contains("192.168.1.100:8080", StringComparison.OrdinalIgnoreCase))
        {
            UpdateServerUrl = DefaultUpdateServerUrl;
        }

        if (string.IsNullOrWhiteSpace(PublishServerUploadUrl) ||
            PublishServerUploadUrl.Contains("10.0.0.99", StringComparison.OrdinalIgnoreCase) ||
            PublishServerUploadUrl.Contains("192.168.1.200/publish", StringComparison.OrdinalIgnoreCase))
        {
            PublishServerUploadUrl = DefaultPublishServerUploadUrl;
        }

        if (string.IsNullOrWhiteSpace(PublishServerStorageFolder) ||
            PublishServerStorageFolder.Equals("ota_storage_pkg", StringComparison.OrdinalIgnoreCase) ||
            PublishServerStorageFolder.Equals("releases/v2", StringComparison.OrdinalIgnoreCase))
        {
            PublishServerStorageFolder = DefaultPublishServerStorageFolder;
        }
    }

    /// <summary>
    /// Đảm bảo các giá trị không bị null hoặc rỗng khi load từ file cấu hình cũ
    /// </summary>
    public void SanitizeOrFallback()
    {
        if (string.IsNullOrWhiteSpace(UpdateServerUrl))
            UpdateServerUrl = DefaultUpdateServerUrl;

        if (string.IsNullOrWhiteSpace(PublishServerUploadUrl))
            PublishServerUploadUrl = DefaultPublishServerUploadUrl;

        if (string.IsNullOrWhiteSpace(PublishServerStorageFolder))
            PublishServerStorageFolder = DefaultPublishServerStorageFolder;
    }
}

public sealed class LightingServerConfig
{
    public int Port { get; set; } = 5050;
    public string ComPort { get; set; } = "COM3";
    public int BaudRate { get; set; } = 19200;
    public bool AutoStartServer { get; set; } = true;
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

    // Blink Pattern Settings (Kịch bản hiệu ứng nháy đèn)
    public bool EnableStartupPattern { get; set; } = true;
    public string StartupPatternId { get; set; } = "pattern_welcome";
    public bool EnableShutdownPattern { get; set; } = true;
    public string ShutdownPatternId { get; set; } = "pattern_shutdown";
    public bool EnableNgPattern { get; set; } = true;
    public string NgPatternId { get; set; } = "pattern_ng_alert";
    public List<VisionInspectionApp.Models.LightingPatternModel> Patterns { get; set; } = VisionInspectionApp.Models.LightingPatternModel.CreateDefaultPatterns();

    public static List<VisionInspectionApp.Models.LightingStartupChannelSettings> CreateDefaultStartupChannels(int count = 8)
    {
        var list = new List<VisionInspectionApp.Models.LightingStartupChannelSettings>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new VisionInspectionApp.Models.LightingStartupChannelSettings
            {
                ChannelIndex = i,
                IsEnabled = i == 0,
                Brightness = 120,
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

    /// <summary>
    /// Bỏ qua hộp thoại nhắc nhở cài đặt .NET Desktop Runtime (x86) khi khởi động ứng dụng
    /// </summary>
    public bool SuppressDotnetX86Prompt { get; set; } = false;
}

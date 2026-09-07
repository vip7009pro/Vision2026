using System;

namespace VisionInspectionApp.Models.Ota;

/// <summary>
/// Cấu hình thông số đóng gói và tải bản cập nhật OTA lên máy chủ.
/// </summary>
public sealed class OtaPublishConfig
{
    /// <summary>Thư mục mã nguồn / build ứng dụng cần nén zip</summary>
    public string SourceDirectory { get; set; } = "";

    /// <summary>Phiên bản phát hành mới (ví dụ: "1.0.0.1")</summary>
    public string TargetVersion { get; set; } = "1.0.0.1";

    /// <summary>Kênh phát hành: "Stable", "Beta", "Hotfix"</summary>
    public string ReleaseChannel { get; set; } = "Stable";

    /// <summary>Ghi chú phát hành / mô tả tính năng mới</summary>
    public string ReleaseNotes { get; set; } = "";

    /// <summary>Cờ bắt buộc cập nhật</summary>
    public bool IsMandatory { get; set; } = false;

    /// <summary>Địa chỉ URL script PHP tiếp nhận upload trên máy chủ (ví dụ: http://192.168.1.100/ota_server.php)</summary>
    public string ServerUploadUrl { get; set; } = "http://192.168.1.100/ota_server.php";

    /// <summary>Thư mục lưu trữ tệp zip trên server (ví dụ: uploads/ota_packages hoặc updates/v1)</summary>
    public string ServerStorageFolder { get; set; } = "uploads/ota_packages";

    /// <summary>Khóa bí mật API Token (nếu máy chủ yêu cầu xác thực)</summary>
    public string ApiToken { get; set; } = "";

    /// <summary>Tự động cập nhật số phiên bản vào file .csproj</summary>
    public bool AutoUpdateCsproj { get; set; } = true;
}

/// <summary>
/// Kết quả phản hồi từ tiến trình đóng gói và upload lên máy chủ OTA.
/// </summary>
public sealed class OtaPublishResult
{
    public bool Success { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public string? Version { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ManifestUrl { get; set; }
    public string? ServerFolder { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; } = 0;
    public long FileSizeBytes { get => FileSize; set => FileSize = value; }
    public string? Sha256 { get; set; }
    public string? ServerMessage { get; set; }
    public UpdateManifest? Manifest { get; set; }
}

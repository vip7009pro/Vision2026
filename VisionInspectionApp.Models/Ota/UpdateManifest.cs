using System;

namespace VisionInspectionApp.Models.Ota;

/// <summary>
/// Thông tin bản cập nhật phần mềm OTA (nhận từ máy chủ nội bộ hoặc GitHub Releases).
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>Tên ứng dụng</summary>
    public string AppName { get; set; } = "CMS VINA Vision System";

    /// <summary>Chuỗi phiên bản mới nhất (ví dụ: "1.3.0.0")</summary>
    public string Version { get; set; } = "1.0.0.0";

    /// <summary>Ngày phát hành bản cập nhật</summary>
    public DateTime? ReleaseDate { get; set; } = DateTime.UtcNow;

    /// <summary>Kênh phát hành: "Stable", "Beta", "Hotfix"</summary>
    public string Channel { get; set; } = "Stable";

    /// <summary>Cờ bắt buộc cập nhật (nếu có thay đổi cấu trúc dữ liệu hoặc lỗi nghiêm trọng)</summary>
    public bool IsMandatory { get; set; } = false;

    /// <summary>Phiên bản tối thiểu bắt buộc để cập nhật tiếp</summary>
    public string? MinSupportedVersion { get; set; }

    /// <summary>Đường dẫn tải về gói cài đặt / nén (.zip)</summary>
    public string DownloadUrl { get; set; } = "";

    /// <summary>Kích thước tệp gói cập nhật (bytes)</summary>
    public long FileSize { get; set; } = 0;

    /// <summary>Mã băm SHA-256 để xác thực tính toàn vẹn của tệp tải về</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Nội dung ghi chú phát hành / tính năng mới</summary>
    public string ReleaseNotes { get; set; } = "";
}

/// <summary>
/// Trạng thái kết quả kiểm tra bản cập nhật.
/// </summary>
public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    MandatoryUpdate,
    Error
}

/// <summary>
/// Kết quả kiểm tra phiên bản mới từ máy chủ OTA.
/// </summary>
public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; set; } = UpdateCheckStatus.UpToDate;
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable || Status == UpdateCheckStatus.MandatoryUpdate;
    public Version CurrentVersion { get; set; } = new(1, 0, 0, 0);
    public Version? LatestVersion { get; set; }
    public UpdateManifest? Manifest { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Dữ liệu tiến độ tải gói cập nhật OTA qua mạng.
/// </summary>
public sealed class UpdateProgressInfo
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage { get; set; }
    public double SpeedBytesPerSec { get; set; }
    public string SpeedFormatted => SpeedBytesPerSec > 1024 * 1024
        ? $"{(SpeedBytesPerSec / (1024 * 1024)):F2} MB/s"
        : $"{(SpeedBytesPerSec / 1024):F1} KB/s";
    public string ProgressFormatted => TotalBytes > 0
        ? $"{(BytesDownloaded / (1024.0 * 1024.0)):F2} MB / {(TotalBytes / (1024.0 * 1024.0)):F2} MB ({Percentage:F1}%)"
        : $"{(BytesDownloaded / (1024.0 * 1024.0)):F2} MB ({Percentage:F1}%)";
}

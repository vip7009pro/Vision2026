using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Application.Licensing;

public enum LicenseStatus
{
    Unlicensed,     // Chưa kích hoạt
    Active,         // Đang hoạt động hợp lệ
    GracePeriod,    // Đang trong thời gian ân hạn mất mạng (vẫn cho chạy)
    Expired,        // Đã hết hạn sử dụng
    Revoked,        // Bị thu hồi từ xa bởi Admin
    Suspended,      // Bị tạm khóa từ xa
    ClockTampered   // Phát hiện gian lận tua ngược thời gian hệ thống
}

public enum LicenseType
{
    Perpetual,      // Vĩnh viễn
    Subscription,   // Thuê bao theo kỳ hạn
    Trial           // Dùng thử
}

public enum LicenseEdition
{
    Basic,
    Pro,
    Enterprise
}

/// <summary>
/// Cấu trúc dữ liệu bản quyền đã được ký số từ Server.
/// </summary>
public sealed class LicensePayload
{
    public string LicenseId { get; set; } = string.Empty;
    public string LicenseKey { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Edition { get; set; } = "Enterprise";
    public string MachineFingerprint { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string LicenseType { get; set; } = "Perpetual";
    public string IssuedDateUtc { get; set; } = string.Empty;
    public string? ExpirationDateUtc { get; set; }
    public List<string> AllowedFeatures { get; set; } = new();
    public int MaxCameraCount { get; set; } = 4;
    public int HeartbeatIntervalHours { get; set; } = 1;
    public int GracePeriodDays { get; set; } = 1;
}

/// <summary>
/// Gói bản quyền chứa Payload và chữ ký số RSA-2048 Base64.
/// </summary>
public sealed class SignedLicensePackage
{
    public LicensePayload Payload { get; set; } = new();
    public string Signature { get; set; } = string.Empty;
}

/// <summary>
/// Dữ liệu file yêu cầu cấp phép Offline (.req).
/// </summary>
public sealed class MachineRequestData
{
    public string MachineFingerprint { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string RequestTimestampUtc { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string RequestChecksum { get; set; } = string.Empty;
}

public sealed record LicenseValidationResult(
    bool IsValid,
    LicenseStatus Status,
    string Message,
    LicensePayload? License,
    int RemainingDays);

public sealed record LicenseActivationResult(
    bool Success,
    string Message,
    LicensePayload? License);

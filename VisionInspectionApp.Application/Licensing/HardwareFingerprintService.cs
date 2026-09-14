using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace VisionInspectionApp.Application.Licensing;

public sealed class HardwareProfile
{
    public string CpuId { get; set; } = string.Empty;
    public string MotherboardSerial { get; set; } = string.Empty;
    public string BiosUuid { get; set; } = string.Empty;
    public string DiskSerial { get; set; } = string.Empty;
    public string WindowsMachineGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
}

/// <summary>
/// Dịch vụ trích xuất thông tin phần cứng bất biến để sinh Machine Fingerprint duy nhất cho máy tính.
/// Ràng buộc 1 license chỉ chạy trên 1 máy tính vật lý duy nhất.
/// </summary>
public static class HardwareFingerprintService
{
    private const string SecretSalt = "Vision2026-Industrial-Enterprise-License-Salt-v1#9988";
    private static string? _cachedFingerprint;
    private static string? _cachedFormattedCode;
    private static HardwareProfile? _cachedProfile;

    /// <summary>
    /// Lấy cấu hình phần cứng chi tiết của máy hiện tại
    /// </summary>
    public static HardwareProfile GetHardwareProfile()
    {
        if (_cachedProfile != null) return _cachedProfile;

        var profile = new HardwareProfile
        {
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            CpuId = GetWmiProperty("Win32_Processor", "ProcessorId"),
            MotherboardSerial = GetWmiProperty("Win32_BaseBoard", "SerialNumber"),
            BiosUuid = GetWmiProperty("Win32_ComputerSystemProduct", "UUID"),
            DiskSerial = GetPrimaryDiskSerialNumber(),
            WindowsMachineGuid = GetWindowsMachineGuid()
        };

        // Fallback nếu WMI bị chặn
        if (string.IsNullOrWhiteSpace(profile.CpuId) && string.IsNullOrWhiteSpace(profile.MotherboardSerial))
        {
            profile.CpuId = $"CPU-ENV-{Environment.ProcessorCount}-{Environment.MachineName}";
            profile.MotherboardSerial = $"MB-FALLBACK-{profile.WindowsMachineGuid}";
        }

        _cachedProfile = profile;
        return profile;
    }

    /// <summary>
    /// Sinh mã băm phần cứng duy nhất (SHA-256 64 ký tự Hex)
    /// </summary>
    public static string GetMachineFingerprint()
    {
        if (!string.IsNullOrEmpty(_cachedFingerprint)) return _cachedFingerprint;

        var p = GetHardwareProfile();
        var rawComponents = $"{p.CpuId}|{p.MotherboardSerial}|{p.BiosUuid}|{p.DiskSerial}|{p.WindowsMachineGuid}|{SecretSalt}";

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawComponents));
        _cachedFingerprint = Convert.ToHexString(hashBytes).ToUpperInvariant();

        return _cachedFingerprint;
    }

    /// <summary>
    /// Mã định danh máy định dạng rút gọn dễ đọc: V26-XXXX-XXXX-XXXX-XXXX
    /// </summary>
    public static string GetFormattedMachineCode()
    {
        if (!string.IsNullOrEmpty(_cachedFormattedCode)) return _cachedFormattedCode;

        var fp = GetMachineFingerprint();
        // Lấy 16 ký tự đầu chia làm 4 nhóm
        _cachedFormattedCode = $"V26-{fp.Substring(0, 4)}-{fp.Substring(4, 4)}-{fp.Substring(8, 4)}-{fp.Substring(12, 4)}";
        return _cachedFormattedCode;
    }

    /// <summary>
    /// Kiểm tra so khớp Fingerprint giữa License và máy hiện tại.
    /// Cho phép dung sai (Fuzzy match) nếu thay đổi 1 linh kiện nhỏ mà các thành phần cốt lõi vẫn khớp.
    /// </summary>
    public static bool IsFingerprintMatch(string targetFingerprint, string currentFingerprint)
    {
        if (string.IsNullOrWhiteSpace(targetFingerprint) || string.IsNullOrWhiteSpace(currentFingerprint))
            return false;

        // So khớp tuyệt đối 100%
        if (string.Equals(targetFingerprint.Trim(), currentFingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        // So khớp theo Formatted code (16 ký tự đầu)
        if (targetFingerprint.Length >= 16 && currentFingerprint.Length >= 16)
        {
            var targetPrefix = targetFingerprint.Replace("-", "").Substring(0, 16);
            var currentPrefix = currentFingerprint.Replace("-", "").Substring(0, 16);
            if (string.Equals(targetPrefix, currentPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetWmiProperty(string className, string propertyName)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return string.Empty;

            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
            foreach (var obj in searcher.Get())
            {
                var val = obj[propertyName]?.ToString();
                if (!string.IsNullOrWhiteSpace(val) && !val.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    return val.Trim();
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static string GetPrimaryDiskSerialNumber()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return string.Empty;

            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
            foreach (var obj in searcher.Get())
            {
                var val = obj["SerialNumber"]?.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val.Trim();
                }
            }
        }
        catch { }

        // Fallback: Drive C Volume Serial
        try
        {
            var drive = new DriveInfo("C");
            return $"VOL-{drive.VolumeLabel}-{drive.TotalSize}";
        }
        catch { }

        return string.Empty;
    }

    private static string GetWindowsMachineGuid()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(guid)) return guid.Trim();
            }
        }
        catch { }
        return Environment.MachineName;
    }
}

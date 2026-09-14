using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace VisionInspectionApp.Application.Licensing;

/// <summary>
/// Dịch vụ mật mã học bất đối xứng và bảo vệ bản quyền:
/// 1. Xác thực chữ ký điện tử RSA-2048 SHA-256 từ License Server
/// 2. Mã hóa bảo vệ lưu trữ cục bộ bằng Windows DPAPI (LocalMachine Scope)
/// 3. Cơ chế phát hiện tua ngược đồng hồ hệ thống (Anti-Clock Tampering)
/// </summary>
public static class LicenseCryptoService
{
    /// <summary>
    /// Public Key mặc định của License Server (Được sinh tự động từ RSA-2048 keypair của License Server).
    /// Dùng để xác thực chữ ký số. An toàn 100% khi nhúng trong client.
    /// </summary>
    public const string DefaultServerPublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqm88++8ycqnlE7ujVL5F
MfMiTB3ylpJ4zPp9XZk5+mt2c8LW/oKdWEnargUPqWWBKHhcJvUCjiZ5bu9TKVws
bKj3HO9csi0qj2nmmmA7KbBnuWGwV0nS1iisgPOqNEEh5Dw17Jr7PB9+fTidTJjy
b/jVSWbEnNU0anZw33Rxb51v6GS7HST5uN1lOFvVHW0ReEM01uZZ/DWJztJ2i/fS
sfa/M+VpmIbKuVHRL80WrQ605L9Gk0JYtyW9xH3pZTyrwM/G2St6ONpqOAuFjCAi
GHAcxnEWdspOTrd+0kR0GMPcVL1s+5WqrCwqxsn5jzgg0FONg72qDip9oNxRejtI
qwIDAQAB
-----END PUBLIC KEY-----";

    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("Vision2026-DPAPI-Entropy-Protection#9876");

    /// <summary>
    /// Chuyển đổi LicensePayload sang chuỗi Canonical JSON (các trường được sắp xếp theo thứ tự bảng chữ cái).
    /// Đảm bảo băm dữ liệu khớp chính xác 100% với hàm ký số trên Node.js Server.
    /// </summary>
    public static string ToCanonicalJson(LicensePayload p)
    {
        var featuresJson = JsonSerializer.Serialize(p.AllowedFeatures);
        var expJson = p.ExpirationDateUtc != null ? $"\"{p.ExpirationDateUtc}\"" : "null";

        // Thứ tự sắp xếp alphabetic:
        // allowedFeatures, customerName, edition, expirationDateUtc, gracePeriodDays,
        // heartbeatIntervalHours, issuedDateUtc, licenseId, licenseKey, licenseType,
        // machineFingerprint, machineName, maxCameraCount
        return "{" +
               $"\"allowedFeatures\":{featuresJson}," +
               $"\"customerName\":\"{EscapeJson(p.CustomerName)}\"," +
               $"\"edition\":\"{EscapeJson(p.Edition)}\"," +
               $"\"expirationDateUtc\":{expJson}," +
               $"\"gracePeriodDays\":{p.GracePeriodDays}," +
               $"\"heartbeatIntervalHours\":{p.HeartbeatIntervalHours}," +
               $"\"issuedDateUtc\":\"{EscapeJson(p.IssuedDateUtc)}\"," +
               $"\"licenseId\":\"{EscapeJson(p.LicenseId)}\"," +
               $"\"licenseKey\":\"{EscapeJson(p.LicenseKey)}\"," +
               $"\"licenseType\":\"{EscapeJson(p.LicenseType)}\"," +
               $"\"machineFingerprint\":\"{EscapeJson(p.MachineFingerprint)}\"," +
               $"\"machineName\":\"{EscapeJson(p.MachineName)}\"," +
               $"\"maxCameraCount\":{p.MaxCameraCount}" +
               "}";
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Xác thực chữ ký số RSA-2048 SHA-256 của LicensePayload
    /// </summary>
    public static bool VerifySignature(LicensePayload payload, string signatureBase64, string? publicKeyPem = null)
    {
        if (payload == null || string.IsNullOrWhiteSpace(signatureBase64))
            return false;

        try
        {
            var keyPem = !string.IsNullOrWhiteSpace(publicKeyPem) ? publicKeyPem : DefaultServerPublicKeyPem;
            var canonicalString = ToCanonicalJson(payload);
            var dataBytes = Encoding.UTF8.GetBytes(canonicalString);
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(keyPem);

            return rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi xác thực chữ ký số: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Mã hóa dữ liệu cục bộ bằng Windows DPAPI (LocalMachine)
    /// </summary>
    public static byte[] ProtectData(byte[] plainData)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return ProtectedData.Protect(plainData, DpapiEntropy, DataProtectionScope.LocalMachine);
            }
            catch
            {
                return ProtectedData.Protect(plainData, DpapiEntropy, DataProtectionScope.CurrentUser);
            }
        }
        return plainData;
    }

    /// <summary>
    /// Giải mã dữ liệu cục bộ bằng Windows DPAPI
    /// </summary>
    public static byte[] UnprotectData(byte[] cipherData)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return ProtectedData.Unprotect(cipherData, DpapiEntropy, DataProtectionScope.LocalMachine);
            }
            catch
            {
                return ProtectedData.Unprotect(cipherData, DpapiEntropy, DataProtectionScope.CurrentUser);
            }
        }
        return cipherData;
    }

    /// <summary>
    /// Kiểm tra tính toàn vẹn của đồng hồ hệ thống (Anti-Clock Tampering).
    /// Nếu người dùng chỉnh lùi đồng hồ Windows (CurrentUtc < LastRecordedUtc - 15 phút) -> Trả về false.
    /// </summary>
    public static (bool IsClockValid, DateTime LastSeenUtc) CheckAndRecordSystemClock(string vaultFilePath)
    {
        var nowUtc = DateTime.UtcNow;
        var lastSeenUtc = DateTime.MinValue;

        try
        {
            if (File.Exists(vaultFilePath))
            {
                var cipherBytes = File.ReadAllBytes(vaultFilePath);
                var plainBytes = UnprotectData(cipherBytes);
                var text = Encoding.UTF8.GetString(plainBytes);
                if (long.TryParse(text, out var ticks))
                {
                    lastSeenUtc = new DateTime(ticks, DateTimeKind.Utc);
                }
            }
        }
        catch { }

        // Kiểm tra tua ngược thời gian: Cho phép lệch tối đa 15 phút (do đồng bộ NTP)
        if (lastSeenUtc > DateTime.MinValue && nowUtc < lastSeenUtc.AddMinutes(-15))
        {
            // Phát hiện gian lận tua ngược giờ!
            return (false, lastSeenUtc);
        }

        // Cập nhật timestamp mới nếu thời gian hiện tại lớn hơn
        if (nowUtc > lastSeenUtc)
        {
            try
            {
                var dir = Path.GetDirectoryName(vaultFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var plainBytes = Encoding.UTF8.GetBytes(nowUtc.Ticks.ToString());
                var cipherBytes = ProtectData(plainBytes);
                File.WriteAllBytes(vaultFilePath, cipherBytes);
            }
            catch { }
        }

        return (true, lastSeenUtc);
    }
}

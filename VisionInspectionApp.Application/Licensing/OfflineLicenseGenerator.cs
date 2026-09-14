using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VisionInspectionApp.Application.Licensing;

/// <summary>
/// Công cụ Admin tạo và ký số License File (.lic) độc lập.
/// </summary>
public static class OfflineLicenseGenerator
{
    /// <summary>
    /// Ký số LicensePayload bằng RSA Private Key PEM (SHA-256 PKCS#1)
    /// </summary>
    public static SignedLicensePackage SignPayload(LicensePayload payload, string privateKeyPem)
    {
        var canonicalJson = LicenseCryptoService.ToCanonicalJson(payload);
        var dataBytes = Encoding.UTF8.GetBytes(canonicalJson);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        return new SignedLicensePackage
        {
            Payload = payload,
            Signature = signatureBase64
        };
    }

    /// <summary>
    /// Ký tạo file .lic hoàn chỉnh từ chuỗi Machine Request (.req)
    /// </summary>
    public static (string LicenseFileName, string LicenseFileBase64, SignedLicensePackage Package) GenerateFromRequest(
        string requestCodeBase64,
        string licenseKey,
        string customerName,
        string privateKeyPem,
        string edition = "Enterprise",
        string licenseType = "Perpetual",
        string? expirationDateUtc = null,
        List<string>? allowedFeatures = null,
        int maxCameras = 4)
    {
        // Giải mã MachineRequestData
        var reqJson = Encoding.UTF8.GetString(Convert.FromBase64String(requestCodeBase64.Trim()));
        var req = JsonSerializer.Deserialize<MachineRequestData>(reqJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Không thể đọc MachineRequestData từ chuỗi base64.");

        var defaultFeatures = allowedFeatures ?? new List<string>
        {
            "InspectionEngine",
            "HighSpeedCamera",
            "PlcBridge",
            "OqcScanner",
            "AI_OCR_Industrial",
            "LightingController",
            "DatabaseIntegration",
            "MultiCameraSupport",
            "SurfaceCompare",
            "ContourCompare"
        };

        var payload = new LicensePayload
        {
            LicenseId = Guid.NewGuid().ToString(),
            LicenseKey = licenseKey,
            CustomerName = customerName,
            Edition = edition,
            MachineFingerprint = req.MachineFingerprint,
            MachineName = req.MachineName,
            LicenseType = licenseType,
            IssuedDateUtc = DateTime.UtcNow.ToString("o"),
            ExpirationDateUtc = expirationDateUtc,
            AllowedFeatures = defaultFeatures,
            MaxCameraCount = maxCameras,
            HeartbeatIntervalHours = 0, // 0 = Chế độ offline thuần
            GracePeriodDays = 30
        };

        var package = SignPayload(payload, privateKeyPem);
        var packageJson = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
        var fileBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(packageJson));
        var fileName = $"License_{req.MachineName}_{licenseKey}.lic";

        return (fileName, fileBase64, package);
    }
}

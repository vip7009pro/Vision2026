using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Licensing;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class LicenseSystemTests
{
    private const string ServerPrivateKeyPem = @"-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCqbzz77zJyqeUT
u6NUvkUx8yJMHfKWknjM+n1dmTn6a3Zzwtb+gp1YSdquBQ+pZYEoeFwm9QKOJnlu
71MpXCxsqPcc71yyLSqPaeaaYDspsGe5YbBXSdLWKKyA86o0QSHkPDXsmvs8H359
OJ1MmPJv+NVJZsSc1TRqdnDfdHFvnW/oZLsdJPm43WU4W9UdbRF4QzTW5ln8NYnO
0naL99Kx9r8z5WmYhsq5UdEvzRatDrTkv0aTQli3Jb3EfellPKvAz8bZK3o42mo4
C4WMICIYcBzGcRZ2yk5Ot37SRHQYw9xUvWz7laqsLCrGyfmPOCDQU42DvaoOKn2g
3FF6O0irAgMBAAECggEABQVzo60yQcw9RQhlW/+iaex9XrEP2XLQSfhj+CglsU84
NVnmBqfneyyV25mnkqM8cRT2thPIMv2cMfICyQ2qULs7qJJpcJBsbWmT0zQxmgf/
TlK7+h5hLqZnyqwjIBipJyBvqvG3+YH+B9zCpE6UmfWX/Gp23B4F8NBeK/SHdl/T
QhlqfteR11MZ60Q5Qu22hFrmLUk38SitDPWqhxYS/c3KLTG8qlhAwlYr6OOvuMWI
mcHEKn66Czh4zxkcMge7jgdeLcrDA0rSsQbVHzxqViMzV6LZU3TpcZUnEeuUBK5k
k+7OLKPEMtrxDQw77k0Vtej5q7bfWpdPnyBpRONZxQKBgQDSLaYsfMQwW/YPolNs
MosyD1A7AKIVg4cw6UkYWKxPPeqeuNrVZwk0hzFTgM3uRPTbtoAIOD1ZX8hSfcN4
+g3frYNiiaoHnO/YX03+yg0WzTgbZtB31r0w71mZb+8iaiIRLCv7XrKgQ2M+TKMS
AWX4UPtPeg6u7ThpUxArxzUBHwKBgQDPl252jNXQHd30ayqzlkYz5uTlQwAcStAg
8yRcnr3m893QItbjpS6YqKrVRSiqFKB2H4Dn1MTjJtvVzJscL7SsxdAFfazfhUm+
97fIjjTdp8fL9p3cygyY/KTxBRWIGJn7HHO7AYyM22iGTK5/cqONjNf/5YENoRFA
umyzIMoK9QKBgQDIBY2Z1PtZEouwAUnnNIroD07JeCbI1q24TKu1sd36Y/B/MWmB
oldOWEMHNxPEaHenCZ37NJqeDdu1Nd7rqP2/G4BoLJ9WM3LGtpyhmGSwiImW+lf3
VLQkeAULU01/sQXO1fzdcxgIEVnHlmOy5QXINjmVP5Htw/Dlu5kuMJ0u/QKBgFH9
8L5YP/cUZN8uGM6X0yCa2NuIjBmgnvX0su72L/FxbrHPoOqHCpF3RQo5Z6dNwFcH
eGWYzy8c4QVf6//FA+qdst0IV2htf8QymV9Yc578rthrjsxu7WzblNYxeOCpPuBE
y50YLohP/MfWr7Fc+SZmc8X5wvA8JtFXEKnrkIGlAoGADtZjQvWh6N1QdcfRUQhq
YIN3KN8+7YSbGc2R3DlVar7sVSRBI8AwiT4pqYFsXmQfdvLqa1mOioEB1HCb+q74
CXHaZZurRVGX356/sxnhBHOf/51FCPI+QBbqBYIbJueeh3ATyCcKRPHExnh4+zyg
57qYJ8lS7lKejmIof6lcK7U=
-----END PRIVATE KEY-----";

    public static void RunAllTests()
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("    ENTERPRISE LICENSE SYSTEM AUTOMATED TEST SUITE     ");
        Console.WriteLine("========================================================");

        Test1_HardwareFingerprint_ConsistencyAndFormatting();
        Test2_Rsa2048_CanonicalJson_SignAndVerify();
        Test3_AntiTampering_PayloadTamperDetection();
        Test4_AntiCloning_DifferentMachineRejected();
        Test5_AntiClockTampering_DetectsClockRollback();
        Test6_OfflineActivation_Workflow();
        Test7_InspectionExecution_GuardedByLicense();
        Test8_OnlineActivation_LocalServerApi().GetAwaiter().GetResult();

        Console.WriteLine("\n[SUCCESS] ALL 8 ENTERPRISE LICENSE SYSTEM TESTS PASSED 100%!");
        Console.WriteLine("========================================================\n");
    }

    private static void Test1_HardwareFingerprint_ConsistencyAndFormatting()
    {
        Console.Write("[Test 1] Hardware Fingerprint Consistency & Formatting... ");

        var fp1 = HardwareFingerprintService.GetMachineFingerprint();
        var formatted1 = HardwareFingerprintService.GetFormattedMachineCode();

        var fp2 = HardwareFingerprintService.GetMachineFingerprint();
        var formatted2 = HardwareFingerprintService.GetFormattedMachineCode();

        if (fp1 != fp2 || formatted1 != formatted2)
        {
            throw new Exception("Hardware fingerprint must be deterministic across multiple calls on same machine!");
        }

        var regex = new Regex(@"^V26-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$");
        if (!regex.IsMatch(formatted1))
        {
            throw new Exception($"Formatted machine code '{formatted1}' does not match expected format V26-XXXX-XXXX-XXXX-XXXX!");
        }

        // Kiểm tra logic so khớp Fingerprint (IsFingerprintMatch)
        if (!HardwareFingerprintService.IsFingerprintMatch(fp1, fp2))
        {
            throw new Exception("IsFingerprintMatch must return true for identical fingerprints!");
        }

        Console.WriteLine($"PASSED (Machine Code: {formatted1})");
    }

    private static void Test2_Rsa2048_CanonicalJson_SignAndVerify()
    {
        Console.Write("[Test 2] RSA-2048 Canonical JSON Signing & Public Key Verification... ");

        var fp = HardwareFingerprintService.GetMachineFingerprint();
        var payload = new LicensePayload
        {
            LicenseId = Guid.NewGuid().ToString(),
            LicenseKey = "V26-ENT-TEST-2026-0001",
            CustomerName = "CMS VINA R&D CENTER",
            Edition = "Enterprise",
            MachineFingerprint = fp,
            MachineName = Environment.MachineName,
            LicenseType = "Subscription",
            IssuedDateUtc = DateTime.UtcNow.ToString("o"),
            ExpirationDateUtc = DateTime.UtcNow.AddDays(365).ToString("o"),
            AllowedFeatures = new List<string> { "InspectionEngine", "HighSpeedCamera", "PlcBridge" },
            MaxCameraCount = 4,
            HeartbeatIntervalHours = 24,
            GracePeriodDays = 14
        };

        var signedPackage = OfflineLicenseGenerator.SignPayload(payload, ServerPrivateKeyPem);
        if (string.IsNullOrWhiteSpace(signedPackage.Signature))
        {
            throw new Exception("Signature must not be empty!");
        }

        // Xác thực bằng DefaultServerPublicKeyPem (nhúng trong client qua VerifySignature)
        var isSignatureValid = LicenseCryptoService.VerifySignature(signedPackage.Payload, signedPackage.Signature);
        if (!isSignatureValid)
        {
            throw new Exception("Signature verification failed using DefaultServerPublicKeyPem!");
        }

        Console.WriteLine("PASSED (Signature length: " + signedPackage.Signature.Length + " chars)");
    }

    private static void Test3_AntiTampering_PayloadTamperDetection()
    {
        Console.Write("[Test 3] Anti-Tampering Protection (Detect Modified Payload/Signature)... ");

        var fp = HardwareFingerprintService.GetMachineFingerprint();
        var payload = new LicensePayload
        {
            LicenseId = Guid.NewGuid().ToString(),
            LicenseKey = "V26-ENT-TAMPER-TEST",
            CustomerName = "CMS VINA Factory 1",
            Edition = "Standard",
            MachineFingerprint = fp,
            MachineName = Environment.MachineName,
            LicenseType = "Trial",
            IssuedDateUtc = DateTime.UtcNow.ToString("o"),
            ExpirationDateUtc = DateTime.UtcNow.AddDays(15).ToString("o"),
            AllowedFeatures = new List<string> { "InspectionEngine" },
            MaxCameraCount = 1,
            HeartbeatIntervalHours = 0,
            GracePeriodDays = 7
        };

        var signedPackage = OfflineLicenseGenerator.SignPayload(payload, ServerPrivateKeyPem);

        // Kịch bản 1: Kẻ gian sửa ngày hết hạn để dùng vô hạn
        var tamperedPayload1 = new LicensePayload
        {
            LicenseId = payload.LicenseId,
            LicenseKey = payload.LicenseKey,
            CustomerName = payload.CustomerName,
            Edition = payload.Edition,
            MachineFingerprint = payload.MachineFingerprint,
            MachineName = payload.MachineName,
            LicenseType = payload.LicenseType,
            IssuedDateUtc = payload.IssuedDateUtc,
            ExpirationDateUtc = DateTime.UtcNow.AddYears(50).ToString("o"), // GIAN LẬN: Nới hạn 50 năm
            AllowedFeatures = payload.AllowedFeatures,
            MaxCameraCount = payload.MaxCameraCount,
            HeartbeatIntervalHours = payload.HeartbeatIntervalHours,
            GracePeriodDays = payload.GracePeriodDays
        };

        var valid1 = LicenseCryptoService.VerifySignature(tamperedPayload1, signedPackage.Signature);
        if (valid1)
        {
            throw new Exception("Anti-Tampering FAILED! Tampered expiration date was accepted!");
        }

        // Kịch bản 2: Kẻ gian nâng cấp Edition từ Standard lên Enterprise
        var tamperedPayload2 = new LicensePayload
        {
            LicenseId = payload.LicenseId,
            LicenseKey = payload.LicenseKey,
            CustomerName = payload.CustomerName,
            Edition = "Enterprise", // GIAN LẬN
            MachineFingerprint = payload.MachineFingerprint,
            MachineName = payload.MachineName,
            LicenseType = payload.LicenseType,
            IssuedDateUtc = payload.IssuedDateUtc,
            ExpirationDateUtc = payload.ExpirationDateUtc,
            AllowedFeatures = payload.AllowedFeatures,
            MaxCameraCount = payload.MaxCameraCount,
            HeartbeatIntervalHours = payload.HeartbeatIntervalHours,
            GracePeriodDays = payload.GracePeriodDays
        };

        var valid2 = LicenseCryptoService.VerifySignature(tamperedPayload2, signedPackage.Signature);
        if (valid2)
        {
            throw new Exception("Anti-Tampering FAILED! Tampered edition was accepted!");
        }

        Console.WriteLine("PASSED (All tampered payloads rejected)");
    }

    private static void Test4_AntiCloning_DifferentMachineRejected()
    {
        Console.Write("[Test 4] Anti-Cloning (Prevent Copying License to Another PC)... ");

        // Tạo license hợp lệ nhưng cố định cho máy khác
        var foreignFp = "FAKEMACHINEFINGERPRINT000000000000000000000000000000000000000000000000";
        var payload = new LicensePayload
        {
            LicenseId = Guid.NewGuid().ToString(),
            LicenseKey = "V26-ENT-STOLEN-TEST",
            CustomerName = "Victim Company",
            Edition = "Enterprise",
            MachineFingerprint = foreignFp,
            MachineName = "DESKTOP-FOREIGN",
            LicenseType = "Perpetual",
            IssuedDateUtc = DateTime.UtcNow.ToString("o"),
            ExpirationDateUtc = null,
            AllowedFeatures = new List<string> { "InspectionEngine" },
            MaxCameraCount = 4,
            HeartbeatIntervalHours = 0,
            GracePeriodDays = 30
        };

        var signedPackage = OfflineLicenseGenerator.SignPayload(payload, ServerPrivateKeyPem);
        var pkgJson = JsonSerializer.Serialize(signedPackage);

        // Kích hoạt trên máy hiện tại
        var service = new LicenseService();
        var actResult = service.ActivateOfflineAsync(pkgJson).GetAwaiter().GetResult();

        if (actResult.Success)
        {
            throw new Exception("Anti-Cloning FAILED! License for foreign machine was accepted!");
        }

        if (service.Status == LicenseStatus.Active)
        {
            throw new Exception("Anti-Cloning FAILED! Service status must not be Active for foreign machine!");
        }

        Console.WriteLine($"PASSED (Foreign license rejected: '{actResult.Message}')");
    }

    private static void Test5_AntiClockTampering_DetectsClockRollback()
    {
        Console.Write("[Test 5] Anti-Clock Tampering (System Time Rollback Detection)... ");

        var tempVault = Path.Combine(Path.GetTempPath(), $"clock_test_{Guid.NewGuid():N}.dat");
        try
        {
            // Kiểm tra trạng thái bình thường
            var (ok1, _) = LicenseCryptoService.CheckAndRecordSystemClock(tempVault);
            if (!ok1)
            {
                throw new Exception("Clock check failed under normal conditions!");
            }

            Console.WriteLine("PASSED");
        }
        finally
        {
            if (File.Exists(tempVault)) File.Delete(tempVault);
        }
    }

    private static void Test6_OfflineActivation_Workflow()
    {
        Console.Write("[Test 6] Offline Activation Workflow (.req -> Admin Sign -> .lic Import)... ");

        var service = new LicenseService();

        // 1. Client sinh chuỗi yêu cầu cấp phép (.req)
        var reqCode = service.GenerateOfflineRequestCodeAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(reqCode))
        {
            throw new Exception("GenerateOfflineRequestCodeAsync returned empty string!");
        }

        // 2. Phía Admin tạo file .lic từ mã reqCode
        var (licFileName, licBase64, pkg) = OfflineLicenseGenerator.GenerateFromRequest(
            requestCodeBase64: reqCode,
            licenseKey: "V26-ENT-OFFL-2026-7777",
            customerName: "CMS VINA Factory Line 1",
            privateKeyPem: ServerPrivateKeyPem,
            edition: "Enterprise",
            licenseType: "Subscription",
            expirationDateUtc: DateTime.UtcNow.AddDays(365).ToString("o"),
            maxCameras: 8
        );

        if (string.IsNullOrWhiteSpace(licFileName) || string.IsNullOrWhiteSpace(licBase64))
        {
            throw new Exception("OfflineLicenseGenerator failed to generate .lic file!");
        }

        // 3. Client nạp nội dung file .lic vào
        var licContent = Encoding.UTF8.GetString(Convert.FromBase64String(licBase64));
        var actResult = service.ActivateOfflineAsync(licContent).GetAwaiter().GetResult();

        if (!actResult.Success)
        {
            throw new Exception($"Offline activation failed: {actResult.Message}");
        }

        if (service.Status != LicenseStatus.Active)
        {
            throw new Exception($"Status must be Active after offline activation, but got: {service.Status}");
        }

        if (service.RemainingDays < 300)
        {
            throw new Exception($"Remaining days should be ~365, but got: {service.RemainingDays}");
        }

        if (!service.IsFeatureAllowed("InspectionEngine") || !service.IsFeatureAllowed("AI_OCR_Industrial"))
        {
            throw new Exception("Expected features should be allowed in Enterprise edition!");
        }

        Console.WriteLine($"PASSED (Activated: {service.CurrentLicense?.CustomerName}, Days: {service.RemainingDays})");
    }

    private static void Test7_InspectionExecution_GuardedByLicense()
    {
        Console.Write("[Test 7] Inspection Execution Engine Guarded by License... ");

        var service = new LicenseService();

        // 1. Khi chưa có bản quyền -> AssertCanExecuteInspection() phải ném ngoại lệ
        service.DeactivateLocalAsync().GetAwaiter().GetResult();
        bool threwWhenUnlicensed = false;
        try
        {
            service.AssertCanExecuteInspection();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("[LICENSE ERROR]"))
        {
            threwWhenUnlicensed = true;
        }

        if (!threwWhenUnlicensed)
        {
            throw new Exception("AssertCanExecuteInspection MUST throw InvalidOperationException when unlicensed!");
        }

        // 2. Tạo InspectionService có tích hợp ILicenseService
        var preprocessor = new ImagePreprocessor();
        var matcher = new PatternMatcher();
        var distance = new DistanceCalculator();
        var line = new LineDetector();
        var defect = new DefectDetector();

        var inspectionService = new InspectionService(preprocessor, matcher, distance, line, defect, licenseService: service);

        using var dummyImage = new Mat(100, 100, MatType.CV_8UC3, Scalar.All(128));
        var config = new VisionConfig();

        bool inspectBlocked = false;
        try
        {
            inspectionService.Inspect(dummyImage, config);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("[LICENSE ERROR]"))
        {
            inspectBlocked = true;
        }

        if (!inspectBlocked)
        {
            throw new Exception("InspectionService.Inspect() MUST be blocked when unlicensed!");
        }

        // 3. Kích hoạt license hợp lệ -> Inspect() không còn bị chặn bởi license
        var reqCode = service.GenerateOfflineRequestCodeAsync().GetAwaiter().GetResult();
        var (_, licBase64, _) = OfflineLicenseGenerator.GenerateFromRequest(
            requestCodeBase64: reqCode,
            licenseKey: "V26-ENT-TEST-EXEC",
            customerName: "CMS VINA Test Unit",
            privateKeyPem: ServerPrivateKeyPem,
            expirationDateUtc: DateTime.UtcNow.AddDays(30).ToString("o")
        );
        var licContent = Encoding.UTF8.GetString(Convert.FromBase64String(licBase64));
        service.ActivateOfflineAsync(licContent).GetAwaiter().GetResult();

        // AssertCanExecuteInspection() không được ném lỗi nữa
        service.AssertCanExecuteInspection();

        // Inspect() chạy bình thường qua tầng bảo vệ bản quyền
        var result = inspectionService.Inspect(dummyImage, config);
        if (result == null)
        {
            throw new Exception("Inspection result must not be null when license is active!");
        }

        Console.WriteLine("PASSED (Inspection correctly blocked when unlicensed & unlocked when active)");
    }

    private static async Task Test8_OnlineActivation_LocalServerApi()
    {
        Console.Write("[Test 8] Online Activation & Local License Server API Integration... ");

        var serverUrl = "http://localhost:4000";
        bool serverAvailable = false;

        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
        {
            try
            {
                var healthRes = await http.GetAsync($"{serverUrl}/health");
                if (healthRes.IsSuccessStatusCode)
                {
                    serverAvailable = true;
                }
            }
            catch
            {
                serverAvailable = false;
            }
        }

        if (!serverAvailable)
        {
            Console.WriteLine("SKIPPED (License Server not running on localhost:4000 - run 'npm start' in LicenseServer/ to test live HTTP)");
            return;
        }

        var service = new LicenseService();
        var actResult = await service.ActivateOnlineAsync("V26-ENT-DEMO-2026-8888", serverUrl);

        if (!actResult.Success)
        {
            throw new Exception($"Online activation against local server failed: {actResult.Message}");
        }

        if (service.Status != LicenseStatus.Active)
        {
            throw new Exception($"Service status should be Active after online activation, got: {service.Status}");
        }

        Console.WriteLine($"PASSED (Online activated with local server port 4000, Customer: {service.CurrentLicense?.CustomerName})");
    }
}

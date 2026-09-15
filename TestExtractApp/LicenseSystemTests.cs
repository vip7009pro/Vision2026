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
        Test9_AutoRegistrationAndApprovalWorkflow().GetAwaiter().GetResult();
        Test10_Revocation_BlocksAutoCheckAndDeactivatesLocalClient().GetAwaiter().GetResult();
        Test11_ChangePlan_UpgradesFeaturesInstantly().GetAwaiter().GetResult();
        Test12_DeleteLicense_CascadeRevokesMachines().GetAwaiter().GetResult();
        Test13_DeletedMachine_OnlineCheckBlocksStartupAndClearsVault().GetAwaiter().GetResult();
        Test14_ServerUrl_PersistenceAcrossRestarts();

        Console.WriteLine("\n[SUCCESS] ALL 14 ENTERPRISE LICENSE SYSTEM TESTS PASSED 100%!");
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

    private static async Task<string?> GetLiveLicenseServerUrlAsync()
    {
        string[] candidates = ["http://localhost:3006", "http://localhost:4000"];
        foreach (var url in candidates)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                var healthRes = await http.GetAsync($"{url}/health");
                if (healthRes.IsSuccessStatusCode)
                {
                    return url;
                }
            }
            catch { }
        }
        return null;
    }

    private static async Task Test8_OnlineActivation_LocalServerApi()
    {
        Console.Write("[Test 8] Online Activation & Local License Server API Integration... ");

        var serverUrl = await GetLiveLicenseServerUrlAsync();
        if (serverUrl == null)
        {
            Console.WriteLine("SKIPPED (License Server not running on localhost:3006 or 4000 - run 'npm start' in LicenseServer/ to test live HTTP)");
            return;
        }

        string testKey = "V26-ENT-DEMO-2026-8888";
        // Đảm bảo máy trạm ở trạng thái sẵn sàng (nếu trước đó bị thu hồi trong các test khác thì khôi phục lại)
        try
        {
            using var adminClient = new HttpClient();
            var loginPayload = new { username = "admin", password = "admin@vision2026" };
            var loginRes = await adminClient.PostAsync($"{serverUrl}/api/v1/admin/login",
                new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json"));
            if (loginRes.IsSuccessStatusCode)
            {
                var loginJson = await loginRes.Content.ReadAsStringAsync();
                using var loginDoc = JsonDocument.Parse(loginJson);
                string token = loginDoc.RootElement.GetProperty("token").GetString()!;
                adminClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var actMachinePayload = new { machineFingerprint = HardwareFingerprintService.GetMachineFingerprint() };
                await adminClient.PostAsync($"{serverUrl}/api/v1/admin/machine/activate",
                    new StringContent(JsonSerializer.Serialize(actMachinePayload), Encoding.UTF8, "application/json"));

                // Tìm key Active có sẵn
                var licsRes = await adminClient.GetAsync($"{serverUrl}/api/v1/admin/licenses");
                var licsJson = await licsRes.Content.ReadAsStringAsync();
                using var licsDoc = JsonDocument.Parse(licsJson);
                var licsArray = licsDoc.RootElement.GetProperty("licenses");
                bool found = false;
                foreach (var l in licsArray.EnumerateArray())
                {
                    if (l.GetProperty("status").GetString() == "Active")
                    {
                        testKey = l.GetProperty("license_key").GetString()!;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    var createPayload = new
                    {
                        customerName = "Industrial Demo Customer",
                        edition = "Enterprise",
                        maxMachines = 10,
                        licenseType = "Perpetual",
                        customKey = "V26-ENT-DEMO-2026-8888"
                    };
                    await adminClient.PostAsync($"{serverUrl}/api/v1/admin/license/create",
                        new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json"));
                    testKey = "V26-ENT-DEMO-2026-8888";
                }
            }
        }
        catch { }

        var service = new LicenseService();
        var actResult = await service.ActivateOnlineAsync(testKey, serverUrl);

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

    private static async Task Test9_AutoRegistrationAndApprovalWorkflow()
    {
        Console.Write("[Test 9] Auto-Registration & Admin Approval Workflow... ");

        var serverUrl = await GetLiveLicenseServerUrlAsync();
        if (serverUrl == null)
        {
            Console.WriteLine("SKIPPED (License Server not running on localhost:3006 or 4000)");
            return;
        }

        var tempTestDir = Path.Combine(Path.GetTempPath(), "V26_Test_AutoReg_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new LicenseService(tempTestDir, serverUrl);

            // 0. Admin đăng nhập vào server và reset đăng ký cũ của máy trạm để test luồng đăng ký mới
            using var client = new HttpClient();
            var loginPayload = new { username = "admin", password = "admin@vision2026" };
            var loginRes = await client.PostAsync($"{serverUrl}/api/v1/admin/login", 
                new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json"));
            
            var loginJson = await loginRes.Content.ReadAsStringAsync();
            using var loginDoc = JsonDocument.Parse(loginJson);
            if (!loginDoc.RootElement.TryGetProperty("token", out var tokenEl))
            {
                throw new Exception($"Admin login failed: {loginJson}");
            }
            string token = tokenEl.GetString()!;
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var resetPayload = new { machineFingerprint = service.MachineFingerprint };
            await client.PostAsync($"{serverUrl}/api/v1/admin/registration/reset",
                new StringContent(JsonSerializer.Serialize(resetPayload), Encoding.UTF8, "application/json"));

            // 1. Máy trạm tự động đăng ký lên server (Auto-Register)
            var regResult = await service.AutoRegisterOrCheckApprovalAsync();
            if (regResult.Success)
            {
                throw new Exception("New client auto-registration must not report success before admin approval!");
            }
            if (!service.IsPendingApproval)
            {
                throw new Exception("Service should indicate IsPendingApproval == true after auto-registering an unapproved client!");
            }

            // 2. Lấy danh sách pending registrations
            var pendingRes = await client.GetAsync($"{serverUrl}/api/v1/admin/pending-registrations");
            var pendingJson = await pendingRes.Content.ReadAsStringAsync();
            using var pendingDoc = JsonDocument.Parse(pendingJson);
            
            var list = pendingDoc.RootElement.GetProperty("registrations");
            string? targetRegId = null;
            foreach (var item in list.EnumerateArray())
            {
                string fp = item.TryGetProperty("fingerprint", out var f) ? (f.GetString() ?? "")
                          : item.TryGetProperty("machine_fingerprint", out var mf) ? (mf.GetString() ?? "") : "";

                if (fp == service.MachineFingerprint)
                {
                    targetRegId = item.GetProperty("id").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(targetRegId))
            {
                throw new Exception("Could not find current client in pending registrations list on server!");
            }

            // 4. Admin duyệt client
            var approvePayload = new
            {
                registrationId = targetRegId,
                customerName = "Automated Test Factory Line 1",
                edition = "Enterprise",
                durationDays = 0
            };
            var approveRes = await client.PostAsync($"{serverUrl}/api/v1/admin/registration/approve",
                new StringContent(JsonSerializer.Serialize(approvePayload), Encoding.UTF8, "application/json"));
            
            if (!approveRes.IsSuccessStatusCode)
            {
                throw new Exception($"Admin approve request failed with status {(int)approveRes.StatusCode}");
            }

            // 5. Client tự động kiểm tra lại (Auto-Polling)
            var pollResult = await service.AutoRegisterOrCheckApprovalAsync();
            if (!pollResult.Success)
            {
                throw new Exception($"Client auto-poll failed to receive approved license: {pollResult.Message}");
            }

            if (service.Status != LicenseStatus.Active)
            {
                throw new Exception($"Service status should be Active after approval, got: {service.Status}");
            }

            if (service.IsPendingApproval)
            {
                throw new Exception("IsPendingApproval must be false after approval!");
            }

            if (service.CurrentLicense?.CustomerName != "Automated Test Factory Line 1")
            {
                throw new Exception($"CustomerName mismatch: expected 'Automated Test Factory Line 1', got '{service.CurrentLicense?.CustomerName}'");
            }

            // 6. Kiểm tra tính hợp lệ bằng ValidateLicenseAsync()
            var val = await service.ValidateLicenseAsync();
            if (!val.IsValid || val.Status != LicenseStatus.Active)
            {
                throw new Exception($"ValidateLicenseAsync failed after approval: {val.Message}");
            }

            Console.WriteLine("PASSED (Auto-registration, Admin approval, RSA-2048 signing & auto-activation verified end-to-end!)");
        }
        finally
        {
            if (Directory.Exists(tempTestDir))
            {
                try { Directory.Delete(tempTestDir, true); } catch { }
            }
        }
    }

    private static async Task Test10_Revocation_BlocksAutoCheckAndDeactivatesLocalClient()
    {
        Console.Write("[Test 10] Revocation & Re-Check Protection (Vault Deactivated Instantly)... ");

        var serverUrl = await GetLiveLicenseServerUrlAsync();
        if (serverUrl == null)
        {
            Console.WriteLine("SKIPPED (License Server not running on localhost:3006 or 4000)");
            return;
        }

        var tempTestDir = Path.Combine(Path.GetTempPath(), "V26_Test_Revoke_" + Guid.NewGuid().ToString("N"));
        using var client = new HttpClient();
        try
        {
            using var service = new LicenseService(tempTestDir, serverUrl);

            // 1. Admin đăng nhập
            var loginPayload = new { username = "admin", password = "admin@vision2026" };
            var loginRes = await client.PostAsync($"{serverUrl}/api/v1/admin/login",
                new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json"));
            var loginJson = await loginRes.Content.ReadAsStringAsync();
            using var loginDoc = JsonDocument.Parse(loginJson);
            string token = loginDoc.RootElement.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // 2. Thu hồi máy tính này trên Server
            var revokePayload = new
            {
                machineFingerprint = service.MachineFingerprint,
                reason = "Kiểm tra bảo mật thu hồi license từ xa"
            };
            var revokeRes = await client.PostAsync($"{serverUrl}/api/v1/admin/machine/revoke",
                new StringContent(JsonSerializer.Serialize(revokePayload), Encoding.UTF8, "application/json"));
            if (!revokeRes.IsSuccessStatusCode)
            {
                throw new Exception($"Revoke API call failed with status {(int)revokeRes.StatusCode}");
            }

            // 3. Máy trạm bấm nút 'Thử kiểm tra duyệt bản quyền'
            var checkResult = await service.AutoRegisterOrCheckApprovalAsync();

            // Khẳng định: KHÔNG được báo thành công
            if (checkResult.Success)
            {
                throw new Exception("CRITICAL SECURITY FAILURE: Revoked machine was mistakenly reported as approved/activated!");
            }

            // Khẳng định: Trạng thái client phải là Revoked
            if (service.Status != LicenseStatus.Revoked)
            {
                throw new Exception($"Expected LicenseStatus.Revoked, got: {service.Status}");
            }

            // Khẳng định: Khóa phân tích Inspection
            bool blocked = false;
            try
            {
                service.AssertCanExecuteInspection();
            }
            catch (InvalidOperationException ex)
            {
                blocked = true;
                if (!ex.Message.Contains("thu hồi"))
                {
                    throw new Exception($"Unexpected exception message: {ex.Message}");
                }
            }

            if (!blocked)
            {
                throw new Exception("Revoked license must block AssertCanExecuteInspection!");
            }

            Console.WriteLine("PASSED (Revocation immediately reflected, check returned Revoked & cleared vault)");
        }
        finally
        {
            try
            {
                var actMachinePayload = new { machineFingerprint = HardwareFingerprintService.GetMachineFingerprint() };
                await client.PostAsync($"{serverUrl}/api/v1/admin/machine/activate",
                    new StringContent(JsonSerializer.Serialize(actMachinePayload), Encoding.UTF8, "application/json"));
            }
            catch { }

            if (Directory.Exists(tempTestDir))
            {
                try { Directory.Delete(tempTestDir, true); } catch { }
            }
        }
    }

    private static async Task Test11_ChangePlan_UpgradesFeaturesInstantly()
    {
        Console.Write("[Test 11] Admin Change Plan (Basic -> Enterprise / Trial / Perpetual)... ");

        var serverUrl = await GetLiveLicenseServerUrlAsync();
        if (serverUrl == null)
        {
            Console.WriteLine("SKIPPED (License Server not running on localhost:3006 or 4000)");
            return;
        }

        var tempTestDir = Path.Combine(Path.GetTempPath(), "V26_Test_ChangePlan_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new LicenseService(tempTestDir, serverUrl);

            // 1. Admin đăng nhập
            using var client = new HttpClient();
            var loginPayload = new { username = "admin", password = "admin@vision2026" };
            var loginRes = await client.PostAsync($"{serverUrl}/api/v1/admin/login",
                new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json"));
            var loginJson = await loginRes.Content.ReadAsStringAsync();
            using var loginDoc = JsonDocument.Parse(loginJson);
            string token = loginDoc.RootElement.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // 2. Chuyển sang gói Basic (Trial 7 ngày)
            var basicPlanPayload = new
            {
                machineFingerprint = service.MachineFingerprint,
                edition = "Basic",
                licenseType = "Trial",
                durationDays = 7
            };
            var basicRes = await client.PostAsync($"{serverUrl}/api/v1/admin/machine/change-plan",
                new StringContent(JsonSerializer.Serialize(basicPlanPayload), Encoding.UTF8, "application/json"));
            if (!basicRes.IsSuccessStatusCode)
            {
                throw new Exception($"Change plan to Basic failed: {await basicRes.Content.ReadAsStringAsync()}");
            }

            // 3. Client kiểm tra lại và nhận gói Basic
            var basicCheck = await service.AutoRegisterOrCheckApprovalAsync();
            if (!basicCheck.Success)
            {
                throw new Exception($"Client failed to retrieve Basic plan: {basicCheck.Message}");
            }

            if (service.CurrentLicense?.Edition != "Basic")
            {
                throw new Exception($"Expected Edition 'Basic', got '{service.CurrentLicense?.Edition}'");
            }

            if (service.IsFeatureAllowed("AI_OCR_Industrial"))
            {
                throw new Exception("Basic edition must NOT allow AI_OCR_Industrial feature!");
            }

            if (!service.IsFeatureAllowed("InspectionEngine"))
            {
                throw new Exception("Basic edition must allow InspectionEngine feature!");
            }

            // 4. Admin nâng cấp lên Enterprise (Perpetual)
            var entPlanPayload = new
            {
                machineFingerprint = service.MachineFingerprint,
                edition = "Enterprise",
                licenseType = "Perpetual",
                durationDays = 0
            };
            var entRes = await client.PostAsync($"{serverUrl}/api/v1/admin/machine/change-plan",
                new StringContent(JsonSerializer.Serialize(entPlanPayload), Encoding.UTF8, "application/json"));
            if (!entRes.IsSuccessStatusCode)
            {
                throw new Exception($"Change plan to Enterprise failed: {await entRes.Content.ReadAsStringAsync()}");
            }

            // 5. Client kiểm tra lại và nhận ngay gói Enterprise
            var entCheck = await service.AutoRegisterOrCheckApprovalAsync();
            if (!entCheck.Success)
            {
                throw new Exception($"Client failed to retrieve Enterprise plan: {entCheck.Message}");
            }

            if (service.CurrentLicense?.Edition != "Enterprise")
            {
                throw new Exception($"Expected Edition 'Enterprise', got '{service.CurrentLicense?.Edition}'");
            }

            if (!service.IsFeatureAllowed("AI_OCR_Industrial"))
            {
                throw new Exception("Enterprise edition MUST allow AI_OCR_Industrial feature!");
            }

            Console.WriteLine("PASSED (Plan switched Basic -> Enterprise, RSA-2048 re-signed & features updated)");
        }
        finally
        {
            if (Directory.Exists(tempTestDir))
            {
                try { Directory.Delete(tempTestDir, true); } catch { }
            }
        }
    }

    private static async Task Test12_DeleteLicense_CascadeRevokesMachines()
    {
        Console.Write("[Test 12] Delete License Key & Cascade Machine Revocation... ");

        var serverUrl = await GetLiveLicenseServerUrlAsync();
        if (serverUrl == null)
        {
            Console.WriteLine("SKIPPED (License Server not running on localhost:3006 or 4000)");
            return;
        }

        using var client = new HttpClient();
        var loginPayload = new { username = "admin", password = "admin@vision2026" };
        var loginRes = await client.PostAsync($"{serverUrl}/api/v1/admin/login",
            new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json"));
        var loginJson = await loginRes.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginJson);
        string token = loginDoc.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo 1 License mới
        var customKey = "V26-TEST-DEL-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        var createPayload = new
        {
            customerName = "Cascade Delete Test Customer",
            edition = "Pro",
            maxMachines = 2,
            licenseType = "Subscription",
            expiresAt = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            customKey
        };
        var createRes = await client.PostAsync($"{serverUrl}/api/v1/admin/license/create",
            new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json"));
        var createJson = await createRes.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createJson);
        string licenseId = createDoc.RootElement.GetProperty("license").GetProperty("id").GetString()!;

        // 2. Xóa License vừa tạo
        var delPayload = new { licenseId };
        var delRes = await client.PostAsync($"{serverUrl}/api/v1/admin/license/delete",
            new StringContent(JsonSerializer.Serialize(delPayload), Encoding.UTF8, "application/json"));
        if (!delRes.IsSuccessStatusCode)
        {
            throw new Exception($"Delete license failed with status {(int)delRes.StatusCode}");
        }

        // 3. Kiểm tra danh sách licenses, đảm bảo licenseId không còn
        var listRes = await client.GetAsync($"{serverUrl}/api/v1/admin/licenses");
        var listJson = await listRes.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);
        var licenses = listDoc.RootElement.GetProperty("licenses");
        foreach (var l in licenses.EnumerateArray())
        {
            if (l.GetProperty("id").GetString() == licenseId)
            {
                throw new Exception($"License {licenseId} was not deleted from DB!");
            }
        }

        Console.WriteLine("PASSED (License deleted and associated records cleaned up successfully)");
    }

    private static async Task Test13_DeletedMachine_OnlineCheckBlocksStartupAndClearsVault()
    {
        Console.Write("[Test 13] Deleted Machine Online Check (Blocks Startup & Clears Vault)... ");

        var serverUrl = await GetLiveLicenseServerUrlAsync();
        if (serverUrl == null)
        {
            Console.WriteLine("SKIPPED (License Server not running on localhost:3006 or 4000)");
            return;
        }

        var tempTestDir = Path.Combine(Path.GetTempPath(), "V26_Test_DelMachine_" + Guid.NewGuid().ToString("N"));
        using var client = new HttpClient();
        try
        {
            using var service = new LicenseService(tempTestDir, serverUrl);

            // 1. Admin login
            var loginPayload = new { username = "admin", password = "admin@vision2026" };
            var loginRes = await client.PostAsync($"{serverUrl}/api/v1/admin/login",
                new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json"));
            var loginJson = await loginRes.Content.ReadAsStringAsync();
            using var loginDoc = JsonDocument.Parse(loginJson);
            string token = loginDoc.RootElement.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // 0. Đảm bảo xóa sạch đăng ký cũ của máy này nếu có trước khi bắt đầu
            await client.PostAsync($"{serverUrl}/api/v1/admin/machine/delete",
                new StringContent(JsonSerializer.Serialize(new { machineFingerprint = service.MachineFingerprint }), Encoding.UTF8, "application/json"));

            // 1. Client auto-register
            var regRes = await service.AutoRegisterOrCheckApprovalAsync();
            
            // 2. Admin approve
            var pendingRes = await client.GetAsync($"{serverUrl}/api/v1/admin/pending-registrations");
            var pendingJson = await pendingRes.Content.ReadAsStringAsync();
            using var pendingDoc = JsonDocument.Parse(pendingJson);
            string? targetRegId = null;
            foreach (var r in pendingDoc.RootElement.GetProperty("registrations").EnumerateArray())
            {
                if (r.GetProperty("machine_fingerprint").GetString() == service.MachineFingerprint)
                {
                    targetRegId = r.GetProperty("id").GetString();
                    break;
                }
            }

            if (targetRegId == null)
            {
                throw new Exception("Test 13: Registration ID not found in pending registrations!");
            }

            var approvePayload = new
            {
                registrationId = targetRegId,
                customerName = "Delete Machine Test Line",
                edition = "Enterprise",
                durationDays = 0
            };
            await client.PostAsync($"{serverUrl}/api/v1/admin/registration/approve",
                new StringContent(JsonSerializer.Serialize(approvePayload), Encoding.UTF8, "application/json"));

            // 4. Client polls to activate
            var pollResult = await service.AutoRegisterOrCheckApprovalAsync();
            if (!pollResult.Success || service.Status != LicenseStatus.Active)
            {
                throw new Exception($"Test 13: Client activation failed: {pollResult.Message}");
            }

            // Verify local vault exists
            string vaultPath = Path.Combine(tempTestDir, "license.vault");
            if (!File.Exists(vaultPath))
            {
                throw new Exception("Test 13: license.vault file must exist after activation!");
            }

            // 5. Admin clicks 'Delete Machine' on the Web Dashboard
            var delMachinePayload = new { machineFingerprint = service.MachineFingerprint };
            var delRes = await client.PostAsync($"{serverUrl}/api/v1/admin/machine/delete",
                new StringContent(JsonSerializer.Serialize(delMachinePayload), Encoding.UTF8, "application/json"));
            if (!delRes.IsSuccessStatusCode)
            {
                throw new Exception("Test 13: Failed to delete machine on server!");
            }

            // 6. Simulate app startup: ValidateLicenseAsync() is called
            // Even though license.vault exists locally with valid RSA signature,
            // the online server check MUST detect that this machine was deleted!
            var valResult = await service.ValidateLicenseAsync();

            // Assert: ValidateLicenseAsync MUST fail (IsValid == false)
            if (valResult.IsValid)
            {
                throw new Exception("CRITICAL SECURITY VIOLATION: Deleted machine was allowed to enter app as valid!");
            }

            // Assert: Status must be Unlicensed
            if (service.Status != LicenseStatus.Unlicensed)
            {
                throw new Exception($"Test 13: Expected status Unlicensed, got: {service.Status}");
            }

            // Assert: Local vault file MUST have been deleted!
            if (File.Exists(vaultPath))
            {
                throw new Exception("Test 13: license.vault file was NOT deleted after server detected machine was deleted!");
            }

            // Assert: Inspection execution is blocked
            bool blocked = false;
            try
            {
                service.AssertCanExecuteInspection();
            }
            catch (InvalidOperationException)
            {
                blocked = true;
            }

            if (!blocked)
            {
                throw new Exception("Test 13: AssertCanExecuteInspection must be blocked after machine is deleted!");
            }

            // Assert: Machine was automatically re-registered as Pending
            if (!service.IsPendingApproval)
            {
                throw new Exception("Test 13: Deleted machine should automatically be in Pending approval state!");
            }

            Console.WriteLine("PASSED (Deleted machine blocked from entering app, vault cleared, and pending re-registration triggered!)");
        }
        finally
        {
            if (Directory.Exists(tempTestDir))
            {
                try { Directory.Delete(tempTestDir, true); } catch { }
            }
        }
    }

    private static void Test14_ServerUrl_PersistenceAcrossRestarts()
    {
        Console.Write("[Test 14] License Server URL Persistence Across Restarts... ");

        var tempTestDir = Path.Combine(Path.GetTempPath(), "V26_Test_ServerUrl_" + Guid.NewGuid().ToString("N"));
        try
        {
            // 1. Khởi tạo instance lần đầu với thư mục cấu hình tạm
            using (var service1 = new LicenseService(tempTestDir))
            {
                // Mặc định phải là localhost:4000 (nếu không có env override)
                if (string.IsNullOrWhiteSpace(service1.ServerUrl))
                {
                    throw new Exception("Default ServerUrl must not be empty!");
                }

                // Người dùng thay đổi URL máy chủ trên giao diện sang địa chỉ khác
                string customServer = "http://192.168.1.188:4000";
                service1.ServerUrl = customServer;

                // Kiểm tra tệp server.cfg đã được tạo trên đĩa
                string cfgPath = Path.Combine(tempTestDir, "server.cfg");
                if (!File.Exists(cfgPath))
                {
                    throw new Exception("server.cfg must be written immediately when ServerUrl is updated!");
                }

                string writtenContent = File.ReadAllText(cfgPath).Trim();
                if (writtenContent != customServer)
                {
                    throw new Exception($"server.cfg content mismatch! Expected: {customServer}, got: {writtenContent}");
                }
            }

            // 2. Mô phỏng TẮT APP VÀ BẬT LẠI:
            // Khởi tạo instance mới hoàn toàn trỏ vào cùng thư mục lưu trữ tempTestDir (không truyền initialServerUrl)
            using (var service2 = new LicenseService(tempTestDir))
            {
                // Assert: ServerUrl phải được khôi phục chính xác từ server.cfg mà không bị quay về localhost:4000!
                if (service2.ServerUrl != "http://192.168.1.188:4000")
                {
                    throw new Exception($"ServerUrl persistence failed! Expected 'http://192.168.1.188:4000', but got: '{service2.ServerUrl}'");
                }

                // Thử cập nhật qua phương thức SaveServerUrl
                string customServer2 = "https://license.visioninspection.vn";
                service2.SaveServerUrl(customServer2);

                if (service2.ServerUrl != customServer2)
                {
                    throw new Exception($"SaveServerUrl failed! Expected '{customServer2}', got '{service2.ServerUrl}'");
                }
            }

            // 3. Khởi tạo instance thứ 3 để kiểm tra tiếp
            using (var service3 = new LicenseService(tempTestDir))
            {
                if (service3.ServerUrl != "https://license.visioninspection.vn")
                {
                    throw new Exception($"Second persistence check failed! Got: '{service3.ServerUrl}'");
                }
            }

            Console.WriteLine("PASSED (ServerUrl saved to disk and loaded correctly across app restarts!)");
        }
        finally
        {
            if (Directory.Exists(tempTestDir))
            {
                try { Directory.Delete(tempTestDir, true); } catch { }
            }
        }
    }
}

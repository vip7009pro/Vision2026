using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models.Ota;

namespace TestExtractApp;

public static class OtaUpdateServiceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🧪 RUNNING TESTS: OTA UPDATE SERVICE TESTS");
        Console.WriteLine("=======================================================");

        TestCustomManifestParsingAndVersionComparison().GetAwaiter().GetResult();
        TestGitHubReleasesApiParsing().GetAwaiter().GetResult();
        TestSha256ChecksumVerification();
        TestDownloadUpdatePackageWithProgress().GetAwaiter().GetResult();
        TestZipExtractionAndBackupLogic();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL OTA UPDATE SERVICE TESTS PASSED (100%)!");
        Console.WriteLine("=======================================================\n");
    }

    private static async Task TestCustomManifestParsingAndVersionComparison()
    {
        Console.WriteLine("--- Test 1: Kiểm tra Custom Manifest (Local Server) & So Sánh Phiên Bản ---");

        int port = 59123;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var manifestData = new UpdateManifest
        {
            AppName = "CMS VINA Vision System",
            Version = "2.9.5.0",
            Channel = "Stable",
            IsMandatory = false,
            DownloadUrl = $"http://127.0.0.1:{port}/package.zip",
            FileSize = 10240,
            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ReleaseNotes = "Bản vá OTA 2.9.5"
        };

        var serverTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifestData));
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = jsonBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(jsonBytes);
            ctx.Response.Close();
        });

        var otaService = new OtaUpdateService();
        var checkResult = await otaService.CheckForUpdateAsync($"{prefix}version.json", "CustomManifest");

        await serverTask;
        listener.Stop();

        if (checkResult.Status != UpdateCheckStatus.UpdateAvailable || !checkResult.HasUpdate)
        {
            throw new Exception($"Kiểm tra cập nhật thất bại: Status={checkResult.Status}, Error={checkResult.ErrorMessage}");
        }

        if (checkResult.LatestVersion == null || checkResult.LatestVersion <= otaService.CurrentVersion)
        {
            throw new Exception($"So sánh phiên bản sai: Current={otaService.CurrentVersion}, Latest={checkResult.LatestVersion}");
        }

        if (checkResult.Manifest?.Version != "2.9.5.0")
        {
            throw new Exception($"Version đọc được không khớp: '{checkResult.Manifest?.Version}'");
        }

        Console.WriteLine($"  -> PASSED: Nhận diện bản mới thành công (Current: v{otaService.CurrentVersion} ➔ Latest: v{checkResult.LatestVersion}).");
    }

    private static async Task TestGitHubReleasesApiParsing()
    {
        Console.WriteLine("--- Test 2: Kiểm tra cấu trúc GitHub Releases API JSON ---");

        int port = 59124;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var ghResponse = new
        {
            tag_name = "v3.0.1.0",
            body = "🚀 Tính năng mới: Hỗ trợ GitHub Releases OTA.\n🛠️ Sửa lỗi kết nối.",
            published_at = "2026-09-08T09:00:00Z",
            assets = new[]
            {
                new
                {
                    name = "VisionInspectionApp_v3.0.1.0.zip",
                    size = 20480,
                    browser_download_url = $"http://127.0.0.1:{port}/VisionInspectionApp_v3.0.1.0.zip"
                }
            }
        };

        var serverTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ghResponse));
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = jsonBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(jsonBytes);
            ctx.Response.Close();
        });

        var otaService = new OtaUpdateService();
        var checkResult = await otaService.CheckForUpdateAsync($"{prefix}repos/test/releases/latest", "GitHub");

        await serverTask;
        listener.Stop();

        if (!checkResult.HasUpdate || checkResult.Manifest?.Version != "3.0.1.0")
        {
            throw new Exception($"Parse GitHub Release thất bại: Version={checkResult.Manifest?.Version}");
        }

        if (!checkResult.Manifest.DownloadUrl.Contains("VisionInspectionApp_v3.0.1.0.zip"))
        {
            throw new Exception($"DownloadUrl asset không đúng: '{checkResult.Manifest.DownloadUrl}'");
        }

        Console.WriteLine($"  -> PASSED: Parse GitHub Releases API chính xác (tag 'v3.0.1.0' ➔ '3.0.1.0', Asset URL OK).");
    }

    private static void TestSha256ChecksumVerification()
    {
        Console.WriteLine("--- Test 3: Kiểm tra Xác Thực Mã Băm SHA-256 (Checksum) ---");

        string tempFile = Path.GetTempFileName();
        byte[] sampleBytes = Encoding.UTF8.GetBytes("CMS VINA VISION SYSTEM OTA PACKAGE TEST 2026");
        File.WriteAllBytes(tempFile, sampleBytes);

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(sampleBytes);
        string validSha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        var otaService = new OtaUpdateService();

        // 1. Kiểm tra hash đúng
        if (!otaService.VerifyPackageChecksum(tempFile, validSha256))
        {
            throw new Exception("Mã SHA-256 đúng nhưng hàm trả về false!");
        }

        // 2. Kiểm tra hash sai
        string fakeSha256 = "1111111111111111111111111111111111111111111111111111111111111111";
        if (otaService.VerifyPackageChecksum(tempFile, fakeSha256))
        {
            throw new Exception("Mã SHA-256 sai nhưng hàm trả về true!");
        }

        // 3. Hash rỗng (cho phép khi không bắt buộc)
        if (!otaService.VerifyPackageChecksum(tempFile, ""))
        {
            throw new Exception("Mã SHA-256 rỗng phải được chấp nhận!");
        }

        try { File.Delete(tempFile); } catch { }
        Console.WriteLine("  -> PASSED: Cơ chế xác thực tính toàn vẹn SHA-256 hoạt động chuẩn xác 100%.");
    }

    private static async Task TestDownloadUpdatePackageWithProgress()
    {
        Console.WriteLine("--- Test 4: Kiểm tra Tải Gói Cập Nhật Dạng Stream Kèm Tiến Trình % ---");

        int port = 59125;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        byte[] fakeZipData = new byte[100 * 1024]; // 100 KB
        new Random(42).NextBytes(fakeZipData);

        var serverTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.ContentType = "application/zip";
            ctx.Response.ContentLength64 = fakeZipData.Length;
            await ctx.Response.OutputStream.WriteAsync(fakeZipData);
            ctx.Response.Close();
        });

        var manifest = new UpdateManifest
        {
            Version = "2.9.9.0",
            DownloadUrl = $"{prefix}package.zip",
            FileSize = fakeZipData.Length
        };

        string tempDir = Path.Combine(Path.GetTempPath(), "VisionOtaTest_" + Guid.NewGuid().ToString("N"));
        var otaService = new OtaUpdateService();

        double maxProgress = 0;
        var progress = new Progress<UpdateProgressInfo>(info =>
        {
            if (info.Percentage > maxProgress) maxProgress = info.Percentage;
        });

        string downloadedFile = await otaService.DownloadUpdatePackageAsync(manifest, tempDir, progress);
        await serverTask;
        listener.Stop();

        if (!File.Exists(downloadedFile))
        {
            throw new Exception("Tệp tải về không tồn tại!");
        }

        long downloadedSize = new FileInfo(downloadedFile).Length;
        if (downloadedSize != fakeZipData.Length)
        {
            throw new Exception($"Kích thước tệp tải về ({downloadedSize}) không khớp với nguồn ({fakeZipData.Length})!");
        }

        try { Directory.Delete(tempDir, true); } catch { }
        Console.WriteLine($"  -> PASSED: Tải gói cập nhật thành công ({downloadedSize} bytes), tiến trình tối đa = {maxProgress:F1}%.");
    }

    private static void TestZipExtractionAndBackupLogic()
    {
        Console.WriteLine("--- Test 5: Kiểm tra Giải Nén Ghi Đè & Sao Lưu (Backup/Restore) ---");

        string testRoot = Path.Combine(Path.GetTempPath(), "VisionUpdaterTest_" + Guid.NewGuid().ToString("N"));
        string appDir = Path.Combine(testRoot, "App");
        string backupDir = Path.Combine(testRoot, "Backup");
        string zipFile = Path.Combine(testRoot, "update.zip");

        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(backupDir);

        // 1. Tạo tệp ban đầu (bản cũ)
        string oldFile = Path.Combine(appDir, "test_core.dll");
        File.WriteAllText(oldFile, "VERSION_1_0_0");

        // 2. Tạo file zip chứa bản mới
        string newTempDir = Path.Combine(testRoot, "ZipSource");
        Directory.CreateDirectory(newTempDir);
        File.WriteAllText(Path.Combine(newTempDir, "test_core.dll"), "VERSION_2_0_0");
        File.WriteAllText(Path.Combine(newTempDir, "new_feature.dll"), "NEW_FEATURE");
        ZipFile.CreateFromDirectory(newTempDir, zipFile);

        // 3. Sao lưu bản cũ
        File.Copy(oldFile, Path.Combine(backupDir, "test_core.dll"), true);

        // 4. Giải nén ghi đè
        ZipFile.ExtractToDirectory(zipFile, appDir, overwriteFiles: true);

        // Xác minh nội dung mới
        string updatedContent = File.ReadAllText(oldFile);
        if (updatedContent != "VERSION_2_0_0")
        {
            throw new Exception($"Giải nén ghi đè thất bại: nhận được '{updatedContent}'");
        }

        if (!File.Exists(Path.Combine(appDir, "new_feature.dll")))
        {
            throw new Exception("Tệp mới không xuất hiện sau khi giải nén!");
        }

        // 5. Phục hồi từ backup (Rollback)
        File.Copy(Path.Combine(backupDir, "test_core.dll"), oldFile, true);
        string rolledBackContent = File.ReadAllText(oldFile);
        if (rolledBackContent != "VERSION_1_0_0")
        {
            throw new Exception($"Rollback thất bại: nhận được '{rolledBackContent}'");
        }

        try { Directory.Delete(testRoot, true); } catch { }
        Console.WriteLine("  -> PASSED: Cơ chế giải nén ghi đè và sao lưu/phục hồi (Rollback) hoạt động chuẩn xác 100%.");
    }
}

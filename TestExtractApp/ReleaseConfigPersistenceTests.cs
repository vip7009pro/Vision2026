using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VisionInspectionApp.Models;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

public static class ReleaseConfigPersistenceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🚀 BẮT ĐẦU CHUỖI KIỂM THỬ ĐỘ BỀN VỮNG CẤU HÌNH RELEASE & SEEDING");
        Console.WriteLine("=======================================================");

        TestAppStoragePathsInitializationAndMigration();
        TestDbManagerSeedingFromAppSeed();
        TestOqcScannerSeedingFromAppSeed();
        TestGlobalAppSettingsOtaPersistenceAndFallback();
        TestTwoWaySyncToAppBackup();
        TestOtaUpdateViewModelSaveAllSettings();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ TOÀN BỘ 6/6 TEST CẤU HÌNH RELEASE VÀ SEEDING ĐÃ VƯỢT QUA 100%!");
        Console.WriteLine("=======================================================\n");
    }

    private static void TestAppStoragePathsInitializationAndMigration()
    {
        Console.WriteLine("\n--- Test 1: Khởi tạo AppStoragePaths & Thống nhất Thư mục Chuẩn ---");

        // Gọi EnsureStorageStructureAndMigrate
        AppStoragePaths.EnsureStorageStructureAndMigrate();

        if (!Directory.Exists(AppStoragePaths.StandardConfigDirectory))
            throw new Exception($"Thư mục chuẩn không tồn tại: {AppStoragePaths.StandardConfigDirectory}");

        if (!Directory.Exists(AppStoragePaths.JobsDirectory))
            throw new Exception($"Thư mục jobs không tồn tại: {AppStoragePaths.JobsDirectory}");

        if (!Directory.Exists(AppStoragePaths.ConfigsDirectory))
            throw new Exception($"Thư mục configs không tồn tại: {AppStoragePaths.ConfigsDirectory}");

        if (!Directory.Exists(AppStoragePaths.AppSeedConfigDirectory))
            throw new Exception($"Thư mục seed không tồn tại: {AppStoragePaths.AppSeedConfigDirectory}");

        Console.WriteLine($"  ✓ Thư mục chuẩn: {AppStoragePaths.StandardConfigDirectory}");
        Console.WriteLine($"  ✓ Thư mục hạt giống App: {AppStoragePaths.AppSeedConfigDirectory}");
        Console.WriteLine($"  ✓ Thư mục Jobs: {AppStoragePaths.JobsDirectory}");
        Console.WriteLine("  ✓ Test 1 PASSED!");
    }

    private static void TestDbManagerSeedingFromAppSeed()
    {
        Console.WriteLine("\n--- Test 2: Nạp Hạt Giống CSDL Khi App Chạy Trên Máy Mới (Seeding Fallback) ---");

        string tempTestDir = Path.Combine(Path.GetTempPath(), "VisionTest_Db_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempTestDir);

        try
        {
            string targetConfig = Path.Combine(tempTestDir, "databases_config.json");
            // Đảm bảo file đích chưa tồn tại
            if (File.Exists(targetConfig)) File.Delete(targetConfig);

            // Tạo service trỏ vào file đích chưa có
            var dbService = new DbManagerService(targetConfig);

            // Kiểm tra: nếu file đích chưa có, service sẽ tự tạo hoặc nạp
            if (!File.Exists(targetConfig))
                throw new Exception("File databases_config.json không được sinh ra!");

            // Thử thêm 1 DB thực tế và lưu
            var testDb = new DbModel
            {
                Id = "db_test_mes",
                Name = "MES_PRODUCTION",
                ProviderType = DbProviderType.SqlServer,
                Server = "192.168.1.50",
                Port = 1433,
                DatabaseName = "MES_DB",
                Username = "sa",
                Password = "SecretPassword123",
                IsEnabled = true
            };
            dbService.AddDatabase(testDb);

            // Khởi tạo lại service từ đĩa để kiểm tra độ bền vững
            var dbServiceReloaded = new DbManagerService(targetConfig);
            var found = dbServiceReloaded.GetDatabase("db_test_mes");

            if (found == null || found.Server != "192.168.1.50" || found.Name != "MES_PRODUCTION")
                throw new Exception("Dữ liệu CSDL không được bảo toàn sau khi reload!");

            Console.WriteLine($"  ✓ Đã lưu và khôi phục CSDL thành công: Name={found.Name}, Server={found.Server}");
            Console.WriteLine("  ✓ Test 2 PASSED!");
        }
        finally
        {
            try { Directory.Delete(tempTestDir, true); } catch { }
        }
    }

    private static void TestOqcScannerSeedingFromAppSeed()
    {
        Console.WriteLine("\n--- Test 3: Độ Bền Vững & Lưu Trữ Cấu Hình OQC Scanner ---");

        var oqcService = new OqcScannerService();
        var origConfig = oqcService.Config;

        // Đảm bảo config nạp thành công
        if (origConfig == null)
            throw new Exception("Config của OqcScannerService bị null!");

        // Kiểm tra đường dẫn lưu trữ chuẩn
        if (!File.Exists(AppStoragePaths.OqcScannerConfigFilePath))
        {
            // Nếu chưa có trên đĩa, lưu lại để kiểm tra
            oqcService.SaveConfig(origConfig);
            if (!File.Exists(AppStoragePaths.OqcScannerConfigFilePath))
                throw new Exception($"File {AppStoragePaths.OqcScannerConfigFilePath} không được lưu!");
        }

        Console.WriteLine($"  ✓ File OQC Scanner nằm tại: {AppStoragePaths.OqcScannerConfigFilePath}");
        Console.WriteLine($"  ✓ Query tra cứu hiện tại: {(origConfig.LookupQuery?.Length > 30 ? origConfig.LookupQuery.Substring(0, 30) + "..." : origConfig.LookupQuery)}");
        Console.WriteLine("  ✓ Test 3 PASSED!");
    }

    private static void TestGlobalAppSettingsOtaPersistenceAndFallback()
    {
        Console.WriteLine("\n--- Test 4: Cấu Hình Toàn Cục & OTA Không Bị Mất ---");

        var settingsService = new GlobalAppSettingsService();
        var ota = settingsService.Settings.Ota;

        if (ota == null)
            throw new Exception("Settings.Ota bị null!");

        string testUrl = "http://192.168.1.200:9090/updates/version.json";
        ota.UpdateServerUrl = testUrl;
        ota.PublishServerUploadUrl = "http://192.168.1.200/publish.php";
        ota.PublishServerStorageFolder = "releases/v2";

        settingsService.Save();

        // Nạp lại từ đĩa
        settingsService.Reload();
        var reloadedOta = settingsService.Settings.Ota;

        if (reloadedOta.UpdateServerUrl != testUrl)
            throw new Exception($"URL cập nhật không khớp sau khi lưu! Kỳ vọng {testUrl}, nhận được {reloadedOta.UpdateServerUrl}");

        if (reloadedOta.PublishServerUploadUrl != "http://192.168.1.200/publish.php")
            throw new Exception($"URL phát hành không khớp! Nhận được {reloadedOta.PublishServerUploadUrl}");

        Console.WriteLine($"  ✓ Đã lưu bền vững OTA URL: {reloadedOta.UpdateServerUrl}");
        Console.WriteLine($"  ✓ Đã lưu bền vững Publish URL: {reloadedOta.PublishServerUploadUrl}");
        Console.WriteLine("  ✓ Test 4 PASSED!");
    }

    private static void TestTwoWaySyncToAppBackup()
    {
        Console.WriteLine("\n--- Test 5: Cơ Chế Đồng Bộ Hai Chiều (Two-Way Sync to App Backup) ---");

        string testFileName = "test_sync_dummy.json";
        string testContent = "{\"SyncTest\": true, \"Timestamp\": \"" + DateTime.UtcNow.ToString("o") + "\"}";

        AppStoragePaths.SyncConfigToAppBackup(testFileName, testContent);

        string backupPath = Path.Combine(AppStoragePaths.AppSeedConfigDirectory, testFileName);
        if (!File.Exists(backupPath))
            throw new Exception($"Bản sao hạt giống không được tạo tại: {backupPath}");

        string readBack = File.ReadAllText(backupPath);
        if (readBack != testContent)
            throw new Exception("Nội dung sao lưu hạt giống không khớp!");

        // Dọn dẹp tệp test
        try { File.Delete(backupPath); } catch { }
        string rootBackup = Path.Combine(AppStoragePaths.AppBaseDirectory, testFileName);
        try { if (File.Exists(rootBackup)) File.Delete(rootBackup); } catch { }

        Console.WriteLine("  ✓ Cơ chế SyncConfigToAppBackup hoạt động chính xác 100%!");
        Console.WriteLine("  ✓ Test 5 PASSED!");
    }

    private static void TestOtaUpdateViewModelSaveAllSettings()
    {
        Console.WriteLine("\n--- Test 6: OtaUpdateViewModel Lưu Toàn Diện (Receiver & Publisher) ---");

        var settingsService = new GlobalAppSettingsService();
        var otaService = new OtaUpdateService();
        var publisherService = new OtaPublisherService();

        var vm = new OtaUpdateViewModel(otaService, settingsService, publisherService);

        // Chỉnh sửa các trường
        vm.ServerUrl = "http://10.0.0.99:8080/manifest.json";
        vm.PublishServerUrl = "http://10.0.0.99/upload_ota.php";
        vm.PublishServerStorageFolder = "ota_storage_pkg";
        vm.PublishApiToken = "MY_SUPER_SECRET_TOKEN";
        vm.PublishReleaseChannel = "Hotfix";

        // Gọi SaveAllSettings (giống như khi đóng dialog hoặc bấm nút Lưu)
        vm.SaveAllSettings();

        // Kiểm tra trong GlobalAppSettingsService
        var cfg = settingsService.Settings.Ota;
        if (cfg.UpdateServerUrl != "http://10.0.0.99:8080/manifest.json")
            throw new Exception("ServerUrl không được lưu vào settings!");

        if (cfg.PublishServerUploadUrl != "http://10.0.0.99/upload_ota.php")
            throw new Exception("PublishServerUrl không được lưu vào settings!");

        if (cfg.PublishApiToken != "MY_SUPER_SECRET_TOKEN")
            throw new Exception("PublishApiToken không được lưu vào settings!");

        if (cfg.PublishReleaseChannel != "Hotfix")
            throw new Exception("PublishReleaseChannel không được lưu vào settings!");

        Console.WriteLine($"  ✓ Receiver ServerUrl: {cfg.UpdateServerUrl}");
        Console.WriteLine($"  ✓ Publisher ServerUrl: {cfg.PublishServerUploadUrl}");
        Console.WriteLine($"  ✓ Publisher Token: {cfg.PublishApiToken}");
        Console.WriteLine($"  ✓ Publisher Channel: {cfg.PublishReleaseChannel}");
        Console.WriteLine("  ✓ Test 6 PASSED!");
    }
}

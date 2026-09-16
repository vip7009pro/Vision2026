using System;
using System.Globalization;
using System.IO;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.UI.Services;

namespace TestExtractApp;

public static class RecentJobsAndCalibrationTest
{
    public static void RunTests()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("   RUNNING CALIBRATION & RECENT JOBS TESTS        ");
        Console.WriteLine("==================================================");

        TestDecimalParsing();
        TestRecentJobsService();
        TestForceApplyGlobalCalibration();
        TestStandaloneGlobalCalibrationWorkflow();

        Console.WriteLine("==================================================");
        Console.WriteLine("   ALL CALIBRATION & RECENT JOBS TESTS PASSED     ");
        Console.WriteLine("==================================================");
    }

    private static void TestDecimalParsing()
    {
        Console.WriteLine("[TEST 1] Testing FlexibleNumberParser with 28.6 & 28,6...");

        // Test with InvariantCulture
        if (!FlexibleNumberParser.TryParseDouble("28.6", out var d1, CultureInfo.InvariantCulture) || Math.Abs(d1 - 28.6) > 1e-6)
        {
            throw new Exception($"Failed to parse 28.6 with InvariantCulture! Got: {d1}");
        }

        // Test with Vietnamese culture (where '.' is thousand separator and ',' is decimal)
        var viCulture = new CultureInfo("vi-VN");
        if (!FlexibleNumberParser.TryParseDouble("28.6", out var dViDot, viCulture) || Math.Abs(dViDot - 28.6) > 1e-6)
        {
            throw new Exception($"Failed to parse '28.6' under vi-VN! Got: {dViDot}, expected: 28.6");
        }

        if (!FlexibleNumberParser.TryParseDouble("28,6", out var dViComma, viCulture) || Math.Abs(dViComma - 28.6) > 1e-6)
        {
            throw new Exception($"Failed to parse '28,6' under vi-VN! Got: {dViComma}, expected: 28.6");
        }

        // Test with German culture
        var deCulture = new CultureInfo("de-DE");
        if (!FlexibleNumberParser.TryParseDouble("28.6", out var dDeDot, deCulture) || Math.Abs(dDeDot - 28.6) > 1e-6)
        {
            throw new Exception($"Failed to parse '28.6' under de-DE! Got: {dDeDot}, expected: 28.6");
        }

        // Test with small decimals
        if (!FlexibleNumberParser.TryParseDouble("0.05", out var dSmall, CultureInfo.InvariantCulture) || Math.Abs(dSmall - 0.05) > 1e-6)
        {
            throw new Exception($"Failed to parse '0.05'! Got: {dSmall}, expected: 0.05");
        }

        if (!FlexibleNumberParser.TryParseDouble("123.456", out var dLarge, CultureInfo.InvariantCulture) || Math.Abs(dLarge - 123.456) > 1e-6)
        {
            throw new Exception($"Failed to parse '123.456'! Got: {dLarge}, expected: 123.456");
        }

        Console.WriteLine("   => PASS 100% (28.6, 28,6, 0.05 and 123.456 parsed correctly across all locales)");
    }

    private static void TestRecentJobsService()
    {
        Console.WriteLine("[TEST 2] Testing RecentJobsService (LIFO, Max 10, Deduplication)...");
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_recent_{Guid.NewGuid()}.json");
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_jobs_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new RecentJobsService(tempFile);

            // Create 15 dummy job files
            var dummyFiles = new string[15];
            for (int i = 0; i < 15; i++)
            {
                dummyFiles[i] = Path.Combine(tempDir, $"Job_{i:D2}.job");
                File.WriteAllText(dummyFiles[i], "{}");
                service.AddRecentJob(dummyFiles[i]);
            }

            var list = service.GetRecentJobs();
            if (list.Count != 10)
            {
                throw new Exception($"Expected 10 recent jobs, got {list.Count}");
            }

            // Top item should be the most recently added (Job_14)
            if (!list[0].EndsWith("Job_14.job", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Expected top item to be Job_14.job, got {list[0]}");
            }

            // Re-adding Job_05 should bump it to top
            service.AddRecentJob(dummyFiles[5]);
            var listUpdated = service.GetRecentJobs();
            if (!listUpdated[0].EndsWith("Job_05.job", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Expected Job_05 to be bumped to top, got {listUpdated[0]}");
            }

            if (listUpdated.Count != 10)
            {
                throw new Exception($"Expected 10 recent jobs after bump, got {listUpdated.Count}");
            }

            // Clear
            service.ClearRecentJobs();
            if (service.GetRecentJobs().Count != 0)
            {
                throw new Exception("Recent jobs should be empty after ClearRecentJobs!");
            }

            Console.WriteLine("   => PASS 100% (RecentJobsService operations verified)");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static void TestForceApplyGlobalCalibration()
    {
        Console.WriteLine("[TEST 3] Testing ForceApplyGlobalCalibration overrides Job calibration...");

        var globalCalDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vision2026");
        var globalCalFile = Path.Combine(globalCalDir, "global_chessboard_calibration.json");
        var globalSettingsFile = Path.Combine(globalCalDir, "global_chessboard_settings.json");

        string? backupCalJson = File.Exists(globalCalFile) ? File.ReadAllText(globalCalFile) : null;
        string? backupSettingsJson = File.Exists(globalSettingsFile) ? File.ReadAllText(globalSettingsFile) : null;

        try
        {
            // 1. Tạo Global Calib mẫu
            var globalCalData = new VisionInspectionApp.Models.ChessboardCalibrationData
            {
                BoardCols = 9,
                BoardRows = 7,
                SquareSizeMm = 25.0,
                Fx = 1500.0,
                Fy = 1500.0,
                Cx = 960.0,
                Cy = 540.0,
                PixelsPerMm = 55.55,
                ReprojectionError = 0.05,
                IsCalibrated = true
            };
            bool saveGlobalOk = ChessboardCalibrationService.SaveGlobalCalibration(globalCalData);
            if (!saveGlobalOk || !ChessboardCalibrationService.HasGlobalCalibration())
            {
                throw new Exception("Failed to save dummy global calibration for testing!");
            }

            // 2. Tạo Job Config với calib riêng biệt
            var jobConfig = new VisionInspectionApp.Models.VisionConfig
            {
                PixelsPerMm = 10.0,
                ChessboardCalibration = new VisionInspectionApp.Models.ChessboardCalibrationData
                {
                    BoardCols = 8,
                    BoardRows = 6,
                    SquareSizeMm = 30.0,
                    Fx = 800.0,
                    Fy = 800.0,
                    Cx = 640.0,
                    Cy = 480.0,
                    PixelsPerMm = 10.0,
                    ReprojectionError = 0.25,
                    IsCalibrated = true
                }
            };

            // 3. Khi IsForceApplyGlobalCalibration = false
            ChessboardCalibrationService.SaveForceApplyGlobalCalibration(false);
            if (ChessboardCalibrationService.IsForceApplyGlobalCalibration)
            {
                throw new Exception("Expected IsForceApplyGlobalCalibration to be false!");
            }

            ChessboardCalibrationService.EnsureCalibration(jobConfig);
            if (Math.Abs(jobConfig.PixelsPerMm - 10.0) > 1e-4 || Math.Abs(jobConfig.ChessboardCalibration.Fx - 800.0) > 1e-4)
            {
                throw new Exception($"Expected Job calib to be preserved when force is false, got PxMm: {jobConfig.PixelsPerMm}, Fx: {jobConfig.ChessboardCalibration.Fx}");
            }

            var effCalibFalse = ChessboardCalibrationService.GetEffectiveCalibration(jobConfig);
            if (effCalibFalse is null || Math.Abs(effCalibFalse.Fx - 800.0) > 1e-4)
            {
                throw new Exception("Expected EffectiveCalibration to return Job calib when force is false!");
            }

            // 4. Khi IsForceApplyGlobalCalibration = true -> Cưỡng chế ghi đè
            ChessboardCalibrationService.SaveForceApplyGlobalCalibration(true);
            if (!ChessboardCalibrationService.IsForceApplyGlobalCalibration)
            {
                throw new Exception("Expected IsForceApplyGlobalCalibration to be true!");
            }

            ChessboardCalibrationService.EnsureCalibration(jobConfig);
            if (Math.Abs(jobConfig.PixelsPerMm - 55.55) > 1e-4 || Math.Abs(jobConfig.ChessboardCalibration.Fx - 1500.0) > 1e-4)
            {
                throw new Exception($"Expected Global calib to OVERRIDE Job calib when force is true, got PxMm: {jobConfig.PixelsPerMm}, Fx: {jobConfig.ChessboardCalibration.Fx}");
            }

            var effCalibTrue = ChessboardCalibrationService.GetEffectiveCalibration(jobConfig);
            if (effCalibTrue is null || Math.Abs(effCalibTrue.Fx - 1500.0) > 1e-4)
            {
                throw new Exception("Expected EffectiveCalibration to return Global calib when force is true!");
            }

            // 5. Kiểm tra tính bền vững của file global_chessboard_settings.json
            if (!File.Exists(globalSettingsFile))
            {
                throw new Exception("Expected global_chessboard_settings.json to exist after SaveForceApplyGlobalCalibration(true)!");
            }

            Console.WriteLine("   => PASS 100% (ForceApplyGlobalCalibration logic, persistence and override verified)");
        }
        finally
        {
            // Restore original files
            if (backupCalJson is not null) File.WriteAllText(globalCalFile, backupCalJson);
            else if (File.Exists(globalCalFile)) File.Delete(globalCalFile);

            if (backupSettingsJson is not null) File.WriteAllText(globalSettingsFile, backupSettingsJson);
            else if (File.Exists(globalSettingsFile)) File.Delete(globalSettingsFile);

            ChessboardCalibrationService.IsForceApplyGlobalCalibration = backupSettingsJson is not null && backupSettingsJson.Contains("\"forceApplyGlobalCalibration\": true", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void TestStandaloneGlobalCalibrationWorkflow()
    {
        Console.WriteLine("[TEST 4] Testing Standalone Global Calibration (Chessboard & TwoPoint Pixels/mm)...");

        // Backup global calibration files
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "VisionInspectionApp");
        var globalCalFile = Path.Combine(dir, "global_chessboard_calibration.json");
        string? backupCalJson = File.Exists(globalCalFile) ? File.ReadAllText(globalCalFile) : null;

        try
        {
            var cameraService = new CameraService();

            // 1. Kiểm tra ChessboardCalibrationViewModel ở chế độ Toàn Cục (Standalone - Không có Job)
            var chessboardVm = new VisionInspectionApp.UI.ViewModels.ChessboardCalibrationViewModel(cameraService);
            chessboardVm.Initialize(null);

            if (!chessboardVm.IsGlobalMode)
            {
                throw new Exception("ChessboardCalibrationViewModel phải ở chế độ IsGlobalMode khi config == null!");
            }
            if (chessboardVm.HasActiveJob)
            {
                throw new Exception("ChessboardCalibrationViewModel phải có HasActiveJob == false khi config == null!");
            }
            if (!chessboardVm.WindowTitle.Contains("Toàn Cục", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"WindowTitle của ChessboardCalibrationViewModel phải chứa 'Toàn Cục', nhận được: '{chessboardVm.WindowTitle}'");
            }
            if (!chessboardVm.ModeBadgeText.Contains("TOÀN CỤC", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"ModeBadgeText của ChessboardCalibrationViewModel phải chứa 'TOÀN CỤC', nhận được: '{chessboardVm.ModeBadgeText}'");
            }
            if (!chessboardVm.IsLiveActive)
            {
                throw new Exception("ChessboardCalibrationViewModel mặc định IsLiveActive phải là true!");
            }

            // Test toggle live
            chessboardVm.ToggleLiveStreamCommand.Execute(null);
            if (chessboardVm.IsLiveActive)
            {
                throw new Exception("Sau khi ToggleLiveStreamCommand, IsLiveActive phải là false!");
            }
            chessboardVm.ToggleLiveStreamCommand.Execute(null);
            if (!chessboardVm.IsLiveActive)
            {
                throw new Exception("Sau khi ToggleLiveStreamCommand lần 2, IsLiveActive phải là true!");
            }

            // Kiểm tra khi có Job (_config != null)
            var jobConfig = new VisionInspectionApp.Models.VisionConfig();
            chessboardVm.Initialize(jobConfig);
            if (chessboardVm.IsGlobalMode)
            {
                throw new Exception("ChessboardCalibrationViewModel không được ở chế độ IsGlobalMode khi có config!");
            }
            if (!chessboardVm.HasActiveJob)
            {
                throw new Exception("ChessboardCalibrationViewModel phải có HasActiveJob == true khi có config!");
            }
            if (!chessboardVm.WindowTitle.Contains("Active Job", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"WindowTitle của ChessboardCalibrationViewModel phải chứa 'Active Job', nhận được: '{chessboardVm.WindowTitle}'");
            }

            // Kiểm tra nhập số thập phân (dấu chấm '.' và dấu phẩy ',') cho Cạnh ô vuông
            chessboardVm.SquareSizeMmText = "25.4";
            if (Math.Abs(chessboardVm.SquareSizeMm - 25.4) > 1e-6)
            {
                throw new Exception($"SquareSizeMmText '25.4' phải cập nhật SquareSizeMm = 25.4, nhận được: {chessboardVm.SquareSizeMm}");
            }
            chessboardVm.SquareSizeMmText = "12,7";
            if (Math.Abs(chessboardVm.SquareSizeMm - 12.7) > 1e-6)
            {
                throw new Exception($"SquareSizeMmText '12,7' phải cập nhật SquareSizeMm = 12.7, nhận được: {chessboardVm.SquareSizeMm}");
            }

            // Kiểm tra lệnh ApplyGlobalToJobCommand khi có Job đang mở
            var mockGlobalCal = new VisionInspectionApp.Models.ChessboardCalibrationData
            {
                BoardCols = 9,
                BoardRows = 6,
                SquareSizeMm = 30.5,
                Fx = 1250.0,
                Fy = 1250.0,
                Cx = 640.0,
                Cy = 480.0,
                PixelsPerMm = 45.5,
                ReprojectionError = 0.05,
                IsCalibrated = true
            };
            ChessboardCalibrationService.SaveGlobalCalibration(mockGlobalCal);

            chessboardVm.ApplyGlobalToJobCommand.Execute(null);
            if (jobConfig.ChessboardCalibration == null || Math.Abs(jobConfig.ChessboardCalibration.Fx - 1250.0) > 1e-4)
            {
                throw new Exception("ApplyGlobalToJobCommand phải cập nhật ChessboardCalibration của Job!");
            }
            if (Math.Abs(jobConfig.PixelsPerMm - 45.5) > 1e-4)
            {
                throw new Exception("ApplyGlobalToJobCommand phải cập nhật PixelsPerMm của Job!");
            }
            if (!chessboardVm.IsCalibrated)
            {
                throw new Exception("chessboardVm.IsCalibrated phải là true sau khi ApplyGlobalToJobCommand!");
            }
            if (Math.Abs(chessboardVm.SquareSizeMm - 30.5) > 1e-4)
            {
                throw new Exception($"chessboardVm.SquareSizeMm phải cập nhật thành 30.5! Nhận được: {chessboardVm.SquareSizeMm}");
            }

            // 2. Kiểm tra CalibrationViewModel (2-point calib) ở chế độ Toàn Cục (Standalone - Không có Job)
            var fakeConfigService = new FakeConfigService();
            var fakeJobService = new FakeJobService();
            var calibVm = new VisionInspectionApp.UI.ViewModels.CalibrationViewModel(
                fakeConfigService,
                new VisionInspectionApp.Application.ConfigStoreOptions(),
                cameraService,
                fakeJobService);

            calibVm.InitializeWithConfig(null, null, null);

            if (!calibVm.IsGlobalMode)
            {
                throw new Exception("CalibrationViewModel phải ở chế độ IsGlobalMode khi config == null!");
            }
            if (!calibVm.WindowTitle.Contains("Toàn Cục", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"WindowTitle của CalibrationViewModel phải chứa 'Toàn Cục', nhận được: '{calibVm.WindowTitle}'");
            }
            if (!calibVm.ModeBadgeText.Contains("TOÀN CỤC", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"ModeBadgeText của CalibrationViewModel phải chứa 'TOÀN CỤC', nhận được: '{calibVm.ModeBadgeText}'");
            }

            // Giả lập người dùng đo khoảng cách 200 pixels tương ứng 20 mm => 10 px/mm
            calibVm.CurrentDistancePx = 200.0;
            calibVm.RealDistanceMm = 20.0;
            calibVm.AddMeasurementCommand.Execute(null);
            calibVm.SavePixelsPerMm();

            // Kiểm tra Global Calibration đã lưu giá trị 10.0 px/mm
            var globalCalib = ChessboardCalibrationService.GetGlobalCalibration();
            if (globalCalib == null || Math.Abs(globalCalib.PixelsPerMm - 10.0) > 1e-4)
            {
                throw new Exception($"Global Calibration PixelsPerMm phải là 10.0 sau khi lưu chế độ Toàn Cục! Nhận được: {globalCalib?.PixelsPerMm}");
            }

            // Kiểm tra Job mới thừa hưởng giá trị này qua EnsureCalibration
            var emptyJob = new VisionInspectionApp.Models.VisionConfig { PixelsPerMm = 0 };
            ChessboardCalibrationService.EnsureCalibration(emptyJob);
            if (Math.Abs(emptyJob.PixelsPerMm - 10.0) > 1e-4)
            {
                throw new Exception($"Job mới phải kế thừa PixelsPerMm = 10.0 từ Global Calib! Nhận được: {emptyJob.PixelsPerMm}");
            }

            // 3. Kiểm tra CalibrationViewModel ở chế độ Active Job (config != null)
            var specificJob = new VisionInspectionApp.Models.VisionConfig { PixelsPerMm = 5.0 };
            calibVm.InitializeWithConfig(specificJob, @"C:\Jobs\MySpecificJob.job", null);

            if (calibVm.IsGlobalMode)
            {
                throw new Exception("CalibrationViewModel không được ở chế độ IsGlobalMode khi config != null!");
            }
            if (!calibVm.ModeBadgeText.Contains("MySpecificJob.job", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"ModeBadgeText phải hiển thị tên job, nhận được: '{calibVm.ModeBadgeText}'");
            }

            calibVm.ClearMeasurementsCommand.Execute(null);
            calibVm.CurrentDistancePx = 300.0;
            calibVm.RealDistanceMm = 10.0; // => 30.0 px/mm
            calibVm.AddMeasurementCommand.Execute(null);
            calibVm.SavePixelsPerMm();

            if (Math.Abs(specificJob.PixelsPerMm - 30.0) > 1e-4)
            {
                throw new Exception($"Specific Job PixelsPerMm phải là 30.0! Nhận được: {specificJob.PixelsPerMm}");
            }

            // Global calibration không bị ghi đè khi lưu ở Active Job mode
            var globalCalibAfter = ChessboardCalibrationService.GetGlobalCalibration();
            if (globalCalibAfter == null || Math.Abs(globalCalibAfter.PixelsPerMm - 10.0) > 1e-4)
            {
                throw new Exception($"Global Calibration PixelsPerMm không được bị thay đổi khi lưu ở Active Job mode! Nhận được: {globalCalibAfter?.PixelsPerMm}");
            }

            Console.WriteLine("   => PASS 100% (Standalone Global Calibration: Chessboard & TwoPoint workflows verified)");
        }
        finally
        {
            if (backupCalJson is not null) File.WriteAllText(globalCalFile, backupCalJson);
            else if (File.Exists(globalCalFile)) File.Delete(globalCalFile);
        }
    }

    private sealed class FakeConfigService : IConfigService
    {
        public VisionInspectionApp.Models.VisionConfig LoadConfig(string productCode) => new();
        public void SaveConfig(VisionInspectionApp.Models.VisionConfig config) { }
    }

    private sealed class FakeJobService : IJobService
    {
        public VisionInspectionApp.Models.VisionConfig LoadJob(string jobFilePath, out string tempWorkingDir)
        {
            tempWorkingDir = string.Empty;
            return new VisionInspectionApp.Models.VisionConfig();
        }

        public void SaveJob(VisionInspectionApp.Models.VisionConfig config, string tempWorkingDir, string jobFilePath) { }
    }
}

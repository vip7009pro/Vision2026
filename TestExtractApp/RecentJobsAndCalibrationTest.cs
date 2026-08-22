using System;
using System.Globalization;
using System.IO;
using VisionInspectionApp.Application.Services;

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
}

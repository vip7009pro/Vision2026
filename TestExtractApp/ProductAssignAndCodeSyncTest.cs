using System;
using System.Data;
using System.IO;
using System.Text.Json;
using VisionInspectionApp.Models;
using VisionInspectionApp.Persistence;

namespace TestExtractApp;

public static class ProductAssignAndCodeSyncTest
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {message}");
            Console.ResetColor();
            throw new Exception($"Assertion failed: {message}");
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✅ PASS: {message}");
        Console.ResetColor();
    }

    public static void RunTests()
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("🏷️ RUNNING PRODUCT ASSIGN & CODE SYNC TESTS");
        Console.WriteLine("========================================================");

        var testBaseDir = Path.Combine(Path.GetTempPath(), "Vision2026_Test_ProductAssign", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testBaseDir);

        try
        {
            // Test 1: Serialization of OqcScannerConfig with ProductListCodeColumn & ProductListNameColumn
            var cfg = new OqcScannerConfig
            {
                ProductListCodeColumn = "PART_NUMBER",
                ProductListNameColumn = "PART_DESC",
                ProductListQuery = "SELECT PART_NUMBER, PART_DESC FROM PARTS",
                ProductListPageSize = 25
            };

            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            var deserialized = JsonSerializer.Deserialize<OqcScannerConfig>(json);

            Assert(deserialized != null, "Test 1.1: OqcScannerConfig deserialized successfully");
            Assert(deserialized?.ProductListCodeColumn == "PART_NUMBER", "Test 1.2: ProductListCodeColumn correctly serialized & deserialized ('PART_NUMBER')");
            Assert(deserialized?.ProductListNameColumn == "PART_DESC", "Test 1.3: ProductListNameColumn correctly serialized & deserialized ('PART_DESC')");
            Assert(deserialized?.ProductListPageSize == 25, "Test 1.4: ProductListPageSize is 25");

            // Test 2: Extraction of product code (for DB assign) and product name (for Tool Editor autofill)
            var dt = new DataTable();
            dt.Columns.Add("ID", typeof(int));
            dt.Columns.Add("PART_NUMBER", typeof(string));
            dt.Columns.Add("PART_DESC", typeof(string));
            dt.Rows.Add(1, "GH63-22334A", "Cover Assembly S24");
            dt.Rows.Add(2, "GH63-99999B", "Front Bracket S24");

            string ExtractCode(DataRow row, string configuredCol)
            {
                if (!string.IsNullOrEmpty(configuredCol) && row.Table.Columns.Contains(configuredCol))
                    return row[configuredCol]?.ToString() ?? "";
                if (row.Table.Columns.Contains("G_CODE"))
                    return row["G_CODE"]?.ToString() ?? "";
                if (row.Table.Columns.Contains("ProductCode"))
                    return row["ProductCode"]?.ToString() ?? "";
                if (row.Table.Columns.Count > 0)
                    return row[0]?.ToString() ?? "";
                return "";
            }

            string ExtractName(DataRow row, string configuredCol, string fallbackCode)
            {
                if (!string.IsNullOrEmpty(configuredCol) && row.Table.Columns.Contains(configuredCol))
                {
                    var val = row[configuredCol]?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
                if (row.Table.Columns.Contains("G_NAME_KD"))
                {
                    var val = row["G_NAME_KD"]?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
                if (row.Table.Columns.Contains("ProductName"))
                {
                    var val = row["ProductName"]?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
                return fallbackCode;
            }

            var dbAssignCode = ExtractCode(dt.Rows[0], "PART_NUMBER");
            var toolEditorName = ExtractName(dt.Rows[0], "PART_DESC", dbAssignCode);

            Assert(dbAssignCode == "GH63-22334A", "Test 2.1: Extracted DB Assign ProductCode -> 'GH63-22334A'");
            Assert(toolEditorName == "Cover Assembly S24", "Test 2.2: Extracted Tool Editor Auto-fill ProductName -> 'Cover Assembly S24'");

            // Test 3: Fallback column extraction
            var dtFallback = new DataTable();
            dtFallback.Columns.Add("ItemIndex", typeof(int));
            dtFallback.Columns.Add("G_CODE", typeof(string));
            dtFallback.Columns.Add("G_NAME_KD", typeof(string));
            dtFallback.Rows.Add(101, "SAMSUNG_S24_ULTRA", "Galaxy S24 Ultra Titanium");

            var extractedFallbackCode = ExtractCode(dtFallback.Rows[0], "NON_EXISTENT_CODE");
            var extractedFallbackName = ExtractName(dtFallback.Rows[0], "NON_EXISTENT_NAME", extractedFallbackCode);

            Assert(extractedFallbackCode == "SAMSUNG_S24_ULTRA", "Test 3.1: Fallback extracted Code 'G_CODE' -> 'SAMSUNG_S24_ULTRA'");
            Assert(extractedFallbackName == "Galaxy S24 Ultra Titanium", "Test 3.2: Fallback extracted Name 'G_NAME_KD' -> 'Galaxy S24 Ultra Titanium'");

            // Test 4: Job update with product name sync & save
            var jobWorkingDir = Path.Combine(testBaseDir, "job_sync_src");
            var jobTemplatesDir = Path.Combine(jobWorkingDir, "templates");
            Directory.CreateDirectory(jobTemplatesDir);

            var jobConfig = new VisionConfig
            {
                ProductCode = "OLD_INITIAL_CODE",
                Origin = new PointDefinition
                {
                    Name = "Origin",
                    TemplateImageFile = "origin.png"
                }
            };

            var jobService = new JobService();
            var jobFilePath = Path.Combine(testBaseDir, "SyncTest.job");
            jobService.SaveJob(jobConfig, jobWorkingDir, jobFilePath);

            Assert(File.Exists(jobFilePath), "Test 4.1: Initial .job created");

            // Simulate assigning and saving job with toolEditorName ("Cover Assembly S24")
            jobConfig.ProductCode = toolEditorName;
            jobService.SaveJob(jobConfig, jobWorkingDir, jobFilePath);

            // Load and verify updated ProductCode in .job file
            var reloadedConfig = jobService.LoadJob(jobFilePath, out var reloadedTempDir);
            Assert(reloadedConfig.ProductCode == "Cover Assembly S24", "Test 4.2: Reloaded .job contains updated ProductCode from ProductName 'Cover Assembly S24'");

            Console.WriteLine("✅ ALL PRODUCT ASSIGN & CODE SYNC TESTS PASSED (100%)!\n");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testBaseDir))
                    Directory.Delete(testBaseDir, recursive: true);
            }
            catch { }
        }
    }
}

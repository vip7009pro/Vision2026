using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử tính năng tự động khớp CSDL trên OQC Scanner và hệ thống Xuất/Nạp toàn bộ cấu hình (System Config Backup & Restore).
/// </summary>
public static class SystemConfigBackupAndOqcDbMatchTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("📦  RUNNING SYSTEM CONFIG BACKUP & OQC DB MATCH TESTS");
        Console.WriteLine("=========================================================");

        Test_01_OqcDbMatch_ExactIdMatch();
        Test_02_OqcDbMatch_NameMatchWithDifferentId();
        Test_03_OqcDbMatch_CatalogMatch();
        Test_04_OqcDbMatch_DbIdMatchesDbName();
        Test_05_SystemConfigPackage_ExportAndInspect().GetAwaiter().GetResult();
        Test_06_SystemConfigPackage_RestoreAndDbRemap().GetAwaiter().GetResult();

        Console.WriteLine("✅ ALL SYSTEM CONFIG BACKUP & OQC DB MATCH TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Test_01_OqcDbMatch_ExactIdMatch()
    {
        Console.Write("--- [1/6] OQC DB Match: Khớp chính xác theo ID... ");

        var db1 = new DbModel { Id = "guid-111", Name = "MES_PROD", DatabaseName = "MesDb" };
        var db2 = new DbModel { Id = "guid-222", Name = "ERP_LINE1", DatabaseName = "ErpDb" };
        var list = new List<DbModel> { db1, db2 };

        var resolved = OqcScannerViewModel.ResolveDatabaseId("guid-222", "ERP_LINE1", list);
        if (resolved != "guid-222")
            throw new Exception($"Expected guid-222, got: {resolved}");

        Console.WriteLine("OK");
    }

    private static void Test_02_OqcDbMatch_NameMatchWithDifferentId()
    {
        Console.Write("--- [2/6] OQC DB Match: Khớp theo Name khi ID từ máy khác khác biệt... ");

        // Trên máy mới, CSDL tên 'MES_PROD' có ID mới là 'local-new-guid'
        var dbLocal = new DbModel { Id = "local-new-guid", Name = "MES_PROD", DatabaseName = "ProductionMES" };
        var list = new List<DbModel> { dbLocal };

        // File cấu hình import từ máy cũ lưu Id là 'foreign-machine-guid', Name là 'MES_PROD'
        var resolved = OqcScannerViewModel.ResolveDatabaseId("foreign-machine-guid", "MES_PROD", list);
        if (resolved != "local-new-guid")
            throw new Exception($"Expected local-new-guid, got: {resolved}");

        Console.WriteLine("OK");
    }

    private static void Test_03_OqcDbMatch_CatalogMatch()
    {
        Console.Write("--- [3/6] OQC DB Match: Khớp theo Database Catalog name... ");

        var dbLocal = new DbModel { Id = "local-sql-id", Name = "Kết Nối Xưởng 1", DatabaseName = "OqcInspectionDb" };
        var list = new List<DbModel> { dbLocal };

        // Khi export DbName lưu Database Catalog name 'OqcInspectionDb'
        var resolved = OqcScannerViewModel.ResolveDatabaseId("unknown-guid", "OqcInspectionDb", list);
        if (resolved != "local-sql-id")
            throw new Exception($"Expected local-sql-id, got: {resolved}");

        Console.WriteLine("OK");
    }

    private static void Test_04_OqcDbMatch_DbIdMatchesDbName()
    {
        Console.Write("--- [4/6] OQC DB Match: DbId lưu chuỗi Tên CSDL thay vì GUID... ");

        var dbLocal = new DbModel { Id = "id-real-guid", Name = "OQC_DB", DatabaseName = "OqcMain" };
        var list = new List<DbModel> { dbLocal };

        // Trường hợp cấu hình cũ lưu dbId = "OQC_DB"
        var resolved = OqcScannerViewModel.ResolveDatabaseId("OQC_DB", null, list);
        if (resolved != "id-real-guid")
            throw new Exception($"Expected id-real-guid, got: {resolved}");

        Console.WriteLine("OK");
    }

    private static async Task Test_05_SystemConfigPackage_ExportAndInspect()
    {
        Console.Write("--- [5/6] System Config Package: Xuất gói & Phân tích cấu trúc... ");

        var tempDir = Path.Combine(Path.GetTempPath(), "VisionBackupTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var exportFilePath = Path.Combine(tempDir, "BackupTest.viscfg");

            var dbConfigPath = Path.Combine(tempDir, "databases.json");
            var dbService = new DbManagerService(dbConfigPath);
            dbService.LoadDatabases(new List<DbModel>
            {
                new DbModel { Id = "db-01", Name = "DB_A", DatabaseName = "DbA_Catalog" }
            });

            var oqcService = new OqcScannerService();
            var plcManager = new PlcManagerService();

            var backupService = new SystemConfigBackupService(dbService, plcManager, oqcService);

            var options = new SystemConfigBackupOptions
            {
                IncludeAppSettings = true,
                IncludePlcConfig = true,
                IncludeDatabaseConfig = true,
                IncludeOqcConfig = true,
                IncludeCalibrationConfig = true,
                IncludeCameraConfig = true
            };

            var pkg = await backupService.ExportBackupPackageToFileAsync(exportFilePath, options);
            if (!File.Exists(exportFilePath))
                throw new Exception("Export file was not created!");

            // Kiểm tra inspect
            var inspection = backupService.InspectBackupPackage(exportFilePath);
            if (!inspection.IsValid)
                throw new Exception($"Inspection reported invalid package: {inspection.ErrorMessage}");

            if (inspection.DatabaseCount != 1)
                throw new Exception($"Expected 1 database, got: {inspection.DatabaseCount}");

            if (inspection.Header != SystemConfigPackage.CurrentPackageHeader)
                throw new Exception($"Header mismatch: {inspection.Header}");

            Console.WriteLine("OK");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    private static async Task Test_06_SystemConfigPackage_RestoreAndDbRemap()
    {
        Console.Write("--- [6/6] System Config Package: Nạp gói & Tự động ánh xạ DB ID... ");

        var tempDir = Path.Combine(Path.GetTempPath(), "VisionRestoreTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var exportFilePath = Path.Combine(tempDir, "MachineA_Config.viscfg");

            // Giả lập gói từ máy A: có CSDL 'OQC_MASTER' với ID 'machine-a-guid'
            var package = new SystemConfigPackage
            {
                Header = SystemConfigPackage.CurrentPackageHeader,
                Version = SystemConfigPackage.CurrentPackageVersion,
                ExportedFromMachine = "MACHINE_A",
                Description = "Cấu hình Máy A xuất sang Máy B",
                DatabaseConfig = new List<DbModel>
                {
                    new DbModel { Id = "machine-a-guid", Name = "OQC_MASTER", DatabaseName = "OqcCatalog" }
                },
                OqcConfig = new OqcScannerConfig
                {
                    LookupDbId = "machine-a-guid",
                    LookupDbName = "OQC_MASTER",
                    ProductNameDbId = "machine-a-guid",
                    ProductNameDbName = "OQC_MASTER",
                    ProductListDbId = "machine-a-guid",
                    ProductListDbName = "OQC_MASTER",
                    AssignDbId = "machine-a-guid",
                    AssignDbName = "OQC_MASTER",
                    JobManagerDbId = "machine-a-guid",
                    JobManagerDbName = "OQC_MASTER",
                    UpdateTeachImageDbId = "machine-a-guid",
                    UpdateTeachImageDbName = "OQC_MASTER",
                    LogResultDbId = "machine-a-guid",
                    LogResultDbName = "OQC_MASTER",
                    LogDetailResultDbId = "machine-a-guid",
                    LogDetailResultDbName = "OQC_MASTER"
                },
                AppSettings = new GlobalAppSettings
                {
                    ManualPixelsPerMm = 15.5
                },
                PlcConfig = new PlcConfigContainer()
            };

            var json = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(exportFilePath, json);

            // Môi trường máy B:
            var dbConfigBPath = Path.Combine(tempDir, "db_b.json");
            var dbServiceB = new DbManagerService(dbConfigBPath);
            var oqcServiceB = new OqcScannerService();
            var plcManagerB = new PlcManagerService();
            var backupServiceB = new SystemConfigBackupService(dbServiceB, plcManagerB, oqcServiceB);

            // Giả sử máy B đã có sẵn DB tên 'OQC_MASTER' nhưng ID khác: 'machine-b-guid'
            dbServiceB.LoadDatabases(new List<DbModel>
            {
                new DbModel { Id = "machine-b-guid", Name = "OQC_MASTER", DatabaseName = "OqcCatalog" }
            });

            bool eventFired = false;
            backupServiceB.SystemConfigRestored += (s, e) => { eventFired = true; };

            var restoreOptions = new SystemConfigRestoreOptions
            {
                RestoreDatabaseConfig = false, // Giữ nguyên DB máy B, không ghi đè DB từ file
                RestoreOqcConfig = true,
                RestoreAppSettings = true,
                RestorePlcConfig = true,
                RestoreCalibrationConfig = false,
                RestoreCameraConfig = false
            };

            var result = await backupServiceB.RestoreBackupPackageFromFileAsync(exportFilePath, restoreOptions);
            if (!result.Success)
                throw new Exception($"Restore failed: {result.Message}");

            if (!eventFired)
                throw new Exception("SystemConfigRestored event was not fired!");

            // Kiểm tra OQC scanner config trên máy B: Đã được tự động map về 'machine-b-guid'
            if (oqcServiceB.Config.LookupDbId != "machine-b-guid")
                throw new Exception($"LookupDbId was not remapped! Expected 'machine-b-guid', got '{oqcServiceB.Config.LookupDbId}'");

            if (oqcServiceB.Config.ProductNameDbId != "machine-b-guid")
                throw new Exception($"ProductNameDbId was not remapped! Expected 'machine-b-guid', got '{oqcServiceB.Config.ProductNameDbId}'");

            Console.WriteLine("OK");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}

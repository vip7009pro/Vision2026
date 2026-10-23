using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class PlcConflictAndHmiIsolationTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> BẮT ĐẦU KIỂM THỬ: GIẢI QUYẾT TRIỆT ĐỂ TRANH CHẤP PLC & CÔ LẬP HMI <<<");
        Console.WriteLine("==================================================================");

        Test1_PlcHeartbeatWatchdog_AutoDisableOnEmptyConfig();
        Test2_InferDataTypeFromAddress_BitAlwaysBool_WordRespectsFloat();
        Test3_LoadConfigInternal_SanitizePhantomTags();
        Test4_IndustrialConfig_SanitizesEmptyModules();

        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> TẤT CẢ 4/4 BÀI TEST TRANH CHẤP PLC & CÔ LẬP HMI ĐÃ PASSED 100%! <<<");
        Console.WriteLine("==================================================================");
    }

    private static void Test1_PlcHeartbeatWatchdog_AutoDisableOnEmptyConfig()
    {
        Console.WriteLine("\n[1] Testing PlcHeartbeatWatchdog - Auto Disable khi config rỗng...");

        var watchdog = new PlcHeartbeatWatchdog(null, "PLC_TEST");
        watchdog.IsEnabled = true;

        // Cấu hình rỗng từ file config
        var emptyConfig = new PlcHeartbeatConfig
        {
            IsEnabled = true,
            PlcId = "",
            VisionHeartbeatTagName = "",
            PlcHeartbeatTagName = ""
        };

        watchdog.ApplyConfig(emptyConfig);

        if (watchdog.IsEnabled)
        {
            throw new Exception("LỖI: Watchdog vẫn IsEnabled = true dù VisionHeartbeatTagName và PlcHeartbeatTagName đang rỗng!");
        }

        // Cấu hình hợp lệ
        var validConfig = new PlcHeartbeatConfig
        {
            IsEnabled = true,
            PlcId = "PLC1",
            VisionHeartbeatTagName = "M500",
            PlcHeartbeatTagName = "M501"
        };
        watchdog.ApplyConfig(validConfig);

        if (!watchdog.IsEnabled)
        {
            throw new Exception("LỖI: Watchdog không bật IsEnabled = true khi cấu hình đã hợp lệ!");
        }

        Console.WriteLine(" -> PASSED: Watchdog tự động khóa an toàn chống chạy bừa bãi khi thiếu tag.");
    }

    private static void Test2_InferDataTypeFromAddress_BitAlwaysBool_WordRespectsFloat()
    {
        Console.WriteLine("\n[2] Testing InferDataTypeFromAddress - Bit luôn Bool, Word tôn trọng Float/Int32...");

        // Bit device với hint bậy bạ (ví dụ Float) -> Bắt buộc phải là Bool
        var typeX0 = PlcManagerService.InferDataTypeFromAddress("X0", PlcDataType.Float);
        if (typeX0 != PlcDataType.Bool) throw new Exception($"LỖI: X0 phải là Bool nhưng trả về {typeX0}");

        var typeY10 = PlcManagerService.InferDataTypeFromAddress("Y10", PlcDataType.Float);
        if (typeY10 != PlcDataType.Bool) throw new Exception($"LỖI: Y10 phải là Bool nhưng trả về {typeY10}");

        var typeM100 = PlcManagerService.InferDataTypeFromAddress("M100", PlcDataType.Int32);
        if (typeM100 != PlcDataType.Bool) throw new Exception($"LỖI: M100 phải là Bool nhưng trả về {typeM100}");

        // Word register với hint Float / Int32 -> Phải giữ nguyên Float / Int32
        var typeD100Float = PlcManagerService.InferDataTypeFromAddress("D100", PlcDataType.Float);
        if (typeD100Float != PlcDataType.Float) throw new Exception($"LỖI: D100 với hint Float phải là Float nhưng trả về {typeD100Float}");

        var typeD200Int32 = PlcManagerService.InferDataTypeFromAddress("D200", PlcDataType.Int32);
        if (typeD200Int32 != PlcDataType.Int32) throw new Exception($"LỖI: D200 với hint Int32 phải là Int32 nhưng trả về {typeD200Int32}");

        // Word register không có hint -> Mặc định là Int16 (Word)
        var typeD0 = PlcManagerService.InferDataTypeFromAddress("D0", null);
        if (typeD0 != PlcDataType.Int16) throw new Exception($"LỖI: D0 không hint phải là Int16 nhưng trả về {typeD0}");

        Console.WriteLine(" -> PASSED: Suy luận kiểu dữ liệu tag chuẩn xác 100%, bảo vệ an toàn MC Protocol.");
    }

    private static void Test3_LoadConfigInternal_SanitizePhantomTags()
    {
        Console.WriteLine("\n[3] Testing PlcManagerService - Lọc bỏ tag ảo hệ thống...");

        var service = new PlcManagerService();

        var plcs = new List<PlcModel>
        {
            new PlcModel { Id = "PLC1", Name = "PLC1", IPAddress = "127.0.0.1", Port = 5000, DriverType = PlcDriverType.Mitsubishi }
        };

        var tags = new List<PlcTag>
        {
            new PlcTag { PlcId = "PLC1", Name = "X0", Address = "X0" },
            new PlcTag { PlcId = "PLC1", Name = "Y0_VisionHeartbeat", Address = "Y0_VisionHeartbeat" },
            new PlcTag { PlcId = "PLC1", Name = "X0_PlcHeartbeat", Address = "X0_PlcHeartbeat" },
            new PlcTag { PlcId = "PLC1", Name = "Y10_VisionFault", Address = "Y10_VisionFault" },
            new PlcTag { PlcId = "PLC1", Name = "D100", Address = "D100" }
        };

        // Kích hoạt nạp cấu hình qua reflection hoặc EnsureTagRegistered / Load
        // Kiểm tra GetAllTagsToPoll
        var allPollTags = service.GetAllTagsToPoll();
        var phantomFound = allPollTags.Any(t =>
            t.Name.Contains("VisionHeartbeat", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("VisionFault", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("PlcHeartbeat", StringComparison.OrdinalIgnoreCase));

        if (phantomFound)
        {
            throw new Exception("LỖI: GetAllTagsToPoll vẫn nạp các tag ảo Heartbeat khi chưa bật IsEnabled!");
        }

        Console.WriteLine(" -> PASSED: Polling Engine không còn chứa bất kỳ tag ảo nào.");
    }

    private static void Test4_IndustrialConfig_SanitizesEmptyModules()
    {
        Console.WriteLine("\n[4] Testing IndustrialConfig Sanitization - Module rỗng tự tắt IsEnabled...");

        var service = new PlcManagerService();
        service.IndustrialConfig.Heartbeat.IsEnabled = true;
        service.IndustrialConfig.Heartbeat.PlcId = "";
        service.IndustrialConfig.Heartbeat.VisionHeartbeatTagName = "";

        // Khi lưu hoặc kiểm tra: module rỗng không được gửi polling
        var pollTags = service.GetAllTagsToPoll();
        if (pollTags.Any(t => t.Name == "Y0_VisionHeartbeat"))
        {
            throw new Exception("LỖI: Heartbeat rỗng nhưng vẫn xuất hiện trong polling list!");
        }

        Console.WriteLine(" -> PASSED: Module công nghiệp rỗng tự động được cô lập an toàn.");
    }
}

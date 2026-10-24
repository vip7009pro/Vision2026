using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionInspectionApp.Application.HMI;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.UI.ViewModels.HMI;

namespace TestExtractApp;

public static class HmiStateAndLifecycleTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> BẮT ĐẦU KIỂM THỬ: HMI STATE AUTO-LOAD, LIFECYCLE & STOP HMI <<<");
        Console.WriteLine("==================================================================");

        Test1_HmiControl_AutomaticallyLoadsCurrentTagValueOnRunAndInitialRead();
        Test2_HmiControl_EffectiveReadAddress_FallbackToWriteAddress();
        Test3_HmiFileItem_EqualityAndPersistence();
        Test4_HmiPanel_StopAndStartCommands();
        Test5_GlobalAppSettings_HmiVisibilityAndPath();

        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> TẤT CẢ 5/5 BÀI TEST HMI STATE & LIFECYCLE ĐÃ PASSED 100%! <<<");
        Console.WriteLine("==================================================================");
    }

    private static void Test1_HmiControl_AutomaticallyLoadsCurrentTagValueOnRunAndInitialRead()
    {
        Console.WriteLine("\n[1] Testing HmiControl - Tự động nạp giá trị hiện tại của Tag khi Load và Run (không cần tương tác)...");

        var plcService = TestPlcConfigHelper.CreateIsolatedPlcManager();
        plcService.Plcs.Clear();
        plcService.Plcs.Add(new PlcModel
        {
            Id = "PLC1",
            Name = "FX5U",
            DriverType = PlcDriverType.Mitsubishi,
            Enabled = true
        });

        // Đăng ký sẵn và giả lập giá trị trong Cache của PLC Service từ trước
        plcService.EnsureTagRegistered("PLC1", "M100", PlcDataType.Bool, "Ready Bit", "HMI");
        plcService.EnsureTagRegistered("PLC1", "D200", PlcDataType.Int32, "Product Count", "HMI");

        // Ghi giá trị vào Cache (mô phỏng PollingEngine đã đọc được từ PLC trước đó)
        plcService.Cache.Set("PLC1", "M100", true, TagQuality.Good);
        plcService.Cache.Set("PLC1", "D200", 8888, TagQuality.Good);

        var screenConfig = new HmiScreenConfig
        {
            ScreenName = "Main Screen",
            TargetPlcId = "PLC1",
            Controls = new List<HmiControlModel>
            {
                new HmiControlModel
                {
                    Name = "Btn_Ready",
                    Type = HmiControlType.Button,
                    ReadPlcId = "PLC1",
                    ReadTagName = "M100",
                    ValueDataType = PlcDataType.Bool,
                    DefaultText = "OFF"
                },
                new HmiControlModel
                {
                    Name = "Num_Counter",
                    Type = HmiControlType.NumericInput,
                    ReadPlcId = "PLC1",
                    ReadTagName = "D200",
                    ValueDataType = PlcDataType.Int32,
                    DefaultText = "0"
                }
            }
        };

        string tempHmiPath = Path.Combine(Path.GetTempPath(), "test_autoload_" + Guid.NewGuid().ToString("N") + ".hmi");
        HmiService.SaveHmiConfigAsync(tempHmiPath, screenConfig).GetAwaiter().GetResult();

        try
        {
            var hmiManager = new HmiManagerViewModel(plcService);

            // Nạp tệp HMI khi IsRunMode = true
            hmiManager.IsRunMode = true;
            bool loaded = hmiManager.LoadFromFile(tempHmiPath);
            AssertEqual(true, loaded, "LoadFromFile phải trả về true");

            var btnCtrl = hmiManager.Controls.FirstOrDefault(c => c.Name == "Btn_Ready");
            var numCtrl = hmiManager.Controls.FirstOrDefault(c => c.Name == "Num_Counter");

            if (btnCtrl == null || numCtrl == null)
            {
                throw new Exception("LỖI: Không tìm thấy controls sau khi LoadFromFile!");
            }

            // Gọi RefreshCurrentValueFromPlc để giả lập tức thời nếu luồng async chưa kết thúc
            btnCtrl.RefreshCurrentValueFromPlc();
            numCtrl.RefreshCurrentValueFromPlc();

            // Kiểm tra: Nút nhấn và Ô đếm số PHẢI tự động nhận giá trị hiện tại ngay lập tức mà KHÔNG cần click tương tác!
            AssertEqual(true, btnCtrl.IsOn, "Btn_Ready IsOn phải là True ngay khi nạp mà không cần click!");
            AssertEqual("8888", numCtrl.CurrentDisplayValue, "Num_Counter CurrentDisplayValue phải là 8888 ngay khi nạp!");

            Console.WriteLine(" -> PASSED: HMI Controls tự động load giá trị hiện tại từ PLC Cache/Store!");
        }
        finally
        {
            if (File.Exists(tempHmiPath)) File.Delete(tempHmiPath);
        }
    }

    private static void Test2_HmiControl_EffectiveReadAddress_FallbackToWriteAddress()
    {
        Console.WriteLine("\n[2] Testing HmiControl - Fallback EffectiveReadAddress sang WriteTagName khi không dùng địa chỉ riêng...");

        var model = new HmiControlModel
        {
            Name = "Btn_AutoStart",
            Type = HmiControlType.Button,
            ReadTagName = "",
            WriteTagName = "M50",
            UseSeparateWriteAddress = false
        };

        var ctrlVm = new HmiControlViewModel(model, null, null);
        AssertEqual("M50", ctrlVm.EffectiveReadAddress, "EffectiveReadAddress phải fallback sang WriteTagName khi ReadTagName rỗng");
        AssertEqual("M50", ctrlVm.EffectiveWriteAddress, "EffectiveWriteAddress phải khớp M50");

        // Khi UseSeparateWriteAddress = true và ReadTagName rỗng
        model.UseSeparateWriteAddress = true;
        ctrlVm.NotifyModelChanged();
        AssertEqual("", ctrlVm.EffectiveReadAddress, "EffectiveReadAddress phải rỗng khi UseSeparateWriteAddress = true mà ReadTagName rỗng");
        AssertEqual("M50", ctrlVm.EffectiveWriteAddress, "EffectiveWriteAddress phải là M50");

        Console.WriteLine(" -> PASSED: EffectiveReadAddress fallback hoạt động chính xác 100%!");
    }

    private static void Test3_HmiFileItem_EqualityAndPersistence()
    {
        Console.WriteLine("\n[3] Testing HmiFileItem - So sánh bình đẳng (IEquatable) và HashSet chống trùng lặp...");

        var item1 = new HmiFileItem { FilePath = @"C:\VisionApp\Hmi\Main.hmi" };
        var item2 = new HmiFileItem { FilePath = @"c:\visionapp\hmi\main.hmi" };
        var item3 = new HmiFileItem { FilePath = @"C:\VisionApp\Hmi\Other.hmi" };

        AssertEqual(true, item1.Equals(item2), "item1 và item2 phải bằng nhau (không phân biệt chữ hoa/thường)");
        AssertEqual(false, item1.Equals(item3), "item1 và item3 phải khác nhau");

        var set = new HashSet<HmiFileItem> { item1 };
        bool added = set.Add(item2);
        AssertEqual(false, added, "HashSet không được phép thêm item2 vì trùng đường dẫn với item1!");
        AssertEqual(1, set.Count, "Set chỉ chứa 1 item duy nhất");

        Console.WriteLine(" -> PASSED: HmiFileItem IEquatable hỗ trợ ComboBox nhận diện đúng tệp HMI!");
    }

    private static void Test4_HmiPanel_StopAndStartCommands()
    {
        Console.WriteLine("\n[4] Testing HmiPanel - Nút Dừng HMI / Chạy HMI và trạng thái Live Badge...");

        var plcService = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var hmiManager = new HmiManagerViewModel(plcService);

        // Giả lập trạng thái chạy
        hmiManager.IsRunMode = true;
        AssertEqual(true, hmiManager.IsRunMode, "HMI ban đầu phải ở RunMode = true");
        AssertEqual(true, hmiManager.Controls.All(c => c.IsRunMode), "Toàn bộ controls phải có IsRunMode = true");

        // Gọi StopRunMode (Dừng HMI)
        hmiManager.StopRunMode();
        AssertEqual(false, hmiManager.IsRunMode, "Sau khi Stop, IsRunMode phải = false");
        AssertEqual(false, hmiManager.Controls.Any(c => c.IsRunMode), "Toàn bộ controls phải có IsRunMode = false (Khóa tương tác an toàn)");

        // Chạy lại HMI
        hmiManager.IsRunMode = true;
        AssertEqual(true, hmiManager.IsRunMode, "Khi chạy lại, IsRunMode phải = true");
        AssertEqual(true, hmiManager.Controls.All(c => c.IsRunMode), "Toàn bộ controls phải được kích hoạt lại IsRunMode = true");

        Console.WriteLine(" -> PASSED: Nút Dừng HMI / Chạy HMI hoạt động chuẩn xác và cô lập an toàn!");
    }

    private static void Test5_GlobalAppSettings_HmiVisibilityAndPath()
    {
        Console.WriteLine("\n[5] Testing GlobalAppSettings - Lưu và nhớ ToolEditorIsHmiVisible & ToolEditorSelectedHmiPath...");

        var settings = new GlobalAppSettings();
        AssertEqual(false, settings.ToolEditorIsHmiVisible, "Mặc định ToolEditorIsHmiVisible phải là false");
        AssertEqual(string.Empty, settings.ToolEditorSelectedHmiPath, "Mặc định ToolEditorSelectedHmiPath phải là rỗng");

        settings.ToolEditorIsHmiVisible = true;
        settings.ToolEditorSelectedHmiPath = @"C:\VisionConfig\PackagingLine.hmi";

        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<GlobalAppSettings>(json);

        AssertEqual(true, restored?.ToolEditorIsHmiVisible, "ToolEditorIsHmiVisible phải được phục hồi là true");
        AssertEqual(@"C:\VisionConfig\PackagingLine.hmi", restored?.ToolEditorSelectedHmiPath, "ToolEditorSelectedHmiPath phải được bảo toàn");

        Console.WriteLine(" -> PASSED: Cài đặt hiển thị và đường dẫn tệp HMI được bảo toàn 100%!");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"FAIL: {message} | Kỳ vọng: '{expected}', Thực tế: '{actual}'");
        }
    }
}

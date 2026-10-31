using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels.PLC;

namespace TestExtractApp;

public static class PlcHandshakeNumericModeTests
{
    public static async Task RunAllTestsAsync()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("BẮT ĐẦU KIỂM THỬ: CHẾ ĐỘ XUẤT KẾT QUẢ SỐ NGUYÊN (INT16) & SONG HÀNH BẮT TAY 24/7");
        Console.WriteLine("=======================================================");

        Test1_JsonSerializationAndDeserialization();
        Test2_Defaults_And_HelperMethods();
        await Test3_NumericMode_Emits_OK2_And_NG3_Async();
        await Test4_BothMode_Emits_Bit_And_Numeric_Concurrently_Async();
        await Test5_Custom_Numeric_Values_Async();
        Test6_PlcManagerService_Registers_ResultWord_As_Int16();
        Test7_PlcManagerViewModel_OutputModes_And_TagIntegrity();

        Console.WriteLine("=======================================================");
        Console.WriteLine("TẤT CẢ 7/7 TEST CHẾ ĐỘ XUẤT KẾT QUẢ SỐ NGUYÊN (INT16) & SONG HÀNH ĐÃ VƯỢT QUA (PASSED 100%)");
        Console.WriteLine("=======================================================\n");
    }

    private static void Test1_JsonSerializationAndDeserialization()
    {
        Console.WriteLine("--> Test 1: JSON Serialization & Deserialization với ResultOutputMode, ResultWordTagName & Numeric Values");

        var config = new IndustrialHandshakeConfig
        {
            PlcId = "PLC_FX5U",
            ResultOutputMode = PlcResultOutputMode.NumericInt16,
            ResultWordTagName = "D100",
            PassNumericValue = 2,
            NgNumericValue = 3,
            IdleNumericValue = 0,
            ClearResultWordOnReady = true
        };

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<IndustrialHandshakeConfig>(json);

        if (deserialized == null)
            throw new Exception("Deserialized config cannot be null.");

        if (deserialized.ResultOutputMode != PlcResultOutputMode.NumericInt16)
            throw new Exception($"ResultOutputMode mismatch: expected NumericInt16, got {deserialized.ResultOutputMode}");

        if (deserialized.ResultWordTagName != "D100")
            throw new Exception($"ResultWordTagName mismatch: expected D100, got {deserialized.ResultWordTagName}");

        if (deserialized.PassNumericValue != 2 || deserialized.NgNumericValue != 3 || deserialized.IdleNumericValue != 0)
            throw new Exception($"Numeric values mismatch: expected 2/3/0, got {deserialized.PassNumericValue}/{deserialized.NgNumericValue}/{deserialized.IdleNumericValue}");

        if (!deserialized.ClearResultWordOnReady)
            throw new Exception("ClearResultWordOnReady mismatch: expected true");

        // Test chế độ Song hành (Both)
        config.ResultOutputMode = PlcResultOutputMode.Both;
        string jsonBoth = JsonSerializer.Serialize(config);
        var desBoth = JsonSerializer.Deserialize<IndustrialHandshakeConfig>(jsonBoth);
        if (desBoth?.ResultOutputMode != PlcResultOutputMode.Both)
            throw new Exception("ResultOutputMode Both serialization mismatch");

        Console.WriteLine("    [OK] JSON Serialization & Deserialization bảo toàn cấu hình dạng số 100%.");
    }

    private static void Test2_Defaults_And_HelperMethods()
    {
        Console.WriteLine("--> Test 2: Defaults, IsBitModeActive, IsNumericModeActive & CopyFrom Helper Methods");

        var cfg = new IndustrialHandshakeConfig();

        // Mặc định ban đầu là Bit
        if (cfg.ResultOutputMode != PlcResultOutputMode.Bit)
            throw new Exception("Mặc định ResultOutputMode phải là Bit.");
        if (!cfg.IsBitModeActive || cfg.IsNumericModeActive)
            throw new Exception("Khi ResultOutputMode == Bit: IsBitModeActive phải true, IsNumericModeActive phải false.");

        // Chuyển sang NumericInt16
        cfg.ResultOutputMode = PlcResultOutputMode.NumericInt16;
        if (cfg.IsBitModeActive || !cfg.IsNumericModeActive)
            throw new Exception("Khi ResultOutputMode == NumericInt16: IsBitModeActive phải false, IsNumericModeActive phải true.");

        // Chuyển sang Both (Song hành)
        cfg.ResultOutputMode = PlcResultOutputMode.Both;
        if (!cfg.IsBitModeActive || !cfg.IsNumericModeActive)
            throw new Exception("Khi ResultOutputMode == Both: cả IsBitModeActive và IsNumericModeActive đều phải true.");

        // Test ApplyFx5uDefaults
        cfg.ApplyFx5uDefaults("PLC2");
        if (cfg.ResultWordTagName != "D100" || cfg.PassNumericValue != 2 || cfg.NgNumericValue != 3 || cfg.IdleNumericValue != 0)
            throw new Exception("ApplyFx5uDefaults thiết lập sai giá trị mặc định cho ResultWordTagName / PassNumericValue.");

        // Test ApplyStandardIoDefaults
        cfg.ApplyStandardIoDefaults("PLC1");
        if (cfg.ResultWordTagName != "MW100" || cfg.PassNumericValue != 2 || cfg.NgNumericValue != 3 || cfg.IdleNumericValue != 0)
            throw new Exception("ApplyStandardIoDefaults thiết lập sai giá trị mặc định cho ResultWordTagName.");

        // Test CopyFrom
        var target = new IndustrialHandshakeConfig();
        target.CopyFrom(cfg);
        if (target.ResultWordTagName != "MW100" || target.PassNumericValue != 2 || target.NgNumericValue != 3 || target.ResultOutputMode != cfg.ResultOutputMode)
            throw new Exception("CopyFrom sao chép sai các thuộc tính số nguyên.");

        Console.WriteLine("    [OK] Defaults, IsBitModeActive, IsNumericModeActive và CopyFrom chuẩn xác 100%.");
    }

    private static PlcManagerService CreateConnectedSimulationPlcManager(string plcId = "PLC1")
    {
        var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var plc = new PlcModel
        {
            Id = plcId,
            Name = plcId,
            DriverType = PlcDriverType.Mitsubishi,
            IPAddress = "127.0.0.1",
            Port = 5007,
            Enabled = true,
            State = PlcConnectionState.Connected
        };
        var tags = new[]
        {
            new PlcTag { Id = "T_M101", PlcId = plcId, Name = "M101_VisionReady", Address = "M101", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M102", PlcId = plcId, Name = "M102_VisionBusy", Address = "M102", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M103", PlcId = plcId, Name = "M103_VisionDone", Address = "M103", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M104", PlcId = plcId, Name = "M104_VisionPass", Address = "M104", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M105", PlcId = plcId, Name = "M105_VisionNG", Address = "M105", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_D100", PlcId = plcId, Name = "D100_VisionResult", Address = "D100", DataType = PlcDataType.Int16 }
        };
        plcManager.LoadConfig(new[] { plc }, tags);
        var driver = (MitsubishiDriver)plcManager.GetDriver(plcId)!;
        driver.ForceSimulationMode = true;
        return plcManager;
    }

    private static async Task Test3_NumericMode_Emits_OK2_And_NG3_Async()
    {
        Console.WriteLine("--> Test 3: Chế Độ Dạng Số (Int16 Word) Ghi OK=2 và NG=3 Vào Thanh Ghi D100");

        using var plcManager = CreateConnectedSimulationPlcManager("PLC1");
        var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
        {
            ResultOutputMode = PlcResultOutputMode.NumericInt16,
            ResultWordTagName = "D100",
            PassNumericValue = 2,
            NgNumericValue = 3,
            IdleNumericValue = 0,
            ClearResultWordOnReady = true,
            PlcAckTagName = "", // Non-blocking
            NonBlockingMode = true,
            IsEnabled = true
        };

        // 1. Chuyển sang Ready: D100 phải được xóa về 0
        await sm.SetReadyAsync();
        var readyVal = plcManager.GetTagValue("PLC1", "D100")?.CurrentValue;
        short shortReady = Convert.ToInt16(readyVal);
        if (shortReady != 0)
            throw new Exception($"SetReadyAsync không đưa D100 về 0, nhận: {shortReady}");

        // 2. Chốt kết quả ĐẠT (OK / Pass = true): D100 phải mang giá trị 2
        await sm.StartInspectionAsync();
        bool passResult = await sm.CompleteHandshakeAsync(isPass: true);
        if (!passResult)
            throw new Exception("CompleteHandshakeAsync trả về false khi kết quả Pass.");

        var okVal = plcManager.GetTagValue("PLC1", "D100")?.CurrentValue;
        short shortOk = Convert.ToInt16(okVal);
        if (shortOk != 2)
            throw new Exception($"CompleteHandshakeAsync(isPass: true) không ghi 2 vào D100, nhận: {shortOk}");

        // 3. Chuẩn bị đón phôi tiếp theo: SetReadyAsync xóa D100 về 0
        await sm.SetReadyAsync();
        var resetVal = plcManager.GetTagValue("PLC1", "D100")?.CurrentValue;
        if (Convert.ToInt16(resetVal) != 0)
            throw new Exception($"SetReadyAsync lần 2 không đưa D100 về 0, nhận: {resetVal}");

        // 4. Chốt kết quả LỖI (NG = false): D100 phải mang giá trị 3
        await sm.StartInspectionAsync();
        bool ngResult = await sm.CompleteHandshakeAsync(isPass: false);
        if (!ngResult)
            throw new Exception("CompleteHandshakeAsync trả về false khi kết quả NG.");

        var ngVal = plcManager.GetTagValue("PLC1", "D100")?.CurrentValue;
        short shortNg = Convert.ToInt16(ngVal);
        if (shortNg != 3)
            throw new Exception($"CompleteHandshakeAsync(isPass: false) không ghi 3 vào D100, nhận: {shortNg}");

        Console.WriteLine("    [OK] Chế độ Dạng Số Int16 ghi chính xác OK=2, NG=3 và Reset=0 vào thanh ghi Word.");
    }

    private static async Task Test4_BothMode_Emits_Bit_And_Numeric_Concurrently_Async()
    {
        Console.WriteLine("--> Test 4: Chế Độ Song Hành (Both) Gửi Đồng Thời Cả Bit Bool và Số Int16");

        using var plcManager = CreateConnectedSimulationPlcManager("PLC1");
        var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
        {
            ResultOutputMode = PlcResultOutputMode.Both,
            PassTagName = "M104",
            NgTagName = "M105",
            ResultWordTagName = "D100",
            PassNumericValue = 2,
            NgNumericValue = 3,
            IdleNumericValue = 0,
            PassSignalType = PlcSignalType.Level,
            NgSignalType = PlcSignalType.Level,
            PlcAckTagName = "",
            NonBlockingMode = true,
            IsEnabled = true
        };

        // Chốt kết quả ĐẠT (OK): Bit Pass (M104) = true, NG (M105) = false VÀ D100 = 2
        await sm.SetReadyAsync();
        await sm.StartInspectionAsync();
        await sm.CompleteHandshakeAsync(isPass: true);

        bool m104Val = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M104")?.CurrentValue);
        bool m105Val = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M105")?.CurrentValue);
        short d100Val = Convert.ToInt16(plcManager.GetTagValue("PLC1", "D100")?.CurrentValue);

        if (!m104Val || m105Val || d100Val != 2)
        {
            throw new Exception($"Song hành Pass thất bại: M104={m104Val} (mong đợi true), M105={m105Val} (mong đợi false), D100={d100Val} (mong đợi 2)");
        }

        // Chốt kết quả LỖI (NG): Bit Pass (M104) = false, NG (M105) = true VÀ D100 = 3
        await sm.SetReadyAsync();
        await sm.StartInspectionAsync();
        await sm.CompleteHandshakeAsync(isPass: false);

        bool m104Ng = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M104")?.CurrentValue);
        bool m105Ng = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M105")?.CurrentValue);
        short d100Ng = Convert.ToInt16(plcManager.GetTagValue("PLC1", "D100")?.CurrentValue);

        if (m104Ng || !m105Ng || d100Ng != 3)
        {
            throw new Exception($"Song hành NG thất bại: M104={m104Ng} (mong đợi false), M105={m105Ng} (mong đợi true), D100={d100Ng} (mong đợi 3)");
        }

        Console.WriteLine("    [OK] Chế độ Song Hành phát đồng thời cả tín hiệu Bit (M104/M105) và Số Int16 (D100) chuẩn xác 100%.");
    }

    private static async Task Test5_Custom_Numeric_Values_Async()
    {
        Console.WriteLine("--> Test 5: Giá Trị Số Tùy Chỉnh Của Người Dùng (Ví Dụ: OK = 10, NG = 99, Idle = -1)");

        using var plcManager = CreateConnectedSimulationPlcManager("PLC1");
        var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
        {
            ResultOutputMode = PlcResultOutputMode.NumericInt16,
            ResultWordTagName = "D100",
            PassNumericValue = 10,
            NgNumericValue = 99,
            IdleNumericValue = -1,
            ClearResultWordOnReady = true,
            PlcAckTagName = "",
            NonBlockingMode = true,
            IsEnabled = true
        };

        await sm.SetReadyAsync();
        if (Convert.ToInt16(plcManager.GetTagValue("PLC1", "D100")?.CurrentValue) != -1)
            throw new Exception("SetReadyAsync không ghi giá trị IdleNumericValue = -1 tùy chỉnh.");

        await sm.StartInspectionAsync();
        await sm.CompleteHandshakeAsync(isPass: true);
        if (Convert.ToInt16(plcManager.GetTagValue("PLC1", "D100")?.CurrentValue) != 10)
            throw new Exception("CompleteHandshakeAsync không ghi giá trị PassNumericValue = 10 tùy chỉnh.");

        await sm.SetReadyAsync();
        await sm.StartInspectionAsync();
        await sm.CompleteHandshakeAsync(isPass: false);
        if (Convert.ToInt16(plcManager.GetTagValue("PLC1", "D100")?.CurrentValue) != 99)
            throw new Exception("CompleteHandshakeAsync không ghi giá trị NgNumericValue = 99 tùy chỉnh.");

        Console.WriteLine("    [OK] Giá trị số nguyên tùy chỉnh (OK=10, NG=99, Idle=-1) hoạt động hoàn hảo.");
    }

    private static void Test6_PlcManagerService_Registers_ResultWord_As_Int16()
    {
        Console.WriteLine("--> Test 6: PlcManagerService Tự Động Đăng Ký Thanh Ghi Word với Kiểu PlcDataType.Int16");

        using var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        plcManager.IndustrialConfig.Handshake.ResultOutputMode = PlcResultOutputMode.NumericInt16;
        plcManager.IndustrialConfig.Handshake.ResultWordTagName = "D100";
        plcManager.IndustrialConfig.Handshake.PassNumericValue = 2;
        plcManager.IndustrialConfig.Handshake.NgNumericValue = 3;

        plcManager.EnsureIndustrialTagsRegistered();

        var d100Tag = plcManager.Tags.FirstOrDefault(t => string.Equals(t.Address, "D100", StringComparison.OrdinalIgnoreCase) ||
                                                          string.Equals(t.Name, "D100", StringComparison.OrdinalIgnoreCase));
        if (d100Tag == null)
            throw new Exception("EnsureIndustrialTagsRegistered không tự động đăng ký D100 vào Tags.");

        if (d100Tag.DataType != PlcDataType.Int16)
            throw new Exception($"D100 phải có kiểu dữ liệu PlcDataType.Int16, nhận: {d100Tag.DataType}");

        var pollingTags = plcManager.GetAllTagsToPoll();
        var pollD100 = pollingTags.FirstOrDefault(t => string.Equals(t.Address, "D100", StringComparison.OrdinalIgnoreCase));
        if (pollD100 == null || pollD100.DataType != PlcDataType.Int16)
            throw new Exception("GetAllTagsToPoll phải chứa D100 với kiểu PlcDataType.Int16.");

        Console.WriteLine("    [OK] PlcManagerService đăng ký và đưa D100 vào Polling với kiểu Int16 chính xác 100%.");
    }

    private static void Test7_PlcManagerViewModel_OutputModes_And_TagIntegrity()
    {
        Console.WriteLine("--> Test 7: PlcManagerViewModel Cung Cấp 3 Chế Độ Xuất Kết Quả & Gợi Ý Tag");

        using var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var vm = new PlcManagerViewModel(plcManager);

        if (vm.AvailableResultOutputModes == null || vm.AvailableResultOutputModes.Count != 3)
            throw new Exception($"AvailableResultOutputModes phải có đúng 3 lựa chọn, nhận: {vm.AvailableResultOutputModes?.Count}");

        var modeValues = vm.AvailableResultOutputModes.Select(m => m.Value).ToList();
        if (!modeValues.Contains(PlcResultOutputMode.Bit) ||
            !modeValues.Contains(PlcResultOutputMode.NumericInt16) ||
            !modeValues.Contains(PlcResultOutputMode.Both))
        {
            throw new Exception("AvailableResultOutputModes thiếu một trong các chế độ Bit, NumericInt16, Both.");
        }

        vm.RefreshAvailableTagsAndPlcs();
        if (!vm.AvailableTagNames.Contains("D100") || !vm.AvailableTagNames.Contains("MW100"))
            throw new Exception("AvailableTagNames phải chứa các địa chỉ thanh ghi số tiêu chuẩn D100 và MW100.");

        Console.WriteLine("    [OK] ViewModel cung cấp đầy đủ 3 chế độ xuất kết quả và danh mục thanh ghi Word.");
    }
}


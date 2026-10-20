using System;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels.PLC;

namespace TestExtractApp;

public static class PlcSignalTypeTests
{
    public static async Task RunAllTestsAsync()
    {
        Console.WriteLine("\n=== [TEST SUITE] PLC Signal Type (Level / Rising Edge / Falling Edge) Tests ===");

        Test_JsonSerializationAndDeserialization();
        await Test_RisingEdgeSignalEmissionAsync();
        await Test_FallingEdgeSignalEmissionAsync();
        await Test_LevelSignalEmissionAsync();
        Test_PlcManagerViewModelSignalTypes();

        Console.WriteLine("=== [PASSED] All PLC Signal Type Tests Completed Successfully! ===\n");
    }

    private static void Test_JsonSerializationAndDeserialization()
    {
        Console.WriteLine("--> Test 1: JSON Serialization & Deserialization IndustrialHandshakeConfig with Signal Types");

        var config = new IndustrialHandshakeConfig
        {
            PlcId = "PLC1",
            PassTagName = "M104",
            NgTagName = "M105",
            DoneTagName = "M103",
            PassSignalType = PlcSignalType.RisingEdge,
            NgSignalType = PlcSignalType.FallingEdge,
            DoneSignalType = PlcSignalType.Level,
            PulseDurationMs = 120
        };

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<IndustrialHandshakeConfig>(json);

        if (deserialized == null)
            throw new Exception("Deserialized config cannot be null");

        if (deserialized.PassSignalType != PlcSignalType.RisingEdge)
            throw new Exception($"PassSignalType mismatch: expected RisingEdge, got {deserialized.PassSignalType}");

        if (deserialized.NgSignalType != PlcSignalType.FallingEdge)
            throw new Exception($"NgSignalType mismatch: expected FallingEdge, got {deserialized.NgSignalType}");

        if (deserialized.DoneSignalType != PlcSignalType.Level)
            throw new Exception($"DoneSignalType mismatch: expected Level, got {deserialized.DoneSignalType}");

        if (deserialized.PulseDurationMs != 120)
            throw new Exception($"PulseDurationMs mismatch: expected 120, got {deserialized.PulseDurationMs}");

        Console.WriteLine("    [OK] Serialization và Deserialization bảo toàn cấu hình kiểu tín hiệu 100%.");
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
            new PlcTag { Id = "T_M103", PlcId = plcId, Name = "M103_VisionDone", Address = "M103", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M104", PlcId = plcId, Name = "M104_VisionPass", Address = "M104", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M105", PlcId = plcId, Name = "M105_VisionNG", Address = "M105", DataType = PlcDataType.Bool }
        };
        plcManager.LoadConfig(new[] { plc }, tags);
        var driver = (VisionInspectionApp.Application.PLC.Drivers.MitsubishiDriver)plcManager.GetDriver(plcId)!;
        driver.ForceSimulationMode = true;
        return plcManager;
    }

    private static async Task Test_RisingEdgeSignalEmissionAsync()
    {
        Console.WriteLine("--> Test 2: Rising Edge Signal Emission (0 -> 1 -> sau PulseDurationMs -> 0)");

        using var plcManager = CreateConnectedSimulationPlcManager("PLC1");
        var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
        {
            PassTagName = "M104",
            PassSignalType = PlcSignalType.RisingEdge,
            PulseDurationMs = 50, // 50ms test duration
            IsEnabled = true
        };

        // Kích hoạt tín hiệu Pass
        await sm.EmitSignalAsync("M104", PlcSignalType.RisingEdge, true);

        // Giá trị tức thời phải là True (1)
        var instantVal = plcManager.GetTagValue("PLC1", "M104")?.CurrentValue;
        if (instantVal is not bool b1 || !b1)
        {
            throw new Exception($"RisingEdge instant value should be true, got {instantVal}");
        }

        // Chờ sau 75ms (> 50ms pulse duration)
        await Task.Delay(75);

        // Giá trị phải tự động hạ về False (0)
        var afterVal = plcManager.GetTagValue("PLC1", "M104")?.CurrentValue;
        if (afterVal is not bool b2 || b2)
        {
            throw new Exception($"RisingEdge after pulse value should be false (0), got {afterVal}");
        }

        Console.WriteLine("    [OK] Tín hiệu Rising Edge phát xung mức cao (0 -> 1 -> 0) chuẩn xác.");
    }

    private static async Task Test_FallingEdgeSignalEmissionAsync()
    {
        Console.WriteLine("--> Test 3: Falling Edge Signal Emission (1 -> 0 -> sau PulseDurationMs -> 1)");

        using var plcManager = CreateConnectedSimulationPlcManager("PLC1");
        var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
        {
            NgTagName = "M105",
            NgSignalType = PlcSignalType.FallingEdge,
            PulseDurationMs = 50,
            IsEnabled = true
        };

        // Khi SetReady, mức tĩnh của Falling Edge phải là True (1)
        await sm.SetReadyAsync();
        var readyVal = plcManager.GetTagValue("PLC1", "M105")?.CurrentValue;
        if (readyVal is not bool b0 || !b0)
        {
            throw new Exception($"FallingEdge idle value must be true (1), got {readyVal}");
        }

        // Kích hoạt tín hiệu NG phát xung cạnh xuống (Active = True)
        await sm.EmitSignalAsync("M105", PlcSignalType.FallingEdge, true);

        // Giá trị tức thời phải rớt xuống False (0)
        var instantVal = plcManager.GetTagValue("PLC1", "M105")?.CurrentValue;
        if (instantVal is not bool b1 || b1)
        {
            throw new Exception($"FallingEdge active pulse value should be false (0), got {instantVal}");
        }

        // Chờ sau 75ms (> 50ms pulse duration)
        await Task.Delay(75);

        // Giá trị phải tự động quay lại mức tĩnh True (1)
        var afterVal = plcManager.GetTagValue("PLC1", "M105")?.CurrentValue;
        if (afterVal is not bool b2 || !b2)
        {
            throw new Exception($"FallingEdge after pulse value should recover to true (1), got {afterVal}");
        }

        Console.WriteLine("    [OK] Tín hiệu Falling Edge phát xung mức thấp (1 -> 0 -> 1) chuẩn xác.");
    }

    private static async Task Test_LevelSignalEmissionAsync()
    {
        Console.WriteLine("--> Test 4: Level Signal Emission (Duy trì mức tĩnh)");

        using var plcManager = CreateConnectedSimulationPlcManager("PLC1");
        var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
        {
            PassTagName = "M104",
            PassSignalType = PlcSignalType.Level,
            PulseDurationMs = 50,
            IsEnabled = true
        };

        // Kích hoạt Pass (Level)
        await sm.EmitSignalAsync("M104", PlcSignalType.Level, true);

        var val1 = plcManager.GetTagValue("PLC1", "M104")?.CurrentValue;
        if (val1 is not bool b1 || !b1)
        {
            throw new Exception($"Level signal active value must be true, got {val1}");
        }

        // Chờ sau 75ms: Level không được tự động hạ mà phải giữ nguyên True!
        await Task.Delay(75);
        var val2 = plcManager.GetTagValue("PLC1", "M104")?.CurrentValue;
        if (val2 is not bool b2 || !b2)
        {
            throw new Exception($"Level signal must sustain true, got {val2}");
        }

        // Hạ mức chủ động
        await sm.EmitSignalAsync("M104", PlcSignalType.Level, false);
        var val3 = plcManager.GetTagValue("PLC1", "M104")?.CurrentValue;
        if (val3 is not bool b3 || b3)
        {
            throw new Exception($"Level signal inactive value must be false, got {val3}");
        }

        Console.WriteLine("    [OK] Tín hiệu Level duy trì trạng thái liên tục không bị mất.");
    }

    private static void Test_PlcManagerViewModelSignalTypes()
    {
        Console.WriteLine("--> Test 5: PlcManagerViewModel AvailableSignalTypes List Integrity");

        using var plcManager = CreateConnectedSimulationPlcManager("PLC1");
        var vm = new PlcManagerViewModel(plcManager);

        if (vm.AvailableSignalTypes == null || vm.AvailableSignalTypes.Count < 3)
            throw new Exception("AvailableSignalTypes must contain at least 3 options");

        bool hasLevel = false;
        bool hasRising = false;
        bool hasFalling = false;

        foreach (var opt in vm.AvailableSignalTypes)
        {
            if (opt.Value == PlcSignalType.Level) hasLevel = true;
            if (opt.Value == PlcSignalType.RisingEdge) hasRising = true;
            if (opt.Value == PlcSignalType.FallingEdge) hasFalling = true;
        }

        if (!hasLevel || !hasRising || !hasFalling)
            throw new Exception("Missing one or more required signal types in AvailableSignalTypes");

        Console.WriteLine("    [OK] AvailableSignalTypes cung cấp đầy đủ Level, Rising Edge và Falling Edge.");
    }
}

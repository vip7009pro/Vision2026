using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

public static class PlcResetQueueAndHmiTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> RUNNING PLC RESET QUEUE & HMI PERSISTENCE TESTS <<<");
        Console.WriteLine("==================================================================");

        Test_PlcInputSignalDetector_RisingEdge();
        Test_PlcInputSignalDetector_FallingEdge();
        Test_PlcInputSignalDetector_LongPress();
        Test_IndustrialHandshakeConfig_ResetQueueSerialization();
        Test_GlobalAppSettings_HmiPathPersistence();
        Test_BidirectionalResetQueue_WpfToPlcAndPlcToWpf_NoEchoLoopAsync().GetAwaiter().GetResult();

        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> ALL 6/6 PLC RESET QUEUE & HMI TESTS PASSED SUCCESSFULLY! <<<");
        Console.WriteLine("==================================================================");
    }

    private static void Test_PlcInputSignalDetector_RisingEdge()
    {
        Console.WriteLine("\n[1] Testing PlcInputSignalDetector - Rising Edge...");
        int triggerCount = 0;
        using var detector = new PlcInputSignalDetector
        {
            SignalType = PlcInputSignalType.RisingEdge,
            DurationMs = 50
        };
        detector.OnTriggered += (s, e) => triggerCount++;

        // Initial state 0
        detector.ProcessSample(false);
        AssertEqual(0, triggerCount, "Initial sample 0 should not trigger");

        // Edge 0 -> 1 (Rising edge!)
        detector.ProcessSample(true);
        AssertEqual(1, triggerCount, "0 -> 1 transition must trigger Rising Edge");

        // Holding 1 -> 1
        detector.ProcessSample(true);
        AssertEqual(1, triggerCount, "Holding 1 should not retrigger Rising Edge");

        // Release 1 -> 0
        detector.ProcessSample(false);
        AssertEqual(1, triggerCount, "Falling transition should not trigger Rising Edge detector");

        // Wait debounce lockout
        Thread.Sleep(60);

        // Second rising edge 0 -> 1
        detector.ProcessSample(true);
        AssertEqual(2, triggerCount, "Subsequent rising edge after debounce should trigger");
        Console.WriteLine(" -> PASSED: Rising Edge detected accurately with debounce.");
    }

    private static void Test_PlcInputSignalDetector_FallingEdge()
    {
        Console.WriteLine("\n[2] Testing PlcInputSignalDetector - Falling Edge...");
        int triggerCount = 0;
        using var detector = new PlcInputSignalDetector
        {
            SignalType = PlcInputSignalType.FallingEdge,
            DurationMs = 50
        };
        detector.OnTriggered += (s, e) => triggerCount++;

        // Initial state 1
        detector.ProcessSample(true);
        AssertEqual(0, triggerCount, "Initial sample 1 should not trigger");

        // Transition 1 -> 0 (Falling edge!)
        detector.ProcessSample(false);
        AssertEqual(1, triggerCount, "1 -> 0 transition must trigger Falling Edge");

        // Holding 0 -> 0
        detector.ProcessSample(false);
        AssertEqual(1, triggerCount, "Holding 0 should not retrigger Falling Edge");

        // Transition 0 -> 1
        detector.ProcessSample(true);
        AssertEqual(1, triggerCount, "Rising transition should not trigger Falling Edge detector");

        Thread.Sleep(60);

        // Second falling edge 1 -> 0
        detector.ProcessSample(false);
        AssertEqual(2, triggerCount, "Subsequent falling edge after debounce should trigger");
        Console.WriteLine(" -> PASSED: Falling Edge detected accurately with debounce.");
    }

    private static void Test_PlcInputSignalDetector_LongPress()
    {
        Console.WriteLine("\n[3] Testing PlcInputSignalDetector - Long Press...");
        int triggerCount = 0;
        using var detector = new PlcInputSignalDetector
        {
            SignalType = PlcInputSignalType.LongPress,
            DurationMs = 150 // 150ms required
        };
        detector.OnTriggered += (s, e) => triggerCount++;

        // Case A: Short pulse (50ms) - should NOT trigger
        detector.ProcessSample(false);
        detector.ProcessSample(true);
        Thread.Sleep(50);
        detector.ProcessSample(false);
        AssertEqual(0, triggerCount, "Short pulse (< 150ms) must not trigger Long Press");

        // Case B: Full long press (held for 250ms) - should trigger via timer or sample
        detector.ProcessSample(true);
        Thread.Sleep(250); // Held >= 150ms
        AssertEqual(1, triggerCount, "Long press >= 150ms must trigger once");

        // Still holding at 300ms - should NOT trigger again
        detector.ProcessSample(true);
        AssertEqual(1, triggerCount, "Continuing to hold must not retrigger Long Press");

        // Release signal
        detector.ProcessSample(false);
        AssertEqual(1, triggerCount, "Release must not trigger");

        // Another long press
        Thread.Sleep(50);
        detector.ProcessSample(true);
        Thread.Sleep(250);
        AssertEqual(2, triggerCount, "New long press after release must trigger a second time");
        detector.ProcessSample(false);

        Console.WriteLine(" -> PASSED: Long Press timing and debounce verified.");
    }

    private static void Test_IndustrialHandshakeConfig_ResetQueueSerialization()
    {
        Console.WriteLine("\n[4] Testing IndustrialHandshakeConfig - Reset Queue Serialization & Defaults...");
        var config = new IndustrialHandshakeConfig();
        config.ApplyFx5uDefaults();

        AssertEqual("M106", config.ResetQueueTagName, "FX5U default ResetQueueTagName should be M106");
        AssertEqual(PlcInputSignalType.RisingEdge, config.ResetQueueSignalType, "FX5U default ResetQueueSignalType should be RisingEdge");
        AssertEqual(50, config.ResetQueueDurationMs, "FX5U default ResetQueueDurationMs should be 50ms");

        var standardConfig = new IndustrialHandshakeConfig();
        standardConfig.ApplyStandardIoDefaults();
        AssertEqual("X2_ResetQueue", standardConfig.ResetQueueTagName, "Standard IO default ResetQueueTagName should be X2_ResetQueue");

        // Custom config serialization test
        config.ResetQueueTagName = "D200.5";
        config.ResetQueueSignalType = PlcInputSignalType.LongPress;
        config.ResetQueueDurationMs = 1500;

        string json = JsonSerializer.Serialize(config);
        var restored = JsonSerializer.Deserialize<IndustrialHandshakeConfig>(json);

        AssertEqual("D200.5", restored?.ResetQueueTagName, "Restored ResetQueueTagName must match");
        AssertEqual(PlcInputSignalType.LongPress, restored?.ResetQueueSignalType, "Restored ResetQueueSignalType must match");
        AssertEqual(1500, restored?.ResetQueueDurationMs, "Restored ResetQueueDurationMs must match");

        // Test CopyFrom
        var target = new IndustrialHandshakeConfig();
        target.CopyFrom(config);
        AssertEqual("D200.5", target.ResetQueueTagName, "Copied ResetQueueTagName must match");
        AssertEqual(PlcInputSignalType.LongPress, target.ResetQueueSignalType, "Copied ResetQueueSignalType must match");
        AssertEqual(1500, target.ResetQueueDurationMs, "Copied ResetQueueDurationMs must match");

        Console.WriteLine(" -> PASSED: IndustrialHandshakeConfig ResetQueue properties correctly persisted.");
    }

    private static void Test_GlobalAppSettings_HmiPathPersistence()
    {
        Console.WriteLine("\n[5] Testing GlobalAppSettings - ToolEditorSelectedHmiPath...");
        var settings = new GlobalAppSettings();
        AssertEqual(string.Empty, settings.ToolEditorSelectedHmiPath, "Default ToolEditorSelectedHmiPath should be empty");

        settings.ToolEditorSelectedHmiPath = @"C:\VisionApp\HmiScreens\MainLine.hmi";
        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<GlobalAppSettings>(json);

        AssertEqual(@"C:\VisionApp\HmiScreens\MainLine.hmi", restored?.ToolEditorSelectedHmiPath, "Persisted ToolEditorSelectedHmiPath must match");
        Console.WriteLine(" -> PASSED: GlobalAppSettings ToolEditorSelectedHmiPath persisted properly.");
    }

    private static async Task Test_BidirectionalResetQueue_WpfToPlcAndPlcToWpf_NoEchoLoopAsync()
    {
        Console.WriteLine("\n[6] Testing Bidirectional Reset Queue (WPF <-> PLC) & Self-Echo Suppression...");

        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        try
        {
            using var plcManager = new PlcManagerService(tempConfigPath);
            const string plcId = "PLC_BIDIR";
            var plc = new PlcModel
            {
                Id = plcId,
                Name = plcId,
                DriverType = PlcDriverType.Mitsubishi,
                IPAddress = "127.0.0.1",
                Port = 5008,
                Enabled = true,
                State = PlcConnectionState.Connected
            };
            var tags = new[]
            {
                new PlcTag { Id = "T_M101", PlcId = plcId, Name = "M101", Address = "M101", DataType = PlcDataType.Bool },
                new PlcTag { Id = "T_M106", PlcId = plcId, Name = "M106", Address = "M106", DataType = PlcDataType.Bool },
                new PlcTag { Id = "T_X2", PlcId = plcId, Name = "X2_ResetQueue", Address = "X2_ResetQueue", DataType = PlcDataType.Bool }
            };
            plcManager.LoadConfig(new[] { plc }, tags);
            var driver = (MitsubishiDriver)plcManager.GetDriver(plcId)!;
            driver.ForceSimulationMode = true;

            using var sm = new IndustrialHandshakeStateMachine(plcManager, plcId)
            {
                PlcId = plcId,
                IsEnabled = true,
                ReadyTagName = "M101",
                ResetQueueTagName = "M106",
                ResetQueueSignalType = PlcInputSignalType.RisingEdge,
                ResetQueueDurationMs = 50,
                PulseDurationMs = 20
            };

            var vm = new ToolEditorViewModel();
            InjectField(vm, "_plcManagerService", plcManager);
            InjectField(vm, "_handshakeStateMachine", sm);

            int plcResetCallbackCount = 0;
            sm.OnResetQueueRequested += (_, _) =>
            {
                plcResetCallbackCount++;
                vm.ResetQueueAndCounters();
            };

            var m106Events = new List<bool>();
            var m106Debug = new List<string>();
            var x2Events = new List<bool>();
            var testSw = System.Diagnostics.Stopwatch.StartNew();
            plcManager.OnTagChanged += (_, e) =>
            {
                if (!string.Equals(e.PlcId, plcId, StringComparison.OrdinalIgnoreCase)) return;
                if (string.Equals(e.TagName, "M106", StringComparison.OrdinalIgnoreCase) && e.NewValue is bool b1)
                {
                    lock (m106Events)
                    {
                        m106Events.Add(b1);
                        m106Debug.Add($"{e.PlcId}:{e.OldValue}->{b1}@{testSw.ElapsedMilliseconds}ms");
                    }
                }
                else if (string.Equals(e.TagName, "X2_ResetQueue", StringComparison.OrdinalIgnoreCase) && e.NewValue is bool b2)
                {
                    lock (x2Events) x2Events.Add(b2);
                }
            };

            // Thiết lập trạng thái tĩnh ban đầu M106 = false
            sm.HandleResetQueueTagSample("M106", false);

            // Seed dữ liệu hàng đợi/đếm trên ToolEditor
            using var okMat = new Mat(20, 20, MatType.CV_8UC3, Scalar.All(100));
            var res = new InspectionResult { Pass = true };
            vm.IsRunningFolderFlow = true;
            vm.PushRecentPartInspectionResult(res, okMat);
            vm.RecordContinuousPartCompleted(res, updateStatsNow: true);
            AssertEqual(1, vm.RecentPartsTotalCount, "Phải có 1 item trước khi Reset");
            AssertEqual(1, vm.ProcessedImageCount, "ProcessedImageCount phải là 1 trước khi Reset");

            // --- CASE A: Khi đang Chạy liên tục (PlcPollingEngine chạy thật song song) -> WPF bấm Reset Queue ---
            // Phải phát xung M106 (true -> false) sang PLC, xóa sạch queue, đồng thời ZERO self-echo dù Polling đọc lại M106!
            bool sentRisingContinuous = await vm.ExecuteResetSessionAndQueueCoreAsync();
            await sm.SetReadyAsync();

            AssertEqual(true, sentRisingContinuous, "Bấm Reset khi đang chạy liên tục phải gửi được xung qua M106");
            AssertEqual(0, vm.RecentPartsTotalCount, "Queue RecentParts phải về 0 sau Reset");
            AssertEqual(0, vm.ProcessedImageCount, "ProcessedImageCount phải về 0 sau Reset");
            AssertEqual(0, plcResetCallbackCount, "Tự phát xung M106 từ WPF KHÔNG được tự kích hoạt ngược OnResetQueueRequested");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1000)
            {
                lock (m106Events)
                {
                    if (m106Events.Contains(true) && m106Events[^1] == false &&
                        plcManager.GetTagValue(plcId, "M106")?.CurrentValue is bool cur && !cur)
                    {
                        break;
                    }
                }
                await Task.Delay(15);
            }

            lock (m106Events)
            {
                AssertEqual(true, m106Events.Contains(true), $"M106 phải ghi mức true khi Reset (actual: [{string.Join(", ", m106Events)}])");
                AssertEqual(false, m106Events[^1], $"M106 phải tự hạ về false sau xung (actual: [{string.Join(", ", m106Events)}])");
            }
            AssertEqual(false, Convert.ToBoolean(plcManager.GetTagValue(plcId, "M106")?.CurrentValue ?? true),
                "M106 trong PLC cache phải về false sau độ rộng xung dù có SetReadyAsync xen giữa");
            AssertEqual(0, plcResetCallbackCount, "Sau khi Polling & xung M106 hạ về false vẫn không được self-echo");

            // Chờ hết cửa sổ chống echo rồi kiểm chứng exact write sequence (tạm dừng polling nền)
            vm.IsRunningFolderFlow = false;
            await Task.Delay(450);

            lock (m106Events) { m106Events.Clear(); m106Debug.Clear(); }
            vm.PushRecentPartInspectionResult(res, okMat);
            bool sentExact = await vm.ExecuteResetSessionAndQueueCoreAsync();
            AssertEqual(true, sentExact, "Bấm Reset phải gửi đúng xung RisingEdge M106");
            sw.Restart();
            while (sw.ElapsedMilliseconds < 1000)
            {
                lock (m106Events)
                {
                    if (m106Events.Count >= 2 && m106Events[^1] == false) break;
                }
                await Task.Delay(15);
            }
            lock (m106Events)
            {
                string detail = string.Join(", ", m106Debug);
                AssertEqual(2, m106Events.Count, $"M106 (không polling nhiễu) phải ghi đúng 2 nhịp [true, false] (actual: [{detail}])");
                AssertEqual(true, m106Events[0], "Nhịp đầu xung RisingEdge M106 phải là true");
                AssertEqual(false, m106Events[1], "Nhịp sau xung RisingEdge M106 phải tự hạ về false");
            }

            // --- CASE B: Chờ qua cửa sổ chống echo (450ms), PLC chủ động gửi xung M106 (0 -> 1) -> WPF phải reset mà không phát lại ---
            await Task.Delay(450);
            vm.PushRecentPartInspectionResult(res, okMat);
            AssertEqual(1, vm.RecentPartsTotalCount, "Phải seed lại được 1 item sau reset");

            int eventsBeforePlcTrigger;
            lock (m106Events) eventsBeforePlcTrigger = m106Events.Count;

            await plcManager.WriteTagValueAsync(plcId, "M106", true);
            AssertEqual(1, plcResetCallbackCount, "Khi PLC kích M106 sau cửa sổ suppress, WPF phải nhận được OnResetQueueRequested");
            AssertEqual(0, vm.RecentPartsTotalCount, "WPF phải xóa sạch queue khi PLC yêu cầu Reset");

            await Task.Delay(80);
            lock (m106Events)
            {
                AssertEqual(eventsBeforePlcTrigger + 1, m106Events.Count,
                    "Khi PLC reset WPF, WPF không được phát ngược lại xung M106 sang PLC (chống loop)");
            }
            await plcManager.WriteTagValueAsync(plcId, "M106", false);

            // --- CASE C: Kiểu FallingEdge (tĩnh true -> xung false -> trả true) trên tag X2_ResetQueue ---
            lock (x2Events) x2Events.Clear();
            sm.ResetQueueTagName = "X2_ResetQueue";
            sm.ResetQueueSignalType = PlcInputSignalType.FallingEdge;
            sm.ResetQueueDurationMs = 40;
            sm.HandleResetQueueTagSample("X2_ResetQueue", true);

            bool sentFalling = await vm.ExecuteResetSessionAndQueueCoreAsync();
            AssertEqual(true, sentFalling, "FallingEdge ResetQueue phải phát được xung qua X2_ResetQueue");
            AssertEqual(1, plcResetCallbackCount, "FallingEdge từ WPF không được tự kích hoạt ngược lại OnResetQueueRequested");

            sw.Restart();
            while (sw.ElapsedMilliseconds < 1000)
            {
                lock (x2Events)
                {
                    if (x2Events.Count >= 2 && x2Events[^1] == true) break;
                }
                await Task.Delay(15);
            }
            lock (x2Events)
            {
                string x2Detail = string.Join(", ", x2Events);
                AssertEqual(2, x2Events.Count, $"X2_ResetQueue FallingEdge phải ghi [false, true] (actual: [{x2Detail}])");
                AssertEqual(false, x2Events[0], "Nhịp đầu FallingEdge phải kéo xuống false");
                AssertEqual(true, x2Events[1], "Nhịp sau FallingEdge phải trả về mức tĩnh true");
            }
            AssertEqual(1, plcResetCallbackCount, "Sau khi X2_ResetQueue trả về true vẫn không được self-echo");

            // --- CASE D: Điều kiện biên (Tag rỗng / Handshake tắt / PLC ngắt kết nối) ---
            vm.PushRecentPartInspectionResult(res, okMat);
            sm.ResetQueueTagName = "";
            AssertEqual(false, await vm.ExecuteResetSessionAndQueueCoreAsync(), "Tag rỗng không được gửi xuống PLC");
            AssertEqual(0, vm.RecentPartsTotalCount, "Dù tag rỗng vẫn phải reset queue cục bộ trên WPF");

            sm.ResetQueueTagName = "M106";
            sm.IsEnabled = false;
            AssertEqual(false, await vm.ExecuteResetSessionAndQueueCoreAsync(), "Khi Handshake tắt không được gửi xuống PLC");

            sm.IsEnabled = true;
            driver.ForceSimulationMode = false;
            plc.State = PlcConnectionState.Disconnected;
            AssertEqual(false, await vm.ExecuteResetSessionAndQueueCoreAsync(), "Khi PLC mất kết nối không được gửi xuống PLC");

            vm.IsRunningFolderFlow = false;
            Console.WriteLine(" -> PASSED: Bidirectional Reset Queue (WPF <-> PLC) & self-echo suppression verified 100%.");
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
            try { File.Delete(tempConfigPath + ".backup"); } catch { }
        }
    }

    private static void InjectField(object target, string fieldName, object? value)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }
        if (field is null) throw new Exception($"InjectField: không tìm thấy '{fieldName}'");
        field.SetValue(target, value);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!object.Equals(expected, actual))
        {
            throw new Exception($"[ASSERTION FAILED] {message}. Expected: {expected}, Actual: {actual}");
        }
    }
}

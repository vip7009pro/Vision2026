using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

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

        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> ALL PLC RESET QUEUE & HMI TESTS PASSED SUCCESSFULLY! <<<");
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

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!object.Equals(expected, actual))
        {
            throw new Exception($"[ASSERTION FAILED] {message}. Expected: {expected}, Actual: {actual}");
        }
    }
}

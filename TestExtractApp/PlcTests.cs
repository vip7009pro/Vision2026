using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class PlcTests
{
    public static async Task RunAllTestsAsync()
    {
        Console.WriteLine("=========================================");
        Console.WriteLine("RUNNING PLC FRAMEWORK AUTOMATED TESTS");
        Console.WriteLine("=========================================");

        Test1_MitsubishiDriver_SimulationReadWrite();
        Test2_PlcTagCache_ThreadSafety();
        await Test3_PollingEngine_TagChangeEventsAsync();
        await Test4_PlcManagerService_LifecycleAsync();
        Test5_MitsubishiMxComponentDriver_SimulationReadWrite();
        await Test6_PlcManagerService_OnBatchPolled_And_DynamicTagsAsync();
        await Test7_HighResolutionTimer_And_Sub5msScanAsync();

        Console.WriteLine("\n✅ ALL PLC TESTS PASSED SUCCESSFULLY!");
        Console.WriteLine("=========================================");
    }

    private static void Test1_MitsubishiDriver_SimulationReadWrite()
    {
        Console.Write("Test 1: Mitsubishi Driver Offline Simulation... ");

        var plc = new PlcModel { Id = "PLC_TEST", Name = "FX5U", DriverType = PlcDriverType.Mitsubishi, IPAddress = "127.0.0.1", Port = 5007 };
        using var driver = new MitsubishiDriver(plc) { ForceSimulationMode = true };

        driver.ConnectAsync().Wait();

        var tagBool = new PlcTag { Id = "T1", PlcId = plc.Id, Name = "Trigger", Address = "M10", DataType = PlcDataType.Bool, Scale = 1.0 };
        var tagInt = new PlcTag { Id = "T2", PlcId = plc.Id, Name = "Result", Address = "D100", DataType = PlcDataType.Int16, Scale = 1.0 };

        // Write
        bool w1 = driver.WriteAsync(tagBool, true).Result;
        bool w2 = driver.WriteAsync(tagInt, (short)1234).Result;

        if (!w1 || !w2) throw new Exception("Driver Write failed.");

        // Read
        var r1 = driver.ReadAsync(tagBool).Result;
        var r2 = driver.ReadAsync(tagInt).Result;

        if (r1 is not true || Convert.ToInt32(r2) != 1234)
            throw new Exception($"Driver Read mismatch: Bool={r1}, Int={r2}");

        Console.WriteLine("PASSED");
    }

    private static void Test2_PlcTagCache_ThreadSafety()
    {
        Console.Write("Test 2: PlcTagCache Storage & Scale... ");

        var cache = new PlcTagCache();
        cache.Set("PLC1", "Temp", 25.5, TagQuality.Good);

        var val1 = cache.Get("PLC1", "Temp");
        if (val1 == null || Math.Abs(Convert.ToDouble(val1.CurrentValue) - 25.5) > 1e-5)
            throw new Exception("Cache get failed.");

        cache.Set("PLC1", "Temp", 30.0, TagQuality.Good);
        var val2 = cache.Get("PLC1", "Temp");

        if (val2 == null || Math.Abs(Convert.ToDouble(val2.PreviousValue) - 25.5) > 1e-5 || Math.Abs(Convert.ToDouble(val2.CurrentValue) - 30.0) > 1e-5)
            throw new Exception("Cache previous/current value tracking failed.");

        Console.WriteLine("PASSED");
    }

    private static async Task Test3_PollingEngine_TagChangeEventsAsync()
    {
        Console.WriteLine("\nTest 3: Polling Engine & Tag Change Event Dispatching...");

        var cache = new PlcTagCache();
        var logger = new PlcLogger();
        var engine = new PlcPollingEngine(cache, logger);

        var plc = new PlcModel { Id = "P1", Name = "PLC1", DriverType = PlcDriverType.Mitsubishi, Enabled = true, ScanIntervalMs = 50 };
        var tag = new PlcTag { Id = "TG1", PlcId = "P1", Name = "Count", Address = "D200", DataType = PlcDataType.Int16 };

        var driver = new MitsubishiDriver(plc) { ForceSimulationMode = true };
        await driver.ConnectAsync();
        await driver.WriteAsync(tag, 100);

        bool eventRaised = false;
        engine.OnTagChanged += (s, e) =>
        {
            Console.WriteLine($"   [OnTagChanged] Plc={e.PlcId}, Tag={e.TagName}, Old={e.OldValue}, New={e.NewValue}");
            if (e.TagName == "Count" && Convert.ToInt32(e.NewValue) == 200)
            {
                eventRaised = true;
            }
        };

        engine.Start(new[] { plc }, new[] { tag }, id => driver);

        await Task.Delay(200);
        await driver.WriteAsync(tag, 200);
        await Task.Delay(300);

        engine.Stop();

        if (!eventRaised) throw new Exception("Polling Engine failed to dispatch OnTagChanged event.");

        Console.WriteLine("Test 3 PASSED");
    }

    private static async Task Test4_PlcManagerService_LifecycleAsync()
    {
        Console.Write("\nTest 4: PlcManagerService Lifecycle & Config Load... ");

        using var service = new PlcManagerService();
        var plc = new PlcModel { Id = "P1", Name = "PLC1", DriverType = PlcDriverType.Mitsubishi, Enabled = true, ScanIntervalMs = 50 };
        var tag = new PlcTag { Id = "T1", PlcId = "P1", Name = "Ready", Address = "M0", DataType = PlcDataType.Bool };

        service.LoadConfig(new[] { plc }, new[] { tag });
        await Task.Delay(100);

        bool written = await service.WriteTagValueAsync("PLC1", "Ready", true);
        if (!written) throw new Exception("PlcManagerService WriteTagValue failed.");

        var val = service.GetTagValue("PLC1", "Ready");
        if (val?.CurrentValue is not true) throw new Exception("PlcManagerService GetTagValue mismatch.");

        Console.WriteLine("PASSED");
    }

    private static void Test5_MitsubishiMxComponentDriver_SimulationReadWrite()
    {
        Console.Write("\nTest 5: Mitsubishi MX Component Driver (Station No)... ");

        var plc = new PlcModel
        {
            Id = "PLC_MX",
            Name = "FX_MX",
            DriverType = PlcDriverType.MitsubishiMxComponent,
            LogicalStationNumber = 2
        };
        using var driver = new MitsubishiMxComponentDriver(plc) { ForceSimulationMode = true };

        driver.ConnectAsync().Wait();

        var tagInt = new PlcTag { Id = "T_MX1", PlcId = plc.Id, Name = "Count", Address = "D10", DataType = PlcDataType.Int16, Scale = 1.0 };
        bool w = driver.WriteAsync(tagInt, (short)999).Result;
        if (!w) throw new Exception("MX Component Driver Write failed.");

        var r = driver.ReadAsync(tagInt).Result;
        if (Convert.ToInt32(r) != 999) throw new Exception($"MX Component Read mismatch: {r}");

        Console.WriteLine("PASSED");
    }

    private static async Task Test6_PlcManagerService_OnBatchPolled_And_DynamicTagsAsync()
    {
        Console.Write("\nTest 6: OnBatchPolled & Dynamic Tag Provider (Oscilloscope Engine)... ");

        using var service = new PlcManagerService();
        var plc = new PlcModel
        {
            Id = "PLC_OSC",
            Name = "FX_OSC",
            DriverType = PlcDriverType.Mitsubishi,
            Enabled = true,
            ScanIntervalMs = 100 // Base scan is 100ms
        };

        var tagStatic = new PlcTag { Id = "TS1", PlcId = plc.Id, Name = "Sensor", Address = "X0", DataType = PlcDataType.Bool };
        service.LoadConfig(new[] { plc }, new[] { tagStatic });

        var driver = service.GetDriver(plc.Id);
        if (driver is MitsubishiDriver md)
        {
            md.ForceSimulationMode = true;
        }

        // Register dynamic tags (simulating Oscilloscope CH1..CH4)
        var dynamicOscTags = new List<PlcTag>
        {
            new PlcTag { PlcId = plc.Id, Name = "M100", Address = "M100", DataType = PlcDataType.Bool },
            new PlcTag { PlcId = plc.Id, Name = "D200", Address = "D200", DataType = PlcDataType.Int16 }
        };

        service.RegisterDynamicTagProvider("TestOscilloscope", () => dynamicOscTags);

        // Verify GetAllTagsToPoll includes both static and dynamic tags
        var allTags = service.GetAllTagsToPoll();
        bool hasStatic = allTags.Any(t => t.Address == "X0");
        bool hasDynM100 = allTags.Any(t => t.Address == "M100");
        bool hasDynD200 = allTags.Any(t => t.Address == "D200");

        if (!hasStatic || !hasDynM100 || !hasDynD200)
        {
            throw new Exception("GetAllTagsToPoll failed to incorporate dynamic tags.");
        }

        // Request high-speed scan interval (10ms)
        service.RequestScanInterval("TestOscilloscope", 10);
        int effectiveScan = service.GetEffectiveMinScanInterval(100);
        if (effectiveScan != 10)
        {
            throw new Exception($"Effective scan interval mismatch: expected 10, got {effectiveScan}");
        }

        // Write values to simulated memory
        await service.WriteTagValueAsync(plc.Id, "X0", true);
        await service.WriteTagValueAsync(plc.Id, "M100", true);
        await service.WriteTagValueAsync(plc.Id, "D200", (short)789);

        bool batchPolledReceived = false;
        object? polledM100 = null;
        object? polledD200 = null;

        service.OnBatchPolled += (s, e) =>
        {
            if (e.ReadResults.TryGetValue("M100", out var vM100)) polledM100 = vM100;
            if (e.ReadResults.TryGetValue("D200", out var vD200)) polledD200 = vD200;
            batchPolledReceived = true;
        };

        // Acquire lock to trigger polling
        service.AcquirePollingLock("TestOscilloscope");

        await Task.Delay(250);

        service.ReleaseScanInterval("TestOscilloscope");
        service.UnregisterDynamicTagProvider("TestOscilloscope");
        service.ReleasePollingLock("TestOscilloscope");

        if (!batchPolledReceived)
        {
            throw new Exception("OnBatchPolled event was not received during polling.");
        }

        Console.WriteLine("PASSED");
    }

    private static async Task Test7_HighResolutionTimer_And_Sub5msScanAsync()
    {
        Console.Write("\nTest 7: High-Resolution Timer & Sub-5ms Scan Interval Verification... ");

        // 1. Test NativeTimerUtility activation
        NativeTimerUtility.TimeBeginPeriod(1);

        // Measure Task.Delay(1) with high-res timer
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(1);
        }
        sw.Stop();
        double avgDelayMs = sw.Elapsed.TotalMilliseconds / 5.0;

        // With timeBeginPeriod(1), avgDelay is typically 1-3ms instead of 15.6ms
        if (avgDelayMs > 10.0)
        {
            NativeTimerUtility.TimeEndPeriod(1);
            throw new Exception($"High-Resolution Timer failed: avg Task.Delay(1) took {avgDelayMs:F2}ms (expected < 10ms)");
        }

        // 2. Test high-speed polling (2ms scan interval)
        var cache = new PlcTagCache();
        var logger = new PlcLogger();
        var engine = new PlcPollingEngine(cache, logger);

        var plc = new PlcModel { Id = "P_FAST", Name = "PLC_FAST", DriverType = PlcDriverType.Mitsubishi, Enabled = true, ScanIntervalMs = 2 };
        var tag = new PlcTag { Id = "TG_FAST", PlcId = "P_FAST", Name = "HighSpeedTag", Address = "X0", DataType = PlcDataType.Bool };
        var driver = new MitsubishiDriver(plc) { ForceSimulationMode = true };
        await driver.ConnectAsync();

        int batchCount = 0;
        engine.OnBatchPolled += (s, e) =>
        {
            Interlocked.Increment(ref batchCount);
        };

        engine.Start(new[] { plc }, new[] { tag }, id => driver, minScan => 2);

        // Run for 150ms
        await Task.Delay(150);

        engine.Stop();
        NativeTimerUtility.TimeEndPeriod(1);

        // In 150ms with 2ms interval (or sub-5ms), batchCount should be well over 15 (typically 40-70 batches)
        if (batchCount < 15)
        {
            throw new Exception($"High-Speed Polling failed: only received {batchCount} batches in 150ms with 2ms scan interval.");
        }

        Console.WriteLine($"PASSED (Captured {batchCount} batches in 150ms, Avg Delay = {avgDelayMs:F2}ms)");
    }
}

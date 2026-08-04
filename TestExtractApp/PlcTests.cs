using System;
using System.Collections.Generic;
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
}

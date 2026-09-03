using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VisionInspectionApp.Application;
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
        await Test8_MitsubishiMcProtocol_SocketCommunication_And_CpuNameAsync();
        await Test9_ResultTransfer_PulseMode_And_LevelModeAsync();
        await Test10_PlcConnection_ManualDisconnect_And_CommandStatesAsync();
        await Test11_AutoConnectStartup_And_PollingReadinessAsync();
        await Test12_HandshakeStateMachine_NonBlocking_And_ImageSourceTimingAsync();
        await Test13_PlcResultTransferQueue_AsyncFifoAndZeroMainFlowLatencyAsync();
        await Test14_PlcDiagnosticService_SocketProbeAndReportAsync();

        Console.WriteLine("\n✅ ALL PLC TESTS PASSED SUCCESSFULLY!");
        Console.WriteLine("=========================================");
    }

    private static async Task Test13_PlcResultTransferQueue_AsyncFifoAndZeroMainFlowLatencyAsync()
    {
        Console.Write("Test 13: PlcResultTransferQueue Dedicated Async FIFO & 0ms Main Flow Latency... ");

        var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var plc = new PlcModel { Id = "PLC_Q", Name = "FX5U_Q", DriverType = PlcDriverType.Mitsubishi, IPAddress = "127.0.0.1", Port = 5007, Enabled = true };
        var tagY0 = new PlcTag { Id = "TQ1", PlcId = "PLC_Q", Name = "Y0_PULSE", Address = "Y0", DataType = PlcDataType.Bool };
        var tagD100 = new PlcTag { Id = "TQ2", PlcId = "PLC_Q", Name = "D100_VAL", Address = "D100", DataType = PlcDataType.Int16 };

        plcManager.LoadConfig(new[] { plc }, new[] { tagY0, tagD100 });
        var driver = (MitsubishiDriver)plcManager.GetDriver("PLC_Q")!;
        driver.ForceSimulationMode = true;
        await plcManager.ConnectAllAsync();

        var config = new VisionConfig
        {
            ResultTransfers = new List<ResultTransferDefinition>
            {
                new ResultTransferDefinition
                {
                    Name = "RT_Dedicated",
                    Items = new List<ResultTransferItem>
                    {
                        new ResultTransferItem { PlcId = "PLC_Q", TagName = "Y0_PULSE", Mode = ResultTransferMode.Pulse, PulseDurationMs = 50, ValueExpression = "TotalPassBit" },
                        new ResultTransferItem { PlcId = "PLC_Q", TagName = "D100_VAL", Mode = ResultTransferMode.Level, ValueExpression = "1234" }
                    }
                }
            }
        };

        var result = new InspectionResult { Pass = true };

        // 1. Đo thời gian Enqueue trên luồng kiểm tra chính (Phải < 2ms, không bao giờ chờ PLC)
        var swMain = System.Diagnostics.Stopwatch.StartNew();
        PlcResultTransferQueue.Enqueue(config, result, plcManager);
        swMain.Stop();

        if (swMain.ElapsedMilliseconds > 10)
            throw new Exception($"PlcResultTransferQueue.Enqueue took {swMain.ElapsedMilliseconds}ms, expected immediate return (< 10ms)");

        // 2. Chờ Background Worker xử lý truyền xong qua PLC
        await Task.Delay(100);

        var valD100 = plcManager.GetTagValue("PLC_Q", "D100_VAL")?.CurrentValue;
        if (Convert.ToInt32(valD100) != 1234)
            throw new Exception($"D100 value should be 1234, got {valD100}");

        int latestTiming = PlcResultTransferQueue.GetLatestTiming("RT_Dedicated");
        if (latestTiming <= 0)
            throw new Exception($"Expected latest timing > 0ms, got {latestTiming}ms");

        await plcManager.DisconnectAllAsync();
        plcManager.Dispose();

        Console.WriteLine($"PASSED (Main Flow Latency={swMain.ElapsedMilliseconds}ms, Background Execution Timing={latestTiming}ms)");
    }

    private static async Task Test12_HandshakeStateMachine_NonBlocking_And_ImageSourceTimingAsync()
    {
        Console.Write("Test 12: Handshake Non-Blocking When Tags Not Configured & ImageSource Timing... ");

        var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var plc = new PlcModel 
        { 
            Id = "PLC_TEST_HS", 
            Name = "FX5U_HS", 
            DriverType = PlcDriverType.Mitsubishi, 
            IPAddress = "127.0.0.1", 
            Port = 5007,
            Enabled = true 
        };
        // Chỉ có tag dữ liệu thông thường, KHÔNG CÓ tag handshake (Ready/Busy/Done/Pass/Ng/PlcAck)
        var tag = new PlcTag { Id = "T_DATA", PlcId = "PLC_TEST_HS", Name = "ProductCount", Address = "D100", DataType = PlcDataType.Int32 };

        plcManager.LoadConfig(new[] { plc }, new[] { tag });
        var driver = (MitsubishiDriver)plcManager.GetDriver("PLC_TEST_HS")!;
        driver.ForceSimulationMode = true;
        await plcManager.ConnectAllAsync();

        var hs = new IndustrialHandshakeStateMachine(plcManager, "PLC_TEST_HS");
        hs.HandshakeTimeoutMs = 500; // Ngưỡng timeout 500ms

        // 1. SetReadyAsync
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await hs.SetReadyAsync();
        sw.Stop();
        if (sw.ElapsedMilliseconds > 50)
            throw new Exception($"SetReadyAsync took {sw.ElapsedMilliseconds}ms, expected immediate bypass (< 50ms)");

        // 2. StartInspectionAsync
        sw.Restart();
        await hs.StartInspectionAsync();
        sw.Stop();
        if (sw.ElapsedMilliseconds > 50)
            throw new Exception($"StartInspectionAsync took {sw.ElapsedMilliseconds}ms, expected immediate bypass (< 50ms)");

        // 3. CompleteHandshakeAsync (Trước đây bị treo 500ms chờ X1_PlcAck timeout, bây giờ phải < 50ms)
        sw.Restart();
        bool hsResult = await hs.CompleteHandshakeAsync(true);
        sw.Stop();
        if (!hsResult)
            throw new Exception("CompleteHandshakeAsync should return true when tags are not configured (bypass mode)");
        if (sw.ElapsedMilliseconds > 50)
            throw new Exception($"CompleteHandshakeAsync took {sw.ElapsedMilliseconds}ms (hung on timeout!), expected immediate bypass (< 50ms)");

        await plcManager.DisconnectAllAsync();
        plcManager.Dispose();

        Console.WriteLine($"PASSED (Bypass executed in {sw.ElapsedMilliseconds}ms without 500ms timeout)");
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

        using var service = TestPlcConfigHelper.CreateIsolatedPlcManager();
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

        using var service = TestPlcConfigHelper.CreateIsolatedPlcManager();
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

        // Warmup timer
        await Task.Delay(1);

        // Measure Task.Delay(1) with high-res timer
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(1);
        }
        sw.Stop();
        double avgDelayMs = sw.Elapsed.TotalMilliseconds / 10.0;

        // With timeBeginPeriod(1), avgDelay is typically 1-4ms instead of 15.6ms (allow up to 25ms on busy CI/VM)
        if (avgDelayMs > 25.0)
        {
            NativeTimerUtility.TimeEndPeriod(1);
            throw new Exception($"High-Resolution Timer failed: avg Task.Delay(1) took {avgDelayMs:F2}ms (expected < 25ms)");
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

    private static async Task Test8_MitsubishiMcProtocol_SocketCommunication_And_CpuNameAsync()
    {
        Console.Write("\nTest 8: Mitsubishi MC Protocol 3E Real Socket Protocol & CPU Name... ");

        // 1. Start a local TCP server mimicking a Mitsubishi FX5U / Q Series PLC
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                byte[] header = new byte[15];

                while (true)
                {
                    int read = await stream.ReadAsync(header, 0, 15);
                    if (read < 15) break;

                    // header[7] and [8] is request data length
                    ushort reqLen = (ushort)(header[7] | (header[8] << 8));
                    int payloadLen = reqLen - 6; // Subtract CPU timer (2B), command (2B), subcmd (2B)
                    byte[] payload = new byte[payloadLen];
                    if (payloadLen > 0)
                    {
                        await stream.ReadAsync(payload, 0, payloadLen);
                    }

                    ushort command = (ushort)(header[11] | (header[12] << 8));
                    ushort subcommand = (ushort)(header[13] | (header[14] << 8));

                    if (command == 0x0101)
                    {
                        // Read CPU model name: Return 16 bytes ASCII "FX5U-32MT/ES    " + 2 bytes CPU code
                        byte[] respHeader = new byte[] {
                            0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                            0x14, 0x00, // Data Length = 20 (2B return code + 16B CPU + 2B code)
                            0x00, 0x00  // End code = 0 (Success)
                        };
                        byte[] cpuBytes = System.Text.Encoding.ASCII.GetBytes("FX5U-32MT/ES    ");
                        byte[] cpuCode = new byte[] { 0x10, 0x02 };
                        byte[] fullResp = respHeader.Concat(cpuBytes).Concat(cpuCode).ToArray();
                        await stream.WriteAsync(fullResp, 0, fullResp.Length);
                    }
                    else if (command == 0x0401 && subcommand == 0x0001)
                    {
                        // Bit Read: Return 1 bit ON (0x10)
                        byte[] respHeader = new byte[] {
                            0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                            0x03, 0x00, // Data Length = 3 (2B return code + 1B data)
                            0x00, 0x00, // End code = 0
                            0x10        // Bit ON
                        };
                        await stream.WriteAsync(respHeader, 0, respHeader.Length);
                    }
                    else if (command == 0x0401 && subcommand == 0x0000)
                    {
                        // Word Read: Return Word = 5678 (0x162E -> 2E 16)
                        byte[] respHeader = new byte[] {
                            0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                            0x04, 0x00, // Data Length = 4 (2B return code + 2B data)
                            0x00, 0x00, // End code = 0
                            0x2E, 0x16  // 5678
                        };
                        await stream.WriteAsync(respHeader, 0, respHeader.Length);
                    }
                    else if (command == 0x1401)
                    {
                        // Write response
                        byte[] respHeader = new byte[] {
                            0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                            0x02, 0x00, // Data Length = 2 (2B return code)
                            0x00, 0x00  // End code = 0
                        };
                        await stream.WriteAsync(respHeader, 0, respHeader.Length);
                    }
                }
            }
            catch { }
        });

        var plc = new PlcModel
        {
            Id = "PLC_MC_REAL",
            Name = "FX5U_REAL",
            DriverType = PlcDriverType.Mitsubishi,
            IPAddress = "127.0.0.1",
            Port = port
        };

        using var driver = new MitsubishiDriver(plc);
        bool connected = await driver.ConnectAsync();
        if (!connected) throw new Exception("Failed to connect to MC Protocol mock server.");

        if (!string.Equals(plc.CpuName, "FX5U-32MT/ES", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"CPU Name mismatch: expected 'FX5U-32MT/ES', got '{plc.CpuName}'");
        }

        // Test Bit Read (X0)
        var tagX0 = new PlcTag { PlcId = plc.Id, Name = "X0_Trigger", Address = "X0", DataType = PlcDataType.Bool };
        var tagD100 = new PlcTag { PlcId = plc.Id, Name = "D100_Data", Address = "D100", DataType = PlcDataType.Int16 };

        var rBit = await driver.ReadAsync(tagX0);
        if (rBit is not true) throw new Exception($"Bit Read failed: expected true, got {rBit}");

        var rWord = await driver.ReadAsync(tagD100);
        if (Convert.ToInt32(rWord) != 5678) throw new Exception($"Word Read failed: expected 5678, got {rWord}");

        // Test Bit Write & Word Write
        bool wBit = await driver.WriteAsync(tagX0, true);
        bool wWord = await driver.WriteAsync(tagD100, (short)9999);
        if (!wBit || !wWord) throw new Exception("Write failed over MC Protocol 3E socket.");

        await driver.DisconnectAsync();
        listener.Stop();

        Console.WriteLine($"PASSED (Connected & Identified CPU '{plc.CpuName}', Bit & Word Verified 100%)");
    }

    private static async Task Test9_ResultTransfer_PulseMode_And_LevelModeAsync()
    {
        Console.Write("Test 9: ResultTransfer Pulse Mode (Toggle & Auto-Restore) & Level Mode... ");

        var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var plc = new PlcModel { Id = "PLC1", Name = "PLC1", DriverType = PlcDriverType.Mitsubishi, IPAddress = "127.0.0.1", Port = 5007 };
        var tagY0 = new PlcTag { Id = "T1", PlcId = "PLC1", Name = "Y0_OK", Address = "Y0", DataType = PlcDataType.Bool };
        var tagY1 = new PlcTag { Id = "T2", PlcId = "PLC1", Name = "Y1_NG", Address = "Y1", DataType = PlcDataType.Bool };
        var tagY2 = new PlcTag { Id = "T3", PlcId = "PLC1", Name = "Y2_Level", Address = "Y2", DataType = PlcDataType.Bool };

        plcManager.LoadConfig(new[] { plc }, new[] { tagY0, tagY1, tagY2 });
        var driver = (MitsubishiDriver)plcManager.GetDriver("PLC1")!;
        driver.ForceSimulationMode = true;
        await plcManager.ConnectAllAsync();

        // 1. Khởi tạo giá trị ban đầu: Y0 = false, Y1 = true, Y2 = false
        await plcManager.WriteTagValueAsync("PLC1", "Y0_OK", false);
        await plcManager.WriteTagValueAsync("PLC1", "Y1_NG", true);
        await plcManager.WriteTagValueAsync("PLC1", "Y2_Level", false);

        var config = new VisionConfig
        {
            ResultTransfers = new List<ResultTransferDefinition>
            {
                new ResultTransferDefinition
                {
                    Name = "ResultTransfer1",
                    Items = new List<ResultTransferItem>
                    {
                        // Y0: Đang false -> Gửi xung 50ms: false -> true -> false
                        new ResultTransferItem { PlcId = "PLC1", TagName = "Y0_OK", ValueExpression = "TotalPassBit", Mode = ResultTransferMode.Pulse, PulseDurationMs = 50 },
                        // Y1: Đang true -> Gửi xung 50ms: true -> false -> true
                        new ResultTransferItem { PlcId = "PLC1", TagName = "Y1_NG", ValueExpression = "TotalFailBit", Mode = ResultTransferMode.Pulse, PulseDurationMs = 50 },
                        // Y2: Đang false -> Gửi level: false -> true (giữ nguyên)
                        new ResultTransferItem { PlcId = "PLC1", TagName = "Y2_Level", ValueExpression = "TotalPass", Mode = ResultTransferMode.Level }
                    }
                }
            }
        };

        var result = new InspectionResult { Pass = true };

        // 2. Kích hoạt ResultTransfer
        var swTest = System.Diagnostics.Stopwatch.StartNew();
        await PlcResultTransferRunner.ExecuteResultTransfersAsync(config, result, plcManager);
        swTest.Stop();

        // Kiểm tra runtime đã được ghi nhận vào NodeTimings
        if (!result.Timings.NodeTimings.TryGetValue("ResultTransfer1", out var rtMs))
            throw new Exception("ResultTransfer1 runtime was not recorded in result.Timings.NodeTimings!");
        if (swTest.ElapsedMilliseconds > 150)
            throw new Exception($"ExecuteResultTransfersAsync took {swTest.ElapsedMilliseconds}ms, should be non-blocking fast return (< 150ms)!");

        // Trong lúc đang ở khoảng giữa xung (sau 20ms)
        await Task.Delay(20);
        var y0DuringPulse = plcManager.GetTagValue("PLC1", "Y0_OK")?.CurrentValue;
        var y1DuringPulse = plcManager.GetTagValue("PLC1", "Y1_NG")?.CurrentValue;
        var y2Level = plcManager.GetTagValue("PLC1", "Y2_Level")?.CurrentValue;

        if (Convert.ToInt32(y0DuringPulse) != 1) throw new Exception($"Y0 during pulse should be 1/true, got {y0DuringPulse}");
        if (Convert.ToInt32(y1DuringPulse) != 0) throw new Exception($"Y1 during pulse should be 0/false, got {y1DuringPulse}");
        if (y2Level is not true) throw new Exception($"Y2 Level should be true, got {y2Level}");

        // Chờ hoàn thành xung (sau 60ms nữa, tổng > 50ms)
        await Task.Delay(60);

        var y0AfterPulse = plcManager.GetTagValue("PLC1", "Y0_OK")?.CurrentValue;
        var y1AfterPulse = plcManager.GetTagValue("PLC1", "Y1_NG")?.CurrentValue;
        var y2AfterLevel = plcManager.GetTagValue("PLC1", "Y2_Level")?.CurrentValue;

        if (Convert.ToInt32(y0AfterPulse) != 0) throw new Exception($"Y0 after pulse should restore to 0/false, got {y0AfterPulse}");
        if (Convert.ToInt32(y1AfterPulse) != 1) throw new Exception($"Y1 after pulse should restore to 1/true, got {y1AfterPulse}");
        if (y2AfterLevel is not true) throw new Exception($"Y2 Level should remain true, got {y2AfterLevel}");

        await plcManager.DisconnectAllAsync();
        plcManager.Dispose();

        Console.WriteLine($"PASSED (NodeTimings={rtMs}ms, Pulse Inversion & Non-blocking Auto-Restore verified 100%)");
    }

    private static async Task Test10_PlcConnection_ManualDisconnect_And_CommandStatesAsync()
    {
        Console.Write("Test 10: Manual Disconnect (No Auto-Reconnect) & Connection Command States... ");

        var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var plc = new PlcModel { Id = "PLC_TEST", Name = "PLC_TEST", DriverType = PlcDriverType.Mitsubishi, IPAddress = "127.0.0.1", Port = 5007 };
        var tag = new PlcTag { Id = "T1", PlcId = "PLC_TEST", Name = "D100", Address = "D100", DataType = PlcDataType.Int16 };

        plcManager.LoadConfig(new[] { plc }, new[] { tag });
        var driver = (MitsubishiDriver)plcManager.GetDriver("PLC_TEST")!;
        driver.ForceSimulationMode = true;

        // 1. Khởi động kết nối và Polling Engine
        await plcManager.ConnectAllAsync();
        plcManager.AcquirePollingLock("TestEngine");
        await Task.Delay(100);

        if (plc.State != PlcConnectionState.Connected)
        {
            throw new Exception($"PLC should be Connected, but was {plc.State}");
        }

        // 2. Người dùng chủ động bấm Ngắt Kết Nối
        await plcManager.DisconnectAllAsync();
        if (plc.State != PlcConnectionState.Disconnected || !plc.IsManuallyDisconnected)
        {
            throw new Exception($"PLC should be Disconnected and IsManuallyDisconnected=true after DisconnectAllAsync");
        }

        // 3. Đợi 250ms trong khi PollingEngine đang chạy nền
        await Task.Delay(250);

        // PollingEngine KHÔNG được tự ý kết nối lại khi PLC ở trạng thái Disconnected / IsManuallyDisconnected
        if (plc.State != PlcConnectionState.Disconnected)
        {
            throw new Exception($"PLC auto-reconnected unexpectedly after manual disconnect! Current State: {plc.State}");
        }

        // 4. Khi người dùng chủ động bấm Kết Nối lại
        await plcManager.ConnectAllAsync();
        if (plc.State != PlcConnectionState.Connected || plc.IsManuallyDisconnected)
        {
            throw new Exception($"PLC should be Connected and IsManuallyDisconnected=false after manual ConnectAllAsync, but State={plc.State}");
        }

        plcManager.ReleasePollingLock("TestEngine");
        plcManager.Dispose();

        Console.WriteLine("PASSED (Manual Disconnect respected, Auto-Reconnect prevented 100%)");
    }

    private static async Task Test11_AutoConnectStartup_And_PollingReadinessAsync()
    {
        Console.Write("Test 11: AutoConnectStartup Background Connection & Polling Readiness... ");

        var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        var plc = new PlcModel 
        { 
            Id = "PLC_AUTO", 
            Name = "FX5U_Auto", 
            DriverType = PlcDriverType.Mitsubishi, 
            IPAddress = "127.0.0.1", 
            Port = 5007,
            Enabled = true 
        };
        var tag = new PlcTag { Id = "T_AUTO", PlcId = "PLC_AUTO", Name = "Count_Auto", Address = "D500", DataType = PlcDataType.Int32 };

        plcManager.LoadConfig(new[] { plc }, new[] { tag });
        var driver = (MitsubishiDriver)plcManager.GetDriver("PLC_AUTO")!;
        driver.ForceSimulationMode = true;

        // 1. Kích hoạt AutoConnectStartup (như khi App vừa bật lên)
        await plcManager.AutoConnectStartupAsync();

        if (plc.State != PlcConnectionState.Connected)
        {
            throw new Exception($"PLC should be Connected automatically upon startup, but was {plc.State}");
        }

        if (!plcManager.IsPollingActive)
        {
            throw new Exception("Polling Engine should be Active automatically via 'AutoStartup' lock.");
        }

        // 2. Kiểm tra việc đọc ghi biến qua cache
        await Task.Delay(100);
        await plcManager.WriteTagValueAsync("PLC_AUTO", "Count_Auto", 123456);
        await Task.Delay(100);

        var val = plcManager.GetTagValue("PLC_AUTO", "Count_Auto");
        if (val == null || Convert.ToInt32(val.CurrentValue) != 123456)
        {
            throw new Exception($"Expected auto-polled tag value 123456, got {val?.CurrentValue}");
        }

        // 3. Người dùng ngắt kết nối
        await plcManager.DisconnectAllAsync();
        plcManager.ReleasePollingLock("AutoStartup");
        plcManager.Dispose();

        Console.WriteLine("PASSED (Auto-connect on Startup & Polling Lock operational 100%)");
    }

    private static async Task Test14_PlcDiagnosticService_SocketProbeAndReportAsync()
    {
        Console.Write("Test 14: PlcDiagnosticService Network Ping, Socket Probe & Hex Log... ");

        int port = 5098;
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
        listener.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                byte[] buffer = new byte[64];
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read >= 11)
                {
                    // Trả về phản hồi MC Protocol 3E chuẩn cho Command 0x0101 (Read CPU)
                    byte[] cpuBytes = System.Text.Encoding.ASCII.GetBytes("FX5U-64MT/ESS   ");
                    byte[] respHeader = new byte[] {
                        0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                        (byte)(2 + cpuBytes.Length), 0x00, // Data Length
                        0x00, 0x00  // End code = 0
                    };
                    byte[] fullResp = respHeader.Concat(cpuBytes).ToArray();
                    await stream.WriteAsync(fullResp, 0, fullResp.Length);
                }
            }
            catch { }
        });

        var report = await PlcDiagnosticService.RunDiagnosticAsync("127.0.0.1", port, PlcDriverType.Mitsubishi);
        listener.Stop();

        if (!report.SocketConnected) throw new Exception("Expected SocketConnected=true for mock server");
        if (!report.McProtocolSuccess) throw new Exception("Expected McProtocolSuccess=true for 3E mock response");
        if (!report.CpuModelDetected.StartsWith("FX5U", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Expected CPU FX5U, got '{report.CpuModelDetected}'");
        if (string.IsNullOrWhiteSpace(report.TxHexDump) || string.IsNullOrWhiteSpace(report.RxHexDump))
            throw new Exception("Expected non-empty TX and RX Hex dumps");
        if (string.IsNullOrWhiteSpace(report.FullReportText))
            throw new Exception("Expected non-empty FullReportText");
        if (string.IsNullOrWhiteSpace(report.SavedLogFilePath) || !System.IO.File.Exists(report.SavedLogFilePath))
            throw new Exception("Expected SavedLogFilePath to exist on disk");

        // Thử chẩn đoán cổng đóng (ví dụ port 5099 không mở)
        var closedReport = await PlcDiagnosticService.RunDiagnosticAsync("127.0.0.1", 5099, PlcDriverType.Mitsubishi);
        if (closedReport.SocketConnected) throw new Exception("Expected SocketConnected=false for closed port");
        if (closedReport.McProtocolSuccess) throw new Exception("Expected McProtocolSuccess=false for closed port");
        if (!closedReport.DiagnosisAdvice.Contains("LỖI CỔNG TCP"))
            throw new Exception("Expected DiagnosisAdvice to mention closed TCP port");

        Console.WriteLine($"PASSED (Probe OK, CPU='{report.CpuModelDetected}', Hex Log Saved at '{System.IO.Path.GetFileName(report.SavedLogFilePath)}')");
    }
}

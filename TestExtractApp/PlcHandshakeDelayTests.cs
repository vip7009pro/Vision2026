using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.UI.ViewModels.PLC;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

/// <summary>
/// Bộ kiểm thử tự động cho tính năng Delay Time trong Bắt Tay 24/7 (Handshake):
/// - Lưu/nạp/sao chép cấu hình DelayTimeMs.
/// - Kiểm tra & lưu kết quả sản phẩm theo thời gian thực ngay lập tức.
/// - Gửi tín hiệu về PLC trễ đúng DelayTimeMs theo mô hình Pipelined FIFO liên tục đúng thứ tự.
/// - Hủy sạch hàng đợi trễ khi Reset Queue / Stop (SetIdleAsync) / Dispose.
/// </summary>
public static class PlcHandshakeDelayTests
{
    public static async Task RunAllTestsAsync()
    {
        Console.WriteLine("\n==================================================================");
        Console.WriteLine("BẮT ĐẦU KIỂM THỬ: HANDSHAKE DELAY TIME (PIPELINED FIFO 24/7)");
        Console.WriteLine("==================================================================");

        Test1_ConfigSerialization_Defaults_And_CopyFrom();
        await Test2_SingleItem_RespectsDelayTime_BeforeSendingToPlcAsync();
        await Test3_PipelinedContinuousSequentialOrder_NoCumulativeDelayAsync();
        await Test4_ZeroGapBurst_PreservesStrictFifoOrder_And_PulseSafetyAsync();
        await Test5_SetIdle_And_ClearDelayedQueue_CancelPendingSignalsAsync();
        await Test6_DisabledOrOffline_BypassesDelayImmediatelyAsync();
        Test7_PlcManagerService_SaveGlobalConfig_PersistsAndPropagatesDelayTime();
        await Test8_ToolEditorContinuousPipeline_SavesRealTime_WhileDelayingPlcAsync();

        Console.WriteLine("==================================================================");
        Console.WriteLine("TẤT CẢ 8/8 TEST HANDSHAKE DELAY TIME ĐÃ VƯỢT QUA (PASSED 100%)!");
        Console.WriteLine("==================================================================\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"[ASSERTION FAILED] {message}");
    }

    private static PlcManagerService CreateConnectedSimulationPlcManager(out string configPath, string plcId = "PLC1")
    {
        configPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        var plcManager = new PlcManagerService(configPath);
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
            new PlcTag { Id = "T_M101", PlcId = plcId, Name = "M101", Address = "M101", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M102", PlcId = plcId, Name = "M102", Address = "M102", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M103", PlcId = plcId, Name = "M103", Address = "M103", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M104", PlcId = plcId, Name = "M104", Address = "M104", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M105", PlcId = plcId, Name = "M105", Address = "M105", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_M200", PlcId = plcId, Name = "M200", Address = "M200", DataType = PlcDataType.Bool },
            new PlcTag { Id = "T_D100", PlcId = plcId, Name = "D100", Address = "D100", DataType = PlcDataType.Int16 }
        };
        plcManager.LoadConfig(new[] { plc }, tags);
        var driver = (MitsubishiDriver)plcManager.GetDriver(plcId)!;
        driver.ForceSimulationMode = true;
        return plcManager;
    }

    private static void CleanupConfigFiles(string configPath)
    {
        try { if (File.Exists(configPath)) File.Delete(configPath); } catch { }
        try { if (File.Exists(configPath + ".backup")) File.Delete(configPath + ".backup"); } catch { }
    }

    private static void Test1_ConfigSerialization_Defaults_And_CopyFrom()
    {
        Console.WriteLine("--> Test 1: Serialization JSON, ApplyDefaults & CopyFrom cho DelayTimeMs");

        var cfg = new IndustrialHandshakeConfig();
        Assert(cfg.DelayTimeMs == 0, $"Mặc định DelayTimeMs phải là 0, nhận: {cfg.DelayTimeMs}");

        cfg.DelayTimeMs = 750;
        string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<IndustrialHandshakeConfig>(json);
        Assert(restored != null && restored.DelayTimeMs == 750,
            $"JSON Deserialize sai DelayTimeMs: {restored?.DelayTimeMs}");

        var copy = new IndustrialHandshakeConfig();
        copy.CopyFrom(cfg);
        Assert(copy.DelayTimeMs == 750, $"CopyFrom sai DelayTimeMs: {copy.DelayTimeMs}");

        cfg.ApplyFx5uDefaults("PLC1");
        Assert(cfg.DelayTimeMs == 0, $"ApplyFx5uDefaults phải đưa DelayTimeMs về 0, nhận: {cfg.DelayTimeMs}");

        cfg.DelayTimeMs = 420;
        cfg.ApplyStandardIoDefaults("PLC1");
        Assert(cfg.DelayTimeMs == 0, $"ApplyStandardIoDefaults phải đưa DelayTimeMs về 0, nhận: {cfg.DelayTimeMs}");

        Console.WriteLine("    [OK] DelayTimeMs serialize, copy và reset mặc định chuẩn 100%.");
    }

    private static async Task Test2_SingleItem_RespectsDelayTime_BeforeSendingToPlcAsync()
    {
        Console.WriteLine("--> Test 2: Tín hiệu đơn lẻ phải đợi đủ DelayTimeMs mới gửi xuống PLC");

        using var plcManager = CreateConnectedSimulationPlcManager(out var configPath);
        try
        {
            using var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
            {
                ReadyTagName = "M101",
                BusyTagName = "M102",
                DoneTagName = "M103",
                PassTagName = "M104",
                NgTagName = "M105",
                ResultWordTagName = "D100",
                ResultOutputMode = PlcResultOutputMode.Both,
                PassSignalType = PlcSignalType.RisingEdge,
                NgSignalType = PlcSignalType.RisingEdge,
                DoneSignalType = PlcSignalType.Level,
                PulseDurationMs = 20,
                PlcAckTagName = "",
                NonBlockingMode = true,
                DelayTimeMs = 250,
                IsEnabled = true
            };

            long? passSentAtMs = null;
            var sw = Stopwatch.StartNew();
            plcManager.OnTagChanged += (_, e) =>
            {
                if (e.TagName == "D100" && e.NewValue is short s && s == 2)
                {
                    passSentAtMs ??= sw.ElapsedMilliseconds;
                }
            };

            await sm.SetReadyAsync();
            await sm.StartInspectionAsync();

            var handshakeTask = sm.CompleteHandshakeAsync(isPass: true);

            // Kiểm tra sau 100ms (chưa hết delay 250ms): Busy phải hạ về false để sẵn sàng nhận phôi tiếp theo,
            // nhưng kết quả Pass/Done/D100 CHƯA ĐƯỢC PHÉP gửi sang PLC!
            await Task.Delay(100);
            Assert(!handshakeTask.IsCompleted, "CompleteHandshakeAsync không được hoàn tất trước khi hết DelayTimeMs!");
            Assert(sm.PendingDelayedCount == 1, $"PendingDelayedCount phải là 1, thực tế: {sm.PendingDelayedCount}");

            short earlyD100 = Convert.ToInt16(plcManager.GetTagValue("PLC1", "D100")?.CurrentValue ?? 0);
            bool earlyDone = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M103")?.CurrentValue ?? false);
            bool busyDuringDelay = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M102")?.CurrentValue ?? false);
            bool readyDuringDelay = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M101")?.CurrentValue ?? false);

            Assert(earlyD100 == 0, $"D100 chưa được ghi trước DelayTimeMs, nhận: {earlyD100}");
            Assert(!earlyDone, "Done (M103) chưa được bật trước DelayTimeMs!");
            Assert(!busyDuringDelay, "Busy (M102) phải hạ về 0 ngay khi kiểm tra xong trong lúc chờ delay!");
            Assert(readyDuringDelay, "Ready (M101) phải phục hồi về 1 ngay khi kiểm tra xong để đón phôi tiếp theo!");

            bool ok = await handshakeTask;
            Assert(ok, "CompleteHandshakeAsync phải trả về true sau khi hết DelayTimeMs.");
            Assert(passSentAtMs.HasValue, "D100 = 2 phải được gửi sau khi hết DelayTimeMs.");
            long actualSentMs = passSentAtMs ?? 0;
            Assert(actualSentMs >= 230, $"Tín hiệu được gửi quá sớm ({actualSentMs}ms < 230ms với delay 250ms)!");

            short finalD100 = Convert.ToInt16(plcManager.GetTagValue("PLC1", "D100")?.CurrentValue ?? 0);
            Assert(finalD100 == 2, $"Sau delay, D100 phải là 2, nhận: {finalD100}");
            Assert(sm.PendingDelayedCount == 0, $"Sau khi gửi xong, PendingDelayedCount phải về 0, nhận: {sm.PendingDelayedCount}");

            Console.WriteLine($"    [OK] Busy/Ready giải phóng tức thì; tín hiệu kết quả gửi chuẩn xác sau {passSentAtMs}ms (Delay=250ms).");
        }
        finally
        {
            CleanupConfigFiles(configPath);
        }
    }

    private static async Task Test3_PipelinedContinuousSequentialOrder_NoCumulativeDelayAsync()
    {
        Console.WriteLine("--> Test 3: Pipelined FIFO liên tục (5 sản phẩm cách nhau ~60ms, Delay=300ms -> gửi liên tục đúng thứ tự)");

        using var plcManager = CreateConnectedSimulationPlcManager(out var configPath);
        try
        {
            using var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
            {
                ReadyTagName = "M101",
                BusyTagName = "M102",
                DoneTagName = "M103",
                PassTagName = "M104",
                NgTagName = "M105",
                ResultWordTagName = "D100",
                QueueRegisterStart = "M200",
                ResultOutputMode = PlcResultOutputMode.Both,
                PassNumericValue = 2,
                NgNumericValue = 3,
                PassSignalType = PlcSignalType.RisingEdge,
                NgSignalType = PlcSignalType.RisingEdge,
                DoneSignalType = PlcSignalType.Level,
                PulseDurationMs = 20,
                PlcAckTagName = "",
                NonBlockingMode = true,
                DelayTimeMs = 300,
                IsEnabled = true
            };

            var sentSequence = new List<(short WordVal, long SentAtMs)>();
            var sw = Stopwatch.StartNew();

            plcManager.OnTagChanged += (_, e) =>
            {
                if (e.TagName == "D100" && e.NewValue is short s && (s == 2 || s == 3))
                {
                    lock (sentSequence)
                    {
                        sentSequence.Add((s, sw.ElapsedMilliseconds));
                    }
                }
            };

            await sm.SetReadyAsync();

            // Chuỗi 5 sản phẩm kiểm tra liên tiếp mỗi ~60ms: OK, NG, OK, NG, OK
            bool[] pattern = { true, false, true, false, true };
            short[] expectedWords = { 2, 3, 2, 3, 2 };
            var tasks = new List<Task<bool>>();
            var inspectTimesMs = new List<long>();

            for (int i = 0; i < pattern.Length; i++)
            {
                if (i > 0)
                {
                    await Task.Delay(60);
                }

                inspectTimesMs.Add(sw.ElapsedMilliseconds);
                tasks.Add(sm.CompleteHandshakeAsync(pattern[i]));
            }

            long allEnqueuedAtMs = sw.ElapsedMilliseconds;
            // Tại thời điểm vừa kiểm tra xong cả 5 con (~240ms), chưa đến Delay=300ms nên CHƯA con nào được gửi sang PLC!
            lock (sentSequence)
            {
                Assert(sentSequence.Count == 0,
                    $"Tại t={allEnqueuedAtMs}ms (< Delay 300ms), chưa được gửi tín hiệu nào, nhưng đã gửi {sentSequence.Count} tín hiệu!");
            }

            bool[] results = await Task.WhenAll(tasks);
            long allSentAtMs = sw.ElapsedMilliseconds;

            Assert(results.All(r => r), "Tất cả 5 task handshake đều phải trả về true.");
            Assert(sentSequence.Count == 5, $"Phải gửi đủ 5 tín hiệu về PLC, thực tế: {sentSequence.Count}");

            for (int i = 0; i < 5; i++)
            {
                Assert(sentSequence[i].WordVal == expectedWords[i],
                    $"Sai thứ tự FIFO tại phần tử #{i}: mong đợi {expectedWords[i]}, thực tế {sentSequence[i].WordVal}");

                long actualDelay = sentSequence[i].SentAtMs - inspectTimesMs[i];
                Assert(actualDelay >= 280,
                    $"Phần tử #{i} bị gửi sớm: actualDelay={actualDelay}ms (DelayTimeMs=300ms)");
            }

            // Nếu bị lỗi cộng dồn (sequential wait-then-delay), 5 x 300ms sẽ mất >= 1500ms!
            // Với pipelined FIFO, tổng thời gian phải xấp xỉ 240 + 300 = 540ms (< 950ms kể cả overhead Windows).
            Assert(allSentAtMs < 950,
                $"Phát hiện cộng dồn delay! Tổng thời gian cho 5 tín hiệu = {allSentAtMs}ms (mong đợi pipelined ~540ms < 950ms).");

            Console.WriteLine($"    [OK] 5 tín hiệu phát đúng thứ tự FIFO ({string.Join(" -> ", sentSequence.Select(x => $"{x.WordVal}@{x.SentAtMs}ms"))}) trong {allSentAtMs}ms!");
        }
        finally
        {
            CleanupConfigFiles(configPath);
        }
    }

    private static async Task Test4_ZeroGapBurst_PreservesStrictFifoOrder_And_PulseSafetyAsync()
    {
        Console.WriteLine("--> Test 4: Burst 6 sản phẩm liên tiếp 0ms gap không đảo thứ tự & không bị pulse cũ cắt nhầm");

        using var plcManager = CreateConnectedSimulationPlcManager(out var configPath);
        try
        {
            using var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
            {
                ReadyTagName = "M101",
                BusyTagName = "M102",
                DoneTagName = "M103",
                PassTagName = "M104",
                NgTagName = "M105",
                ResultWordTagName = "D100",
                ResultOutputMode = PlcResultOutputMode.Both,
                PassNumericValue = 2,
                NgNumericValue = 3,
                PassSignalType = PlcSignalType.RisingEdge,
                NgSignalType = PlcSignalType.RisingEdge,
                DoneSignalType = PlcSignalType.Level,
                PulseDurationMs = 30,
                PlcAckTagName = "",
                NonBlockingMode = true,
                DelayTimeMs = 150,
                IsEnabled = true
            };

            var observedWords = new List<short>();
            plcManager.OnTagChanged += (_, e) =>
            {
                if (e.TagName == "D100" && e.NewValue is short s && (s == 2 || s == 3))
                {
                    lock (observedWords) observedWords.Add(s);
                }
            };

            await sm.SetReadyAsync();

            // Bắn dồn dập 6 kết quả cách nhau 0ms: OK, OK, NG, OK, NG, NG
            bool[] burst = { true, true, false, true, false, false };
            short[] expected = { 2, 2, 3, 2, 3, 3 };
            var tasks = burst.Select(p => sm.CompleteHandshakeAsync(p)).ToArray();

            bool[] results = await Task.WhenAll(tasks);
            Assert(results.All(r => r), "Tất cả burst items phải hoàn tất thành công.");
            Assert(observedWords.Count == 6, $"Phải nhận đủ 6 kết quả, nhận: {observedWords.Count}");

            for (int i = 0; i < 6; i++)
            {
                Assert(observedWords[i] == expected[i],
                    $"Sai thứ tự burst tại #{i}: mong đợi {expected[i]}, nhận {observedWords[i]}");
            }

            // Chờ qua PulseDurationMs (30ms) + buffer để xác nhận xung cuối cùng đã tự hạ về false sạch sẽ
            await Task.Delay(70);
            bool m103After = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M103")?.CurrentValue ?? false);
            bool m104After = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M104")?.CurrentValue ?? false);
            bool m105After = Convert.ToBoolean(plcManager.GetTagValue("PLC1", "M105")?.CurrentValue ?? false);
            Assert(!m103After && !m104After && !m105After,
                $"Sau khi phát hết xung, Done/Pass/NG phải về 0 (M103={m103After}, M104={m104After}, M105={m105After})");

            Console.WriteLine("    [OK] Burst 0ms bảo toàn 100% thứ tự FIFO và tự hạ xung cuối sạch sẽ.");
        }
        finally
        {
            CleanupConfigFiles(configPath);
        }
    }

    private static async Task Test5_SetIdle_And_ClearDelayedQueue_CancelPendingSignalsAsync()
    {
        Console.WriteLine("--> Test 5: SetIdleAsync & ClearDelayedQueue hủy tức thì tín hiệu đang chờ, chống rò rỉ sau Stop/Reset");

        using var plcManager = CreateConnectedSimulationPlcManager(out var configPath);
        try
        {
            using var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1")
            {
                ReadyTagName = "M101",
                BusyTagName = "M102",
                DoneTagName = "M103",
                PassTagName = "M104",
                NgTagName = "M105",
                ResultWordTagName = "D100",
                ResultOutputMode = PlcResultOutputMode.Both,
                PlcAckTagName = "",
                NonBlockingMode = true,
                DelayTimeMs = 500,
                IsEnabled = true
            };

            int sentCount = 0;
            plcManager.OnTagChanged += (_, e) =>
            {
                if (e.TagName == "D100" && e.NewValue is short s && (s == 2 || s == 3))
                {
                    Interlocked.Increment(ref sentCount);
                }
            };

            await sm.SetReadyAsync();
            var t1 = sm.CompleteHandshakeAsync(true);
            var t2 = sm.CompleteHandshakeAsync(false);
            await Task.Delay(50);

            Assert(sm.PendingDelayedCount == 2, $"Phải có 2 item chờ trong hàng đợi, thực tế: {sm.PendingDelayedCount}");

            // Hủy bằng SetIdleAsync (như khi người dùng bấm Stop)
            var sw = Stopwatch.StartNew();
            await sm.SetIdleAsync();
            sw.Stop();

            Assert(sw.ElapsedMilliseconds < 150, $"SetIdleAsync phải hủy tức thì, không được chờ 500ms (tốn {sw.ElapsedMilliseconds}ms)");
            Assert(sm.PendingDelayedCount == 0, $"Sau SetIdleAsync, PendingDelayedCount phải là 0, nhận: {sm.PendingDelayedCount}");

            bool[] r = await Task.WhenAll(t1, t2);
            Assert(!r[0] && !r[1], "Các task bị hủy bởi SetIdleAsync phải trả về false.");

            // Chờ vượt qua 500ms để bảo đảm KHÔNG CÓ tín hiệu rò rỉ nào tự bắn ra sau khi đã Stop!
            await Task.Delay(550);
            Assert(Volatile.Read(ref sentCount) == 0, $"Không được gửi tín hiệu rò rỉ sau SetIdleAsync, đã gửi: {sentCount}");

            // Kiểm tra ClearDelayedQueue vẫn cho phép chạy lượt mới sau đó
            await sm.SetReadyAsync();
            var t3 = sm.CompleteHandshakeAsync(false);
            await Task.Delay(30);
            sm.ClearDelayedQueue();
            Assert(sm.PendingDelayedCount == 0, "ClearDelayedQueue phải xóa ngay lập tức.");
            Assert(!(await t3), "Task bị xóa bởi ClearDelayedQueue phải trả về false.");

            // Lượt mới với delay ngắn 80ms phải hoạt động bình thường sau khi clear
            sm.DelayTimeMs = 80;
            bool okAfterClear = await sm.CompleteHandshakeAsync(true);
            Assert(okAfterClear, "Sau ClearDelayedQueue, lượt gửi mới phải hoạt động bình thường.");
            Assert(Volatile.Read(ref sentCount) == 1, $"Phải gửi đúng 1 tín hiệu ở lượt mới, nhận: {sentCount}");

            Console.WriteLine("    [OK] SetIdleAsync & ClearDelayedQueue hủy sạch hàng đợi trễ không rò rỉ và khôi phục chạy lại chuẩn xác.");
        }
        finally
        {
            CleanupConfigFiles(configPath);
        }
    }

    private static async Task Test6_DisabledOrOffline_BypassesDelayImmediatelyAsync()
    {
        Console.WriteLine("--> Test 6: Bypass 0ms khi Handshake tắt hoặc PLC Offline dù DelayTimeMs lớn");

        using var plcManager = TestPlcConfigHelper.CreateIsolatedPlcManager();
        using var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC_OFFLINE")
        {
            DelayTimeMs = 3000,
            IsEnabled = false
        };

        var sw1 = Stopwatch.StartNew();
        bool r1 = await sm.CompleteHandshakeAsync(true);
        sw1.Stop();
        Assert(r1 && sw1.ElapsedMilliseconds < 50,
            $"Khi IsEnabled=false phải bypass tức thì (<50ms), thực tế: {sw1.ElapsedMilliseconds}ms");

        sm.IsEnabled = true; // PLC vẫn offline
        var sw2 = Stopwatch.StartNew();
        bool r2 = await sm.CompleteHandshakeAsync(false);
        sw2.Stop();
        Assert(r2 && sw2.ElapsedMilliseconds < 50,
            $"Khi PLC Offline phải bypass tức thì (<50ms), thực tế: {sw2.ElapsedMilliseconds}ms");

        Console.WriteLine("    [OK] Bypass tức thì 0ms khi tắt Handshake hoặc mất kết nối PLC.");
    }

    private static void Test7_PlcManagerService_SaveGlobalConfig_PersistsAndPropagatesDelayTime()
    {
        Console.WriteLine("--> Test 7: PlcManagerService.SaveGlobalConfig lưu DelayTimeMs & đồng bộ tức thì tới StateMachine");

        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        try
        {
            using var service = new PlcManagerService(tempConfigPath);
            using var sm = new IndustrialHandshakeStateMachine(service, "PLC1");

            service.OnIndustrialConfigChanged += (_, cfg) => sm.ApplyConfig(cfg.Handshake);

            var vm = new PlcManagerViewModel(service);
            vm.Handshake.DelayTimeMs = 640;

            // Lưu cấu hình (giống nút Save Config / đóng cửa sổ PlcManagerWindow)
            service.SaveGlobalConfig();

            Assert(sm.DelayTimeMs == 640,
                $"StateMachine chưa được cập nhật DelayTimeMs sau SaveGlobalConfig (thực tế: {sm.DelayTimeMs})");

            // Khởi tạo service mới đọc từ đĩa để xác nhận bền vững trên JSON
            using var reloadedService = new PlcManagerService(tempConfigPath);
            Assert(reloadedService.IndustrialConfig.Handshake.DelayTimeMs == 640,
                $"Đọc lại từ đĩa sai DelayTimeMs: {reloadedService.IndustrialConfig.Handshake.DelayTimeMs}");

            Console.WriteLine("    [OK] SaveGlobalConfig lưu đĩa JSON & cập nhật StateMachine thời gian thực thành công 100%.");
        }
        finally
        {
            CleanupConfigFiles(tempConfigPath);
        }
    }

    private static async Task Test8_ToolEditorContinuousPipeline_SavesRealTime_WhileDelayingPlcAsync()
    {
        Console.WriteLine("--> Test 8: Tích hợp ToolEditorViewModel Continuous: Lưu kết quả tức thì, phát PLC trễ liên tục đúng thứ tự");

        using var plcManager = CreateConnectedSimulationPlcManager(out var configPath);
        try
        {
            var vm = new ToolEditorViewModel();
            var inspectionService = new InspectionService(
                new ImagePreprocessor(),
                new PatternMatcher(),
                new DistanceCalculator(),
                new LineDetector(),
                new DefectDetector());

            InjectField(vm, "_inspectionService", inspectionService);
            InjectField(vm, "_plcManagerService", plcManager);

            var sm = new IndustrialHandshakeStateMachine(plcManager, "PLC1");
            InjectField(vm, "_handshakeStateMachine", sm);

            sm.PlcId = "PLC1";
            sm.ReadyTagName = "M101";
            sm.BusyTagName = "M102";
            sm.DoneTagName = "M103";
            sm.PassTagName = "M104";
            sm.NgTagName = "M105";
            sm.ResultWordTagName = "D100";
            sm.ResultOutputMode = PlcResultOutputMode.Both;
            sm.PassNumericValue = 2;
            sm.NgNumericValue = 3;
            sm.PlcAckTagName = "";
            sm.NonBlockingMode = true;
            sm.DelayTimeMs = 260;
            sm.IsEnabled = true;

            var config = new VisionConfig
            {
                ProductCode = "TEST_DELAY_PIPELINE",
                ProductName = "TEST_DELAY_PIPELINE",
                ToolGraph = new ToolGraph
                {
                    Nodes = { new ToolGraphNode { Id = "1", Type = "ImageSource", RefName = "CAM1" } }
                }
            };
            config.ImageSources.Add(new ImageSourceDefinition
            {
                Name = "CAM1",
                SourceType = ImageSourceType.Camera,
                CameraIndex = CameraService.SimulatorCameraIndex,
                TriggerMode = ImageSourceTriggerMode.PlcTrigger
            });
            vm.InitializeWithConfig(config);
            InjectField(vm, "_isRunningFolderFlow", true);

            var sentWords = new List<(short Value, long AtMs)>();
            var sw = Stopwatch.StartNew();
            plcManager.OnTagChanged += (_, e) =>
            {
                if (e.TagName == "D100" && e.NewValue is short s && (s == 2 || s == 3))
                {
                    lock (sentWords) sentWords.Add((s, sw.ElapsedMilliseconds));
                }
            };

            var processMethod = typeof(ToolEditorViewModel).GetMethod(
                "ProcessContinuousFrameAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("Không tìm thấy ProcessContinuousFrameAsync");

            await sm.SetReadyAsync();

            // Chạy 3 frame liên tiếp mỗi 50ms
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) await Task.Delay(50);

                using var mat = new Mat(40, 40, MatType.CV_8UC3, Scalar.All(120));
                var envelope = new ContinuousFrameEnvelope
                {
                    Frame = mat,
                    Metadata = new FrameMetadata { FrameIndex = i + 1, HostTimestamp = DateTime.UtcNow }
                };

                var t = (Task?)processMethod.Invoke(vm, new object[] { envelope, "CAM1", CancellationToken.None });
                await t!;
            }

            // Ngay sau khi chạy xong 3 frame (~100-150ms):
            // 1) Tất cả 3 sản phẩm đã được kiểm tra & lưu kết quả vào UI RecentParts / ProcessedImageCount ngay lập tức!
            Assert(vm.ProcessedImageCount == 3,
                $"ProcessedImageCount phải cập nhật tức thì = 3, nhận: {vm.ProcessedImageCount}");
            Assert(vm.RecentPartsTotalCount == 3,
                $"RecentPartsTotalCount phải lưu đủ 3 kết quả ngay lập tức, nhận: {vm.RecentPartsTotalCount}");

            // 2) Tín hiệu PLC chưa được phép phát ra trước 260ms!
            lock (sentWords)
            {
                Assert(sentWords.Count == 0,
                    $"Trước 260ms chưa được phép gửi tín hiệu sang PLC, nhưng đã gửi {sentWords.Count} lần!");
            }

            // Chờ qua 260ms + đệm
            await Task.Delay(320);

            lock (sentWords)
            {
                Assert(sentWords.Count == 3,
                    $"Sau khi hết DelayTimeMs (260ms), cả 3 tín hiệu phải được gửi liên tiếp tới PLC, nhận: {sentWords.Count}");
                Assert(sentWords.All(x => x.Value == 2),
                    "Cả 3 frame hợp lệ đều phải gửi kết quả OK (2).");
                Assert(sentWords[0].AtMs >= 240,
                    $"Tín hiệu đầu tiên gửi quá sớm: {sentWords[0].AtMs}ms");
            }

            InjectField(vm, "_isRunningFolderFlow", false);
            await sm.SetIdleAsync();
            sm.Dispose();
            Console.WriteLine($"    [OK] ToolEditor kiểm tra & lưu 3 kết quả ngay lập tức; PLC nhận đủ 3 tín hiệu sau {sentWords[0].AtMs}..{sentWords[2].AtMs}ms.");
        }
        finally
        {
            CleanupConfigFiles(configPath);
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

    private static object? GetField(object target, string fieldName)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }
        if (field is null) throw new Exception($"GetField: không tìm thấy '{fieldName}'");
        return field.GetValue(target);
    }
}

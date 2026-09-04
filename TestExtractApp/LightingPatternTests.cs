using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class LightingPatternTests
{
    private static int _passed;
    private static int _failed;

    public static void RunTests()
    {
        _passed = 0;
        _failed = 0;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n════════════════════════════════════════════════════════════════════");
        Console.WriteLine("   ✨ LIGHTING BLINK PATTERN & SCENARIOS — AUTOMATED TESTS");
        Console.WriteLine("════════════════════════════════════════════════════════════════════\n");
        Console.ResetColor();

        // 1. Parser Tests
        TestParseCommaStyle();
        TestParseStructuredMultiLine();
        TestParseStrobeMacro();
        TestParseChaseMacro();
        TestValidationSuccessAndError();
        TestTimingEstimation();

        // 2. Execution & Cycle Tests
        TestPatternExecutionAndRepeatCyclesAsync().GetAwaiter().GetResult();
        TestPatternCancellationAsync().GetAwaiter().GetResult();

        // 3. NG Snapshot & Restore Tests
        TestNgPatternSnapshotAndRestoreAsync().GetAwaiter().GetResult();

        // 4. Persistence & Default Patterns Tests
        TestDefaultPatternsAndClone();

        Console.ForegroundColor = _failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"\n════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"   LIGHTING PATTERN TESTS RESULT: {_passed} PASSED, {_failed} FAILED");
        Console.WriteLine($"════════════════════════════════════════════════════════════════════\n");
        Console.ResetColor();

        if (_failed > 0)
        {
            throw new Exception($"Lighting Pattern unit tests failed: {_failed} test(s) failed.");
        }
    }

    private static void Assert(bool condition, string testName)
    {
        if (condition)
        {
            _passed++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅ PASS: {testName}");
            Console.ResetColor();
        }
        else
        {
            _failed++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {testName}");
            Console.ResetColor();
        }
    }

    private static void TestParseCommaStyle()
    {
        // Kiểm tra đúng định dạng người dùng yêu cầu: L1, ON, 300, L1, OFF, L2, ON, 100, L2, OFF
        string script = "L1, ON, 300, L1, OFF, L2, ON, 100, L2, OFF";
        var steps = LightingPatternParser.Parse(script, channelCount: 4);

        Assert(steps.Count == 4, "Parse Comma Style: Có 4 bước lệnh");
        Assert(steps[0].Channels.SequenceEqual(new[] { 0 }) && steps[0].PowerOn == true && steps[0].DelayMs == 300, "Parse Comma Style: Bước 1 Bật L1 trễ 300ms");
        Assert(steps[1].Channels.SequenceEqual(new[] { 0 }) && steps[1].PowerOn == false && steps[1].DelayMs == 0, "Parse Comma Style: Bước 2 Tắt L1");
        Assert(steps[2].Channels.SequenceEqual(new[] { 1 }) && steps[2].PowerOn == true && steps[2].DelayMs == 100, "Parse Comma Style: Bước 3 Bật L2 trễ 100ms");
        Assert(steps[3].Channels.SequenceEqual(new[] { 1 }) && steps[3].PowerOn == false && steps[3].DelayMs == 0, "Parse Comma Style: Bước 4 Tắt L2");
    }

    private static void TestParseStructuredMultiLine()
    {
        string script =
@"# Ghi chú đầu kịch bản
ALL OFF
DELAY 150
L1 ON 255 120 // Ghi chú cuối dòng
L1 OFF
L2 SET 180 80
ALL ON 200
ALL OFF";

        var steps = LightingPatternParser.Parse(script, channelCount: 4);
        Assert(steps.Count == 7, "Parse Structured Multi-Line: Có 7 bước lệnh");
        Assert(steps[0].Channels.Count == 4 && steps[0].PowerOn == false, "Bước 1: ALL OFF 4 kênh");
        Assert(steps[1].StepType == LightingPatternStepType.Delay && steps[1].DelayMs == 150, "Bước 2: DELAY 150ms");
        Assert(steps[2].Channels.SequenceEqual(new[] { 0 }) && steps[2].Brightness == 255 && steps[2].DelayMs == 120, "Bước 3: L1 ON 255 trễ 120ms");
        Assert(steps[3].Channels.SequenceEqual(new[] { 0 }) && steps[3].PowerOn == false, "Bước 4: L1 OFF");
        Assert(steps[4].Channels.SequenceEqual(new[] { 1 }) && steps[4].Brightness == 180 && steps[4].DelayMs == 80, "Bước 5: L2 SET 180 trễ 80ms");
        Assert(steps[5].Channels.Count == 4 && steps[5].PowerOn == true && steps[5].Brightness == 200, "Bước 6: ALL ON 200");
        Assert(steps[6].Channels.Count == 4 && steps[6].PowerOn == false, "Bước 7: ALL OFF");
    }

    private static void TestParseStrobeMacro()
    {
        // STROBE ALL 60 60 3 255
        string script = "STROBE ALL 60 60 3 255";
        var steps = LightingPatternParser.Parse(script, channelCount: 4);

        Assert(steps.Count == 6, "Parse STROBE: 3 lần chớp = 6 bước (3 ON + 3 OFF)");
        Assert(steps[0].PowerOn == true && steps[0].DelayMs == 60 && steps[0].Brightness == 255, "STROBE Bước 1: Bật ALL trễ 60ms sáng 255");
        Assert(steps[1].PowerOn == false && steps[1].DelayMs == 60, "STROBE Bước 2: Tắt ALL trễ 60ms");
        Assert(steps[4].PowerOn == true && steps[4].DelayMs == 60, "STROBE Bước 5: Lần 3 Bật");
        Assert(steps[5].PowerOn == false && steps[5].DelayMs == 60, "STROBE Bước 6: Lần 3 Tắt");
    }

    private static void TestParseChaseMacro()
    {
        // CHASE 80 200 trên 4 kênh
        string script = "CHASE 80 200";
        var steps = LightingPatternParser.Parse(script, channelCount: 4);

        Assert(steps.Count == 8, "Parse CHASE: 4 kênh = 8 bước (4 cặp ON-OFF)");
        Assert(steps[0].Channels.SequenceEqual(new[] { 0 }) && steps[0].PowerOn == true && steps[0].DelayMs == 80, "CHASE Bước 1: L1 ON trễ 80ms");
        Assert(steps[1].Channels.SequenceEqual(new[] { 0 }) && steps[1].PowerOn == false, "CHASE Bước 2: L1 OFF");
        Assert(steps[6].Channels.SequenceEqual(new[] { 3 }) && steps[6].PowerOn == true && steps[6].DelayMs == 80, "CHASE Bước 7: L4 ON trễ 80ms");
        Assert(steps[7].Channels.SequenceEqual(new[] { 3 }) && steps[7].PowerOn == false, "CHASE Bước 8: L4 OFF");
    }

    private static void TestValidationSuccessAndError()
    {
        // Kịch bản hợp lệ
        var validRes = LightingPatternParser.Validate("L1 ON 255 100; L1 OFF", channelCount: 4);
        Assert(validRes.IsValid && validRes.StepCount == 2 && validRes.EstimatedDurationMsPerCycle == 100, "Validation: Kịch bản hợp lệ");

        // Kịch bản rỗng
        var emptyRes = LightingPatternParser.Validate("", channelCount: 4);
        Assert(!emptyRes.IsValid, "Validation: Kịch bản rỗng báo lỗi");

        // Lỗi kênh không tồn tại
        var invalidChRes = LightingPatternParser.Validate("L9 ON 200", channelCount: 4);
        Assert(!invalidChRes.IsValid, "Validation: Kênh L9 vượt số kênh tối đa báo lỗi");

        // Lỗi lệnh không hợp lệ
        var invalidActionRes = LightingPatternParser.Validate("L1 FLY 200", channelCount: 4);
        Assert(!invalidActionRes.IsValid, "Validation: Hành động không xác định báo lỗi");
    }

    private static void TestTimingEstimation()
    {
        string script =
@"ALL OFF
DELAY 100
L1 ON 255 150
L1 OFF 50
L2 ON 255 200
L2 OFF";
        var res = LightingPatternParser.Validate(script, channelCount: 4);
        Assert(res.IsValid, "Timing Estimation: Phân tích hợp lệ");
        Assert(res.EstimatedDurationMsPerCycle == 100 + 150 + 50 + 200, "Timing Estimation: Tổng ms = 500ms");
    }

    private static async Task TestPatternExecutionAndRepeatCyclesAsync()
    {
        var transport = new MockLightingTransport();
        using var service = new LightingControllerService();
        using var patternService = new LightingPatternService(service);

        // Kịch bản 2 chu kỳ: L1 ON 10ms -> L1 OFF 10ms
        var pattern = new LightingPatternModel
        {
            Id = "test_cycle",
            Name = "Test Cycle Pattern",
            RepeatCycles = 2,
            Script = "L1 ON 255 10\nL1 OFF 10"
        };

        // Khi chưa kết nối: không chạy
        await patternService.PlayPatternAsync(pattern, channelCount: 4);
        Assert(!patternService.IsRunning, "PlayPattern khi chưa kết nối không thực thi");

        // Gắn transport giả lập
        service.AttachTransportForTesting(transport);

        int progressUpdates = 0;
        patternService.OnStepProgress += (_, e) => progressUpdates++;

        await patternService.PlayPatternAsync(pattern, channelCount: 4);

        Assert(!patternService.IsRunning, "PlayPattern kết thúc IsRunning = false");
        Assert(progressUpdates >= 4, $"Tiến trình chu kỳ được cập nhật đầy đủ ({progressUpdates} lần >= 4)");
        Assert(transport.SentCommands.Count > 0, $"Có lệnh gửi xuống controller ({transport.SentCommands.Count} lệnh)");
    }

    private static async Task TestPatternCancellationAsync()
    {
        var transport = new MockLightingTransport();
        using var service = new LightingControllerService();
        using var patternService = new LightingPatternService(service);
        service.AttachTransportForTesting(transport);

        // Kịch bản dài 5 chu kỳ với DELAY 200ms mỗi bước
        var pattern = new LightingPatternModel
        {
            Id = "long_pattern",
            Name = "Long Pattern",
            RepeatCycles = 5,
            Script = "ALL ON 255 200\nALL OFF 200"
        };

        using var cts = new CancellationTokenSource();

        var runTask = Task.Run(async () =>
        {
            await patternService.PlayPatternAsync(pattern, channelCount: 4, cts.Token);
        });

        // Chờ pattern bắt đầu chạy
        await Task.Delay(50);
        Assert(patternService.IsRunning, "Pattern bắt đầu chạy IsRunning = true");

        // Dừng pattern
        patternService.StopCurrentPattern();
        await runTask;

        Assert(!patternService.IsRunning, "Pattern đã dừng IsRunning = false");
    }

    private static async Task TestNgPatternSnapshotAndRestoreAsync()
    {
        var transport = new MockLightingTransport();
        using var service = new LightingControllerService();
        using var patternService = new LightingPatternService(service);
        service.AttachTransportForTesting(transport);

        // Đặt mức sáng làm việc ban đầu
        await service.SetChannelPowerAsync(0, true);
        await service.SetBrightnessAsync(0, 150);

        var ngPattern = new LightingPatternModel
        {
            Id = "ng_test",
            Name = "NG Strobe",
            RepeatCycles = 1,
            Script = "STROBE ALL 15 15 2 255"
        };

        var list = new List<LightingPatternModel> { ngPattern };

        transport.SentCommands.Clear();
        await patternService.PlayNgPatternAsync(true, "ng_test", list, channelCount: 4);

        // Kiểm tra sau khi kết thúc kịch bản NG, hệ thống phải gửi lệnh khôi phục trạng thái sáng ban đầu
        bool hasRestorePower = transport.SentCommands.Any(c => c.Contains("$F0="));
        bool hasRestoreBrightness = transport.SentCommands.Any(c => c.Contains("$L0="));

        Assert(hasRestorePower && hasRestoreBrightness, "Kịch bản NG: Tự động gửi lệnh khôi phục trạng thái sáng sau khi nháy xong");
    }

    private static void TestDefaultPatternsAndClone()
    {
        var defaults = LightingPatternModel.CreateDefaultPatterns();
        Assert(defaults.Count >= 5, $"Tạo sẵn {defaults.Count} kịch bản mẫu chuẩn công nghiệp");

        var welcome = defaults.FirstOrDefault(p => p.Id == "pattern_welcome");
        Assert(welcome != null && welcome.IsBuiltIn, "Kịch bản mẫu Welcome Chase tồn tại");

        var ng = defaults.FirstOrDefault(p => p.Id == "pattern_ng_alert");
        Assert(ng != null && ng.IsBuiltIn, "Kịch bản mẫu NG Alert tồn tại");

        // Clone
        var clone = welcome!.Clone();
        Assert(clone.Id != welcome.Id, "Clone kịch bản tạo GUID mới");
        Assert(!clone.IsBuiltIn, "Clone kịch bản đánh dấu IsBuiltIn = false");
        Assert(clone.Script == welcome.Script, "Clone sao chép toàn vẹn script");
    }

    // =====================================================================
    // Mock Transport for fast in-memory testing
    // =====================================================================
    private sealed class MockLightingTransport : ILightingTransport
    {
        public List<string> SentCommands { get; } = new();
        public bool IsConnected { get; private set; } = true;

        public Task<string> SendAndReceiveAsync(string command, CancellationToken ct = default)
        {
            SentCommands.Add(command);
            return Task.FromResult("+OK");
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}

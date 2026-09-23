using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử hồi quy cho việc KẾ TOÁN THỜI GIAN trong Tool Editor.
///
/// Bối cảnh lỗi: chip "TỔNG (ms)" hiển thị 27ms nhưng tổng bảng "thời gian chạy từng tool"
/// chỉ có Origin 15 + CAM1 4 = 19ms. Nguyên nhân:
///   • Hiệu chuẩn/Undistort và Preprocess toàn ảnh nằm TRONG TotalMs nhưng không được đo.
///   • Thời gian chụp/đọc ảnh nguồn (CAM1) thì ngược lại: được đo ở tầng UI và NẰM NGOÀI TotalMs
///     nhưng vẫn bị trộn vào bảng từng tool.
///   • Code ép mọi node Preprocess về 0 => xóa mất số đo thật của preprocess.
/// </summary>
public static class TimingAccountingTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("⏱️  RUNNING TIMING ACCOUNTING REGRESSION TESTS");
        Console.WriteLine("=========================================================");

        Test_01_Accounting_ExcludesSourceNodeFromEngineSum();
        Test_02_Breakdown_OnlyShowsToolsPresentOnCanvas();
        Test_03_Breakdown_AccountsForEveryMillisecond();
        Test_04_RealRun_MarksSourceNodeAndKeepsPreprocessTiming().GetAwaiter().GetResult();
        Test_05_ApplySourceTimings_DoesNotWipeEngineMeasuredPreprocess();
        Test_06_AllPhasesAreExplicit_ZeroResidualWhenFullyAccounted();

        Console.WriteLine("✅ ALL TIMING ACCOUNTING REGRESSION TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    // ==================================================================
    // 1. Node nguồn ảnh (đo ở UI, ngoài TotalMs) KHÔNG được cộng vào tổng engine
    // ==================================================================
    private static void Test_01_Accounting_ExcludesSourceNodeFromEngineSum()
    {
        Console.Write("--- [1/6] Tổng engine phải loại node nguồn ảnh (CAM1) đo ở tầng UI... ");

        var timings = new InspectionTimings
        {
            TotalMs = 27,
            CalibrationUndistortMs = 3,
            GlobalPreprocessMs = 2,
            SourceCaptureMs = 4
        };
        timings.NodeTimings["Origin"] = 15;
        timings.NodeTimings["CAM1"] = 4;      // đo ở UI — NGOÀI TotalMs
        timings.NodeTimings["PRE1"] = 1;
        timings.NodeTimings["IMG_OUT1"] = 0;
        timings.SourceNodeNames.Add("CAM1");

        Assert(timings.EngineNodeSumMs == 16,
            $"Σ tool trong engine phải = Origin 15 + PRE1 1 + IMG_OUT1 0 = 16 (KHÔNG gồm CAM1), thực tế {timings.EngineNodeSumMs}");

        // 27 = 3 (hiệu chuẩn) + 2 (preprocess) + 16 (tool) + 6 (phần còn lại)
        Assert(timings.ResultAssemblyMs == 6,
            $"Phần 'Ghép KQ & làm tròn' = 27 - (3+2+16) = 6, thực tế {timings.ResultAssemblyMs}");

        Assert(timings.CalibrationUndistortMs + timings.GlobalPreprocessMs + timings.EngineNodeSumMs + timings.ResultAssemblyMs == 27,
            "TỔNG phải bằng tổng của tất cả các ô (không thất lạc ms nào)");

        // Không có node nguồn nào được đánh dấu -> CAM1 bị tính vào engine (đây là hành vi CŨ, sai)
        var legacy = new InspectionTimings { TotalMs = 27, SourceCaptureMs = 4 };
        legacy.NodeTimings["Origin"] = 15;
        legacy.NodeTimings["CAM1"] = 4;
        Assert(legacy.EngineNodeSumMs == 19,
            $"Khi chưa đánh dấu nguồn ảnh thì Σ = 19 (tái hiện đúng lỗi cũ), thực tế {legacy.EngineNodeSumMs}");

        Console.WriteLine("PASSED (CAM1 nằm ngoài TỔNG, tổng các ô = TỔNG)");
    }

    // ==================================================================
    // 2. Dải phân bổ chỉ hiển thị các tool CÓ TRÊN CANVAS
    // ==================================================================
    private static void Test_02_Breakdown_OnlyShowsToolsPresentOnCanvas()
    {
        Console.Write("--- [2/6] Phân bổ thời gian chỉ hiện tool có trên canvas... ");

        var vm = new ToolEditorViewModel();
        var config = new VisionConfig { ProductName = "TIMING_BREAKDOWN_TEST" };
        config.ToolGraph.Nodes.Add(Node("1", "ImageSource", "CAM1"));
        config.ToolGraph.Nodes.Add(Node("2", "Preprocess", "PRE1"));
        config.ToolGraph.Nodes.Add(Node("3", "Origin", "Origin"));
        config.ToolGraph.Nodes.Add(Node("4", "CircleFinder", "CIR1"));
        config.ToolGraph.Nodes.Add(Node("5", "Diameter", "DIA1"));
        config.ToolGraph.Nodes.Add(Node("6", "Diameter", "DIA2"));   // có trên canvas nhưng CHƯA chạy
        config.ToolGraph.Nodes.Add(Node("7", "ImageOutput", "IMG_OUT1"));

        InjectField(vm, "_config", config);

        var res = new InspectionResult();
        res.Timings.TotalMs = 27;
        res.Timings.CalibrationUndistortMs = 3;
        res.Timings.GlobalPreprocessMs = 2;
        res.Timings.NodeTimings["Origin"] = 15;
        res.Timings.NodeTimings["CAM1"] = 4;
        res.Timings.NodeTimings["PRE1"] = 2;
        res.Timings.NodeTimings["CIR1"] = 1;
        res.Timings.NodeTimings["DIA1"] = 0;
        res.Timings.NodeTimings["IMG_OUT1"] = 0;
        res.Timings.SourceCaptureMs = 4;
        res.Timings.SourceNodeNames.Add("CAM1");

        InvokeBreakdown(vm, res);

        var labels = vm.TimingBreakdown.Select(c => c.Label).ToList();

        Assert(labels.Count > 0, "Dải phân bổ không được rỗng");
        Assert(labels.Contains("TỔNG"), $"Phải có ô TỔNG, thực tế: [{string.Join(", ", labels)}]");

        // Các tool CÓ trên canvas và ĐÃ chạy -> phải xuất hiện (trước đây thiếu CIR1/DIA1/IMG_OUT1).
        foreach (var expected in new[] { "Origin", "CIR1", "DIA1", "IMG_OUT1" })
        {
            Assert(labels.Contains(expected),
                $"Thiếu ô '{expected}' — đây là tool có trên canvas và đã chạy. Thực tế: [{string.Join(", ", labels)}]");
        }

        // DIA2 có trên canvas nhưng KHÔNG chạy -> không hiển thị (tránh ô 0ms vô nghĩa).
        Assert(!labels.Contains("DIA2"),
            $"DIA2 chưa chạy nên không được hiển thị. Thực tế: [{string.Join(", ", labels)}]");

        // Các ô định danh khác phải có nhãn riêng, không lẫn vào TỔNG.
        Assert(labels.Contains("Hiệu chuẩn"), $"Phải có ô 'Hiệu chuẩn' (3ms nằm trong TỔNG). Thực tế: [{string.Join(", ", labels)}]");
        Assert(labels.Contains("Preprocess"), $"Phải có ô 'Preprocess' (2ms nằm trong TỔNG). Thực tế: [{string.Join(", ", labels)}]");
        Assert(labels.Contains("Ghép KQ & làm tròn"), $"Phải có ô 'Ghép KQ & làm tròn' cho phần thời gian còn lại. Thực tế: [{string.Join(", ", labels)}]");

        // Node nguồn ảnh vẫn hiển thị (nó có trên canvas) nhưng KHÔNG nằm trong TỔNG.
        Assert(labels.Contains("CAM1"), $"Node nguồn CAM1 phải hiển thị (có trên canvas). Thực tế: [{string.Join(", ", labels)}]");
        Assert(vm.HasOutOfTotalTimingChip,
            "Phải bật cảnh báo 'ô màu cam nằm ngoài tổng' khi hiển thị thời gian chụp ảnh");

        // Nhãn CAM1 phải nằm SAU cùng (nhóm ngoài tổng xếp cuối cùng).
        Assert(labels.IndexOf("CAM1") > labels.IndexOf("Ghép KQ & làm tròn"),
            "Ô thời gian nguồn ảnh phải được xếp sau cùng để tách khỏi phần nằm trong TỔNG");

        Console.WriteLine($"PASSED (ô: {string.Join(" | ", labels)})");
    }

    // ==================================================================
    // 3. Tổng các ô hiển thị = TỔNG (đúng bằng chip TỔNG (ms) ở thẻ trên)
    // ==================================================================
    private static void Test_03_Breakdown_AccountsForEveryMillisecond()
    {
        Console.Write("--- [3/6] Tổng các ô 'trong TỔNG' phải bằng đúng chip TỔNG (ms)... ");

        var vm = new ToolEditorViewModel();
        var config = new VisionConfig { ProductName = "TIMING_SUM_TEST" };
        config.ToolGraph.Nodes.Add(Node("1", "ImageSource", "CAM1"));
        config.ToolGraph.Nodes.Add(Node("2", "Origin", "Origin"));
        config.ToolGraph.Nodes.Add(Node("3", "CircleFinder", "CIR1"));
        InjectField(vm, "_config", config);

        var res = new InspectionResult();
        res.Timings.TotalMs = 27;
        res.Timings.CalibrationUndistortMs = 3;
        res.Timings.GlobalPreprocessMs = 2;
        res.Timings.NodeTimings["Origin"] = 15;
        res.Timings.NodeTimings["CIR1"] = 1;
        res.Timings.NodeTimings["CAM1"] = 4;
        res.Timings.SourceCaptureMs = 4;
        res.Timings.SourceNodeNames.Add("CAM1");

        InvokeBreakdown(vm, res);

        // Các ô NẰM TRONG TỔNG = tất cả trừ ô "TỔNG" (chính là tổng) và trừ ô thời gian nguồn ảnh.
        var insideTotalChips = vm.TimingBreakdown
            .Where(c => c.Label != "TỔNG" && c.Label != "CAM1")
            .ToList();

        var insideTotal = insideTotalChips.Sum(c => double.Parse(c.ValueText));

        Assert(Math.Abs(insideTotal - res.Timings.TotalMs) < 0.001,
            $"Tổng các ô thành phần phải = {res.Timings.TotalMs}ms, thực tế {insideTotal}ms " +
            $"({string.Join(" + ", insideTotalChips.Select(c => $"{c.Label}={c.ValueText}"))})");

        Console.WriteLine($"PASSED (Σ thành phần = {insideTotal:0.0}ms = TỔNG)");
    }

    // ==================================================================
    // 4. Chạy flow THẬT: node nguồn phải được đánh dấu + timing preprocess không bị ghi đè 0
    // ==================================================================
    private static async Task Test_04_RealRun_MarksSourceNodeAndKeepsPreprocessTiming()
    {
        Console.Write("--- [4/6] Chạy flow thật: đánh dấu node nguồn + KHÔNG ghi đè timing Preprocess... ");

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision2026_Timing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        ToolEditorViewModel? vm = null;

        try
        {
            var imgPath = Path.Combine(tempDir, "part_001.png");
            using (var img = new Mat(120, 160, MatType.CV_8UC3, Scalar.All(180)))
            {
                Cv2.ImWrite(imgPath, img);
            }

            var config = new VisionConfig
            {
                ProductCode = "TIMING_TEST",
                ProductName = "TIMING_TEST",
                ToolGraph = new ToolGraph { Nodes = { Node("1", "ImageSource", "CAM1") } }
            };
            config.ImageSources.Add(new ImageSourceDefinition
            {
                Name = "CAM1",
                SourceType = ImageSourceType.Folder,
                FolderPath = tempDir
            });

            vm = new ToolEditorViewModel();
            InjectField(vm, "_preprocessor", new ImagePreprocessor());
            InjectField(vm, "_lineDetector", new LineDetector());
            InjectField(vm, "_inspectionService", CreateInspectionService());

            vm.InitializeWithConfig(config);
            vm.CurrentTempWorkingDir = tempDir;

            var method = typeof(ToolEditorViewModel).GetMethod(
                "RunSingleFlowFromImageFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("Không tìm thấy method 'RunSingleFlowFromImageFileAsync'");

            var task = (Task?)method.Invoke(vm, new object[] { imgPath, "CAM1" })
                       ?? throw new Exception("RunSingleFlowFromImageFileAsync không trả về Task");
            await task;

            var result = vm.LastResult;
            Assert(result is not null, "Chạy flow thật phải tạo ra LastResult");

            var timings = result!.Timings;
            Assert(timings.TotalMs > 0, $"TotalMs phải > 0, thực tế {timings.TotalMs}");

            // Thời gian chuẩn bị ảnh nguồn phải được ĐÁNH DẤU là nằm ngoài TotalMs.
            Assert(timings.SourceNodeNames.Contains("CAM1"),
                "Node nguồn CAM1 phải được đánh dấu trong SourceNodeNames (nếu không, thời gian chụp ảnh " +
                "sẽ bị cộng sai vào tổng engine)");
            Assert(!timings.SourceNodeNames.Any(n => string.Equals(n, "Origin", StringComparison.OrdinalIgnoreCase)),
                "Node Origin KHÔNG được đánh dấu là node nguồn ảnh");

            Assert(timings.EngineNodeSumMs >= 0, "EngineNodeSumMs không được âm");
            Assert(timings.ResultAssemblyMs >= 0, $"ResultAssemblyMs không được âm, thực tế {timings.ResultAssemblyMs}");
            Assert(timings.CalibrationUndistortMs + timings.GlobalPreprocessMs + timings.EngineNodeSumMs <= timings.TotalMs,
                "Tổng các pha đo được không được VƯỢT TotalMs (nếu vượt => đo trùng lặp)");

            // Dải phân bổ vẫn phải dựng được từ kết quả thật (đúng các tool có trên canvas).
            InvokeBreakdown(vm, result);
            var labels = vm.TimingBreakdown.Select(c => c.Label).ToList();
            Assert(labels.Contains("TỔNG") && labels.Contains("CAM1"),
                $"Dải phân bổ từ kết quả thật phải có TỔNG và CAM1. Thực tế: [{string.Join(", ", labels)}]");

            Console.WriteLine($"PASSED (TỔNG={timings.TotalMs}ms, hiệu chuẩn={timings.CalibrationUndistortMs}ms, " +
                              $"preprocess={timings.GlobalPreprocessMs}ms, Σ tool trong engine={timings.EngineNodeSumMs}ms, " +
                              $"ghép kết quả={timings.ResultAssemblyMs}ms, nguồn ảnh={timings.SourceCaptureMs}ms ngoài tổng)");
        }
        finally
        {
            try { vm?.InspectionLogService.Dispose(); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ==================================================================
    // 5. NGUYÊN NHÂN GỐC của "thời gian ẩn": code cũ ép mọi node Preprocess về 0
    //    => thời gian preprocess thật chỉ còn nằm trong TotalMs mà không tool nào hiển thị.
    // ==================================================================
    private static void Test_05_ApplySourceTimings_DoesNotWipeEngineMeasuredPreprocess()
    {
        Console.Write("--- [5/6] KHÔNG được ghi đè thời gian Preprocess đã đo được thành 0... ");

        var config = new VisionConfig { ProductName = "TIMING_PREPROCESS_TEST" };
        config.PreprocessNodes.Add(new PreprocessNodeDefinition { Name = "PRE1" });
        config.PreprocessNodes.Add(new PreprocessNodeDefinition { Name = "PRE2" }); // chưa từng chạy

        var res = new InspectionResult();
        res.Timings.TotalMs = 30;
        res.Timings.NodeTimings["PRE1"] = 7;   // engine ĐÃ đo được preprocess thật
        res.Timings.NodeTimings["Origin"] = 15;

        InvokeApplySourceAndPreprocessTimings(res, "CAM1", 4, config);

        Assert(res.Timings.NodeTimings["PRE1"] == 7,
            $"Thời gian Preprocess thật (7ms) KHÔNG được bị ghi đè thành 0, thực tế {res.Timings.NodeTimings["PRE1"]}ms");
        Assert(res.Timings.NodeTimings["PRE2"] == 0,
            $"Node Preprocess chưa từng chạy phải được ghi 0ms, thực tế {res.Timings.NodeTimings["PRE2"]}ms");
        Assert(res.Timings.NodeTimings["CAM1"] == 4,
            $"Thời gian nguồn ảnh CAM1 phải được ghi (4ms), thực tế {res.Timings.NodeTimings["CAM1"]}ms");
        Assert(res.Timings.SourceCaptureMs == 4,
            $"SourceCaptureMs phải = 4, thực tế {res.Timings.SourceCaptureMs}");
        Assert(res.Timings.SourceNodeNames.Contains("CAM1"),
            "CAM1 phải được đánh dấu là node nguồn ảnh");

        // PRE1 7 + Origin 15 = 22ms thuộc engine; 30 - 22 = 8ms là phần "khác"
        Assert(res.Timings.EngineNodeSumMs == 22,
            $"Σ tool trong engine phải = 7 + 15 = 22 (không gồm CAM1), thực tế {res.Timings.EngineNodeSumMs}");
        Assert(res.Timings.ResultAssemblyMs == 8,
            $"Phần 'Ghép KQ & làm tròn' = 30 - 22 = 8, thực tế {res.Timings.ResultAssemblyMs}");

        Console.WriteLine("PASSED (PRE1 giữ nguyên 7ms, PRE2 chưa chạy = 0ms, CAM1 ngoài tổng)");
    }

    // ==================================================================
    // 6. Khi MỌI pha đều được đo tường minh thì phần dư phải = 0
    //    (tức không còn bất kỳ "thời gian ẩn" nào).
    // ==================================================================
    private static void Test_06_AllPhasesAreExplicit_ZeroResidualWhenFullyAccounted()
    {
        Console.Write("--- [6/6] Mọi pha đều tường minh => phần dư 'Ghép KQ & làm tròn' = 0... ");

        var vm = new ToolEditorViewModel();
        var config = new VisionConfig { ProductName = "TIMING_FULL_ACCOUNT" };
        config.ToolGraph.Nodes.Add(Node("1", "Origin", "T1"));
        InjectField(vm, "_config", config);

        var res = new InspectionResult();
        res.Timings.FrameworkSetupMs = 1;          // Khởi tạo
        res.Timings.CalibrationUndistortMs = 2;    // Hiệu chuẩn
        res.Timings.OriginTemplateLoadMs = 3;      // Tải template
        res.Timings.ToolQueueWaitMs = 4;           // Chờ slot tool
        res.Timings.GlobalPreprocessMs = 5;        // Preprocess toàn ảnh
        res.Timings.NodeTimings["T1"] = 10;        // 1 tool
        res.Timings.ConditionsMs = 6;              // Điều kiện
        res.Timings.DefectsMs = 9;                 // Defect (gồm 5ms preprocess -> net 4)
        res.Timings.TotalMs = 1 + 2 + 3 + 4 + 5 + 10 + 6 + 4;  // = 35

        Assert(res.Timings.DefectsNetMs == 4,
            $"Defect sau khi trừ preprocess dùng chung phải = 9 - 5 = 4, thực tế {res.Timings.DefectsNetMs}");
        Assert(res.Timings.ResultAssemblyMs == 0,
            $"Khi mọi pha tường minh thì phần dư phải = 0, thực tế {res.Timings.ResultAssemblyMs}");
        Assert(res.Timings.AccountedMs == 35,
            $"Tổng các pha đo được phải = 35, thực tế {res.Timings.AccountedMs}");

        InvokeBreakdown(vm, res);
        var labels = vm.TimingBreakdown.Select(c => c.Label).ToList();

        foreach (var phase in new[] { "Khởi tạo", "Hiệu chuẩn", "Tải template", "Chờ slot tool", "Preprocess", "T1", "Điều kiện", "Defect" })
        {
            Assert(labels.Contains(phase),
                $"Thiếu ô tường minh '{phase}'. Thực tế: [{string.Join(", ", labels)}]");
        }

        // Không còn phần dư => không hiện ô "Ghép KQ & làm tròn"
        Assert(!labels.Contains("Ghép KQ & làm tròn"),
            $"Không còn thời gian ẩn thì không được hiện ô dư. Thực tế: [{string.Join(", ", labels)}]");

        var components = vm.TimingBreakdown
            .Where(c => c.Label != "TỔNG")
            .ToList();
        var sum = components.Sum(c => double.Parse(c.ValueText));
        Assert(Math.Abs(sum - 35) < 0.001,
            $"Σ thành phần phải = 35, thực tế {sum} ({string.Join(" + ", components.Select(c => $"{c.Label}={c.ValueText}"))})");

        Console.WriteLine($"PASSED (8 pha tường minh, Σ = {sum:0.0}ms = TỔNG, không còn phần dư)");
    }

    // ==================================================================
    // Helpers
    // ==================================================================
    private static void InvokeApplySourceAndPreprocessTimings(
        InspectionResult res, string? sourceNodeName, int sourceMs, VisionConfig config)
    {
        var method = typeof(ToolEditorViewModel).GetMethod(
            "ApplySourceAndPreprocessTimings", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("Không tìm thấy method 'ApplySourceAndPreprocessTimings'");

        method.Invoke(null, new object?[] { res, sourceNodeName, sourceMs, config });
    }

    private static void InvokeBreakdown(ToolEditorViewModel vm, InspectionResult res)
    {
        var method = typeof(ToolEditorViewModel).GetMethod(
            "RefreshTimingBreakdown", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("Không tìm thấy method 'RefreshTimingBreakdown'");

        method.Invoke(vm, new object?[] { res });
    }

    private static ToolGraphNode Node(string id, string type, string refName)
        => new() { Id = id, Type = type, RefName = refName };

    private static InspectionService CreateInspectionService()
    {
        return new InspectionService(
            new ImagePreprocessor(),
            new PatternMatcher(),
            new DistanceCalculator(),
            new LineDetector(),
            new DefectDetector());
    }

    private static void InjectField(object target, string fieldName, object? value)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }

        if (field is null)
            throw new Exception($"InjectField: không tìm thấy field '{fieldName}'");

        field.SetValue(target, value);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

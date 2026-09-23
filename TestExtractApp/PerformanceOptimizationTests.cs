using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

/// <summary>
/// Bộ kiểm thử tự động cho Phase 1→5 (Tối ưu hiệu năng Tool Editor + sửa lỗi ROI/Point).
///
/// Bao gồm:
///  - Hồi quy lỗi vẽ Template ROI cho Tool Point (Ctrl+Shift+Drag) và thuộc tính Score (MinScore).
///  - Hồi quy lỗi ROI "giật về kích thước cũ" (commit ROI phải refresh overlay ĐỒNG BỘ).
///  - Kiểm chứng cơ chế tối ưu: 1 snapshot / 1 global-preprocess cho mỗi lượt refresh.
///  - Kiểm chứng cache hình học OverlayPolyline.
///  - Benchmark smoke test thời gian refresh Preview.
/// </summary>
public static class PerformanceOptimizationTests
{
    private static string _tempDir = string.Empty;

    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("🚀 RUNNING PERFORMANCE OPTIMIZATION & ROI REGRESSION TESTS");
        Console.WriteLine("=========================================================");

        _tempDir = Path.Combine(Path.GetTempPath(), "Vision2026_PerfTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        try
        {
            Test_01_PointMinScore_PropertyAndClamping();
            Test_02_PointTemplateRoi_LabelRouting_AllToolFamilies();
            Test_03_RoiEditCommit_OverlayRefreshedSynchronously();
            Test_04_RefreshPass_UsesSingleSnapshotClone();
            Test_05_RefreshPass_NoPerToolSnapshotClones_WithLastRun();
            Test_06_RefreshPass_GlobalPreprocessRunsOnce();
            Test_07_OverlayPolyline_GeometryCache_IsScaleAware();
            Test_08_PointMinScore_PipelineGating();
            Test_09_PreviewRefresh_BenchmarkSmokeTest();
        }
        finally
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* bỏ qua lỗi dọn dẹp tạm */ }
        }

        Console.WriteLine("✅ ALL PERFORMANCE OPTIMIZATION & ROI REGRESSION TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    // ==================================================================
    // HELPERS
    // ==================================================================

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void AssertClose(double actual, double expected, double tolerance, string message)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new Exception($"{message} (expected {expected}, actual {actual})");
    }

    /// <summary>
    /// Gán giá trị vào field private/readonly của đối tượng bằng reflection.
    /// Chỉ dùng trong test để bơm service thật (ImagePreprocessor, LineDetector, _lastRun...)
    /// vào ToolEditorViewModel dựng bằng constructor rỗng.
    /// </summary>
    private static void InjectField(object target, string fieldName, object? value)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }

        if (field is null)
            throw new Exception($"InjectField: không tìm thấy field '{fieldName}'");

        field.SetValue(target, value);
    }

    /// <summary>
    /// Tạo ToolEditorViewModel đã được bơm đầy đủ ImagePreprocessor + LineDetector
    /// (constructor rỗng để trống các service này nên các nhánh preview sẽ không chạy được).
    /// </summary>
    private static ToolEditorViewModel CreateVm(VisionConfig config, bool withImage = true)
    {
        var vm = new ToolEditorViewModel();
        InjectField(vm, "_preprocessor", new ImagePreprocessor());
        InjectField(vm, "_lineDetector", new LineDetector());

        // Constructor rỗng không khởi tạo các ICommand ROI (chỉ constructor DI làm việc đó)
        // => nối tạm vào các private handler thật để kiểm thử đúng logic routing nhãn ROI.
        WireRoiCommand(vm, "OnRoiSelected", "RoiSelectedCommand");
        WireRoiCommand(vm, "OnRoiEdited", "RoiEditedCommand");

        vm.InitializeWithConfig(config);

        if (withImage)
        {
            using var img = CreateTestImage(600, 400, MatType.CV_8UC3);
            vm.SharedImageContext.SetImage(img);
        }

        return vm;
    }

    /// <summary>
    /// Gắn một ICommand vào property của VM, gọi thẳng method private (handler thật).
    /// Chỉ dùng trong test; không thay đổi code sản phẩm.
    /// </summary>
    private static void WireRoiCommand(ToolEditorViewModel vm, string methodName, string propertyName)
    {
        var method = typeof(ToolEditorViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new Exception($"WireRoiCommand: không tìm thấy method '{methodName}'");

        var property = typeof(ToolEditorViewModel).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     ?? throw new Exception($"WireRoiCommand: không tìm thấy property '{propertyName}'");

        property.SetValue(vm, new ReflectionMethodCommand(vm, method));
    }

    /// <summary>ICommand tối giản gọi một method private qua reflection (unwrap TargetInvocationException).</summary>
    private sealed class ReflectionMethodCommand : ICommand
    {
        private readonly object _target;
        private readonly MethodInfo _method;

        public ReflectionMethodCommand(object target, MethodInfo method)
        {
            _target = target;
            _method = method;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            try
            {
                _method.Invoke(_target, new[] { parameter });
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }
        }
    }

    private static Mat CreateTestImage(int w, int h, MatType type)
    {
        var img = new Mat(h, w, type, new Scalar(40, 40, 40, 0));
        // Khối caro tương phản cao để tool Point/Origin có đặc trưng rõ ràng.
        for (var y = 0; y < h; y += 40)
        {
            for (var x = 0; x < w; x += 40)
            {
                if (((x / 40) + (y / 40)) % 2 == 0)
                {
                    Cv2.Rectangle(img, new Rect(x, y, 40, 40), Scalar.White, -1);
                }
            }
        }
        return img;
    }

    private static ToolGraphNode Node(string id, string type, string refName)
        => new() { Id = id, Type = type, RefName = refName };

    private static Roi R(int x, int y, int w, int h)
        => new() { X = x, Y = y, Width = w, Height = h };

    // ==================================================================
    // 1. Thuộc tính Score (MinScore) của Tool Point
    // ==================================================================
    private static void Test_01_PointMinScore_PropertyAndClamping()
    {
        Console.Write("--- [1/9] Tool Point: thuộc tính Score (MinScore) + clamp [0..1]... ");

        var config = new VisionConfig
        {
            ProductCode = "PERF_TEST",
            ToolGraph = new ToolGraph
            {
                Nodes = { Node("1", "Point", "P1") }
            }
        };
        config.Points.Add(new PointDefinition { Name = "P1", SearchRoi = R(10, 10, 100, 100) });

        var vm = CreateVm(config);
        vm.SelectedNode = vm.Nodes.First(n => n.Type == "Point");

        AssertClose(vm.Point_MinScore, 0.8, 1e-9, "Giá trị mặc định của Point_MinScore phải là 0.8");

        vm.Point_MinScore = 0.95;
        AssertClose(config.Points[0].MatchScoreThreshold, 0.95, 1e-9, "Point_MinScore phải ghi vào MatchScoreThreshold");
        AssertClose(config.Points[0].MinScore, 0.95, 1e-9, "Alias MinScore phải map về MatchScoreThreshold");

        vm.Point_MinScore = 5.0;
        AssertClose(vm.Point_MinScore, 1.0, 1e-9, "Point_MinScore phải clamp trần về 1.0");

        vm.Point_MinScore = -2.0;
        AssertClose(vm.Point_MinScore, 0.0, 1e-9, "Point_MinScore phải clamp sàn về 0.0");

        Console.WriteLine("PASSED");
    }

    // ==================================================================
    // 2. Routing nhãn ROI (hồi quy lỗi Ctrl+Shift+Drag không vẽ được Template ROI)
    // ==================================================================
    private static void Test_02_PointTemplateRoi_LabelRouting_AllToolFamilies()
    {
        Console.Write("--- [2/9] Routing nhãn ROI cho Point / SurfaceCompare / ContourCompare... ");

        var config = new VisionConfig
        {
            ProductCode = "PERF_TEST",
            ToolGraph = new ToolGraph
            {
                Nodes =
                {
                    Node("1", "Point", "P1"),
                    Node("2", "SurfaceCompare", "SC1"),
                    Node("3", "ContourCompare", "CC1")
                }
            }
        };
        config.Points.Add(new PointDefinition { Name = "P1", SearchRoi = R(5, 5, 50, 50) });
        config.SurfaceCompares.Add(new SurfaceCompareDefinition { Name = "SC1" });
        config.ContourCompares.Add(new ContourCompareDefinition { Name = "CC1" });

        var vm = CreateVm(config);
        vm.CurrentTempWorkingDir = _tempDir; // để TrySaveTemplateImage ghi ảnh mẫu vào thư mục tạm

        void Select(string type) => vm.SelectedNode = vm.Nodes.First(n => n.Type == type);

        void SendRoi(string label, Roi roi)
        {
            var sel = new RoiSelection(label, roi, ModifierKeys.None);
            Assert(vm.RoiSelectedCommand.CanExecute(sel), $"RoiSelectedCommand không nhận nhãn '{label}'");
            vm.RoiSelectedCommand.Execute(sel);
        }

        // ---------- Tool Point: "P1 S" -> SearchRoi, "P1 T" -> TemplateRoi ----------
        Select("Point");
        var p = config.Points[0];
        var pSearchBefore = p.TemplateRoi.Width;

        SendRoi("P1 S", R(100, 110, 120, 90));
        Assert(p.SearchRoi.Width == 120 && p.SearchRoi.Height == 90 && p.SearchRoi.X == 100,
            $"Nhãn 'P1 S' phải cập nhật SearchRoi, thực tế X={p.SearchRoi.X} W={p.SearchRoi.Width} H={p.SearchRoi.Height}");
        Assert(p.TemplateRoi.Width == pSearchBefore, "Nhãn 'P1 S' KHÔNG được chạm vào TemplateRoi");

        SendRoi("P1 T", R(200, 210, 70, 60));
        Assert(p.TemplateRoi.Width == 70 && p.TemplateRoi.Height == 60 && p.TemplateRoi.X == 200,
            $"Nhãn 'P1 T' phải cập nhật TemplateRoi, thực tế X={p.TemplateRoi.X} W={p.TemplateRoi.Width} H={p.TemplateRoi.Height}");
        // Đây chính là hồi quy: bản cũ phân giải sai thành 'P1 CCT' nên TemplateRoi không đổi.
        Assert(p.SearchRoi.Width == 120, "Nhãn 'P1 T' KHÔNG được chạm vào SearchRoi");

        // ---------- SurfaceCompare: "SC1 SC" -> InspectRoi, "SC1 SCT" -> TemplateRoi ----------
        Select("SurfaceCompare");
        var sc = config.SurfaceCompares[0];
        SendRoi("SC1 SC", R(11, 12, 130, 140));
        Assert(sc.InspectRoi.Width == 130, $"Nhãn 'SC1 SC' phải cập nhật InspectRoi, thực tế W={sc.InspectRoi.Width}");

        SendRoi("SC1 SCT", R(21, 22, 33, 44));
        Assert(sc.TemplateRoi.Width == 33 && sc.TemplateRoi.Height == 44,
            $"Nhãn 'SC1 SCT' phải cập nhật TemplateRoi, thực tế W={sc.TemplateRoi.Width} H={sc.TemplateRoi.Height}");
        Assert(sc.InspectRoi.Width == 130, "Nhãn 'SC1 SCT' KHÔNG được chạm vào InspectRoi");

        // ---------- ContourCompare: "CC1 CC" -> InspectRoi, "CC1 CCT" -> TemplateRoi ----------
        Select("ContourCompare");
        var cc = config.ContourCompares[0];
        SendRoi("CC1 CC", R(31, 32, 55, 66));
        Assert(cc.InspectRoi.Width == 55, $"Nhãn 'CC1 CC' phải cập nhật InspectRoi, thực tế W={cc.InspectRoi.Width}");

        SendRoi("CC1 CCT", R(41, 42, 77, 88));
        Assert(cc.TemplateRoi.Width == 77 && cc.TemplateRoi.Height == 88,
            $"Nhãn 'CC1 CCT' phải cập nhật TemplateRoi, thực tế W={cc.TemplateRoi.Width} H={cc.TemplateRoi.Height}");
        Assert(cc.InspectRoi.Width == 55, "Nhãn 'CC1 CCT' KHÔNG được chạm vào InspectRoi");

        Console.WriteLine("PASSED");
    }

    // ==================================================================
    // 3. Commit ROI phải refresh overlay ĐỒNG BỘ, thắng cả cơ chế debounce
    //    (hồi quy lỗi ROI "giật về kích thước cũ")
    // ==================================================================
    private static void Test_03_RoiEditCommit_OverlayRefreshedSynchronously()
    {
        Console.Write("--- [3/9] Chỉnh ROI phải cập nhật Overlay ĐỒNG BỘ (không bị giật về size cũ)... ");

        var config = new VisionConfig
        {
            ProductCode = "PERF_TEST",
            ToolGraph = new ToolGraph { Nodes = { Node("1", "Point", "P1") } }
        };
        config.Points.Add(new PointDefinition { Name = "P1", SearchRoi = R(50, 50, 60, 60) });

        var vm = CreateVm(config);
        vm.CurrentTempWorkingDir = _tempDir;

        // Bật đúng cơ chế coalesce như trong app thật (nếu không, headless sẽ chạy đồng bộ
        // và test sẽ không chứng minh được điều gì).
        vm.RefreshCoalescingDispatcherOverride = System.Windows.Threading.Dispatcher.CurrentDispatcher;

        vm.SelectedNode = vm.Nodes.First(n => n.Type == "Point");
        vm.RefreshPreviewsNow();

        var overlayBefore = vm.SelectedNodeOverlayItems;
        Assert(overlayBefore is not null, "SelectedNodeOverlayItems phải được dựng sau RefreshPreviewsNow()");

        // (a) Cơ chế coalesce phải thực sự hoãn: gọi RefreshPreviews() vài lần liên tiếp
        //     KHÔNG được dựng lại overlay ngay (chưa pump dispatcher nên timer chưa tick).
        vm.RefreshPreviews();
        vm.RefreshPreviews();
        vm.RefreshPreviews();
        Assert(ReferenceEquals(overlayBefore, vm.SelectedNodeOverlayItems),
            "RefreshPreviews() phải được DEBOUNCE (gộp lại), không dựng overlay ngay lập tức");

        // (b) Người dùng kéo/ resize ROI rồi nhả chuột -> control bắn RoiEditedCommand rồi vẽ lại overlay NGAY.
        var newRoi = R(300, 220, 90, 70);
        var sel = new RoiSelection("P1 S", newRoi, ModifierKeys.None);
        Assert(vm.RoiEditedCommand.CanExecute(sel), "RoiEditedCommand không nhận RoiSelection");
        vm.RoiEditedCommand.Execute(sel);

        // 1. Cấu hình phải đổi ngay.
        Assert(config.Points[0].SearchRoi.Width == 90 && config.Points[0].SearchRoi.X == 300,
            $"SearchRoi phải được cập nhật ngay, thực tế X={config.Points[0].SearchRoi.X} W={config.Points[0].SearchRoi.Width}");

        // 2. Overlay PHẢI đã được dựng lại NGAY trong lệnh commit (force-sync thắng debounce),
        //    nếu không ImageViewerControl sẽ vẽ lại overlay CŨ => ROI giật về kích thước cũ.
        var overlayAfter = vm.SelectedNodeOverlayItems;
        Assert(!ReferenceEquals(overlayBefore, overlayAfter),
            "Overlay KHÔNG được dựng lại đồng bộ trong lệnh commit ROI => sẽ bị giật về kích thước cũ");

        var rect = overlayAfter!.OfType<OverlayRectItem>()
            .FirstOrDefault(r => string.Equals(r.Label, "P1 S", StringComparison.OrdinalIgnoreCase));
        Assert(rect is not null, "Không tìm thấy OverlayRectItem nhãn 'P1 S' sau khi commit ROI");
        Assert(rect!.Width == 90 && rect.Height == 70 && rect.X == 300,
            $"Overlay ROI phải khớp kích thước mới (90x70 @300,220), thực tế W={rect.Width} H={rect.Height} X={rect.X}");

        // (c) Sau khi pump dispatcher, lượt refresh đang chờ phải được thực thi bình thường
        //     (chứng minh cơ chế coalesce vẫn hoạt động, không bị treo vĩnh viễn).
        var overlayBeforePump = vm.SelectedNodeOverlayItems;
        config.Points[0].SearchRoi = R(11, 12, 33, 44); // đổi cấu hình trực tiếp rồi yêu cầu refresh
        vm.RefreshPreviews();
        PumpDispatcher(200);
        Assert(!ReferenceEquals(overlayBeforePump, vm.SelectedNodeOverlayItems),
            "Sau khi pump dispatcher, lượt refresh đang chờ phải được thực thi (coalesce không được treo)");

        vm.RefreshCoalescingDispatcherOverride = null;

        Console.WriteLine("PASSED (debounced + forced-sync + coalesce pump OK)");
    }

    /// <summary>Bơm message loop của Dispatcher hiện tại trong khoảng thời gian cho trước.</summary>
    private static void PumpDispatcher(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var stopper = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            System.Windows.Threading.DispatcherPriority.Send,
            (_, _) => frame.Continue = false,
            System.Windows.Threading.Dispatcher.CurrentDispatcher);
        stopper.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        stopper.Stop();
    }

    // ==================================================================
    // 4. Mỗi lượt refresh chỉ clone snapshot ĐÚNG 1 LẦN
    // ==================================================================
    private static void Test_04_RefreshPass_UsesSingleSnapshotClone()
    {
        Console.Write("--- [4/9] Mỗi lượt refresh chỉ clone snapshot 1 lần... ");

        var config = new VisionConfig
        {
            ProductCode = "PERF_TEST",
            ToolGraph = new ToolGraph
            {
                Nodes =
                {
                    Node("1", "ResultView", "ResultView1"),
                    Node("2", "Point", "P1"),
                    Node("3", "BlobDetection", "B1")
                }
            }
        };
        config.Points.Add(new PointDefinition { Name = "P1", SearchRoi = R(10, 10, 80, 80) });
        config.BlobDetections.Add(new BlobDetectionDefinition { Name = "B1", InspectRoi = R(20, 20, 120, 120) });

        var vm = CreateVm(config);
        vm.SelectedNode = vm.Nodes.First(n => n.Type == "ResultView");

        // warm-up để mọi cache/khởi tạo một lần nằm ngoài phép đo
        vm.RefreshPreviewsNow();

        SharedImageContext.ResetSnapshotCloneCount();
        vm.RefreshPreviewsNow();
        var clones = SharedImageContext.SnapshotCloneCount;

        Assert(clones == 1,
            $"Một lượt refresh phải clone snapshot ĐÚNG 1 lần (Final + Selected dùng chung), thực tế = {clones}");

        Console.WriteLine($"PASSED (snapshot clones = {clones})");
    }

    // ==================================================================
    // 5. Không clone snapshot theo từng Line/Caliper khi có kết quả chạy
    // ==================================================================
    private static void Test_05_RefreshPass_NoPerToolSnapshotClones_WithLastRun()
    {
        Console.Write("--- [5/9] Không clone snapshot cho từng Line/Caliper chưa tìm thấy... ");

        const int toolCount = 5;

        var graph = new ToolGraph { Nodes = { Node("0", "ResultView", "ResultView1") } };
        var config = new VisionConfig { ProductCode = "PERF_TEST", ToolGraph = graph };

        for (var i = 0; i < toolCount; i++)
        {
            graph.Nodes.Add(Node($"L{i + 1}", "Line", $"L{i + 1}"));
            graph.Nodes.Add(Node($"C{i + 1}", "Caliper", $"C{i + 1}"));

            config.Lines.Add(new LineToolDefinition
            {
                Name = $"L{i + 1}",
                SearchRoi = R(10 + i * 20, 10, 120, 90),
                Canny1 = 50,
                Canny2 = 150,
                HoughThreshold = 40,
                MinLineLength = 15,
                MaxLineGap = 6
            });

            config.Calipers.Add(new CaliperDefinition
            {
                Name = $"C{i + 1}",
                SearchRoi = R(10 + i * 20, 200, 120, 60),
                Orientation = CaliperOrientation.Horizontal,
                Polarity = EdgePolarity.Any,
                StripCount = 4,
                StripWidth = 8,
                StripLength = 60,
                MinEdgeStrength = 10
            });
        }

        var vm = CreateVm(config);
        vm.SelectedNode = vm.Nodes.First(n => n.Type == "ResultView");

        // Mô phỏng "đã chạy Flow" nhưng KHÔNG có kết quả cho Line/Caliper
        // => toàn bộ nhánh "Live Fallback" chạy cho mọi tool.
        InjectField(vm, "_lastRun", new InspectionResult());

        vm.RefreshPreviewsNow(); // warm-up

        SharedImageContext.ResetSnapshotCloneCount();
        vm.RefreshPreviewsNow();
        var clones = SharedImageContext.SnapshotCloneCount;

        // Bản cũ: 1 (lượt) + 5 (Line) + 5 (Caliper) = 11 lần clone 60MB.
        Assert(clones == 1,
            $"Với {toolCount} Line + {toolCount} Caliper chưa tìm thấy, lượt refresh vẫn chỉ được clone 1 lần, thực tế = {clones}");

        Console.WriteLine($"PASSED (snapshot clones = {clones}, trước tối ưu là {1 + toolCount * 2})");
    }

    // ==================================================================
    // 6. Global Preprocess chỉ chạy 1 lần cho mỗi lượt refresh
    // ==================================================================
    private static void Test_06_RefreshPass_GlobalPreprocessRunsOnce()
    {
        Console.Write("--- [6/9] Global Preprocess chỉ chạy 1 lần cho mỗi lượt refresh... ");

        var config = new VisionConfig
        {
            ProductCode = "PERF_TEST",
            ToolGraph = new ToolGraph { Nodes = { Node("1", "ResultView", "ResultView1") } }
        };

        var vm = CreateVm(config);
        vm.SelectedNode = vm.Nodes.First(n => n.Type == "ResultView");

        Assert(vm.PreprocessPreviewEnabled, "Test này giả định PreprocessPreviewEnabled = true (mặc định)");

        vm.RefreshPreviewsNow(); // warm-up

        ImagePreprocessor.ResetRunCallCount();
        vm.RefreshPreviewsNow();
        var runs = ImagePreprocessor.RunCallCount;

        // Bản cũ: 1 lần cho ảnh hiển thị Final Preview + 1 lần nữa trong BuildFinalOverlay = 2.
        Assert(runs == 1,
            $"Global Preprocess phải chạy ĐÚNG 1 lần cho mỗi lượt refresh, thực tế = {runs}");

        Console.WriteLine($"PASSED (preprocess runs = {runs})");
    }

    // ==================================================================
    // 7. Cache hình học OverlayPolyline (Phase 4)
    // ==================================================================
    private static void Test_07_OverlayPolyline_GeometryCache_IsScaleAware()
    {
        Console.Write("--- [7/9] Cache StreamGeometry của OverlayPolylineItem theo scale... ");

        var poly = new OverlayPolylineItem
        {
            Label = "CC1",
            Points = new List<System.Windows.Point>
            {
                new(0, 0),
                new(10, 0),
                new(10, 5),
                new(0, 5)
            }
        };

        var g1 = poly.GetOrCreateGeometry(1.0, 1.0);
        var g1Again = poly.GetOrCreateGeometry(1.0, 1.0);
        Assert(ReferenceEquals(g1, g1Again),
            "Cùng scale phải tái sử dụng ĐÚNG instance StreamGeometry đã cache");
        Assert(g1.IsFrozen, "Geometry phải được Freeze() để tối ưu render");

        var g2 = poly.GetOrCreateGeometry(2.0, 2.0);
        Assert(!ReferenceEquals(g1, g2), "Scale khác phải tạo geometry mới");
        Assert(ReferenceEquals(g2, poly.GetOrCreateGeometry(2.0, 2.0)),
            "Scale mới phải được cache lại (trước đây chỉ cache khi scale == 1.0)");

        // Kiểm tra toạ độ đã nhân scale đúng
        var b = g2.Bounds;
        AssertClose(b.Width, 20.0, 0.01, "Bounds.Width của geometry ở scale 2 phải là 20");

        Console.WriteLine("PASSED");
    }

    // ==================================================================
    // 8. Pipeline: Point dùng Score (MinScore) để đánh giá OK/NG
    // ==================================================================
    private static void Test_08_PointMinScore_PipelineGating()
    {
        Console.Write("--- [8/9] Pipeline: Point trả OK/NG theo ngưỡng Score... ");

        // Ảnh A: có hình mẫu (khối caro). Ảnh B: hình dạng khác hoàn toàn.
        using var imgMatch = new Mat(300, 300, MatType.CV_8UC1, Scalar.All(30));
        for (var y = 100; y < 200; y += 25)
        {
            for (var x = 100; x < 200; x += 25)
            {
                if (((x / 25) + (y / 25)) % 2 == 0)
                {
                    Cv2.Rectangle(imgMatch, new Rect(x, y, 25, 25), Scalar.White, -1);
                }
            }
        }

        using var imgOther = new Mat(300, 300, MatType.CV_8UC1, Scalar.All(30));
        Cv2.Circle(imgOther, new OpenCvSharp.Point(150, 150), 70, Scalar.White, -1);

        var templateRoi = R(95, 95, 110, 110);
        var tplPath = Path.Combine(_tempDir, "p1_template.png");
        using (var tplCrop = new Mat(imgMatch, new Rect(templateRoi.X, templateRoi.Y, templateRoi.Width, templateRoi.Height)))
        {
            Cv2.ImWrite(tplPath, tplCrop);
        }

        var config = new VisionConfig
        {
            ProductCode = "PERF_TEST",
            PixelsPerMm = 1.0,
            Origin = new PointDefinition { Name = "Origin" },
            ToolGraph = new ToolGraph { Nodes = { Node("1", "Point", "P1") } }
        };
        config.Points.Add(new PointDefinition
        {
            Name = "P1",
            SearchRoi = R(0, 0, 300, 300),
            TemplateRoi = templateRoi,
            TemplateImageFile = tplPath,
            Algorithm = PointFindAlgorithm.TemplateMatch
        });

        var svc = CreateInspectionService();

        // --- 8.1 Ảnh khớp mẫu, ngưỡng thấp => OK ---
        config.Points[0].MatchScoreThreshold = 0.5;
        var rMatch = svc.Inspect(imgMatch, config).Points.FirstOrDefault();
        Assert(rMatch is not null, "Pipeline không trả kết quả cho Tool Point");
        AssertClose(rMatch!.Threshold, 0.5, 1e-9, "Threshold của kết quả phải lấy từ MatchScoreThreshold");
        Assert(rMatch.Pass == (rMatch.Score >= 0.5),
            $"Quy tắc phải là Pass = (Score >= Threshold). Score={rMatch.Score:F4}, Thr={rMatch.Threshold:F4}, Pass={rMatch.Pass}");
        Assert(rMatch.Pass, $"Ảnh khớp mẫu với ngưỡng 0.5 phải đạt OK (Score={rMatch.Score:F4})");

        // --- 8.2 Ảnh KHÁC mẫu, ngưỡng cao => NG ---
        config.Points[0].MatchScoreThreshold = 1.0;
        var rOtherStrict = svc.Inspect(imgOther, config).Points.First();
        Assert(rOtherStrict.Pass == (rOtherStrict.Score >= 1.0),
            "Quy tắc Pass = (Score >= Threshold) phải giữ nguyên ở ngưỡng 1.0");
        Assert(!rOtherStrict.Pass,
            $"Ảnh KHÁC mẫu với ngưỡng 1.0 phải trả NG (Score={rOtherStrict.Score:F4})");

        // --- 8.3 Cùng ảnh KHÁC mẫu, hạ ngưỡng về 0 => OK ---
        config.Points[0].MatchScoreThreshold = 0.0;
        var rOtherLoose = svc.Inspect(imgOther, config).Points.First();
        Assert(rOtherLoose.Pass, $"Hạ ngưỡng Score về 0 phải trả OK (Score={rOtherLoose.Score:F4})");

        Console.WriteLine(
            $"PASSED (Score khớp mẫu = {rMatch.Score:F4}, Score khác mẫu = {rOtherStrict.Score:F4})");
    }

    // ==================================================================
    // 9. Benchmark smoke test
    // ==================================================================
    private static void Test_09_PreviewRefresh_BenchmarkSmokeTest()
    {
        Console.Write("--- [9/9] Benchmark thời gian một lượt refresh Preview... ");

        var graph = new ToolGraph { Nodes = { Node("0", "ResultView", "ResultView1") } };
        var config = new VisionConfig { ProductCode = "PERF_TEST", ToolGraph = graph };

        for (var i = 0; i < 4; i++)
        {
            graph.Nodes.Add(Node($"P{i + 1}", "Point", $"P{i + 1}"));
            config.Points.Add(new PointDefinition { Name = $"P{i + 1}", SearchRoi = R(20 + i * 30, 20, 120, 120) });
        }

        using var bigImg = CreateTestImage(2560, 1920, MatType.CV_8UC3);

        var vm = new ToolEditorViewModel();
        InjectField(vm, "_preprocessor", new ImagePreprocessor());
        InjectField(vm, "_lineDetector", new LineDetector());
        vm.InitializeWithConfig(config);
        vm.SharedImageContext.SetImage(bigImg);
        vm.SelectedNode = vm.Nodes.First(n => n.Type == "ResultView");

        vm.RefreshPreviewsNow(); // warm-up

        const int iterations = 3;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            vm.RefreshPreviewsNow();
        }
        sw.Stop();
        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;

        // Smoke bound rất rộng: chỉ nhằm bắt các hồi quy thảm hoạ (ví dụ clone 60MB hàng chục lần / lượt).
        Assert(avgMs < 5000,
            $"Một lượt refresh Preview trên ảnh 2560x1920 mất quá lâu: {avgMs:F1} ms");

        Console.WriteLine($"PASSED (trung bình {avgMs:F1} ms / lượt, ảnh 2560x1920, {config.Points.Count} Point tool)");
    }

    private static InspectionService CreateInspectionService()
    {
        var pre = new ImagePreprocessor();
        var matcher = new PatternMatcher();
        var dist = new DistanceCalculator();
        var line = new LineDetector();
        var defect = new DefectDetector();
        return new InspectionService(pre, matcher, dist, line, defect);
    }
}

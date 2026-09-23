using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử hồi quy cho luồng Run Continuous Flow:
///  - Không mất kết quả kiểm tra CUỐI khi bấm STOP (InspectionLogService phải drain hàng đợi trước khi chốt phiên).
///  - Hợp đồng sở hữu Mat của SharedImageContext.SetImage(transferOwnership: true) — không clone thừa, không rò rỉ.
/// </summary>
public static class ContinuousFlowRegressionTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("🔁 RUNNING CONTINUOUS FLOW & LOGGING REGRESSION TESTS");
        Console.WriteLine("=========================================================");

        Test_01_InspectionLog_DoesNotLoseLastPartsOnStop();
        Test_02_SharedImageContext_TransferOwnershipContract();
        Test_03_FolderFlow_LogsResultToInspectionHistory();

        Console.WriteLine("✅ ALL CONTINUOUS FLOW & LOGGING REGRESSION TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    /// <summary>Gán giá trị vào field private/readonly của đối tượng bằng reflection (chỉ dùng trong test).</summary>
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

    // ==================================================================
    // 1. Bấm STOP ngay sau con hàng cuối => KHÔNG được mất kết quả
    // ==================================================================
    private static void Test_01_InspectionLog_DoesNotLoseLastPartsOnStop()
    {
        Console.Write("--- [1/2] STOP ngay sau con hàng cuối không được làm mất kết quả trong Lịch sử kiểm tra... ");

        // Tạo backlog đủ lớn để chắc chắn Background Worker chưa xử lý xong tại thời điểm EndSessionAsync.
        const int partCount = 2000;
        const int measurementsPerPart = 20;

        InspectionSessionRecord? session = null;
        InspectionLogService? log = null;

        try
        {
            log = new InspectionLogService();
            session = log.StartSessionAsync("PERF_FLOW_TEST", "perf_flow_test.job", "-").GetAwaiter().GetResult();

            var cfg = new VisionConfig { ProductName = "PERF_FLOW_TEST", PixelsPerMm = 10.0 };

            for (var i = 1; i <= partCount; i++)
            {
                var res = new InspectionResult { Pass = i % 5 != 0 };
                for (var m = 1; m <= measurementsPerPart; m++)
                {
                    res.Distances.Add(new DistanceCheckResult(
                        $"Dim{m}", "P1", "P2", 45.0 + i * 0.001 + m * 0.0005, 45.0, 0.2, 0.2, res.Pass));
                }
                log.EnqueueInspectionResult(res, cfg, i);
            }

            // ❗ KHÔNG sleep: mô phỏng đúng thao tác người dùng bấm STOP ngay khi con hàng cuối vừa chạy xong.
            // (Trước khi sửa, EndSessionAsync() lưu file + xoá session NGAY nên các part còn trong hàng đợi bị mất.)
            log.EndSessionAsync().GetAwaiter().GetResult();

            var parts = log.GetPartsForSessionAsync(session.Id).GetAwaiter().GetResult();
            Assert(parts.Count == partCount,
                $"MẤT KẾT QUẢ: mong đợi {partCount} part trong Lịch sử kiểm tra, thực tế {parts.Count}");

            Assert(!session.IsRunning, "Session phải được đánh dấu đã kết thúc sau EndSessionAsync()");
            Assert(session.TotalParts == partCount,
                $"Session.TotalParts phải là {partCount}, thực tế {session.TotalParts}");

            // Kiểm tra cả file trên đĩa: instance mới phải đọc lại đủ số part
            // (chứng minh dữ liệu đã được flush xuống đĩa TRƯỚC khi chốt phiên).
            using (var fresh = new InspectionLogService())
            {
                var diskParts = fresh.GetPartsForSessionAsync(session.Id).GetAwaiter().GetResult();
                Assert(diskParts.Count == partCount,
                    $"FILE ĐĨA THIẾU DỮ LIỆU: mong đợi {partCount} part, thực tế {diskParts.Count}");

                fresh.DeleteSessionAsync(session.Id).GetAwaiter().GetResult();
            }
        }
        finally
        {
            try { log?.DeleteSessionAsync(session?.Id ?? string.Empty).GetAwaiter().GetResult(); } catch { }
            try { log?.Dispose(); } catch { }
        }

        Console.WriteLine($"PASSED ({partCount} parts được ghi đủ cả RAM và đĩa dù STOP ngay lập tức)");
    }

    // ==================================================================
    // 2. Hợp đồng sở hữu Mat của SharedImageContext
    // ==================================================================
    private static void Test_02_SharedImageContext_TransferOwnershipContract()
    {
        Console.Write("--- [2/2] SharedImageContext.SetImage(transferOwnership) không clone thừa & không rò rỉ... ");

        var ctx = new SharedImageContext();

        // 2.1 transferOwnership: true KHÔNG được clone lại (tiết kiệm 1 ảnh full-size mỗi frame continuous)
        var src = new Mat(120, 160, MatType.CV_8UC3, Scalar.All(120));
        SharedImageContext.ResetSnapshotCloneCount();
        ctx.SetImage(src, transferOwnership: true);

        Assert(SharedImageContext.SnapshotCloneCount == 0,
            "SetImage(transferOwnership: true) không được clone thêm ảnh");
        Assert(!src.IsDisposed, "Context đang sở hữu ảnh nên chưa được dispose ngay");

        using (var snap = ctx.GetSnapshot())
        {
            Assert(snap is not null && snap.Width == 160 && snap.Height == 120,
                "Snapshot sau SetImage phải giữ đúng kích thước ảnh đã gán");
        }

        // 2.2 Gán ảnh mới => context phải dispose ảnh cũ (không rò rỉ bộ nhớ native)
        var src2 = new Mat(60, 80, MatType.CV_8UC3, Scalar.All(200));
        ctx.SetImage(src2, transferOwnership: true);
        Assert(src.IsDisposed, "Context phải dispose ảnh cũ khi nhận ownership của ảnh mới");

        // 2.3 Ảnh rỗng + transferOwnership => phải tự dispose để không rò rỉ
        var empty = new Mat();
        ctx.SetImage(empty, transferOwnership: true);
        Assert(empty.IsDisposed, "Ảnh rỗng gán kèm transferOwnership phải được dispose (trước đây bị rò rỉ)");

        ctx.SetImage(null);

        Console.WriteLine("PASSED");
    }

    // ==================================================================
    // 3. Chạy luồng FOLDER (không phải Camera) cũng phải ghi vào Lịch sử kiểm tra
    // ==================================================================
    private static void Test_03_FolderFlow_LogsResultToInspectionHistory()
    {
        Console.Write("--- [3/3] Luồng FOLDER cũng phải ghi kết quả vào Lịch sử kiểm tra... ");

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision2026_FolderLog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        ToolEditorViewModel? vm = null;
        InspectionSessionRecord? session = null;

        try
        {
            var imgPath = Path.Combine(tempDir, "part_001.png");
            using (var img = new Mat(120, 160, MatType.CV_8UC3, Scalar.All(180)))
            {
                Cv2.ImWrite(imgPath, img);
            }

            var config = new VisionConfig
            {
                ProductCode = "PERF_FOLDER_LOG",
                ProductName = "PERF_FOLDER_LOG",
                ToolGraph = new ToolGraph { Nodes = { Node("1", "ImageSource", "CAM1") } }
            };
            config.ImageSources.Add(new ImageSourceDefinition
            {
                Name = "CAM1",
                SourceType = ImageSourceType.Folder,
                FolderPath = tempDir
            });

            vm = new ToolEditorViewModel();
            // Constructor rỗng để trống các service => bơm service thật để chạy được flow.
            InjectField(vm, "_preprocessor", new ImagePreprocessor());
            InjectField(vm, "_lineDetector", new LineDetector());
            InjectField(vm, "_inspectionService", CreateInspectionService());

            vm.InitializeWithConfig(config);
            vm.CurrentTempWorkingDir = tempDir;

            // Gọi đúng method mà StartFolderFlow() dùng cho mỗi ảnh trong thư mục.
            var method = typeof(ToolEditorViewModel).GetMethod(
                "RunSingleFlowFromImageFileAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("Không tìm thấy method 'RunSingleFlowFromImageFileAsync'");

            var task = (Task?)method.Invoke(vm, new object[] { imgPath, "CAM1" })
                       ?? throw new Exception("RunSingleFlowFromImageFileAsync không trả về Task");
            task.GetAwaiter().GetResult();

            var log = vm.InspectionLogService;
            session = log.CurrentSession;
            Assert(session is not null,
                "Chạy luồng Folder phải TỰ ĐỘNG tạo phiên trong Lịch sử kiểm tra (trước đây chỉ nguồn Camera mới tạo)");

            log.EndSessionAsync().GetAwaiter().GetResult();

            var parts = log.GetPartsForSessionAsync(session!.Id).GetAwaiter().GetResult();
            Assert(parts.Count == 1,
                $"Lịch sử kiểm tra phải có đúng 1 con hàng cho luồng Folder, thực tế {parts.Count}");
            Assert(parts[0].PartIndex == 1,
                $"PartIndex phải là 1 (1-based), thực tế {parts[0].PartIndex}");

            Console.WriteLine($"PASSED (phiên '{session.SessionCode}', {parts.Count} part được ghi)");
        }
        finally
        {
            try
            {
                if (vm is not null && session is not null)
                {
                    vm.InspectionLogService.DeleteSessionAsync(session.Id).GetAwaiter().GetResult();
                }
            }
            catch { }

            try { vm?.InspectionLogService.Dispose(); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static InspectionService CreateInspectionService()
    {
        return new InspectionService(
            new ImagePreprocessor(),
            new PatternMatcher(),
            new DistanceCalculator(),
            new LineDetector(),
            new DefectDetector());
    }

    private static ToolGraphNode Node(string id, string type, string refName)
        => new() { Id = id, Type = type, RefName = refName };
}

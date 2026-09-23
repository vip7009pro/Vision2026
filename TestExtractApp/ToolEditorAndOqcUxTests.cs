using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử hồi quy cho các cải tiến UX trên Tool Editor &amp; OQC Scanner:
///  1. Nút "Reset Phiên &amp; Hàng Đợi" (xả sạch queue + reset bộ đếm + mở phiên Lịch sử kiểm tra mới).
///  2. Thanh Queue phải khớp 16 nấc thiết kế trên giao diện.
///  3. Nút "Đóng Job" trên OQC Scanner xóa ĐÚNG các dòng lịch sử ở trạng thái "Đã nạp Job".
///  4. Xóa CHỌN LỌC nhiều dòng trong "Lịch Sử Quét Mã OQC" (thay vì chỉ xóa tất).
/// </summary>
public static class ToolEditorAndOqcUxTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("🎛️  RUNNING TOOL EDITOR & OQC UX REGRESSION TESTS");
        Console.WriteLine("=========================================================");

        Test_01_ResetSessionAndQueue_DrainsQueueAndClearsCounters();
        Test_02_ResetSessionAndQueue_OpensFreshInspectionLogSession().GetAwaiter().GetResult();
        Test_03_ResetCommand_IsExposedForXamlBinding();
        Test_04_CloseLoadedJob_RemovesOnlyPendingJobHistoryRows();
        Test_05_SelectiveDelete_RemovesOnlyChosenRows();
        Test_06_QueueCapacity_Matches16SlotUi();

        Console.WriteLine("✅ ALL TOOL EDITOR & OQC UX REGRESSION TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    // ==================================================================
    // 1. Reset phiên & hàng đợi: xả sạch queue, dispose Mat, reset bộ đếm
    // ==================================================================
    private static void Test_01_ResetSessionAndQueue_DrainsQueueAndClearsCounters()
    {
        Console.Write("--- [1/6] Reset xả sạch hàng đợi + đưa bộ đếm/thanh 20 nấc về 0... ");

        var vm = new ToolEditorViewModel();

        // Giả lập hàng đợi đang có 3 frame chờ xử lý (mỗi frame giữ 1 Mat native).
        var frameMats = new List<Mat>();
        var channel = CreateFrameQueue(capacity: 16, itemCount: 3, capturedMats: frameMats);
        InjectField(vm, "_industrialCameraFrameChannel", channel);

        vm.QueueCurrentCount = 7;
        vm.ProcessedImageCount = 1234;
        vm.PushRecentPartInspectionResult(true);
        vm.PushRecentPartInspectionResult(false);
        vm.PushRecentPartInspectionResult(true);

        Assert(channel.Reader.Count == 3, $"Hàng đợi giả lập phải có 3 frame, thực tế {channel.Reader.Count}");
        Assert(vm.RecentPartsTotalCount == 3, $"Thanh 20 nấc phải có 3 nấc, thực tế {vm.RecentPartsTotalCount}");

        vm.ResetQueueAndCounters();

        Assert(channel.Reader.Count == 0,
            $"Hàng đợi phải được XẢ SẠCH, còn lại {channel.Reader.Count} frame");
        Assert(frameMats.All(m => m.IsDisposed),
            "Mọi Mat của frame bị xả phải được Dispose() để không rò rỉ bộ nhớ native (~60MB/frame)");
        Assert(vm.QueueCurrentCount == 0, $"QueueCurrentCount phải về 0, thực tế {vm.QueueCurrentCount}");
        Assert(vm.ProcessedImageCount == 0, $"ProcessedImageCount (Count) phải về 0, thực tế {vm.ProcessedImageCount}");
        Assert(vm.RecentPartsTotalCount == 0,
            $"Thanh 20 con hàng gần nhất phải được reset về 0, thực tế {vm.RecentPartsTotalCount}");
        Assert(vm.RecentPartsOkCount == 0 && vm.RecentPartsNgCount == 0,
            "Số OK/NG của thanh 20 nấc phải về 0");
        Assert(vm.QueueStatusText == "0/16",
            $"Nhãn queue sau reset phải là '0/16', thực tế '{vm.QueueStatusText}'");
        Assert(vm.QueueToolTipText.Contains("Dropped): 0"),
            "Bộ đếm frame bị rớt (Dropped) phải được reset về 0");

        Console.WriteLine("PASSED (3 frame được dispose, mọi bộ đếm về 0)");
    }

    // ==================================================================
    // 2. Reset phải CHỐT phiên Lịch sử kiểm tra cũ và mở phiên MỚI
    // ==================================================================
    private static async Task Test_02_ResetSessionAndQueue_OpensFreshInspectionLogSession()
    {
        Console.Write("--- [2/6] Reset phải chốt phiên Lịch sử kiểm tra cũ & mở phiên MỚI... ");

        var vm = new ToolEditorViewModel();
        var config = new VisionConfig { ProductName = "PERF_RESET_TEST", PixelsPerMm = 10.0 };

        InspectionSessionRecord? first = null;
        InspectionSessionRecord? second = null;

        try
        {
            // Lần reset thứ nhất: chưa có phiên nào -> mở phiên mới.
            await vm.RestartInspectionLogSessionAsync();
            first = vm.InspectionLogService.CurrentSession;
            Assert(first is not null, "Reset phải mở được phiên mới trong Lịch sử kiểm tra");

            // Ghi 1 con hàng vào phiên thứ nhất.
            vm.InspectionLogService.EnqueueInspectionResult(new InspectionResult { Pass = true }, config, 1);

            // Lần reset thứ hai: phải CHỐT phiên thứ nhất (drain part rồi lưu) và mở phiên mới.
            await vm.RestartInspectionLogSessionAsync();
            second = vm.InspectionLogService.CurrentSession;

            Assert(second is not null, "Sau reset phải có phiên mới đang mở");
            Assert(!ReferenceEquals(first, second) && first!.Id != second!.Id,
                $"Reset phải mở phiên MỚI (id khác), cũ='{first.Id}' mới='{second.Id}'");
            Assert(!first.IsRunning, "Phiên cũ phải được CHỐT (IsRunning = false)");

            var allSessions = await vm.InspectionLogService.GetAllSessionsAsync();
            Assert(allSessions.Any(s => s.Id == first.Id),
                "Phiên cũ phải được lưu lại trong Lịch sử kiểm tra (không bị mất khi reset)");

            var oldParts = await vm.InspectionLogService.GetPartsForSessionAsync(first.Id);
            Assert(oldParts.Count == 1,
                $"Con hàng của phiên cũ phải được drain & lưu trước khi chốt, thực tế {oldParts.Count}");

            var newParts = await vm.InspectionLogService.GetPartsForSessionAsync(second.Id);
            Assert(newParts.Count == 0,
                $"Phiên mới phải bắt đầu từ 0 con hàng (số liệu SPC/CPK reset), thực tế {newParts.Count}");

            Console.WriteLine($"PASSED ('{first.SessionCode}' đã chốt, phiên mới '{second.SessionCode}' bắt đầu từ 0)");
        }
        finally
        {
            try
            {
                if (first is not null) await vm.InspectionLogService.DeleteSessionAsync(first.Id);
                if (second is not null) await vm.InspectionLogService.DeleteSessionAsync(second.Id);
            }
            catch { }

            try { vm.InspectionLogService.Dispose(); } catch { }
        }
    }

    // ==================================================================
    // 3. Command phải tồn tại đúng tên mà XAML đang bind
    // ==================================================================
    private static void Test_03_ResetCommand_IsExposedForXamlBinding()
    {
        Console.Write("--- [3/6] Command 'ResetSessionAndQueueCommand' phải tồn tại để XAML bind... ");

        var prop = typeof(ToolEditorViewModel).GetProperty("ResetSessionAndQueueCommand");
        Assert(prop is not null,
            "Không tìm thấy property 'ResetSessionAndQueueCommand' — nút Reset trong ToolEditorView.xaml sẽ không hoạt động");

        var vm = new ToolEditorViewModel();
        Assert(vm.ResetSessionAndQueueCommand is not null,
            "ResetSessionAndQueueCommand phải có giá trị (không null) trên instance");

        Console.WriteLine("PASSED");
    }

    // ==================================================================
    // 4. "Đóng Job" chỉ xóa dòng trạng thái "Đã nạp Job"
    // ==================================================================
    private static void Test_04_CloseLoadedJob_RemovesOnlyPendingJobHistoryRows()
    {
        Console.Write("--- [4/6] Nút 'Đóng Job' chỉ xóa dòng 'Đã nạp Job', giữ nguyên kết quả PASS/NG... ");

        var pending1 = new OqcScanHistoryEntry { ScannedCode = "A", InspectResult = "Đã nạp Job" };
        var pending2 = new OqcScanHistoryEntry { ScannedCode = "B", InspectResult = "  đã NẠP job  " };
        var pass = new OqcScanHistoryEntry { ScannedCode = "C", InspectResult = "PASS" };
        var ng = new OqcScanHistoryEntry { ScannedCode = "D", InspectResult = "NG" };
        var running = new OqcScanHistoryEntry { ScannedCode = "E", InspectResult = "Đang kiểm tra..." };
        var empty = new OqcScanHistoryEntry { ScannedCode = "F", InspectResult = "" };

        var history = new List<OqcScanHistoryEntry> { pending1, pass, pending2, ng, running, empty };

        var pending = OqcScannerViewModel.FindPendingJobEntries(history);
        Assert(pending.Count == 2,
            $"Phải nhận diện đúng 2 dòng 'Đã nạp Job' (không phân biệt hoa/thường & khoảng trắng), thực tế {pending.Count}");

        var removed = OqcScannerViewModel.RemoveHistoryEntries(history, pending);
        Assert(removed == 2, $"Phải xóa đúng 2 dòng, thực tế {removed}");
        Assert(history.Count == 4, $"Phải còn lại 4 dòng, thực tế {history.Count}");
        Assert(!history.Contains(pending1) && !history.Contains(pending2),
            "Hai dòng 'Đã nạp Job' phải bị xóa");

        foreach (var keep in new[] { pass, ng, running, empty })
        {
            Assert(history.Contains(keep),
                $"Dòng '{keep.InspectResult}' ({keep.ScannedCode}) KHÔNG được xóa — đây là kết quả kiểm tra thật");
        }

        // Không có dòng nạp Job nào -> không xóa gì (nút vẫn bấm được nhưng phải an toàn).
        var removedAgain = OqcScannerViewModel.RemoveHistoryEntries(
            history, OqcScannerViewModel.FindPendingJobEntries(history));
        Assert(removedAgain == 0, $"Lần 2 phải không xóa thêm dòng nào, thực tế {removedAgain}");
        Assert(history.Count == 4, "Sau lần 2, số dòng phải giữ nguyên là 4");

        Console.WriteLine("PASSED (chỉ 2 dòng 'Đã nạp Job' bị xóa, 4 dòng kết quả thật được giữ)");
    }

    // ==================================================================
    // 5. Xóa chọn lọc: chỉ xóa đúng các dòng user chọn
    // ==================================================================
    private static void Test_05_SelectiveDelete_RemovesOnlyChosenRows()
    {
        Console.Write("--- [5/6] Xóa chọn lọc trong Lịch Sử Quét Mã OQC... ");

        var a = new OqcScanHistoryEntry { ScannedCode = "A", InspectResult = "PASS" };
        var b = new OqcScanHistoryEntry { ScannedCode = "B", InspectResult = "NG" };
        var c = new OqcScanHistoryEntry { ScannedCode = "C", InspectResult = "PASS" };
        var history = new List<OqcScanHistoryEntry> { a, b, c };

        // 5a. Xóa đúng 1 dòng (nút 🗑️ trên từng hàng).
        var removed = OqcScannerViewModel.RemoveHistoryEntries(history, new[] { b });
        Assert(removed == 1, $"Phải xóa đúng 1 dòng, thực tế {removed}");
        Assert(history.Count == 2 && !history.Contains(b), "Dòng B phải bị xóa, A và C còn lại");
        Assert(history.Contains(a) && history.Contains(c), "A và C không được xóa");

        // 5b. Xóa nhiều dòng đã chọn cùng lúc (nút "Xóa Dòng Đã Chọn").
        removed = OqcScannerViewModel.RemoveHistoryEntries(history, new[] { a, c });
        Assert(removed == 2, $"Phải xóa đúng 2 dòng, thực tế {removed}");
        Assert(history.Count == 0, $"Lịch sử phải rỗng, thực tế {history.Count}");

        // 5c. Dòng không thuộc lịch sử (vd: đã bị xóa trước đó) -> bỏ qua, không ném lỗi.
        var removedOutsider = OqcScannerViewModel.RemoveHistoryEntries(
            history, new[] { new OqcScanHistoryEntry { ScannedCode = "X", InspectResult = "PASS" } });
        Assert(removedOutsider == 0, $"Dòng không tồn tại phải bị bỏ qua, thực tế xóa {removedOutsider}");

        // 5d. Danh sách null -> an toàn, trả về 0.
        Assert(OqcScannerViewModel.RemoveHistoryEntries(null, new[] { a }) == 0, "history null phải trả về 0");
        Assert(OqcScannerViewModel.RemoveHistoryEntries(history, null) == 0, "targets null phải trả về 0");

        // 5e. Command sinh ra từ [RelayCommand] phải tồn tại để XAML/nút bấm bind được.
        var closeJobProp = typeof(OqcScannerViewModel).GetProperty("CloseLoadedJobCommand");
        Assert(closeJobProp is not null,
            "Không tìm thấy 'CloseLoadedJobCommand' — nút 'Đóng Job' trên OqcScannerView.xaml sẽ không hoạt động");

        Console.WriteLine("PASSED (xóa từng dòng + xóa nhiều dòng + an toàn với dữ liệu không hợp lệ)");
    }

    // ==================================================================
    // 6. Thanh Queue phải là 16 nấc khớp giao diện
    // ==================================================================
    private static void Test_06_QueueCapacity_Matches16SlotUi()
    {
        Console.Write("--- [6/6] Thanh Queue phải là 16 nấc khớp giao diện... ");

        var vm = new ToolEditorViewModel();
        Assert(vm.QueueCapacity == 16,
            $"QueueCapacity phải là 16 (khớp 16 slot trên UI), thực tế {vm.QueueCapacity}");

        vm.QueueCurrentCount = 16;
        Assert(vm.QueueSlot15Active, "Slot thứ 16 phải sáng khi hàng đợi đầy 16/16");
        Assert(vm.QueueStatusText == "16/16", $"Nhãn queue phải là '16/16', thực tế '{vm.QueueStatusText}'");

        Console.WriteLine("PASSED");
    }

    // ==================================================================
    // Helpers
    // ==================================================================
    private static Channel<ContinuousFrameEnvelope> CreateFrameQueue(
        int capacity, int itemCount, List<Mat> capturedMats)
    {
        var channel = Channel.CreateBounded<ContinuousFrameEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        for (var i = 0; i < itemCount; i++)
        {
            var mat = new Mat(24, 24, MatType.CV_8UC3, Scalar.All(20 + i * 10));
            capturedMats.Add(mat);

            channel.Writer.TryWrite(new ContinuousFrameEnvelope
            {
                Frame = mat,
                Metadata = new FrameMetadata { FrameIndex = i }
            });
        }

        return channel;
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

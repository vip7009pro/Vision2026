using System;
using System.Reflection;
using System.Threading;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử cho Task 431:
///  1. Chạy liên tục khi uncheck Render Canvas:
///     - Hiển thị ảnh tĩnh "OK" to khi kết quả đạt (Pass).
///     - Tự động hiển thị canvas khi gặp kết quả lỗi (NG).
///     - Tiếp tục hiển thị lại ảnh tĩnh "OK" khi sản phẩm tiếp theo đạt.
///  2. Bấm "Quay lại Live" từ chế độ xem lại ảnh NG lịch sử:
///     - Tự động kích hoạt sự kiện RequestAutoFitImagePreview để ResetView/AutoFit.
///  3. Widget Tốc độ tức thời (pcs/s):
///     - Đo đạc chính xác theo Rolling Window giữa các con hàng.
///     - Tự động về 0.0 khi máy dừng (> 1.5s).
///     - Reset về 0.0 khi dừng chạy liên tục.
/// </summary>
public static class ToolEditorContinuousRenderAndSpeedTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("⚡ RUNNING TOOL EDITOR CONTINUOUS PREVIEW & SPEED TESTS");
        Console.WriteLine("=========================================================");

        Test_01_StaticOkImage_CreatedAndValid();
        Test_02_ExitRecentPartView_RaisesAutoFitEvent();
        Test_03_Continuous_UncheckCanvas_ShowsStaticOkWhenPass();
        Test_04_Continuous_UncheckCanvas_RendersCanvasWhenNg();
        Test_05_Continuous_UncheckCanvas_ResumesStaticOkAfterNg();
        Test_06_InstantaneousSpeed_CalculationAndRollingWindow();
        Test_07_InstantaneousSpeed_DropsToZeroWhenPaused();
        Test_08_InstantaneousSpeed_ResetsOnStop();

        Console.WriteLine("✅ ALL TOOL EDITOR CONTINUOUS PREVIEW & SPEED TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void InjectField(object target, string fieldName, object? value)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            type = type.BaseType;
        }
        if (field is null) throw new MissingFieldException($"Field '{fieldName}' không tồn tại trên {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static object? GetField(object target, string fieldName)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            type = type.BaseType;
        }
        if (field is null) throw new MissingFieldException($"Field '{fieldName}' không tồn tại trên {target.GetType().Name}.");
        return field.GetValue(target);
    }

    private static void InvokeMethod(object target, string methodName, params object[] args)
    {
        var type = target.GetType();
        MethodInfo? method = null;
        while (type is not null && method is null)
        {
            method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            type = type.BaseType;
        }
        if (method is null) throw new MissingMethodException($"Method '{methodName}' không tồn tại trên {target.GetType().Name}.");
        method.Invoke(target, args);
    }

    // 1. Ảnh tĩnh "OK" được tạo thành công, hợp lệ, đóng băng và có thể tái sử dụng
    private static void Test_01_StaticOkImage_CreatedAndValid()
    {
        Console.Write("--- [1/8] Kiểm tra ảnh tĩnh OK được tạo hợp lệ và cache... ");

        var img1 = ToolEditorViewModel.GetOrCreateStaticOkImage();
        Assert(img1 != null, "Ảnh tĩnh OK không được null");
        Assert(img1.IsFrozen, "Ảnh tĩnh OK phải được Freeze để an toàn đa luồng");
        Assert(img1.PixelWidth == 1280, $"Chiều rộng ảnh phải là 1280, thực tế: {img1.PixelWidth}");
        Assert(img1.PixelHeight == 720, $"Chiều cao ảnh phải là 720, thực tế: {img1.PixelHeight}");

        var img2 = ToolEditorViewModel.GetOrCreateStaticOkImage();
        Assert(ReferenceEquals(img1, img2), "Ảnh tĩnh OK phải được cache dùng lại (cùng instance)");

        Console.WriteLine("PASSED");
    }

    // 2. Thoát xem lại ảnh lịch sử kích hoạt sự kiện RequestAutoFitImagePreview
    private static void Test_02_ExitRecentPartView_RaisesAutoFitEvent()
    {
        Console.Write("--- [2/8] Thoát xem lại ảnh lịch sử (ExitRecentPartView) bắn sự kiện AutoFit... ");

        var vm = new ToolEditorViewModel();
        bool eventFired = false;
        vm.RequestAutoFitImagePreview += () => eventFired = true;

        vm.IsViewingHistoricalRecentPart = true;
        vm.SelectedRecentSlotIndex = 3;

        vm.ExitRecentPartView();

        Assert(eventFired, "Sự kiện RequestAutoFitImagePreview phải được kích hoạt khi ExitRecentPartView()");
        Assert(!vm.IsViewingHistoricalRecentPart, "IsViewingHistoricalRecentPart phải là false");
        Assert(vm.SelectedRecentSlotIndex == -1, "SelectedRecentSlotIndex phải được đặt về -1");

        Console.WriteLine("PASSED");
    }

    // 3. Chạy liên tục khi uncheck Render Canvas: Kết quả OK -> hiển thị ảnh tĩnh "OK"
    private static void Test_03_Continuous_UncheckCanvas_ShowsStaticOkWhenPass()
    {
        Console.Write("--- [3/8] Chạy liên tục khi bỏ Render Canvas: Sản phẩm OK hiển thị ảnh tĩnh OK... ");

        var vm = new ToolEditorViewModel();
        vm.EnableCanvasRendering = false;
        InjectField(vm, "_isRunningFolderFlow", true); // IsRunningContinuous = true

        // Tạo kết quả Pass
        var passResult = new InspectionResult { Pass = true };
        InjectField(vm, "_lastRun", passResult);

        // Gọi RefreshPreviewsCore
        InvokeMethod(vm, "RefreshPreviewsCore");

        var staticOk = ToolEditorViewModel.GetOrCreateStaticOkImage();
        Assert(ReferenceEquals(vm.SelectedNodePreviewImage, staticOk), "SelectedNodePreviewImage phải là ảnh tĩnh OK");
        Assert(vm.SelectedNodeOverlayItems == null, "SelectedNodeOverlayItems phải là null khi hiển thị ảnh tĩnh");

        Console.WriteLine("PASSED");
    }

    // 4. Chạy liên tục khi uncheck Render Canvas: Kết quả NG -> hiển thị canvas cho con NG
    private static void Test_04_Continuous_UncheckCanvas_RendersCanvasWhenNg()
    {
        Console.Write("--- [4/8] Chạy liên tục khi bỏ Render Canvas: Sản phẩm NG hiển thị canvas lỗi... ");

        var vm = new ToolEditorViewModel();
        vm.EnableCanvasRendering = false;
        InjectField(vm, "_isRunningFolderFlow", true); // IsRunningContinuous = true

        // Tạo ảnh giả lập
        using var testMat = new Mat(480, 640, MatType.CV_8UC3, new Scalar(100, 100, 100));
        var sharedCtx = (SharedImageContext)GetField(vm, "_sharedImage")!;
        sharedCtx.SetImage(testMat);

        // Tạo kết quả Fail (NG)
        var ngResult = new InspectionResult { Pass = false };
        InjectField(vm, "_lastRun", ngResult);

        // Gọi RefreshPreviewsCore
        InvokeMethod(vm, "RefreshPreviewsCore");

        var staticOk = ToolEditorViewModel.GetOrCreateStaticOkImage();
        Assert(!ReferenceEquals(vm.SelectedNodePreviewImage, staticOk), "Khi NG không được hiển thị ảnh tĩnh OK");
        Assert(vm.SelectedNodePreviewImage != null, "SelectedNodePreviewImage phải chứa ảnh canvas thực tế của con NG");

        Console.WriteLine("PASSED");
    }

    // 5. Chạy liên tục khi uncheck Render Canvas: Sau khi gặp NG, sản phẩm tiếp theo OK -> quay lại ảnh tĩnh
    private static void Test_05_Continuous_UncheckCanvas_ResumesStaticOkAfterNg()
    {
        Console.Write("--- [5/8] Sau con NG, con hàng tiếp theo OK quay lại ảnh tĩnh OK... ");

        var vm = new ToolEditorViewModel();
        vm.EnableCanvasRendering = false;
        InjectField(vm, "_isRunningFolderFlow", true);

        using var testMat = new Mat(480, 640, MatType.CV_8UC3, new Scalar(100, 100, 100));
        var sharedCtx = (SharedImageContext)GetField(vm, "_sharedImage")!;
        sharedCtx.SetImage(testMat);

        // Frame 1: NG
        var ngResult = new InspectionResult { Pass = false };
        InjectField(vm, "_lastRun", ngResult);
        InvokeMethod(vm, "RefreshPreviewsCore");
        Assert(!ReferenceEquals(vm.SelectedNodePreviewImage, ToolEditorViewModel.GetOrCreateStaticOkImage()), "Phải hiện ảnh NG");

        // Frame 2: OK
        var passResult = new InspectionResult { Pass = true };
        InjectField(vm, "_lastRun", passResult);
        InvokeMethod(vm, "RefreshPreviewsCore");

        var staticOk = ToolEditorViewModel.GetOrCreateStaticOkImage();
        Assert(ReferenceEquals(vm.SelectedNodePreviewImage, staticOk), "Sau khi con tiếp theo OK, preview phải quay lại ảnh tĩnh OK");
        Assert(vm.SelectedNodeOverlayItems == null, "Overlay phải bị xóa bỏ khi quay lại ảnh tĩnh");

        Console.WriteLine("PASSED");
    }

    // 6. Tính toán tốc độ tức thời qua Rolling Window
    private static void Test_06_InstantaneousSpeed_CalculationAndRollingWindow()
    {
        Console.Write("--- [6/8] Đo đạc tốc độ tức thời chính xác theo Rolling Window... ");

        var vm = new ToolEditorViewModel();
        var sw = (System.Diagnostics.Stopwatch)GetField(vm, "_continuousStopwatch")!;
        sw.Restart();
        vm.ProcessedImageCount = 5;

        // Giả lập 5 con hàng hoàn thành cách nhau 50ms (tương ứng 20 pcs/s)
        long baseTick = System.Diagnostics.Stopwatch.GetTimestamp();
        long stepTick = (long)(System.Diagnostics.Stopwatch.Frequency * 0.050); // 50ms per piece

        var queue = (System.Collections.Generic.Queue<long>)GetField(vm, "_instantaneousStopwatchTicks")!;

        lock (GetField(vm, "_instantaneousSpeedLock")!)
        {
            queue.Clear();
            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(baseTick + i * stepTick);
            }
            InjectField(vm, "_lastPieceCompletionTimestamp", baseTick + 4 * stepTick);
        }

        InvokeMethod(vm, "UpdateContinuousStats");

        // Tốc độ 4 khoảng / 200ms = 20 pcs/s
        Assert(!string.IsNullOrEmpty(vm.InstantaneousSpeedText), "InstantaneousSpeedText không được rỗng");
        double parsedSpeed = double.Parse(vm.InstantaneousSpeedText);
        Assert(Math.Abs(parsedSpeed - 20.0) < 1.0, $"Tốc độ tức thời tính được phải ~20.0 pcs/s, thực tế: {parsedSpeed}");

        Console.WriteLine("PASSED");
    }

    // 7. Tốc độ tức thời tự động về 0 khi máy dừng > 1.5s
    private static void Test_07_InstantaneousSpeed_DropsToZeroWhenPaused()
    {
        Console.Write("--- [7/8] Tốc độ tức thời tự động về 0 khi máy dừng (>1.5s)... ");

        var vm = new ToolEditorViewModel();
        var sw = (System.Diagnostics.Stopwatch)GetField(vm, "_continuousStopwatch")!;
        sw.Restart();
        vm.ProcessedImageCount = 10;

        // Giả lập con hàng cuối đã xong cách đây 2 giây
        long oldTick = System.Diagnostics.Stopwatch.GetTimestamp() - (long)(System.Diagnostics.Stopwatch.Frequency * 2.0);
        var queue = (System.Collections.Generic.Queue<long>)GetField(vm, "_instantaneousStopwatchTicks")!;
        lock (GetField(vm, "_instantaneousSpeedLock")!)
        {
            queue.Clear();
            queue.Enqueue(oldTick - 1000);
            queue.Enqueue(oldTick);
            InjectField(vm, "_lastPieceCompletionTimestamp", oldTick);
        }

        InvokeMethod(vm, "UpdateContinuousStats");

        Assert(vm.InstantaneousSpeedText == "0.0", $"Khi máy dừng >1.5s, InstantaneousSpeedText phải là 0.0, thực tế: {vm.InstantaneousSpeedText}");

        Console.WriteLine("PASSED");
    }

    // 8. Tốc độ tức thời reset về 0 khi dừng chạy liên tục
    private static void Test_08_InstantaneousSpeed_ResetsOnStop()
    {
        Console.Write("--- [8/8] Tốc độ tức thời reset về 0 khi dừng chạy liên tục... ");

        var vm = new ToolEditorViewModel();
        vm.InstantaneousSpeedText = "35.2";

        InvokeMethod(vm, "StopContinuousFlow");

        Assert(vm.InstantaneousSpeedText == "0.0", $"Sau khi StopContinuousFlow, InstantaneousSpeedText phải là 0.0, thực tế: {vm.InstantaneousSpeedText}");

        Console.WriteLine("PASSED");
    }
}

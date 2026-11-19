using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử cho tính năng:
///  1. Khi Chạy liên tục trên Tool Editor: chỉ hiển thị 2 cột Preview và Kết quả kiểm tra (50/50);
///     cột Kết quả kiểm tra chia đôi thành 2 hàng (hàng trên kết quả như thường, hàng dưới preview nấc được chọn).
///  2. Bấm chọn nấc trên thanh 20 nấc khi đang Chạy liên tục: giữ nguyên Live View bên Preview chính,
///     hiển thị preview của nấc được chọn ở hàng dưới cột Kết quả kiểm tra.
///  3. Nút "Ghim nấc đã chọn": dù đang Live View, nếu có ảnh NG tại nấc được chọn sẽ show ảnh output
///     lên preview mới thêm và giữ cố định không bị ảnh OK ghi đè; tự động bắt NG mới khi lọt vào nấc đang ghim.
/// </summary>
public static class ToolEditorContinuousSlotPreviewTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("📌 RUNNING TOOL EDITOR CONTINUOUS SLOT PREVIEW & PIN TESTS");
        Console.WriteLine("=========================================================");

        Test_01_DebugColumnSplitsTwoRows5050OnContinuousAndRestoresOnStop();
        Test_02_SelectingSlotDuringContinuous_KeepsLiveView_ShowsInBottomPreview();
        Test_03_SelectingSlotWhenStopped_KeepsMainHistoricalPreviewBehavior();
        Test_04_PinSelectedSlotWithNgOutputFile_ShowsOutputImageDespiteLiveView_AndHoldsAgainstOk();
        Test_05_PinSelectedSlotWithNgRamSnapshot_BuildsOutputOverlays();
        Test_06_PinSelectedSlotWhileOk_AutoCapturesAndShowsNgOutputWhenNgReachesPinnedSlot();
        Test_07_Unpin_RestoresUnpinnedLiveTracking_AndPinWithoutSelectionIsSafe();
        Test_08_ResetAndCloseJob_ClearsPinAndSlotPreviewState();
        Test_09_AutoFindNearestNgCheckbox_IsDefaultChecked_AndPicksNearestNgAfterSelectedSlot();
        Test_10_AutoFindNearestNgCheckbox_FallsBackToSelectedSlotWhenAll20AreOk();
        Test_11_AutoFindNearestNgCheckbox_IgnoresNgBeforeSelectedSlot_OnlyLooksAtOrAfter();
        Test_12_AutoFindNearestNgCheckbox_ToggleOnOff_UpdatesPreviewAndInteractsWithPin();
        Test_13_Recent20SlotsBar_Numbered1To20LeftToRight_WithReadableForeground();
        Test_14_SlotIndexDisplay_Synced1To20_ForBarTitleAndStatus();

        Console.WriteLine("✅ ALL 14 TOOL EDITOR CONTINUOUS SLOT PREVIEW & PIN TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Test_01_DebugColumnSplitsTwoRows5050OnContinuousAndRestoresOnStop()
    {
        Console.Write("--- [1/8] Khi Chạy liên tục: chỉ hiện 2 cột & cột kết quả chia đôi 2 hàng (50/50)... ");

        var vm = new ToolEditorViewModel();
        Assert(vm.DebugResultsRowHeight.GridUnitType == GridUnitType.Star && vm.DebugResultsRowHeight.Value == 1.0,
            "Ban đầu hàng kết quả phải chiếm trọn (1*)");
        Assert(vm.DebugSlotPreviewRowHeight.Value == 0,
            $"Ban đầu hàng preview nấc phải ẩn (0), thực tế: {vm.DebugSlotPreviewRowHeight.Value}");
        Assert(vm.DebugSlotPreviewMinHeight == 0, "Ban đầu DebugSlotPreviewMinHeight phải là 0");

        vm.IsRunningFolderFlow = true;

        Assert(vm.IsRunningContinuous, "IsRunningContinuous phải là true");
        Assert(!vm.IsToolboxColumnVisible && !vm.IsCanvasColumnVisible && vm.IsDebugColumnVisible,
            "Chỉ hiển thị 2 cột Preview và Kết quả kiểm tra");
        Assert(vm.PreviewColumnWidth.Value == 1.0 && vm.DebugColumnWidth.Value == 1.0,
            "2 cột Preview và Kết quả kiểm tra phải chia 50/50 (1*:1*)");
        Assert(vm.DebugResultsRowHeight.GridUnitType == GridUnitType.Star && vm.DebugResultsRowHeight.Value == 1.0,
            "Hàng trên (kết quả kiểm tra) phải là 1.0*");
        Assert(vm.DebugSlotPreviewRowHeight.GridUnitType == GridUnitType.Star && vm.DebugSlotPreviewRowHeight.Value == 1.0,
            "Hàng dưới (preview nấc 20) phải là 1.0* (chia đôi cột)");
        Assert(vm.DebugSlotPreviewMinHeight >= 100, "DebugSlotPreviewMinHeight phải >= 100 khi chạy liên tục");

        vm.IsRunningFolderFlow = false;

        Assert(vm.IsToolboxColumnVisible && vm.IsCanvasColumnVisible, "Khi dừng phải hiện lại Cột 1 & 2");
        Assert(vm.DebugResultsRowHeight.GridUnitType == GridUnitType.Star && vm.DebugResultsRowHeight.Value == 1.0,
            "Khi dừng hàng trên kết quả vẫn là 1.0*");
        Assert(vm.DebugSlotPreviewRowHeight.Value == 0,
            $"Khi dừng hàng dưới preview nấc phải thu về 0, thực tế: {vm.DebugSlotPreviewRowHeight.Value}");
        Assert(vm.DebugSlotPreviewMinHeight == 0, "Khi dừng DebugSlotPreviewMinHeight phải về 0");

        Console.WriteLine("PASSED");
    }

    private static void Test_02_SelectingSlotDuringContinuous_KeepsLiveView_ShowsInBottomPreview()
    {
        Console.Write("--- [2/8] Chọn nấc khi đang Chạy liên tục: giữ Live View bên trái, hiện preview nấc ở hàng dưới... ");

        var vm = new ToolEditorViewModel();
        vm.EnableCanvasRendering = false;
        vm.IsRunningFolderFlow = true;

        var staticOk = ToolEditorViewModel.GetOrCreateStaticOkImage();
        Assert(ReferenceEquals(vm.SelectedNodePreviewImage, staticOk), "Preview chính phải đang ở Live View (Static OK)");

        bool autoFitFired = false;
        vm.RequestAutoFitRecentSlotPreview += () => autoFitFired = true;

        using var okMat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(30, 180, 30));
        var okResult = new InspectionResult { Pass = true };
        vm.PushRecentPartInspectionResult(okResult, okMat);

        vm.SelectRecentPartSlotCommand.Execute("0");

        Assert(!vm.IsViewingHistoricalRecentPart,
            "Chọn nấc khi đang Chạy liên tục KHÔNG được tắt Live View ở Preview chính");
        Assert(ReferenceEquals(vm.SelectedNodePreviewImage, staticOk),
            "Preview chính phải tiếp tục giữ nguyên Live View");
        Assert(vm.SelectedRecentSlotIndex == 0, "SelectedRecentSlotIndex phải là 0");
        Assert(autoFitFired, "RequestAutoFitRecentSlotPreview phải được phát");
        Assert(vm.RecentSlotPreviewImage is BitmapSource bmp && bmp.PixelWidth == 320 && bmp.PixelHeight == 240,
            "Preview mới thêm ở hàng dưới phải hiển thị ảnh của nấc #0");

        Console.WriteLine("PASSED");
    }

    private static void Test_03_SelectingSlotWhenStopped_KeepsMainHistoricalPreviewBehavior()
    {
        Console.Write("--- [3/8] Chọn nấc khi KHÔNG chạy liên tục: giữ nguyên hành vi xem lịch sử trên Preview chính... ");

        var vm = new ToolEditorViewModel();
        Assert(!vm.IsRunningContinuous, "Ban đầu không chạy liên tục");

        using var ngMat = new Mat(200, 300, MatType.CV_8UC3, new Scalar(20, 20, 220));
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);

        vm.SelectRecentPartSlotCommand.Execute("0");

        Assert(vm.IsViewingHistoricalRecentPart, "Khi dừng máy, chọn nấc phải bật IsViewingHistoricalRecentPart như cũ");
        Assert(vm.SelectedNodePreviewImage is BitmapSource bmp && bmp.PixelWidth == 300 && bmp.PixelHeight == 200,
            "Preview chính phải hiển thị ảnh nấc #0 khi không chạy liên tục");

        Console.WriteLine("PASSED");
    }

    private static void Test_04_PinSelectedSlotWithNgOutputFile_ShowsOutputImageDespiteLiveView_AndHoldsAgainstOk()
    {
        Console.Write("--- [4/8] Ghim nấc đã chọn (có file ảnh Output NG): show ảnh output dù đang Live View & giữ cố định... ");

        var tempFile = Path.Combine(Path.GetTempPath(), $"vision_pin_test_{Guid.NewGuid():N}.png");
        try
        {
            using (var outputMat = new Mat(420, 640, MatType.CV_8UC3, new Scalar(0, 0, 255)))
            {
                Cv2.PutText(outputMat, "NG OUTPUT", new OpenCvSharp.Point(40, 200), HersheyFonts.HersheySimplex, 1.5, Scalar.White, 3);
                Cv2.ImWrite(tempFile, outputMat);
            }

            var vm = new ToolEditorViewModel();
            vm.EnableCanvasRendering = false;
            vm.IsRunningFolderFlow = true;

            using var rawNgMat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(10, 10, 10));
            var ngResult = new InspectionResult { Pass = false };
            ngResult.ImageOutputs.Add(new ImageOutputResult("IO_NG", true, tempFile, ""));
            vm.PushRecentPartInspectionResult(ngResult, rawNgMat);

            // Chọn nấc #0 và bấm Ghim khi Live View vẫn đang chạy
            vm.SelectRecentPartSlotCommand.Execute("0");
            vm.TogglePinRecentSlotCommand.Execute(null);

            Assert(vm.IsPinnedRecentSlot, "IsPinnedRecentSlot phải là true");
            Assert(vm.PinnedRecentSlotIndex == 0, "PinnedRecentSlotIndex phải là 0");
            Assert(!vm.IsViewingHistoricalRecentPart, "Preview chính vẫn phải ở chế độ Live View");
            Assert(vm.RecentSlotPreviewImage is BitmapSource outBmp && outBmp.PixelWidth == 640 && outBmp.PixelHeight == 420,
                $"Preview mới thêm phải show đúng ảnh Output ({tempFile}) kích thước 640x420");

            // Đẩy tiếp nhiều sản phẩm OK trong Live View -> ảnh Output NG đã ghim KHÔNG bị ghi đè bởi OK
            using var okMat = new Mat(120, 160, MatType.CV_8UC3, new Scalar(0, 200, 0));
            for (int i = 0; i < 5; i++)
            {
                vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
            }

            Assert(vm.RecentSlotPreviewImage is BitmapSource heldBmp && heldBmp.PixelWidth == 640 && heldBmp.PixelHeight == 420,
                "Sau khi Live View đẩy thêm 5 con OK, ảnh Output NG đang ghim vẫn phải được giữ nguyên!");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }

        Console.WriteLine("PASSED");
    }

    private static void Test_05_PinSelectedSlotWithNgRamSnapshot_BuildsOutputOverlays()
    {
        Console.Write("--- [5/8] Ghim nấc NG khi không có file đĩa (RAM snapshot): tự dựng overlay output đầy đủ... ");

        var vm = new ToolEditorViewModel();
        vm.ShowResultOverlay = false; // Kể cả khi tắt overlay ở preview chính, preview ghim vẫn dựng overlay kết quả
        vm.IsRunningFolderFlow = true;

        using var ngMat = new Mat(480, 640, MatType.CV_8UC3, new Scalar(40, 40, 40));
        var ngResult = new InspectionResult { Pass = false };
        ngResult.Points.Add(new PointMatchResult(
            "P1", new Point2d(120, 150), new OpenCvSharp.Rect(100, 130, 40, 40), 0.42, 0.7, false, 0.0));

        vm.PushRecentPartInspectionResult(ngResult, ngMat);
        vm.SelectRecentPartSlotCommand.Execute("0");
        vm.TogglePinRecentSlotCommand.Execute(null);

        Assert(vm.IsPinnedRecentSlot, "IsPinnedRecentSlot phải là true");
        Assert(vm.RecentSlotPreviewImage is BitmapSource bmp && bmp.PixelWidth == 640 && bmp.PixelHeight == 480,
            "Phải hiển thị ảnh NG từ RAM snapshot");
        Assert(vm.RecentSlotOverlayItems != null && vm.RecentSlotOverlayItems.Count > 0,
            "Phải dựng sẵn OverlayItems kết quả kiểm tra cho ảnh output NG");

        Console.WriteLine("PASSED");
    }

    private static void Test_06_PinSelectedSlotWhileOk_AutoCapturesAndShowsNgOutputWhenNgReachesPinnedSlot()
    {
        Console.Write("--- [6/8] Ghim nấc #2 khi đang OK -> tự động show ảnh output ngay khi NG trượt tới nấc #2... ");

        var tempNg1 = Path.Combine(Path.GetTempPath(), $"vision_pin_ng1_{Guid.NewGuid():N}.png");
        var tempNg2 = Path.Combine(Path.GetTempPath(), $"vision_pin_ng2_{Guid.NewGuid():N}.png");
        try
        {
            using (var m1 = new Mat(300, 500, MatType.CV_8UC3, new Scalar(0, 0, 200))) Cv2.ImWrite(tempNg1, m1);
            using (var m2 = new Mat(360, 520, MatType.CV_8UC3, new Scalar(0, 0, 255))) Cv2.ImWrite(tempNg2, m2);

            var vm = new ToolEditorViewModel();
            vm.IsRunningFolderFlow = true;

            using var okMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 200, 0));
            using var rawNgMat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(20, 20, 20));

            // Ban đầu có 3 con OK -> chọn nấc #2 và bấm Ghim (lúc này nấc #2 đang OK)
            for (int i = 0; i < 3; i++) vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);

            vm.SelectRecentPartSlotCommand.Execute("2");
            vm.TogglePinRecentSlotCommand.Execute(null);

            Assert(vm.IsPinnedRecentSlot && vm.PinnedRecentSlotIndex == 2, "Phải đang ghim nấc #2");
            Assert(vm.RecentSlotPreviewImage is BitmapSource b0 && b0.PixelWidth == 240, "Ban đầu đang hiện OK #2");

            // Đẩy 1 con NG #1 vào #0 (lúc này NG #1 ở #0, nấc #2 vẫn OK)
            var ng1 = new InspectionResult { Pass = false };
            ng1.ImageOutputs.Add(new ImageOutputResult("IO", true, tempNg1, ""));
            vm.PushRecentPartInspectionResult(ng1, rawNgMat);
            Assert(vm.RecentSlotPreviewImage is BitmapSource b1 && b1.PixelWidth == 240,
                "NG chưa tới nấc #2 nên chưa thay ảnh NG");

            // Đẩy 1 con OK -> NG #1 ở #1, nấc #2 vẫn OK
            vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
            Assert(vm.RecentSlotPreviewImage is BitmapSource b2 && b2.PixelWidth == 240,
                "NG đang ở #1 nên nấc #2 vẫn chưa có NG");

            // Đẩy thêm 1 con OK -> NG #1 trượt đúng vào nấc #2!
            vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
            Assert(vm.RecentSlotPreviewImage is BitmapSource b3 && b3.PixelWidth == 500 && b3.PixelHeight == 300,
                "Khi NG #1 trượt tới nấc #2 đang ghim, phải tự động show ảnh output 500x300 lên preview!");

            // Đẩy thêm nhiều OK nữa -> ảnh NG #1 vẫn giữ cố định
            for (int i = 0; i < 4; i++) vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
            Assert(vm.RecentSlotPreviewImage is BitmapSource b4 && b4.PixelWidth == 500 && b4.PixelHeight == 300,
                "Các con OK tiếp theo không được làm mất ảnh NG #1 đang ghim");

            // Đẩy NG #2 vào #0 rồi thêm 2 OK để NG #2 trượt tới nấc #2 -> cập nhật sang NG #2
            var ng2 = new InspectionResult { Pass = false };
            ng2.ImageOutputs.Add(new ImageOutputResult("IO", true, tempNg2, ""));
            vm.PushRecentPartInspectionResult(ng2, rawNgMat);
            vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
            vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);

            Assert(vm.RecentSlotPreviewImage is BitmapSource b5 && b5.PixelWidth == 520 && b5.PixelHeight == 360,
                "Khi có ảnh NG #2 mới tại nấc #2, preview ghim phải cập nhật sang ảnh Output NG #2 (520x360)");
        }
        finally
        {
            try { if (File.Exists(tempNg1)) File.Delete(tempNg1); } catch { }
            try { if (File.Exists(tempNg2)) File.Delete(tempNg2); } catch { }
        }

        Console.WriteLine("PASSED");
    }

    private static void Test_07_Unpin_RestoresUnpinnedLiveTracking_AndPinWithoutSelectionIsSafe()
    {
        Console.Write("--- [7/8] Bỏ ghim quay lại theo dõi live nấc chọn & bấm ghim khi chưa chọn nấc an toàn... ");

        var vm = new ToolEditorViewModel();
        vm.IsRunningFolderFlow = true;

        // Chưa chọn nấc nào -> bấm ghim phải an toàn, không bật IsPinnedRecentSlot
        vm.TogglePinRecentSlotCommand.Execute(null);
        Assert(!vm.IsPinnedRecentSlot, "Khi chưa chọn nấc nào, IsPinnedRecentSlot phải giữ false");
        Assert(vm.PinnedRecentSlotIndex == -1, "PinnedRecentSlotIndex phải là -1");

        // Chọn nấc #0 đang NG -> ghim -> sau đó đẩy OK -> bỏ ghim -> phải cập nhật về OK đang ở #0
        using var ngMat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(0, 0, 255));
        using var okMat = new Mat(150, 200, MatType.CV_8UC3, new Scalar(0, 255, 0));

        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
        vm.SelectRecentPartSlotCommand.Execute("0");
        vm.TogglePinRecentSlotCommand.Execute(null);
        Assert(vm.IsPinnedRecentSlot, "Phải ghim thành công");

        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        Assert(vm.RecentSlotPreviewImage is BitmapSource pinnedBmp && pinnedBmp.PixelWidth == 320,
            "Khi đang ghim vẫn phải giữ ảnh NG 320px");

        vm.TogglePinRecentSlotCommand.Execute(null);
        Assert(!vm.IsPinnedRecentSlot && vm.PinnedRecentSlotIndex == -1, "Sau khi bỏ ghim, cờ ghim phải tắt");
        // Khi bật mặc định tìm NG sau nấc chọn, #1 đang có NG nên preview hiển thị NG gần nhất (#1)
        Assert(vm.RecentSlotPreviewImage is BitmapSource nearestNgBmp && nearestNgBmp.PixelWidth == 320,
            "Khi bỏ ghim với checkbox bật, #1 vẫn đang NG nên preview hiển thị nấc #1 (320px)");

        // Khi tắt checkbox tìm NG sau nấc chọn -> quay về đúng ảnh OK 200px tại nấc #0
        vm.AutoFindNearestNgAfterSelectedSlot = false;
        Assert(vm.RecentSlotPreviewImage is BitmapSource liveBmp && liveBmp.PixelWidth == 200,
            "Khi tắt checkbox tìm NG sau nấc chọn, preview nấc #0 phải hiển thị đúng ảnh OK 200px tại #0");

        Console.WriteLine("PASSED");
    }

    private static void Test_08_ResetAndCloseJob_ClearsPinAndSlotPreviewState()
    {
        Console.Write("--- [8/8] Reset hàng đợi & CloseJob xóa sạch trạng thái ghim và preview nấc... ");

        var vm = new ToolEditorViewModel();
        vm.IsRunningFolderFlow = true;

        using var ngMat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(0, 0, 255));
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
        vm.SelectRecentPartSlotCommand.Execute("0");
        vm.TogglePinRecentSlotCommand.Execute(null);

        Assert(vm.IsPinnedRecentSlot && vm.RecentSlotPreviewImage != null, "Phải có ảnh & ghim trước khi Reset");

        vm.ResetQueueAndCounters();
        Assert(!vm.IsPinnedRecentSlot && vm.PinnedRecentSlotIndex == -1, "Reset phải tắt ghim");
        Assert(vm.SelectedRecentSlotIndex == -1, "Reset phải đặt SelectedRecentSlotIndex = -1");
        Assert(vm.RecentSlotPreviewImage == null, "Reset phải xóa RecentSlotPreviewImage");

        // Test CloseJob
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
        vm.SelectRecentPartSlotCommand.Execute("0");
        vm.TogglePinRecentSlotCommand.Execute(null);
        Assert(vm.IsPinnedRecentSlot && vm.RecentSlotPreviewImage != null, "Phải có ảnh & ghim trước khi CloseJob");

        vm.CloseJob();
        Assert(!vm.IsPinnedRecentSlot && vm.PinnedRecentSlotIndex == -1, "CloseJob phải tắt ghim");
        Assert(vm.RecentSlotPreviewImage == null, "CloseJob phải xóa RecentSlotPreviewImage");

        Console.WriteLine("PASSED");
    }

    private static void Test_09_AutoFindNearestNgCheckbox_IsDefaultChecked_AndPicksNearestNgAfterSelectedSlot()
    {
        Console.Write("--- [9/12] Checkbox mặc định Checked: chọn nấc OK nhưng có NG sau đó -> hiển thị nấc NG gần nhất... ");

        var vm = new ToolEditorViewModel();
        Assert(vm.AutoFindNearestNgAfterSelectedSlot, "Checkbox tìm NG sau nấc chọn phải mặc định là Checked (true)");
        vm.IsRunningFolderFlow = true;

        using var okMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 200, 0));
        using var ngOlderMat = new Mat(300, 410, MatType.CV_8UC3, new Scalar(0, 0, 220));
        using var ngNearerMat = new Mat(320, 460, MatType.CV_8UC3, new Scalar(0, 0, 255));

        // Đẩy vào luồng để có: #0=OK, #1=OK, #2=NG(460px), #3=OK, #4=NG(410px)
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngOlderMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngNearerMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);

        // Chọn nấc 1 (index 0, hiện đang OK)
        vm.SelectRecentPartSlotCommand.Execute("0");

        Assert(vm.RecentSlotPreviewImage is BitmapSource bmp && bmp.PixelWidth == 460 && bmp.PixelHeight == 320,
            "Khi nấc #1 (index 0) đang OK, phải tự động lấy ảnh của nấc NG gần nhất sau #1 là nấc #3 (index 2, 460x320)");
        Assert(vm.RecentSlotPreviewTitleText.Contains("#3") && vm.RecentSlotPreviewTitleText.Contains("#1"),
            $"Tiêu đề preview phải ghi rõ đang hiển thị nấc #3 (NG sau #1), thực tế: '{vm.RecentSlotPreviewTitleText}'");

        // Đẩy thêm 1 con OK -> nấc #3 (index 2) trượt sang #4 (index 3), preview vẫn bám theo nấc NG gần nhất đó (#4)
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        Assert(vm.RecentSlotPreviewImage is BitmapSource bmpShifted && bmpShifted.PixelWidth == 460,
            "Khi nấc NG trượt sang #4 (index 3), preview vẫn giữ hiển thị ảnh NG gần nhất đó");
        Assert(vm.RecentSlotPreviewTitleText.Contains("#4"),
            $"Tiêu đề preview phải cập nhật sang #4, thực tế: '{vm.RecentSlotPreviewTitleText}'");

        Console.WriteLine("PASSED");
    }

    private static void Test_10_AutoFindNearestNgCheckbox_FallsBackToSelectedSlotWhenAll20AreOk()
    {
        Console.Write("--- [10/12] Khi cả 20 nấc đều đang OK -> quay về hiển thị preview của nấc được chọn... ");

        var vm = new ToolEditorViewModel();
        vm.IsRunningFolderFlow = true;

        using var ngMat = new Mat(300, 440, MatType.CV_8UC3, new Scalar(0, 0, 255));
        using var okMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 200, 0));

        // Đẩy 1 NG rồi chọn nấc #0
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
        vm.SelectRecentPartSlotCommand.Execute("0");
        Assert(vm.RecentSlotPreviewImage is BitmapSource ngBmp && ngBmp.PixelWidth == 440,
            "Ban đầu phải đang hiện NG 440px");

        // Đẩy 19 con OK -> con NG ở #19 (nấc cuối cùng), vẫn còn NG trong 20 nấc nên vẫn hiện NG 440px
        for (int i = 0; i < 19; i++)
        {
            vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        }
        Assert(vm.RecentSlotPreviewImage is BitmapSource stillNgBmp && stillNgBmp.PixelWidth == 440,
            "Khi NG vẫn còn ở nấc #19, preview vẫn phải hiển thị NG đó");

        // Đẩy con OK thứ 20 -> toàn bộ 20 nấc đều là OK! Phải quay về preview OK của nấc được chọn (#0, 240px)
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        Assert(vm.RecentPartsNgCount == 0 && vm.RecentPartsOkCount == 20, "Cả 20 nấc đều phải là OK");
        Assert(vm.RecentSlotPreviewImage is BitmapSource okBmp && okBmp.PixelWidth == 240 && okBmp.PixelHeight == 180,
            "Khi cả 20 nấc đều OK, phải quay lại hiển thị preview của nấc được chọn #0 (240x180)");

        Console.WriteLine("PASSED");
    }

    private static void Test_11_AutoFindNearestNgCheckbox_IgnoresNgBeforeSelectedSlot_OnlyLooksAtOrAfter()
    {
        Console.Write("--- [11/12] Chỉ xét nấc được chọn hoặc các nấc SAU nấc được chọn (bỏ qua NG ở nấc trước đó)... ");

        var vm = new ToolEditorViewModel();
        vm.IsRunningFolderFlow = true;

        using var okMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 200, 0));
        using var ngBeforeMat = new Mat(300, 480, MatType.CV_8UC3, new Scalar(0, 0, 255));
        using var ngAfterMat = new Mat(340, 520, MatType.CV_8UC3, new Scalar(0, 0, 200));

        // Đẩy để có: #0=OK, #1=NG_before(480px), #2=OK, #3=OK, #4=OK
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngBeforeMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);

        // Chọn nấc #3 (các nấc #3, #4 đều OK; chỉ #1 trước #3 là NG)
        vm.SelectRecentPartSlotCommand.Execute("3");
        Assert(vm.RecentSlotPreviewImage is BitmapSource okAt3 && okAt3.PixelWidth == 240,
            "Khi chỉ có NG trước nấc #3 (#1) và từ #3 trở về sau đều OK, phải hiển thị đúng preview nấc #3 (240px)");

        // Đẩy thêm để có NG_after(520px) ở nấc #5 (sau nấc #3):
        // Tạo trực tiếp trạng thái: push NG_after rồi đẩy 5 OK cho trượt tới #5
        vm.ResetRecentPartsHistory();
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngAfterMat);
        for (int i = 0; i < 5; i++) vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        // Lúc này #0..#4=OK, #5=NG_after(520px)
        vm.SelectRecentPartSlotCommand.Execute("3");
        Assert(vm.RecentSlotPreviewImage is BitmapSource ngAt5 && ngAt5.PixelWidth == 520,
            "Khi có NG ở nấc #5 (sau nấc #3), phải tự động hiển thị ảnh NG tại nấc #5 (520px)");

        Console.WriteLine("PASSED");
    }

    private static void Test_12_AutoFindNearestNgCheckbox_ToggleOnOff_UpdatesPreviewAndInteractsWithPin()
    {
        Console.Write("--- [12/12] Bật/Tắt checkbox cập nhật preview tức thì & kết hợp chuẩn với nút Ghim... ");

        var vm = new ToolEditorViewModel();
        vm.IsRunningFolderFlow = true;

        using var okMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 200, 0));
        using var ngMat = new Mat(310, 470, MatType.CV_8UC3, new Scalar(0, 0, 255));

        // #0=OK, #1=OK, #2=NG(470px)
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);

        vm.SelectRecentPartSlotCommand.Execute("0");
        Assert(vm.RecentSlotPreviewImage is BitmapSource bOn && bOn.PixelWidth == 470,
            "Checkbox bật -> chọn #0 phải thấy NG ở #2 (470px)");

        // Tắt checkbox -> lập tức chuyển về đúng con hàng OK tại #0 (240px)
        vm.AutoFindNearestNgAfterSelectedSlot = false;
        Assert(vm.RecentSlotPreviewImage is BitmapSource bOff && bOff.PixelWidth == 240,
            "Tắt checkbox -> phải lập tức chỉ hiển thị đúng con hàng ở nấc #0 (240px)");

        // Bật lại checkbox -> lập tức tìm lại NG gần nhất ở #2 (470px)
        vm.AutoFindNearestNgAfterSelectedSlot = true;
        Assert(vm.RecentSlotPreviewImage is BitmapSource bReOn && bReOn.PixelWidth == 470,
            "Bật lại checkbox -> phải lập tức hiển thị lại NG ở #2 (470px)");

        // Bấm Ghim nấc #0 khi đang hiển thị NG #2 -> giữ chặt ảnh NG 470px kể cả sau khi đẩy 25 con OK (all 20 OK)
        vm.TogglePinRecentSlotCommand.Execute(null);
        Assert(vm.IsPinnedRecentSlot && vm.PinnedRecentSlotIndex == 0, "Phải ghim được nấc #0");
        for (int i = 0; i < 25; i++) vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);

        Assert(vm.RecentPartsNgCount == 0, "Toàn bộ 20 nấc đã toàn OK");
        Assert(vm.RecentSlotPreviewImage is BitmapSource bPinned && bPinned.PixelWidth == 470,
            "Vì đã bấm Ghim, ảnh NG 470px vẫn phải được giữ cố định dù 20 nấc đã toàn OK!");

        Console.WriteLine("PASSED");
    }

    private static void Test_13_Recent20SlotsBar_Numbered1To20LeftToRight_WithReadableForeground()
    {
        Console.Write("--- [13/13] Thanh 20 nấc đánh số 1..20 từ trái sang phải, màu chữ tương phản theo trạng thái... ");

        string xamlPath = FindRepoFile("VisionInspectionApp.UI", "Views", "ToolEditorView.xaml");
        var doc = XDocument.Load(xamlPath);
        XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styleElement = doc.Descendants()
            .FirstOrDefault(el => el.Name.LocalName == "Style" && string.Equals((string?)el.Attribute(xNs + "Key"), "RecentSlotButtonStyle", StringComparison.Ordinal));
        Assert(styleElement is not null, "Phải tìm thấy RecentSlotButtonStyle trong ToolEditorView.xaml");

        var slotButtonElements = doc.Descendants()
            .Where(el => el.Name.LocalName == "Button"
                         && string.Equals((string?)el.Attribute("Style"), "{StaticResource RecentSlotButtonStyle}", StringComparison.Ordinal))
            .ToList();
        Assert(slotButtonElements.Count == 20, $"ToolEditorView.xaml phải có đúng 20 nút nấc, thực tế: {slotButtonElements.Count}");

        for (int i = 0; i < 20; i++)
        {
            var el = slotButtonElements[i];
            Assert(string.Equals((string?)el.Attribute("Content"), (i + 1).ToString(), StringComparison.Ordinal),
                $"XAML nấc thứ {i} phải có Content='{i + 1}', thực tế: '{(string?)el.Attribute("Content")}'");
            Assert(string.Equals((string?)el.Attribute("CommandParameter"), i.ToString(), StringComparison.Ordinal),
                $"XAML nấc {i + 1} phải giữ CommandParameter='{i}', thực tế: '{(string?)el.Attribute("CommandParameter")}'");
        }

        // Dựng live Style từ ToolEditorView.xaml + bind với ToolEditorViewModel thật trên STA Window để kiểm chứng Foreground & Trigger
        Exception? staEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var styleXml = new XElement(styleElement!);
                styleXml.SetAttributeValue("xmlns", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
                styleXml.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
                styleXml.SetAttributeValue(XNamespace.Xmlns + "vm", "clr-namespace:VisionInspectionApp.UI.ViewModels;assembly=VisionInspectionApp.UI");
                styleXml.Attribute(xNs + "Key")?.Remove();

                var liveStyle = (Style)XamlReader.Parse(styleXml.ToString());

                var vm = new ToolEditorViewModel { IsRunningFolderFlow = true };
                var mutedBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
                mutedBrush.Freeze();

                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Resources["TextMutedBrush"] = mutedBrush;

                var buttons = new List<Button>(20);
                for (int i = 0; i < 20; i++)
                {
                    var btn = new Button
                    {
                        Content = (string?)slotButtonElements[i].Attribute("Content"),
                        CommandParameter = (string?)slotButtonElements[i].Attribute("CommandParameter"),
                        Style = liveStyle
                    };
                    btn.SetBinding(Button.BackgroundProperty, new Binding($"RecentPartSlot{i}Bg"));
                    btn.SetBinding(Button.BorderBrushProperty, new Binding($"RecentPartSlot{i}Border"));
                    btn.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"RecentPartSlot{i}ToolTip"));
                    btn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(nameof(ToolEditorViewModel.SelectRecentPartSlotCommand)));
                    panel.Children.Add(btn);
                    buttons.Add(btn);
                }

                var win = new System.Windows.Window
                {
                    DataContext = vm,
                    Content = panel,
                    Width = 480,
                    Height = 80,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None
                };
                win.Show();
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        var btn = buttons[i];
                        Assert(Equals(btn.Content?.ToString(), (i + 1).ToString()), $"Nấc {i} phải hiển thị '{i + 1}'");
                        Assert(btn.Width >= 17, $"Nấc {i + 1} phải đủ rộng cho 2 chữ số (Width >= 17), thực tế: {btn.Width}");
                        Assert(ReferenceEquals(btn.Background, ToolEditorViewModel.EmptySlotBrush), $"Ban đầu nấc {i + 1} phải là EmptySlotBrush");
                        Assert(Equals(btn.Foreground, mutedBrush), $"Nấc trống {i + 1} phải dùng TextMutedBrush");
                    }

                    // Đẩy 1 NG rồi 1 OK -> mới nhất (nấc 1) = OK, nấc 2 = NG, nấc 3 = Empty
                    using var okMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 200, 0));
                    using var ngMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 0, 255));
                    vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
                    vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);

                    Assert(ReferenceEquals(buttons[0].Background, ToolEditorViewModel.OkSlotBrush), "Nấc 1 phải có nền OK");
                    Assert(Equals(buttons[0].Foreground, Brushes.White), "Nấc 1 (OK) phải có chữ trắng tương phản cao");
                    Assert(ReferenceEquals(buttons[1].Background, ToolEditorViewModel.NgSlotBrush), "Nấc 2 phải có nền NG");
                    Assert(Equals(buttons[1].Foreground, Brushes.White), "Nấc 2 (NG) phải có chữ trắng tương phản cao");
                    Assert(ReferenceEquals(buttons[2].Background, ToolEditorViewModel.EmptySlotBrush), "Nấc 3 vẫn trống");
                    Assert(Equals(buttons[2].Foreground, mutedBrush), "Nấc 3 trống phải giữ chữ TextMutedBrush");

                    // Click nút số "2" (CommandParameter="1") phải chọn đúng index 1
                    buttons[1].Command.Execute(buttons[1].CommandParameter);
                    Assert(vm.SelectedRecentSlotIndex == 1, $"Click nút số 2 phải chọn index 1, thực tế: {vm.SelectedRecentSlotIndex}");

                    Assert(vm.RecentPartSlot0ToolTip.Contains("Nấc 1"), $"Tooltip nấc 0 phải ghi 'Nấc 1', thực tế: {vm.RecentPartSlot0ToolTip}");
                    Assert(vm.RecentPartSlot1ToolTip.Contains("Nấc 2"), $"Tooltip nấc 1 phải ghi 'Nấc 2', thực tế: {vm.RecentPartSlot1ToolTip}");
                    Assert(vm.RecentPartSlot19ToolTip.Contains("Nấc 20"), $"Tooltip nấc 19 phải ghi 'Nấc 20', thực tế: {vm.RecentPartSlot19ToolTip}");
                }
                finally
                {
                    win.Close();
                }
            }
            catch (Exception ex)
            {
                staEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (staEx is not null)
            throw new Exception(staEx.Message, staEx);

        Console.WriteLine("PASSED");
    }

    private static void Test_14_SlotIndexDisplay_Synced1To20_ForBarTitleAndStatus()
    {
        Console.Write("--- [14/14] Đồng bộ đánh số 1..20 giữa thanh nấc và tiêu đề/trạng thái hiển thị (#1..#20)... ");

        var vm = new ToolEditorViewModel { IsRunningFolderFlow = true };
        Assert(vm.RecentSlotEmptyHintText.Contains("1–20") && !vm.RecentSlotEmptyHintText.Contains("0–19"),
            $"Gợi ý ban đầu phải ghi (1–20), thực tế: '{vm.RecentSlotEmptyHintText}'");

        // 1. Khi chưa có dữ liệu: click nút số 1 (param "0") và nút số 12 (param "11")
        vm.SelectRecentPartSlotCommand.Execute("0");
        Assert(vm.RecentSlotPreviewTitleText.Contains("Nấc #1") && !vm.RecentSlotPreviewTitleText.Contains("#0"),
            $"Click nấc 1 trống phải hiện #1 (không hiện #0), thực tế: '{vm.RecentSlotPreviewTitleText}'");
        Assert(vm.RecentSlotEmptyHintText.Contains("#1") && !vm.RecentSlotEmptyHintText.Contains("#0"),
            $"EmptyHint nấc 1 phải hiện #1, thực tế: '{vm.RecentSlotEmptyHintText}'");
        Assert(vm.StatusBarText.Contains("#1") && !vm.StatusBarText.Contains("#0"),
            $"StatusBar nấc 1 phải hiện #1, thực tế: '{vm.StatusBarText}'");

        vm.SelectRecentPartSlotCommand.Execute("11");
        Assert(vm.RecentSlotPreviewTitleText.Contains("Nấc #12") && !vm.RecentSlotPreviewTitleText.Contains("#11"),
            $"Click nấc 12 trống phải hiện #12 (không hiện #11), thực tế: '{vm.RecentSlotPreviewTitleText}'");
        Assert(vm.RecentSlotEmptyHintText.Contains("#12") && !vm.RecentSlotEmptyHintText.Contains("#11"),
            $"EmptyHint nấc 12 phải hiện #12, thực tế: '{vm.RecentSlotEmptyHintText}'");

        // 2. Khi có 1 NG ở index 14 (nấc #15) và các nấc còn lại đến #1..#14 là OK
        using var okMat = new Mat(180, 240, MatType.CV_8UC3, new Scalar(0, 200, 0));
        using var ngMat = new Mat(300, 420, MatType.CV_8UC3, new Scalar(0, 0, 255));
        vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
        for (int i = 0; i < 14; i++)
        {
            vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
        }

        // Tắt tự tìm NG để kiểm tra đúng từng nấc 1 và nấc 12
        vm.AutoFindNearestNgAfterSelectedSlot = false;
        vm.SelectRecentPartSlotCommand.Execute("0");
        Assert(vm.RecentSlotPreviewTitleText.Contains("Nấc #1 ") && !vm.RecentSlotPreviewTitleText.Contains("#0"),
            $"Click nấc 1 có dữ liệu phải hiện Nấc #1, thực tế: '{vm.RecentSlotPreviewTitleText}'");
        Assert(vm.StatusBarText.Contains("#1") && !vm.StatusBarText.Contains("#0"),
            $"StatusBar khi xem nấc 1 phải hiện #1, thực tế: '{vm.StatusBarText}'");

        vm.SelectRecentPartSlotCommand.Execute("11");
        Assert(vm.RecentSlotPreviewTitleText.Contains("Nấc #12 ") && !vm.RecentSlotPreviewTitleText.Contains("#11"),
            $"Click nấc 12 có dữ liệu phải hiện Nấc #12, thực tế: '{vm.RecentSlotPreviewTitleText}'");
        Assert(vm.StatusBarText.Contains("#12") && !vm.StatusBarText.Contains("#11"),
            $"StatusBar khi xem nấc 12 phải hiện #12, thực tế: '{vm.StatusBarText}'");

        // 3. Bật lại tìm NG sau nấc & ghim nấc 12 (NG đang ở index 14 => nấc #15)
        vm.AutoFindNearestNgAfterSelectedSlot = true;
        vm.TogglePinRecentSlotCommand.Execute(null);
        Assert(vm.RecentSlotPreviewTitleText.Contains("#12") && vm.RecentSlotPreviewTitleText.Contains("#15")
               && !vm.RecentSlotPreviewTitleText.Contains("#11") && !vm.RecentSlotPreviewTitleText.Contains("#14"),
            $"Ghim nấc 12 với NG ở 15 phải hiện #12 -> #15, thực tế: '{vm.RecentSlotPreviewTitleText}'");
        Assert(vm.StatusBarText.Contains("#12") && vm.StatusBarText.Contains("#15")
               && !vm.StatusBarText.Contains("#11") && !vm.StatusBarText.Contains("#14"),
            $"StatusBar khi ghim nấc 12 phải hiện #12 và #15, thực tế: '{vm.StatusBarText}'");

        // 4. Chế độ dừng máy (xem lịch sử ở preview chính) cũng phải đồng bộ #1..#20
        vm.IsRunningFolderFlow = false;
        vm.SelectRecentPartSlotCommand.Execute("19"); // nấc 20 đang trống
        Assert(vm.StatusBarText.Contains("#20") && !vm.StatusBarText.Contains("#19"),
            $"Khi dừng, click nấc 20 trống phải hiện #20, thực tế: '{vm.StatusBarText}'");

        vm.SelectRecentPartSlotCommand.Execute("11"); // nấc 12 có dữ liệu
        Assert(vm.HistoricalViewingStatusText.Contains("#12") && !vm.HistoricalViewingStatusText.Contains("#11"),
            $"HistoricalViewingStatusText nấc 12 phải hiện #12, thực tế: '{vm.HistoricalViewingStatusText}'");
        Assert(vm.StatusBarText.Contains("#12") && !vm.StatusBarText.Contains("#11"),
            $"StatusBarText khi xem lại nấc 12 phải hiện #12, thực tế: '{vm.StatusBarText}'");

        Console.WriteLine("PASSED");
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Không tìm thấy file {Path.Combine(relativeParts)} từ {AppContext.BaseDirectory}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[ASSERT FAILED] {message}");
        }
    }
}

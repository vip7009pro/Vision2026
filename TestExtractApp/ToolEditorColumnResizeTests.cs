using System;
using System.Collections.Generic;
using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử tự động cho cơ chế tự động co giãn full width của 4 cột trên Tool Editor:
/// 1. Tất cả các cột hiển thị (Toolbox, Canvas, Preview, Debug) đều sử dụng tỷ lệ Star (GridUnitType.Star).
/// 2. Khi ẩn Cột Canvas (Cột 2), Cột Debug (Cột 4), hoặc Cột Toolbox (Cột 1):
///    - Cột bị ẩn thu về Width = 0, MinWidth = 0, SplitterWidth = 0.
///    - Các cột còn lại giữ nguyên tính chất Star để tự động co giãn full width trong không gian hiện có.
/// 3. Bắn sự kiện PropertyChanged đầy đủ cho tất cả thuộc tính layout liên quan.
/// 4. Các tổ hợp ẩn cột đa dạng (ẩn 1 cột, ẩn 2 cột, ẩn 3 cột chỉ chừa lại Preview full width).
/// 5. Phục hồi hoàn hảo tỷ lệ Star và MinWidth khi bật lại các cột.
/// </summary>
public static class ToolEditorColumnResizeTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("📐 RUNNING TOOL EDITOR COLUMN RESIZE & FULL-WIDTH TESTS");
        Console.WriteLine("=========================================================");

        Test_01_InitialColumnWidths_AreAllStarUnits();
        Test_02_HideCanvasColumn_WidthBecomesZero_RemainingColumnsRemainStar();
        Test_03_HideDebugColumn_WidthBecomesZero_RemainingColumnsRemainStar();
        Test_04_HideToolboxColumn_WidthBecomesZero_RemainingColumnsRemainStar();
        Test_05_HideAllOtherColumns_PreviewBecomesSoleStarFullWidth();
        Test_06_PropertyChangedEvents_FiredForAllAffectedLayoutProperties();
        Test_07_RestoreHiddenColumns_ReinstatesStarWidthAndMinWidth();

        Console.WriteLine("✅ ALL 7 TOOL EDITOR COLUMN RESIZE TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Test_01_InitialColumnWidths_AreAllStarUnits()
    {
        Console.Write("--- [1/7] Kiểm tra tất cả 4 cột ban đầu đều là GridUnitType.Star (không fix pixel)... ");

        var vm = new ToolEditorViewModel();

        Assert(vm.IsToolboxColumnVisible, "Mặc định Toolbox phải hiển thị");
        Assert(vm.IsCanvasColumnVisible, "Mặc định Canvas phải hiển thị");
        Assert(vm.IsDebugColumnVisible, "Mặc định Debug phải hiển thị");

        Assert(vm.ToolboxColumnWidth.GridUnitType == GridUnitType.Star, 
            $"ToolboxColumnWidth phải là Star, thực tế: {vm.ToolboxColumnWidth.GridUnitType}");
        Assert(vm.ToolboxColumnWidth.Value > 0, "ToolboxColumnWidth.Value phải > 0");
        Assert(vm.ToolboxColumnMinWidth > 0, "ToolboxColumnMinWidth phải > 0");

        Assert(vm.CanvasColumnWidth.GridUnitType == GridUnitType.Star, 
            $"CanvasColumnWidth phải là Star, thực tế: {vm.CanvasColumnWidth.GridUnitType}");
        Assert(vm.CanvasColumnWidth.Value > 0, "CanvasColumnWidth.Value phải > 0");
        Assert(vm.CanvasColumnMinWidth > 0, "CanvasColumnMinWidth phải > 0");

        Assert(vm.PreviewColumnWidth.GridUnitType == GridUnitType.Star, 
            $"PreviewColumnWidth phải là Star, thực tế: {vm.PreviewColumnWidth.GridUnitType}");
        Assert(vm.PreviewColumnWidth.Value > 0, "PreviewColumnWidth.Value phải > 0");

        Assert(vm.DebugColumnWidth.GridUnitType == GridUnitType.Star, 
            $"DebugColumnWidth phải là Star, thực tế: {vm.DebugColumnWidth.GridUnitType}");
        Assert(vm.DebugColumnWidth.Value > 0, "DebugColumnWidth.Value phải > 0");
        Assert(vm.DebugColumnMinWidth > 0, "DebugColumnMinWidth phải > 0");

        Console.WriteLine("PASSED");
    }

    private static void Test_02_HideCanvasColumn_WidthBecomesZero_RemainingColumnsRemainStar()
    {
        Console.Write("--- [2/7] Ẩn cột Canvas (Cột 2): Width=0, MinWidth=0, các cột khác giữ Star... ");

        var vm = new ToolEditorViewModel();
        vm.IsCanvasColumnVisible = false;

        Assert(vm.CanvasColumnWidth.Value == 0, $"CanvasColumnWidth phải về 0, thực tế: {vm.CanvasColumnWidth.Value}");
        Assert(vm.CanvasColumnMinWidth == 0, $"CanvasColumnMinWidth phải về 0, thực tế: {vm.CanvasColumnMinWidth}");
        Assert(vm.CanvasSplitterWidth.Value == 0, $"CanvasSplitterWidth phải về 0, thực tế: {vm.CanvasSplitterWidth.Value}");

        // Các cột còn lại vẫn phải là Star để tự động chia sẻ không gian full width
        Assert(vm.ToolboxColumnWidth.GridUnitType == GridUnitType.Star && vm.ToolboxColumnWidth.Value > 0,
            "ToolboxColumnWidth phải tiếp tục là Star khi Canvas ẩn");
        Assert(vm.PreviewColumnWidth.GridUnitType == GridUnitType.Star && vm.PreviewColumnWidth.Value > 0,
            "PreviewColumnWidth phải tiếp tục là Star khi Canvas ẩn");
        Assert(vm.DebugColumnWidth.GridUnitType == GridUnitType.Star && vm.DebugColumnWidth.Value > 0,
            "DebugColumnWidth phải tiếp tục là Star khi Canvas ẩn");

        Console.WriteLine("PASSED");
    }

    private static void Test_03_HideDebugColumn_WidthBecomesZero_RemainingColumnsRemainStar()
    {
        Console.Write("--- [3/7] Ẩn cột Debug (Cột 4): Width=0, MinWidth=0, các cột khác giữ Star... ");

        var vm = new ToolEditorViewModel();
        vm.IsDebugColumnVisible = false;

        Assert(vm.DebugColumnWidth.Value == 0, $"DebugColumnWidth phải về 0, thực tế: {vm.DebugColumnWidth.Value}");
        Assert(vm.DebugColumnMinWidth == 0, $"DebugColumnMinWidth phải về 0, thực tế: {vm.DebugColumnMinWidth}");
        Assert(vm.DebugSplitterWidth.Value == 0, $"DebugSplitterWidth phải về 0, thực tế: {vm.DebugSplitterWidth.Value}");

        Assert(vm.ToolboxColumnWidth.GridUnitType == GridUnitType.Star && vm.ToolboxColumnWidth.Value > 0,
            "ToolboxColumnWidth phải tiếp tục là Star khi Debug ẩn");
        Assert(vm.CanvasColumnWidth.GridUnitType == GridUnitType.Star && vm.CanvasColumnWidth.Value > 0,
            "CanvasColumnWidth phải tiếp tục là Star khi Debug ẩn");
        Assert(vm.PreviewColumnWidth.GridUnitType == GridUnitType.Star && vm.PreviewColumnWidth.Value > 0,
            "PreviewColumnWidth phải tiếp tục là Star khi Debug ẩn");

        Console.WriteLine("PASSED");
    }

    private static void Test_04_HideToolboxColumn_WidthBecomesZero_RemainingColumnsRemainStar()
    {
        Console.Write("--- [4/7] Ẩn cột Toolbox (Cột 1): Width=0, MinWidth=0, các cột khác giữ Star... ");

        var vm = new ToolEditorViewModel();
        vm.IsToolboxColumnVisible = false;

        Assert(vm.ToolboxColumnWidth.Value == 0, $"ToolboxColumnWidth phải về 0, thực tế: {vm.ToolboxColumnWidth.Value}");
        Assert(vm.ToolboxColumnMinWidth == 0, $"ToolboxColumnMinWidth phải về 0, thực tế: {vm.ToolboxColumnMinWidth}");
        Assert(vm.ToolboxSplitterWidth.Value == 0, $"ToolboxSplitterWidth phải về 0, thực tế: {vm.ToolboxSplitterWidth.Value}");

        Assert(vm.CanvasColumnWidth.GridUnitType == GridUnitType.Star && vm.CanvasColumnWidth.Value > 0,
            "CanvasColumnWidth phải tiếp tục là Star khi Toolbox ẩn");
        Assert(vm.PreviewColumnWidth.GridUnitType == GridUnitType.Star && vm.PreviewColumnWidth.Value > 0,
            "PreviewColumnWidth phải tiếp tục là Star khi Toolbox ẩn");
        Assert(vm.DebugColumnWidth.GridUnitType == GridUnitType.Star && vm.DebugColumnWidth.Value > 0,
            "DebugColumnWidth phải tiếp tục là Star khi Toolbox ẩn");

        Console.WriteLine("PASSED");
    }

    private static void Test_05_HideAllOtherColumns_PreviewBecomesSoleStarFullWidth()
    {
        Console.Write("--- [5/7] Ẩn cả 3 cột (Toolbox, Canvas, Debug): Preview chiếm trọn 100% full width... ");

        var vm = new ToolEditorViewModel();
        vm.IsToolboxColumnVisible = false;
        vm.IsCanvasColumnVisible = false;
        vm.IsDebugColumnVisible = false;

        Assert(vm.ToolboxColumnWidth.Value == 0, "ToolboxColumnWidth phải là 0");
        Assert(vm.CanvasColumnWidth.Value == 0, "CanvasColumnWidth phải là 0");
        Assert(vm.DebugColumnWidth.Value == 0, "DebugColumnWidth phải là 0");

        Assert(vm.PreviewColumnWidth.GridUnitType == GridUnitType.Star, "PreviewColumnWidth phải là Star");
        Assert(vm.PreviewColumnWidth.Value > 0, "PreviewColumnWidth phải có giá trị > 0");

        Console.WriteLine("PASSED (Preview là cột Star duy nhất -> lấp đầy 100% Grid)");
    }

    private static void Test_06_PropertyChangedEvents_FiredForAllAffectedLayoutProperties()
    {
        Console.Write("--- [6/7] Kiểm tra sự kiện PropertyChanged bắn ra đầy đủ khi thay đổi Visibility... ");

        var vm = new ToolEditorViewModel();
        var changedProps = new HashSet<string>();

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        // Đổi IsToolboxColumnVisible
        changedProps.Clear();
        vm.IsToolboxColumnVisible = false;
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.ToolboxColumnWidth)), "Phải bắn ToolboxColumnWidth");
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.ToolboxSplitterWidth)), "Phải bắn ToolboxSplitterWidth");
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.ToolboxColumnMinWidth)), "Phải bắn ToolboxColumnMinWidth");

        // Đổi IsCanvasColumnVisible
        changedProps.Clear();
        vm.IsCanvasColumnVisible = false;
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.CanvasColumnWidth)), "Phải bắn CanvasColumnWidth");
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.CanvasSplitterWidth)), "Phải bắn CanvasSplitterWidth");
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.CanvasColumnMinWidth)), "Phải bắn CanvasColumnMinWidth");

        // Đổi IsDebugColumnVisible
        changedProps.Clear();
        vm.IsDebugColumnVisible = false;
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.DebugColumnWidth)), "Phải bắn DebugColumnWidth");
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.DebugSplitterWidth)), "Phải bắn DebugSplitterWidth");
        Assert(changedProps.Contains(nameof(ToolEditorViewModel.DebugColumnMinWidth)), "Phải bắn DebugColumnMinWidth");

        Console.WriteLine("PASSED");
    }

    private static void Test_07_RestoreHiddenColumns_ReinstatesStarWidthAndMinWidth()
    {
        Console.Write("--- [7/7] Hiện lại các cột đã ẩn: Phục hồi lại tỷ lệ Star và MinWidth... ");

        var vm = new ToolEditorViewModel();
        vm.IsToolboxColumnVisible = false;
        vm.IsCanvasColumnVisible = false;
        vm.IsDebugColumnVisible = false;

        // Bật lại
        vm.IsToolboxColumnVisible = true;
        vm.IsCanvasColumnVisible = true;
        vm.IsDebugColumnVisible = true;

        Assert(vm.ToolboxColumnWidth.GridUnitType == GridUnitType.Star && vm.ToolboxColumnWidth.Value > 0,
            "ToolboxColumnWidth phải phục hồi là Star");
        Assert(vm.ToolboxColumnMinWidth >= 200, "ToolboxColumnMinWidth phải phục hồi >= 200");
        Assert(vm.ToolboxSplitterWidth.Value == 4, "ToolboxSplitterWidth phải phục hồi là 4");

        Assert(vm.CanvasColumnWidth.GridUnitType == GridUnitType.Star && vm.CanvasColumnWidth.Value > 0,
            "CanvasColumnWidth phải phục hồi là Star");
        Assert(vm.CanvasColumnMinWidth >= 200, "CanvasColumnMinWidth phải phục hồi >= 200");
        Assert(vm.CanvasSplitterWidth.Value == 4, "CanvasSplitterWidth phải phục hồi là 4");

        Assert(vm.DebugColumnWidth.GridUnitType == GridUnitType.Star && vm.DebugColumnWidth.Value > 0,
            "DebugColumnWidth phải phục hồi là Star");
        Assert(vm.DebugColumnMinWidth >= 200, "DebugColumnMinWidth phải phục hồi >= 200");
        Assert(vm.DebugSplitterWidth.Value == 4, "DebugSplitterWidth phải phục hồi là 4");

        Console.WriteLine("PASSED");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[ASSERT FAILED] {message}");
        }
    }
}

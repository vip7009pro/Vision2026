using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.UI.Converters;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.UI.Views;

namespace TestExtractApp;

public static class ToolEditorQueueDirectionTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("======================================================================");
        Console.WriteLine("TEST ĐẢO CHIỀU HIỂN THỊ QUEUE 16 NẤC & 20 NẤC TRONG TAB TOOL EDITOR");
        Console.WriteLine("======================================================================");

        Test_01_XamlBindingsAndStructure();

        Exception? staEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                Test_02_GlobalAppSettingsPersistenceAndTooltips();
                Test_03_LiveWpfBothQueuesFlipCoordinatesAndExcludeSlotClicks();
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
        {
            throw new Exception(staEx.Message, staEx);
        }

        Console.WriteLine("\n✅ TẤT CẢ 3/3 TEST ĐẢO CHIỀU QUEUE 16 & 20 NẤC ĐỀU PASSED 100%!");
    }

    private static void Test_01_XamlBindingsAndStructure()
    {
        Console.Write("--- [1/3] Kiểm tra XAML: 2 khối queue, binding FlowDirection, mũi tên hướng & style LTR nút nấc... ");

        string xamlPath = FindRepoFile("VisionInspectionApp.UI", "Views", "ToolEditorView.xaml");
        var doc = XDocument.Load(xamlPath);
        XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement? FindByName(string name) => doc.Descendants()
            .FirstOrDefault(el => string.Equals((string?)el.Attribute(xNs + "Name"), name, StringComparison.Ordinal));

        var recentBlock = FindByName("RecentPartsQueueBlock");
        var queueBlock = FindByName("InspectionQueueBlock");
        Assert(recentBlock is not null, "Thiếu x:Name='RecentPartsQueueBlock'");
        Assert(queueBlock is not null, "Thiếu x:Name='InspectionQueueBlock'");
        Assert(string.Equals((string?)recentBlock!.Attribute("MouseLeftButtonDown"), "QueueBlock_MouseLeftButtonDown", StringComparison.Ordinal),
            "RecentPartsQueueBlock phải gắn MouseLeftButtonDown='QueueBlock_MouseLeftButtonDown'");
        Assert(string.Equals((string?)queueBlock!.Attribute("MouseLeftButtonDown"), "QueueBlock_MouseLeftButtonDown", StringComparison.Ordinal),
            "InspectionQueueBlock phải gắn MouseLeftButtonDown='QueueBlock_MouseLeftButtonDown'");
        Assert(string.Equals((string?)recentBlock.Attribute("Cursor"), "Hand", StringComparison.Ordinal),
            "RecentPartsQueueBlock phải có Cursor='Hand'");
        Assert(string.Equals((string?)queueBlock.Attribute("Cursor"), "Hand", StringComparison.Ordinal),
            "InspectionQueueBlock phải có Cursor='Hand'");

        var recentSlotsPanel = FindByName("RecentPartsSlotsPanel");
        var queueSlotsPanel = FindByName("InspectionQueueSlotsPanel");
        Assert(recentSlotsPanel is not null && string.Equals((string?)recentSlotsPanel.Attribute("FlowDirection"), "{Binding QueueFlowDirection}", StringComparison.Ordinal),
            "RecentPartsSlotsPanel phải bind FlowDirection='{Binding QueueFlowDirection}'");
        Assert(queueSlotsPanel is not null && string.Equals((string?)queueSlotsPanel.Attribute("FlowDirection"), "{Binding QueueFlowDirection}", StringComparison.Ordinal),
            "InspectionQueueSlotsPanel phải bind FlowDirection='{Binding QueueFlowDirection}'");

        var recentSlotsContainer = FindByName("RecentPartsSlotsContainer");
        var queueSlotsContainer = FindByName("InspectionQueueSlotsContainer");
        Assert(recentSlotsContainer is not null && string.Equals((string?)recentSlotsContainer.Attribute("Cursor"), "Arrow", StringComparison.Ordinal),
            "RecentPartsSlotsContainer phải tồn tại với Cursor='Arrow'");
        Assert(queueSlotsContainer is not null && string.Equals((string?)queueSlotsContainer.Attribute("Cursor"), "Arrow", StringComparison.Ordinal),
            "InspectionQueueSlotsContainer phải tồn tại với Cursor='Arrow'");

        var recentArrow = FindByName("TxtRecentQueueDirectionArrow");
        var queueArrow = FindByName("TxtInspectionQueueDirectionArrow");
        Assert(recentArrow is not null && string.Equals((string?)recentArrow.Attribute("Text"), "{Binding QueueDirectionArrowText}", StringComparison.Ordinal),
            "TxtRecentQueueDirectionArrow phải bind QueueDirectionArrowText");
        Assert(queueArrow is not null && string.Equals((string?)queueArrow.Attribute("Text"), "{Binding QueueDirectionArrowText}", StringComparison.Ordinal),
            "TxtInspectionQueueDirectionArrow phải bind QueueDirectionArrowText");

        var styleEl = doc.Descendants().FirstOrDefault(el =>
            el.Name.LocalName == "Style" && string.Equals((string?)el.Attribute(xNs + "Key"), "RecentSlotButtonStyle", StringComparison.Ordinal));
        Assert(styleEl is not null, "Thiếu RecentSlotButtonStyle");
        bool hasLtrSetter = styleEl!.Elements().Any(el =>
            el.Name.LocalName == "Setter"
            && string.Equals((string?)el.Attribute("Property"), "FlowDirection", StringComparison.Ordinal)
            && string.Equals((string?)el.Attribute("Value"), "LeftToRight", StringComparison.Ordinal));
        Assert(hasLtrSetter, "RecentSlotButtonStyle phải set FlowDirection='LeftToRight' để số 1..20 & tooltip không bị lỗi RTL");

        Console.WriteLine("PASSED");
    }

    private static void Test_03_LiveWpfBothQueuesFlipCoordinatesAndExcludeSlotClicks()
    {
        Console.Write("--- [3/3] Live WPF ToolEditorView: kiểm tra tọa độ 16 & 20 nấc và lọc click nấc vs click khối... ");

        EnsureWpfAppResources();

        var vm = new ToolEditorViewModel { IsRunningFolderFlow = false };
        var view = new ToolEditorView { DataContext = vm };

        var win = new System.Windows.Window
        {
            Content = view,
            Width = 1600,
            Height = 900,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None
        };

        win.Show();
        try
                {
                    win.UpdateLayout();

                    var recentBlock = (Border)view.FindName("RecentPartsQueueBlock")!;
                    var queueBlock = (Border)view.FindName("InspectionQueueBlock")!;
                    var recentContainer = (Border)view.FindName("RecentPartsSlotsContainer")!;
                    var queueContainer = (Border)view.FindName("InspectionQueueSlotsContainer")!;
                    var recentPanel = (StackPanel)view.FindName("RecentPartsSlotsPanel")!;
                    var queuePanel = (StackPanel)view.FindName("InspectionQueueSlotsPanel")!;
                    var recentArrow = (TextBlock)view.FindName("TxtRecentQueueDirectionArrow")!;
                    var queueArrow = (TextBlock)view.FindName("TxtInspectionQueueDirectionArrow")!;
                    var exitLiveBtn = (Button)view.FindName("BtnExitRecentPartView")!;

                    var slot20Buttons = recentPanel.Children.OfType<Button>().ToList();
                    var slot16Borders = queuePanel.Children.OfType<Border>().ToList();
                    Assert(slot20Buttons.Count == 20, $"Phải có 20 nút nấc, thực tế: {slot20Buttons.Count}");
                    Assert(slot16Borders.Count == 16, $"Phải có 16 đoạn nấc, thực tế: {slot16Borders.Count}");

                    static double GetCenterX(FrameworkElement element, Visual ancestor)
                    {
                        var bounds = element.TransformToAncestor(ancestor)
                            .TransformBounds(new System.Windows.Rect(0, 0, element.ActualWidth, element.ActualHeight));
                        return (bounds.Left + bounds.Right) * 0.5;
                    }

                    // 1. Trạng thái mặc định: Trái -> Phải cho CẢ 2 queue
                    Assert(!vm.IsQueueRightToLeft, "Mặc định IsQueueRightToLeft phải là false");
                    Assert(recentPanel.FlowDirection == FlowDirection.LeftToRight, "RecentPartsSlotsPanel mặc định phải là LeftToRight");
                    Assert(queuePanel.FlowDirection == FlowDirection.LeftToRight, "InspectionQueueSlotsPanel mặc định phải là LeftToRight");
                    Assert(recentArrow.Text == "→" && queueArrow.Text == "→", "Mũi tên mặc định phải là '→'");

                    for (int i = 0; i < 19; i++)
                    {
                        double xA = GetCenterX(slot20Buttons[i], recentContainer);
                        double xB = GetCenterX(slot20Buttons[i + 1], recentContainer);
                        Assert(xA < xB, $"LTR Queue 20: nấc {i + 1} (x={xA:F1}) phải nằm bên TRÁI nấc {i + 2} (x={xB:F1})");
                    }
                    for (int i = 0; i < 15; i++)
                    {
                        double xA = GetCenterX(slot16Borders[i], queueContainer);
                        double xB = GetCenterX(slot16Borders[i + 1], queueContainer);
                        Assert(xA < xB, $"LTR Queue 16: slot {i} (x={xA:F1}) phải nằm bên TRÁI slot {i + 1} (x={xB:F1})");
                    }

                    // Helper bắn sự kiện chuột trái chuẩn WPF (Mouse.MouseDownEvent bubbling -> tự kích hoạt MouseLeftButtonDown)
                    static void RaiseLeftMouseDown(IInputElement target)
                    {
                        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                        {
                            RoutedEvent = Mouse.MouseDownEvent,
                            Source = target
                        };
                        target.RaiseEvent(args);
                    }

                    // 2. Click vào khối Queue 16 (trên mũi tên hướng / tiêu đề, KHÔNG phải nấc) -> ĐẢO CHIỀU CẢ 2 QUEUE sang Phải -> Trái
                    RaiseLeftMouseDown(queueArrow);
                    win.UpdateLayout();

                    Assert(vm.IsQueueRightToLeft, "Click vào khối Queue 16 phải bật IsQueueRightToLeft = true");
                    Assert(recentPanel.FlowDirection == FlowDirection.RightToLeft, "Queue 20 phải đảo sang RightToLeft");
                    Assert(queuePanel.FlowDirection == FlowDirection.RightToLeft, "Queue 16 phải đảo sang RightToLeft");
                    Assert(recentArrow.Text == "←" && queueArrow.Text == "←", "Mũi tên sau khi đảo phải là '←'");

                    for (int i = 0; i < 19; i++)
                    {
                        double xA = GetCenterX(slot20Buttons[i], recentContainer);
                        double xB = GetCenterX(slot20Buttons[i + 1], recentContainer);
                        Assert(xA > xB, $"RTL Queue 20: nấc {i + 1} (x={xA:F1}) phải nằm bên PHẢI nấc {i + 2} (x={xB:F1})");
                    }
                    for (int i = 0; i < 15; i++)
                    {
                        double xA = GetCenterX(slot16Borders[i], queueContainer);
                        double xB = GetCenterX(slot16Borders[i + 1], queueContainer);
                        Assert(xA > xB, $"RTL Queue 16: slot {i} (x={xA:F1}) phải nằm bên PHẢI slot {i + 1} (x={xB:F1})");
                    }

                    foreach (var btn in slot20Buttons)
                    {
                        Assert(btn.FlowDirection == FlowDirection.LeftToRight,
                            $"Nút nấc '{btn.Content}' phải giữ FlowDirection=LeftToRight để số & tooltip hiển thị chuẩn");
                    }

                    // Kiểm tra dữ liệu thực tế trên 2 queue khi ở chế độ Phải -> Trái
                    vm.QueueCurrentCount = 3;
                    Assert(vm.QueueSlot0Active && vm.QueueSlot1Active && vm.QueueSlot2Active && !vm.QueueSlot3Active,
                        "Queue 16 với count=3 phải sáng slot 0..2 (3 nấc bên PHẢI cùng khi RTL)");
                    double activeRightmostX = GetCenterX(slot16Borders[0], queueContainer);
                    double inactiveLeftX = GetCenterX(slot16Borders[3], queueContainer);
                    Assert(activeRightmostX > inactiveLeftX,
                        $"Khi RTL, slot sáng (slot 0, x={activeRightmostX:F1}) phải nằm bên PHẢI slot tắt (slot 3, x={inactiveLeftX:F1})");

                    using var okMat = new Mat(120, 160, MatType.CV_8UC3, new Scalar(0, 200, 0));
                    using var ngMat = new Mat(120, 160, MatType.CV_8UC3, new Scalar(0, 0, 255));
                    vm.PushRecentPartInspectionResult(new InspectionResult { Pass = false }, ngMat);
                    vm.PushRecentPartInspectionResult(new InspectionResult { Pass = true }, okMat);
                    Assert(ReferenceEquals(slot20Buttons[0].Background, ToolEditorViewModel.OkSlotBrush),
                        "Con hàng mới nhất (OK) nằm ở nấc 1 (bên phải cùng khi RTL)");
                    Assert(ReferenceEquals(slot20Buttons[1].Background, ToolEditorViewModel.NgSlotBrush),
                        "Con hàng trước đó (NG) trượt sang trái vào nấc 2");

                    // 3. Click vào các NẤC & nút con -> KHÔNG được đảo chiều!
                    Assert(view.IsClickExcludedFromQueueBlockToggle(slot16Borders[0]), "Nấc 0 của Queue 16 phải bị loại trừ");
                    Assert(view.IsClickExcludedFromQueueBlockToggle(slot16Borders[7]), "Nấc 7 của Queue 16 phải bị loại trừ");
                    Assert(view.IsClickExcludedFromQueueBlockToggle(queueContainer), "Vùng chứa nấc Queue 16 phải bị loại trừ");
                    Assert(view.IsClickExcludedFromQueueBlockToggle(slot20Buttons[0]), "Nấc 1 của Queue 20 phải bị loại trừ");
                    Assert(view.IsClickExcludedFromQueueBlockToggle(slot20Buttons[11]), "Nấc 12 của Queue 20 phải bị loại trừ");
                    Assert(view.IsClickExcludedFromQueueBlockToggle(recentContainer), "Vùng chứa nấc Queue 20 phải bị loại trừ");
                    Assert(view.IsClickExcludedFromQueueBlockToggle(exitLiveBtn), "Nút ✕ Live phải bị loại trừ");

                    RaiseLeftMouseDown(slot16Borders[0]);
                    Assert(vm.IsQueueRightToLeft, "Click vào nấc Queue 16 KHÔNG được đảo chiều");

                    RaiseLeftMouseDown(queueContainer);
                    Assert(vm.IsQueueRightToLeft, "Click vào khe/viền vùng nấc Queue 16 KHÔNG được đảo chiều");

                    RaiseLeftMouseDown(slot20Buttons[1]);
                    Assert(vm.IsQueueRightToLeft, "Click vào nấc Queue 20 KHÔNG được đảo chiều");

                    RaiseLeftMouseDown(recentContainer);
                    Assert(vm.IsQueueRightToLeft, "Click vào khe/viền vùng nấc Queue 20 KHÔNG được đảo chiều");

                    RaiseLeftMouseDown(exitLiveBtn);
                    Assert(vm.IsQueueRightToLeft, "Click vào nút ✕ Live KHÔNG được đảo chiều");

                    // Chọn nấc 2 (NG) vẫn hoạt động bình thường khi đang RTL
                    slot20Buttons[1].Command.Execute(slot20Buttons[1].CommandParameter);
                    Assert(vm.SelectedRecentSlotIndex == 1 && vm.IsQueueRightToLeft,
                        "Bấm nút nấc 2 khi RTL phải chọn index 1 và giữ nguyên trạng thái RTL");

                    // 4. Click vào Run (ContentElement không phải Visual) trong TextBlock của khối Recent -> KHÔNG throw và đảo lại Trái -> Phải!
                    var inlineRun = recentArrow.Inlines.OfType<Run>().FirstOrDefault();
                    Assert(inlineRun is not null, "TxtRecentQueueDirectionArrow phải có Run inline để test ContentElement");
                    Assert(!view.IsClickExcludedFromQueueBlockToggle(inlineRun), "Click vào Run trong tiêu đề khối Recent phải hợp lệ để đảo chiều");

                    RaiseLeftMouseDown(recentArrow);
                    win.UpdateLayout();

                    Assert(!vm.IsQueueRightToLeft, "Click vào khối Queue 20 phải đảo CẢ 2 queue quay lại Trái -> Phải");
                    Assert(recentPanel.FlowDirection == FlowDirection.LeftToRight && queuePanel.FlowDirection == FlowDirection.LeftToRight,
                        "Cả 2 panel phải quay lại LeftToRight");
                    Assert(GetCenterX(slot20Buttons[0], recentContainer) < GetCenterX(slot20Buttons[19], recentContainer),
                        "Queue 20 phải quay về Nấc 1 bên trái, Nấc 20 bên phải");
                    Assert(GetCenterX(slot16Borders[0], queueContainer) < GetCenterX(slot16Borders[15], queueContainer),
                        "Queue 16 phải quay về Slot 0 bên trái, Slot 15 bên phải");

                    // 5. Click trực tiếp vào nền Border ngoài của từng khối đều đảo chiều được cả 2 queue
                    RaiseLeftMouseDown(recentBlock);
                    Assert(vm.IsQueueRightToLeft, "Click nền RecentPartsQueueBlock phải bật RTL");

                    // 6. Chạy liên tục + ResetQueueAndCounters phải bảo toàn chiều hiển thị đã cài đặt
                    vm.IsRunningFolderFlow = true;
                    vm.SelectRecentPartSlotCommand.Execute("1");
                    vm.TogglePinRecentSlotCommand.Execute(null);
                    Assert(vm.IsQueueRightToLeft && vm.IsPinnedRecentSlot && vm.PinnedRecentSlotIndex == 1,
                        "Phải giữ nguyên RTL và trạng thái ghim khi chạy liên tục");
                    RaiseLeftMouseDown(queueBlock);
                    Assert(!vm.IsQueueRightToLeft && vm.IsPinnedRecentSlot && vm.PinnedRecentSlotIndex == 1,
                        "Đảo chiều khi đang chạy liên tục phải giữ nguyên nấc đang ghim");
                    RaiseLeftMouseDown(recentBlock);
                    vm.ResetQueueAndCounters();
                    Assert(vm.IsQueueRightToLeft, "ResetQueueAndCounters không được làm mất cài đặt chiều queue RTL");
                    vm.IsRunningFolderFlow = false;
                    RaiseLeftMouseDown(queueBlock);
                    Assert(!vm.IsQueueRightToLeft, "Click nền InspectionQueueBlock phải tắt RTL");
                }
        finally
        {
            win.Close();
        }

        Console.WriteLine("PASSED");
    }

    private static void Test_02_GlobalAppSettingsPersistenceAndTooltips()
    {
        Console.Write("--- [2/3] Kiểm tra lưu cài đặt bền vững (GlobalAppSettings), Tooltip & StatusBar... ");

        string tempDir = Path.Combine(Path.GetTempPath(), "VisionTest_QueueDir_" + Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(tempDir, "global_settings.json");

        try
        {
            var settings1 = new GlobalAppSettingsService(settingsPath, disableBackupSync: true);
            Assert(!settings1.Settings.ToolEditorQueueRightToLeft, "Mặc định GlobalAppSettings.ToolEditorQueueRightToLeft phải là false");

            var vm1 = new ToolEditorViewModel();
            vm1.ConfigureQueueDirectionSettingsForTesting(settings1);
            Assert(!vm1.IsQueueRightToLeft, "VM1 khởi tạo phải là Trái -> Phải");
            Assert(vm1.QueueToolTipText.Contains("Trái ➔ Phải"), $"Tooltip Queue16 LTR chưa đúng: {vm1.QueueToolTipText}");
            Assert(vm1.RecentPartsToolTipText.Contains("Trái (Mới nhất: Nấc 1) ➔ Phải (Cũ dần: Nấc 20)"),
                $"Tooltip Queue20 LTR chưa đúng: {vm1.RecentPartsToolTipText}");

            // Bật đảo chiều Phải -> Trái
            vm1.ToggleQueueDirectionCommand.Execute(null);
            Assert(vm1.IsQueueRightToLeft, "Sau khi toggle phải là true");
            Assert(vm1.QueueFlowDirection == FlowDirection.RightToLeft, "QueueFlowDirection phải là RightToLeft");
            Assert(vm1.QueueDirectionArrowText == "←", "Mũi tên phải là '←'");
            Assert(vm1.QueueToolTipText.Contains("Phải ➔ Trái"), $"Tooltip Queue16 RTL chưa đúng: {vm1.QueueToolTipText}");
            Assert(vm1.RecentPartsToolTipText.Contains("Phải (Mới nhất: Nấc 1) ➔ Trái (Cũ dần: Nấc 20)"),
                $"Tooltip Queue20 RTL chưa đúng: {vm1.RecentPartsToolTipText}");
            Assert(vm1.StatusBarText.Contains("Phải ➔ Trái"), $"StatusBarText RTL chưa đúng: {vm1.StatusBarText}");
            Assert(settings1.Settings.ToolEditorQueueRightToLeft, "GlobalAppSettings phải cập nhật ToolEditorQueueRightToLeft = true");

            // Khởi động lại phiên mới: đọc lại từ đĩa
            var settings2 = new GlobalAppSettingsService(settingsPath, disableBackupSync: true);
            Assert(settings2.Settings.ToolEditorQueueRightToLeft, "File global_settings.json phải lưu bền vững ToolEditorQueueRightToLeft = true");

            var vm2 = new ToolEditorViewModel();
            vm2.ConfigureQueueDirectionSettingsForTesting(settings2);
            Assert(vm2.IsQueueRightToLeft, "VM2 sau khi mở lại phải khôi phục IsQueueRightToLeft = true");
            Assert(vm2.QueueFlowDirection == FlowDirection.RightToLeft, "VM2 phải có QueueFlowDirection = RightToLeft");
            Assert(vm2.QueueToolTipText.Contains("Phải ➔ Trái"), "VM2 tooltip Queue 16 phải khôi phục đúng chiều Phải -> Trái");
            Assert(vm2.RecentPartsToolTipText.Contains("Phải (Mới nhất: Nấc 1) ➔ Trái (Cũ dần: Nấc 20)"),
                "VM2 tooltip Queue 20 phải khôi phục đúng chiều Phải -> Trái");

            // Đảo ngược lại về Trái -> Phải và kiểm chứng lưu đĩa
            vm2.ToggleQueueDirectionCommand.Execute(null);
            Assert(!vm2.IsQueueRightToLeft, "VM2 toggle lần 2 phải quay về false");
            Assert(vm2.StatusBarText.Contains("Trái ➔ Phải"), $"StatusBarText LTR chưa đúng: {vm2.StatusBarText}");

            var settings3 = new GlobalAppSettingsService(settingsPath, disableBackupSync: true);
            Assert(!settings3.Settings.ToolEditorQueueRightToLeft, "File global_settings.json phải lưu lại ToolEditorQueueRightToLeft = false");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }

        Console.WriteLine("PASSED");
    }

    private static void EnsureWpfAppResources()
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        app.Resources["DoubleConv"] = new FlexibleDoubleConverter();
        app.Resources["OkNgConv"] = new BoolToOkNgConverter();
        app.Resources["InverseBoolConverter"] = new InverseBoolConverter();
        app.Resources["PassFailColorConverter"] = new PassFailColorConverter();
        app.Resources["PassRateConverter"] = new PassRateConverter();
        app.Resources["ColorToBrushConverter"] = new ColorToBrushConverter();
        app.Resources["InverseBoolToVisConverter"] = new InverseBoolToVisConverter();
        app.Resources["BoolToVis"] = new BooleanToVisibilityConverter();
        app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
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

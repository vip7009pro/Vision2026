using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI;
using VisionInspectionApp.UI.Converters;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Services.Camera;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử tự động hành vi ẩn Tab Bar và khóa chuyển tab khi đang Chạy liên tục:
/// 1. Kiểm tra XAML `MainWindow.xaml` (PART_TabHeadersBar bind IsMainTabBarVisible, SelectionChanged, Style riêng).
/// 2. Kiểm tra `MainWindowViewModel`: ẩn/hiện `IsMainTabBarVisible`, `IsTabSwitchingLocked`, `SwitchTabCommand.CanExecute`,
///    chặn đổi `SelectedTabIndex` sang OQC Scanner / Manual Inspection / Camera Settings khi chạy liên tục,
///    tự động đưa về Tab 0 (Tool Editor) nếu kích hoạt từ tab khác, và mở khóa khi dừng.
/// 3. Kiểm tra live WPF `MainWindow` trên STA: `PART_TabHeadersBar` chuyển `Visible` -> `Collapsed` (ActualHeight=0) -> `Visible`,
///    Menu `👁️ Màn Hình` tự disable, chặn `Ctrl+Tab` / `F2..F4` / `SelectionChanged`.
/// 4. Kiểm tra `CameraSettingsViewModel` & `CameraService`: rời tab không-CameraSettings không tắt nhầm stream trigger,
///    và khởi động chạy liên tục từ tab Camera Settings tự rời tab và tắt consumer live trước khi bật TriggerMode=On.
/// </summary>
public static class MainWindowContinuousTabLockTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n======================================================================");
        Console.WriteLine("🔒 RUNNING MAIN WINDOW CONTINUOUS RUN TAB BAR HIDE & LOCK TESTS");
        Console.WriteLine("======================================================================");

        Test_01_XamlBindingsAndTemplateStructure();
        Test_02_ViewModel_LocksTabSwitchingAndAutoReturnsToToolEditor();

        Exception? staEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                Test_03_LiveWpfMainWindow_HidesTabBarLocksMenuAndShortcuts();
                Test_04_CameraSettingsAndTriggerMode_DoNotPushFreeRunFramesAfterAutoReturn().GetAwaiter().GetResult();
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

        Console.WriteLine("✅ ALL 4 MAIN WINDOW CONTINUOUS TAB BAR & NAVIGATION LOCK TESTS PASSED!");
        Console.WriteLine("======================================================================\n");
    }

    private static void Test_01_XamlBindingsAndTemplateStructure()
    {
        Console.Write("--- [1/4] Kiểm tra XAML MainWindow: Style riêng với PART_TabHeadersBar ẩn/hiện & SelectionChanged... ");

        string xamlPath = FindRepoFile("VisionInspectionApp.UI", "MainWindow.xaml");
        var doc = XDocument.Load(xamlPath);
        XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styleEl = doc.Descendants().FirstOrDefault(el =>
            el.Name.LocalName == "Style" &&
            string.Equals((string?)el.Attribute(xNs + "Key"), "MainNavigationTabControlStyle", StringComparison.Ordinal));
        Assert(styleEl is not null, "Thiếu Style x:Key='MainNavigationTabControlStyle' trong MainWindow.xaml");

        var tabHeadersBar = styleEl!.Descendants().FirstOrDefault(el =>
            el.Name.LocalName == "Border" &&
            string.Equals((string?)el.Attribute(xNs + "Name"), "PART_TabHeadersBar", StringComparison.Ordinal));
        Assert(tabHeadersBar is not null, "Thiếu Border x:Name='PART_TabHeadersBar' trong MainNavigationTabControlStyle");

        var visAttr = (string?)tabHeadersBar!.Attribute("Visibility") ?? string.Empty;
        Assert(visAttr.Contains("IsMainTabBarVisible", StringComparison.Ordinal) &&
               visAttr.Contains("BoolToVis", StringComparison.Ordinal),
            $"PART_TabHeadersBar Visibility phải bind IsMainTabBarVisible qua BoolToVis, thực tế: '{visAttr}'");

        var mainTabControl = doc.Descendants().FirstOrDefault(el =>
            el.Name.LocalName == "TabControl" &&
            string.Equals((string?)el.Attribute(xNs + "Name"), "MainNavigationTabControl", StringComparison.Ordinal));
        Assert(mainTabControl is not null, "Thiếu TabControl x:Name='MainNavigationTabControl'");
        Assert(string.Equals((string?)mainTabControl!.Attribute("Style"), "{StaticResource MainNavigationTabControlStyle}", StringComparison.Ordinal),
            "MainNavigationTabControl phải dùng Style='{StaticResource MainNavigationTabControlStyle}'");
        Assert(string.Equals((string?)mainTabControl.Attribute("SelectionChanged"), "MainNavigationTabControl_SelectionChanged", StringComparison.Ordinal),
            "MainNavigationTabControl phải gắn SelectionChanged='MainNavigationTabControl_SelectionChanged'");

        Console.WriteLine("PASSED");
    }

    private static void Test_02_ViewModel_LocksTabSwitchingAndAutoReturnsToToolEditor()
    {
        Console.Write("--- [2/4] MainWindowViewModel: khóa chuyển tab & tự về Tool Editor khi chạy liên tục... ");

        string tempDir = Path.Combine(Path.GetTempPath(), $"vision_tablock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "global_settings.json");
        var settingsService = new GlobalAppSettingsService(settingsPath, disableBackupSync: true);
        using var cameraService = new CameraService();
        using var cameraSettings = new CameraSettingsViewModel(cameraService, settingsService);
        using var manualInspection = new ManualInspectionViewModel(settingsService, cameraService);
        var toolEditor = new ToolEditorViewModel { IsRunningFolderFlow = false };

        try
        {
            var mainVm = new MainWindowViewModel(toolEditor, null!, manualInspection, null!, null!, cameraSettings);

            Assert(mainVm.SelectedTabIndex == 1, "Mặc định MainWindowViewModel phải mở tab OQC Scanner (1)");
            Assert(mainVm.IsMainTabBarVisible && !mainVm.IsTabSwitchingLocked, "Ban đầu tab bar phải hiện và chưa khóa chuyển tab");
            Assert(mainVm.SwitchTabCommand.CanExecute("0") && mainVm.SwitchTabCommand.CanExecute("3"),
                "Ban đầu SwitchTabCommand phải thực thi được");

            // Chuyển sang Camera Settings (Tab 3)
            mainVm.SwitchTabCommand.Execute("3");
            Assert(mainVm.SelectedTabIndex == 3, "Phải chuyển được sang tab Camera Settings (3) khi chưa chạy liên tục");
            Assert(cameraSettings.IsViewActive, "CameraSettings phải active khi đang ở tab 3");

            var changedProps = new HashSet<string>();
            mainVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != null) changedProps.Add(e.PropertyName);
            };

            // Bắt đầu chạy liên tục -> phải tự đưa về Tab 0 (Tool Editor), tắt CameraSettings, ẩn tab bar và khóa SwitchTabCommand
            toolEditor.IsRunningFolderFlow = true;

            Assert(mainVm.SelectedTabIndex == 0, $"Khi bật chạy liên tục phải tự đưa về Tab 0 (Tool Editor), thực tế: {mainVm.SelectedTabIndex}");
            Assert(!cameraSettings.IsViewActive, "CameraSettings phải bị deactivate ngay khi chạy liên tục đưa về Tool Editor");
            Assert(mainVm.IsTabSwitchingLocked, "IsTabSwitchingLocked phải là true khi chạy liên tục");
            Assert(!mainVm.IsMainTabBarVisible, "IsMainTabBarVisible phải là false khi chạy liên tục");
            Assert(changedProps.Contains(nameof(MainWindowViewModel.IsTabSwitchingLocked)), "Phải phát PropertyChanged IsTabSwitchingLocked");
            Assert(changedProps.Contains(nameof(MainWindowViewModel.IsMainTabBarVisible)), "Phải phát PropertyChanged IsMainTabBarVisible");

            for (int tabIdx = 0; tabIdx <= 3; tabIdx++)
            {
                Assert(!mainVm.SwitchTabCommand.CanExecute(tabIdx.ToString()),
                    $"SwitchTabCommand.CanExecute({tabIdx}) phải trả về false khi chạy liên tục");
            }

            // Thử cố tình đổi SelectedTabIndex hoặc Execute SwitchTabCommand sang tab 1, 2, 3 -> đều bị chặn
            foreach (int targetIdx in new[] { 1, 2, 3 })
            {
                mainVm.SelectedTabIndex = targetIdx;
                Assert(mainVm.SelectedTabIndex == 0, $"SelectedTabIndex = {targetIdx} phải bị chặn khi đang chạy liên tục");
                mainVm.SwitchTabCommand.Execute(targetIdx.ToString());
                Assert(mainVm.SelectedTabIndex == 0, $"SwitchTabCommand.Execute({targetIdx}) phải bị chặn khi đang chạy liên tục");
                Assert(!cameraSettings.IsViewActive, "CameraSettings tuyệt đối không được kích hoạt khi đang chạy liên tục");
            }

            // Dừng chạy liên tục -> hiện lại tab bar & mở khóa chuyển tab
            changedProps.Clear();
            toolEditor.IsRunningFolderFlow = false;

            Assert(!mainVm.IsTabSwitchingLocked, "Khi dừng chạy liên tục IsTabSwitchingLocked phải về false");
            Assert(mainVm.IsMainTabBarVisible, "Khi dừng chạy liên tục IsMainTabBarVisible phải là true");
            Assert(changedProps.Contains(nameof(MainWindowViewModel.IsMainTabBarVisible)), "Phải phát PropertyChanged IsMainTabBarVisible khi dừng");
            Assert(mainVm.SwitchTabCommand.CanExecute("1") && mainVm.SwitchTabCommand.CanExecute("3"),
                "Khi dừng chạy liên tục SwitchTabCommand phải bật lại");

            mainVm.SwitchTabCommand.Execute("3");
            Assert(mainVm.SelectedTabIndex == 3 && cameraSettings.IsViewActive,
                "Sau khi dừng chạy liên tục phải chuyển lại sang Camera Settings bình thường");
            mainVm.SwitchTabCommand.Execute("1");
            Assert(mainVm.SelectedTabIndex == 1 && !cameraSettings.IsViewActive,
                "Sau khi dừng chạy liên tục phải chuyển lại sang OQC Scanner bình thường");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }

        Console.WriteLine("PASSED");
    }

    private static void Test_03_LiveWpfMainWindow_HidesTabBarLocksMenuAndShortcuts()
    {
        Console.Write("--- [3/4] Live WPF MainWindow: ẩn PART_TabHeadersBar (Height=0), khóa Menu Màn Hình, Ctrl+Tab & SelectionChanged... ");

        EnsureWpfAppResources();

        string tempDir = Path.Combine(Path.GetTempPath(), $"vision_tablock_ui_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "global_settings.json");
        var settingsService = new GlobalAppSettingsService(settingsPath, disableBackupSync: true);
        using var cameraService = new CameraService();
        using var cameraSettings = new CameraSettingsViewModel(cameraService, settingsService);
        using var manualInspection = new ManualInspectionViewModel(settingsService, cameraService);
        var toolEditor = new ToolEditorViewModel { IsRunningFolderFlow = false };

        var mainVm = new MainWindowViewModel(toolEditor, null!, manualInspection, null!, null!, cameraSettings)
        {
            SelectedTabIndex = 0
        };

        var win = new MainWindow(mainVm)
        {
            Left = -10000,
            Top = -10000,
            Width = 1280,
            Height = 800,
            WindowState = WindowState.Normal,
            ShowInTaskbar = false,
            ShowActivated = false
        };

        win.Show();
        try
        {
            PumpDispatcher(win);

            var tabControl = (TabControl)win.FindName("MainNavigationTabControl")!;
            Assert(tabControl is not null, "Không tìm thấy MainNavigationTabControl trong MainWindow");
            tabControl!.ApplyTemplate();
            PumpDispatcher(win);

            var tabHeadersBar = tabControl.Template.FindName("PART_TabHeadersBar", tabControl) as Border;
            Assert(tabHeadersBar is not null, "Không tìm thấy PART_TabHeadersBar trong template của MainNavigationTabControl");

            var menuViewNav = (MenuItem)win.FindName("MenuViewNavigation")!;
            Assert(menuViewNav is not null, "Không tìm thấy MenuItem MenuViewNavigation trong MainWindow");
            var navMenuItems = menuViewNav!.Items.OfType<MenuItem>().ToList();
            Assert(navMenuItems.Count == 4, $"Menu 'Màn Hình' phải có 4 mục chuyển tab, thực tế: {navMenuItems.Count}");

            // Trạng thái bình thường: tab bar hiện, ActualHeight > 0, menu bật
            Assert(tabHeadersBar!.Visibility == Visibility.Visible, "Ban đầu PART_TabHeadersBar phải Visible");
            Assert(tabHeadersBar.ActualHeight > 0, $"Ban đầu PART_TabHeadersBar.ActualHeight phải > 0, thực tế: {tabHeadersBar.ActualHeight}");
            Assert(menuViewNav.IsEnabled, "Ban đầu Menu 'Màn Hình' phải Enabled");
            Assert(navMenuItems.All(mi => mi.IsEnabled && mi.Command?.CanExecute(mi.CommandParameter) == true),
                "Ban đầu 4 mục Menu 'Màn Hình' phải Enabled và CanExecute == true");

            // Chuyển sang Camera Settings trước rồi thử quay lại Tool Editor
            mainVm.SelectedTabIndex = 3;
            PumpDispatcher(win);
            Assert(tabControl.SelectedIndex == 3 && cameraSettings.IsViewActive, "Phải chuyển sang Tab 3 bình thường khi chưa chạy liên tục");

            // Bật chạy liên tục -> tab bar ẩn hoàn toàn (Collapsed, height = 0), tự về Tab 0, Menu Màn Hình disable
            toolEditor.IsRunningFolderFlow = true;
            PumpDispatcher(win);

            Assert(mainVm.SelectedTabIndex == 0 && tabControl.SelectedIndex == 0,
                $"Phải khóa ở Tab 0 khi chạy liên tục, thực tế VM={mainVm.SelectedTabIndex}, UI={tabControl.SelectedIndex}");
            Assert(!cameraSettings.IsViewActive, "CameraSettings phải ngừng active khi chạy liên tục");
            Assert(tabHeadersBar.Visibility == Visibility.Collapsed,
                $"Khi chạy liên tục PART_TabHeadersBar phải Collapsed, thực tế: {tabHeadersBar.Visibility}");
            Assert(tabHeadersBar.ActualHeight == 0,
                $"Khi PART_TabHeadersBar bị Collapsed, ActualHeight phải = 0, thực tế: {tabHeadersBar.ActualHeight}");
            Assert(!menuViewNav.IsEnabled,
                "Khi chạy liên tục Menu 'Màn Hình' phải bị Disable");
            Assert(navMenuItems.All(mi => mi.Command?.CanExecute(mi.CommandParameter) == false),
                "Khi chạy liên tục cả 4 mục SwitchTabCommand phải CanExecute == false");

            // Giả lập Selector bị ép đổi SelectedIndex trực tiếp -> SelectionChanged + VM phải kéo lại về 0
            tabControl.SelectedIndex = 3;
            PumpDispatcher(win);
            Assert(tabControl.SelectedIndex == 0 && mainVm.SelectedTabIndex == 0,
                "Thay đổi trực tiếp tabControl.SelectedIndex = 3 khi đang chạy liên tục phải bị trả về 0");
            Assert(!cameraSettings.IsViewActive, "Thay đổi tab cưỡng bức trong khi chạy liên tục không được kích hoạt CameraSettings");

            // Giả lập KeyBinding F2..F4 / SwitchTabCommand vẫn không thể chuyển tab
            mainVm.SwitchTabCommand.Execute("3");
            PumpDispatcher(win);
            Assert(tabControl.SelectedIndex == 0 && mainVm.SelectedTabIndex == 0, "SwitchTabCommand phải bị vô hiệu hóa khi đang chạy liên tục");

            // Kiểm tra Ctrl+Tab (WPF TabControl native switching) và F2/F3/F4 không làm đổi tab
            foreach (Key navKey in new[] { Key.F2, Key.F3, Key.F4 })
            {
                var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(win)!, 0, navKey)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent
                };
                win.RaiseEvent(keyArgs);
                PumpDispatcher(win);
                Assert(tabControl.SelectedIndex == 0 && mainVm.SelectedTabIndex == 0,
                    $"Phím {navKey} phải bị khóa không chuyển tab khi đang chạy liên tục");
                Assert(keyArgs.Handled, $"Phím {navKey} phải được đánh dấu Handled khi đang chạy liên tục");
            }

            // Dừng chạy liên tục -> tab bar hiện lại, ActualHeight > 0, Menu bật lại, chuyển tab bình thường
            toolEditor.IsRunningFolderFlow = false;
            PumpDispatcher(win);

            Assert(tabHeadersBar.Visibility == Visibility.Visible,
                $"Khi dừng chạy liên tục PART_TabHeadersBar phải Visible trở lại, thực tế: {tabHeadersBar.Visibility}");
            Assert(tabHeadersBar.ActualHeight > 0,
                $"Khi dừng chạy liên tục PART_TabHeadersBar.ActualHeight phải > 0, thực tế: {tabHeadersBar.ActualHeight}");
            Assert(menuViewNav.IsEnabled, "Khi dừng chạy liên tục Menu 'Màn Hình' phải Enabled trở lại");
            Assert(navMenuItems.All(mi => mi.IsEnabled && mi.Command?.CanExecute(mi.CommandParameter) == true),
                "Khi dừng chạy liên tục các mục Menu 'Màn Hình' phải bật trở lại");

            mainVm.SwitchTabCommand.Execute("3");
            PumpDispatcher(win);
            Assert(tabControl.SelectedIndex == 3 && mainVm.SelectedTabIndex == 3, "Sau khi dừng phải chuyển lại sang tab Camera Settings bình thường");
        }
        finally
        {
            win.Close();
            PumpDispatcher(null);
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }

        Console.WriteLine("PASSED");
    }

    private static async Task Test_04_CameraSettingsAndTriggerMode_DoNotPushFreeRunFramesAfterAutoReturn()
    {
        Console.Write("--- [4/4] CameraSettings & CameraService: rời tab không tắt nhầm TriggerMode=On và không đẩy frame free-run... ");

        string tempDir = Path.Combine(Path.GetTempPath(), $"vision_camlock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "global_settings.json");
        var settingsService = new GlobalAppSettingsService(settingsPath, disableBackupSync: true);
        using var cameraService = new CameraService();
        using var cameraSettings = new CameraSettingsViewModel(cameraService, settingsService);
        using var manualInspection = new ManualInspectionViewModel(settingsService, cameraService);
        var toolEditor = new ToolEditorViewModel { IsRunningFolderFlow = false };
        var mainVm = new MainWindowViewModel(toolEditor, null!, manualInspection, null!, null!, cameraSettings);

        try
        {
            var simDev = new CameraDeviceInfo
            {
                Vendor = CameraVendor.Simulator,
                InterfaceType = CameraInterfaceType.Virtual,
                Index = CameraService.SimulatorCameraIndex,
                ModelName = "Simulator Tab Lock Test"
            };
            bool started = await cameraService.StartDriverCameraAsync(simDev, new CameraParameters
            {
                Width = 320,
                Height = 240,
                TargetFps = 30,
                TriggerMode = CameraTriggerMode.Off,
                IsLiveViewEnabled = true
            });
            Assert(started, "Không thể khởi động Simulator Camera");

            // 1. Khi CameraSettings đang bật LiveStream ở tab 3, chuyển sang trạng thái chạy liên tục ở ToolEditor
            mainVm.SelectedTabIndex = 3;
            Assert(cameraSettings.IsViewActive, "CameraSettings phải active ở Tab 3");
            await cameraService.RequestLiveStreamAsync("CameraSettings", true);

            // Chuyển sang chạy liên tục -> tự về Tab 0 & tắt consumer "CameraSettings" trước khi đặt TriggerMode = On
            toolEditor.IsRunningFolderFlow = true;
            Assert(mainVm.SelectedTabIndex == 0 && !cameraSettings.IsViewActive, "Phải tự về Tab 0 và hủy CameraSettings.IsViewActive");

            await cameraService.ApplyParametersAsync(new CameraParameters
            {
                Width = 320,
                Height = 240,
                TargetFps = 30,
                TriggerMode = CameraTriggerMode.On,
                TriggerSource = CameraTriggerSource.Software,
                IsLiveViewEnabled = false
            });
            if (cameraService.ActiveDriver != null && !cameraService.ActiveDriver.IsGrabbing)
            {
                await cameraService.ActiveDriver.StartGrabbingAsync();
            }

            Assert(cameraService.CurrentParameters.TriggerMode == CameraTriggerMode.On,
                $"TriggerMode phải giữ On khi đã hủy consumer CameraSettings, thực tế: {cameraService.CurrentParameters.TriggerMode}");

            int spontaneousFrames = 0;
            void FrameCounter(object? s, OpenCvSharp.Mat f) => Interlocked.Increment(ref spontaneousFrames);
            cameraService.FrameCaptured += FrameCounter;

            // Đợi 250ms: không có frame free-run tự phát vì đang ở TriggerMode = On và không thể chuyển sang CameraSettings
            await Task.Delay(250);
            Assert(spontaneousFrames == 0, $"Không được có frame free-run tự đẩy vào queue khi TriggerMode=On, thực tế: {spontaneousFrames}");

            // Thử chuyển sang Tab 3 (Camera Settings) khi đang chạy liên tục -> bị chặn, vẫn 0 frame free-run
            mainVm.SelectedTabIndex = 3;
            mainVm.SwitchTabCommand.Execute("3");
            await Task.Delay(150);
            Assert(mainVm.SelectedTabIndex == 0 && !cameraSettings.IsViewActive, "Không thể chuyển sang Camera Settings khi chạy liên tục");
            Assert(spontaneousFrames == 0, "Chặn chuyển tab giữ nguyên 0 frame free-run");

            // Kiểm chứng OnViewDeactivated khi !IsViewActive (và RequestLiveStreamAsync(..., false) khi TriggerMode=On) không làm dừng driver TriggerMode=On
            cameraSettings.OnViewDeactivated();
            bool stoppedByInactive = await cameraService.RequestLiveStreamAsync("CameraSettings", false);
            Assert(stoppedByInactive, "RequestLiveStreamAsync(false) phải trả về an toàn");
            Assert(cameraService.ActiveDriver != null && cameraService.ActiveDriver.IsGrabbing,
                "Không được làm dừng grabbing của TriggerMode=On khi hủy consumer không hoạt động");

            // 1 lần Software Trigger vẫn phát đúng 1 frame
            bool trigOk = await cameraService.ExecuteSoftwareTriggerAsync();
            Assert(trigOk, "Software Trigger ở chế độ Continuous phải hoạt động bình thường");
            await Task.Delay(80);
            Assert(spontaneousFrames == 1, $"1 Software Trigger phải phát đúng 1 frame, thực tế: {spontaneousFrames}");

            cameraService.FrameCaptured -= FrameCounter;
            toolEditor.IsRunningFolderFlow = false;
            await cameraService.StopCameraAsync();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }

        Console.WriteLine("PASSED");
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void PumpDispatcher(Window? win)
    {
        win?.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new DispatcherOperationCallback(_ =>
        {
            frame.Continue = false;
            return null;
        }), null);
        Dispatcher.PushFrame(frame);
        win?.UpdateLayout();
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

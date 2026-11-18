using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using VisionInspectionApp.Application.HMI;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.HMI;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.UI.ViewModels.HMI;
using VisionInspectionApp.UI.Views.HMI;

namespace TestExtractApp;

public static class HmiButtonShortcutTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> BẮT ĐẦU KIỂM THỬ: PHÍM TẮT BUTTON HMI (DESIGNER & TOOL EDITOR) <<<");
        Console.WriteLine("==================================================================");

        Test1_ShortcutKey_PersistenceAndBackwardCompatibility();
        Test2_HmiShortcutHelper_FormatParseMatchAndBadge();
        Test3_HmiDesigner_ShortcutAssignUndoWarningAndRunModeExecution().GetAwaiter().GetResult();
        Test4_ToolEditor_ShortcutsWorkBothWhenHmiHiddenAndShown().GetAwaiter().GetResult();
        Test5_StaWpfFocusGuard_ProtectsTextInputsAndAllowsButtonsAndFunctionKeys();

        Console.WriteLine("==================================================================");
        Console.WriteLine(">>> TẤT CẢ 5/5 BÀI TEST PHÍM TẮT HMI BUTTON ĐÃ PASSED 100%! <<<");
        Console.WriteLine("==================================================================");
    }

    private static void Test1_ShortcutKey_PersistenceAndBackwardCompatibility()
    {
        Console.WriteLine("\n[1] Testing ShortcutKey JSON Persistence & Backward Compatibility...");

        var config = new HmiScreenConfig
        {
            ScreenName = "Shortcut Persistence",
            TargetPlcId = "PLC1",
            Controls = new List<HmiControlModel>
            {
                new HmiControlModel { Name = "Btn_F7", Type = HmiControlType.Button, ReadTagName = "M1", ShortcutKey = "F7" },
                new HmiControlModel { Name = "Btn_Space", Type = HmiControlType.Button, ReadTagName = "M2", ShortcutKey = "Space" },
                new HmiControlModel { Name = "Btn_Ctrl1", Type = HmiControlType.Button, ReadTagName = "M3", ShortcutKey = "Ctrl+1" },
                new HmiControlModel { Name = "Btn_Combo", Type = HmiControlType.Button, ReadTagName = "M4", ShortcutKey = "Alt+Shift+S" }
            }
        };

        string tempPath = Path.Combine(Path.GetTempPath(), $"hmi_shortcut_{Guid.NewGuid():N}.hmi");
        try
        {
            HmiService.SaveHmiConfigAsync(tempPath, config).GetAwaiter().GetResult();
            var reloaded = HmiService.LoadHmiConfig(tempPath);

            AssertEqual("F7", reloaded.Controls[0].ShortcutKey, "Shortcut F7 phải được bảo toàn");
            AssertEqual("Space", reloaded.Controls[1].ShortcutKey, "Shortcut Space phải được bảo toàn");
            AssertEqual("Ctrl+1", reloaded.Controls[2].ShortcutKey, "Shortcut Ctrl+1 phải được bảo toàn");
            AssertEqual("Alt+Shift+S", reloaded.Controls[3].ShortcutKey, "Shortcut Alt+Shift+S phải được bảo toàn");

            // Kiểm tra tương thích ngược file .hmi cũ không có trường ShortcutKey
            string legacyJson = """
            {
              "ScreenId": "Legacy",
              "TargetPlcId": "PLC1",
              "Controls": [
                { "Name": "OldBtn", "Type": 0, "ReadTagName": "M0" }
              ]
            }
            """;
            File.WriteAllText(tempPath, legacyJson);
            var legacyLoaded = HmiService.LoadHmiConfig(tempPath);
            AssertEqual(string.Empty, legacyLoaded.Controls[0].ShortcutKey, "File .hmi cũ phải mặc định ShortcutKey = rỗng");
            Console.WriteLine(" -> PASSED");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void Test2_HmiShortcutHelper_FormatParseMatchAndBadge()
    {
        Console.WriteLine("\n[2] Testing HmiShortcutHelper & HasShortcutBadge...");

        AssertEqual("Ctrl+1", HmiShortcutHelper.FormatShortcut(ModifierKeys.Control, Key.D1), "D1 + Ctrl phải format thành Ctrl+1");
        AssertEqual("NumPad1", HmiShortcutHelper.FormatShortcut(ModifierKeys.None, Key.NumPad1), "NumPad1 giữ nguyên tên NumPad1");
        AssertEqual("Alt+Shift+Enter", HmiShortcutHelper.FormatShortcut(ModifierKeys.Alt | ModifierKeys.Shift, Key.Return), "Return phải format thành Enter");
        AssertEqual(string.Empty, HmiShortcutHelper.FormatShortcut(ModifierKeys.Control, Key.LeftCtrl), "Phím modifier thuần không hợp lệ");
        AssertEqual("Ctrl+Shift+Enter", HmiShortcutHelper.NormalizeShortcutString("ctrl + shift + return"), "Normalize phải chuẩn hóa thứ tự và alias");

        AssertEqual(true, HmiShortcutHelper.Matches("Ctrl+1", Key.D1, ModifierKeys.Control), "Ctrl+1 phải khớp D1 + Ctrl");
        AssertEqual(false, HmiShortcutHelper.Matches("Ctrl+1", Key.NumPad1, ModifierKeys.Control), "Ctrl+1 không được nhầm với NumPad1");
        AssertEqual(false, HmiShortcutHelper.Matches("F7", Key.F7, ModifierKeys.Control), "F7 không được khớp khi đang giữ Ctrl");

        var btnVm = new HmiControlViewModel(new HmiControlModel { Name = "B1", Type = HmiControlType.Button, ShortcutKey = "F7" });
        AssertEqual(true, btnVm.IsButtonControl, "Button phải có IsButtonControl = true");
        AssertEqual(true, btnVm.HasShortcutBadge, "Button có phím tắt phải hiện badge");

        btnVm.Type = HmiControlType.Lamp;
        AssertEqual(false, btnVm.IsButtonControl, "Khi đổi sang Lamp, IsButtonControl phải = false");
        AssertEqual(false, btnVm.HasShortcutBadge, "Lamp không được hiện badge phím tắt");

        Console.WriteLine(" -> PASSED");
    }

    private static async Task Test3_HmiDesigner_ShortcutAssignUndoWarningAndRunModeExecution()
    {
        Console.WriteLine("\n[3] Testing HMI Designer (Assign, Undo/Redo, Duplicate Warning & RunMode Execution)...");

        var plcService = CreatePlcWithDevice("PLC1");
        var vm = new HmiManagerViewModel(plcService);
        vm.Controls.Clear();

        var btnToggle = new HmiControlViewModel(new HmiControlModel
        {
            Name = "Btn_Toggle",
            Type = HmiControlType.Button,
            ButtonBehavior = HmiButtonBehavior.Toggle,
            ReadPlcId = "PLC1",
            ReadTagName = "M10"
        }, plcService, vm.ScreenConfig);

        var btnMomentary = new HmiControlViewModel(new HmiControlModel
        {
            Name = "Btn_Momentary",
            Type = HmiControlType.Button,
            ButtonBehavior = HmiButtonBehavior.Momentary,
            ReadPlcId = "PLC1",
            ReadTagName = "M11",
            ShortcutKey = "Ctrl+Space"
        }, plcService, vm.ScreenConfig);

        var btnSetTrue = new HmiControlViewModel(new HmiControlModel
        {
            Name = "Btn_SetTrue",
            Type = HmiControlType.Button,
            ButtonBehavior = HmiButtonBehavior.SetTrue,
            ReadPlcId = "PLC1",
            ReadTagName = "M12",
            ShortcutKey = "F8"
        }, plcService, vm.ScreenConfig);

        var btnSetFalse = new HmiControlViewModel(new HmiControlModel
        {
            Name = "Btn_SetFalse",
            Type = HmiControlType.Button,
            ButtonBehavior = HmiButtonBehavior.SetFalse,
            ReadPlcId = "PLC1",
            ReadTagName = "M13",
            ShortcutKey = "F9"
        }, plcService, vm.ScreenConfig);

        var lamp = new HmiControlViewModel(new HmiControlModel
        {
            Name = "Lamp_Status",
            Type = HmiControlType.Lamp,
            ReadPlcId = "PLC1",
            ReadTagName = "M14"
        }, plcService, vm.ScreenConfig);

        vm.Controls.Add(btnToggle);
        vm.Controls.Add(btnMomentary);
        vm.Controls.Add(btnSetTrue);
        vm.Controls.Add(btnSetFalse);
        vm.Controls.Add(lamp);

        // Không gán phím tắt cho thiết bị không phải Button
        vm.SelectControl(lamp);
        vm.AssignButtonShortcut(lamp, "F12");
        AssertEqual(string.Empty, lamp.ShortcutKey, "Không được gán phím tắt cho Lamp");

        // Gán, Undo, Redo cho Button
        vm.SelectControl(btnToggle);
        vm.AssignButtonShortcut(btnToggle, "F7");
        AssertEqual("F7", btnToggle.ShortcutKey, "Phải gán được F7 cho Btn_Toggle");

        vm.UndoCommand.Execute(null);
        AssertEqual(string.Empty, btnToggle.ShortcutKey, "Undo phải hoàn tác phím tắt về rỗng");

        vm.RedoCommand.Execute(null);
        AssertEqual("F7", btnToggle.ShortcutKey, "Redo phải khôi phục lại F7");

        // Cảnh báo trùng phím tắt
        vm.AssignButtonShortcut(btnToggle, "F8");
        AssertEqual(true, vm.HasSelectedControlShortcutWarning, "Phải cảnh báo khi trùng phím F8 với Btn_SetTrue");
        vm.AssignButtonShortcut(btnToggle, "F7");
        AssertEqual(false, vm.HasSelectedControlShortcutWarning, "Hết trùng phím thì cảnh báo phải biến mất");

        // Edit Mode (IsRunMode = false) -> không kích hoạt PLC
        vm.IsRunMode = false;
        bool handledInEdit = vm.TryHandleShortcutKeyDown(Key.F7, ModifierKeys.None, isRepeat: false, allowWhenNotRunMode: false, out _);
        AssertEqual(false, handledInEdit, "Ở chế độ thiết kế (Edit Mode) không được kích hoạt phím tắt Button");
        AssertEqual(false, btnToggle.IsOn, "Btn_Toggle vẫn phải tắt ở Edit Mode");

        // Run Mode -> kích hoạt vật lý như click chuột
        vm.IsRunMode = true;

        // 1. Toggle + Repeat suppression
        AssertEqual(true, vm.TryHandleShortcutKeyDown(Key.F7, ModifierKeys.None, isRepeat: false, allowWhenNotRunMode: false, out _), "F7 phải khớp ở RunMode");
        await vm.WaitForPendingShortcutActionsAsync();
        AssertEqual(true, btnToggle.IsOn, "F7 lần 1 phải bật Toggle");
        AssertEqual(true, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M10")?.CurrentValue), "M10 phải ghi True vào PLC");

        AssertEqual(true, vm.TryHandleShortcutKeyDown(Key.F7, ModifierKeys.None, isRepeat: true, allowWhenNotRunMode: false, out _), "Auto-repeat phải được nuốt (handled)");
        await vm.WaitForPendingShortcutActionsAsync();
        AssertEqual(true, btnToggle.IsOn, "Auto-repeat KHÔNG được đảo trạng thái Toggle");

        AssertEqual(true, vm.TryHandleShortcutKeyUp(Key.F7, allowWhenNotRunMode: false), "KeyUp F7 phải trả về handled");
        AssertEqual(true, btnToggle.IsOn, "Toggle giữ nguyên trạng thái sau KeyUp");

        vm.TryHandleShortcutKeyDown(Key.F7, ModifierKeys.None, isRepeat: false, allowWhenNotRunMode: false, out _);
        await vm.WaitForPendingShortcutActionsAsync();
        AssertEqual(false, btnToggle.IsOn, "F7 lần 2 phải tắt Toggle");
        AssertEqual(false, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M10")?.CurrentValue), "M10 phải ghi False");

        // 2. Momentary Ctrl+Space: nhả Ctrl trước khi nhả Space vẫn phải tắt đúng
        AssertEqual(true, vm.TryHandleShortcutKeyDown(Key.Space, ModifierKeys.Control, isRepeat: false, allowWhenNotRunMode: false, out _), "Ctrl+Space phải kích hoạt Momentary");
        AssertEqual(true, btnMomentary.IsOn, "Momentary phải ON khi giữ phím");
        AssertEqual(true, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M11")?.CurrentValue), "M11 phải là True khi giữ");

        // Nhả phím Space (không còn ModifierKeys.Control trong sự kiện KeyUp)
        AssertEqual(true, vm.TryHandleShortcutKeyUp(Key.Space, allowWhenNotRunMode: false), "Nhả Space phải tìm thấy phím Momentary đang giữ");
        await vm.WaitForPendingShortcutActionsAsync();
        AssertEqual(false, btnMomentary.IsOn, "Momentary phải tắt sau khi nhả phím");
        AssertEqual(false, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M11")?.CurrentValue), "M11 phải ghi False khi nhả phím");

        // 3. SetTrue / SetFalse
        vm.TryHandleShortcutKeyDown(Key.F8, ModifierKeys.None, isRepeat: false, allowWhenNotRunMode: false, out _);
        await vm.WaitForPendingShortcutActionsAsync();
        AssertEqual(true, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M12")?.CurrentValue), "SetTrue F8 phải ghi True");

        plcService.Cache.Set("PLC1", "M13", true, TagQuality.Good);
        btnSetFalse.IsOn = true;
        vm.TryHandleShortcutKeyDown(Key.F9, ModifierKeys.None, isRepeat: false, allowWhenNotRunMode: false, out _);
        await vm.WaitForPendingShortcutActionsAsync();
        AssertEqual(false, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M13")?.CurrentValue), "SetFalse F9 phải ghi False");

        // 4. ReleaseAll khi chuyển về Edit Mode giữa lúc đang giữ phím Momentary
        vm.TryHandleShortcutKeyDown(Key.Space, ModifierKeys.Control, isRepeat: false, allowWhenNotRunMode: false, out _);
        AssertEqual(true, btnMomentary.IsOn, "Đang giữ Space trước khi dừng RunMode");
        vm.IsRunMode = false;
        await vm.WaitForPendingShortcutActionsAsync();
        AssertEqual(false, btnMomentary.IsOn, "Thoát RunMode phải tự nhả Momentary đang giữ");

        Console.WriteLine(" -> PASSED");
    }

    private static async Task Test4_ToolEditor_ShortcutsWorkBothWhenHmiHiddenAndShown()
    {
        Console.WriteLine("\n[4] Testing ToolEditorViewModel - Phím tắt nhận cả khi ẨN và HIỆN HMI...");

        string tempDir = Path.Combine(Path.GetTempPath(), $"Vision2026_HmiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string hmiPath1 = Path.Combine(tempDir, "line1.hmi");
        string hmiPath2 = Path.Combine(tempDir, "line2.hmi");
        string settingsPath = Path.Combine(tempDir, "global_settings.json");

        try
        {
            var config1 = new HmiScreenConfig
            {
                ScreenName = "Line 1",
                TargetPlcId = "PLC1",
                Controls = new List<HmiControlModel>
                {
                    new HmiControlModel { Name = "Btn_Jog", Type = HmiControlType.Button, ButtonBehavior = HmiButtonBehavior.Momentary, ReadPlcId = "PLC1", ReadTagName = "M20", ShortcutKey = "F8" },
                    new HmiControlModel { Name = "Btn_Run", Type = HmiControlType.Button, ButtonBehavior = HmiButtonBehavior.Toggle, ReadPlcId = "PLC1", ReadTagName = "M21", ShortcutKey = "Ctrl+1" },
                    new HmiControlModel { Name = "Btn_Sync", Type = HmiControlType.Button, ButtonBehavior = HmiButtonBehavior.Toggle, ReadPlcId = "PLC1", ReadTagName = "M22", ShortcutKey = "F10" }
                }
            };
            await HmiService.SaveHmiConfigAsync(hmiPath1, config1);

            var config2 = new HmiScreenConfig
            {
                ScreenName = "Line 2",
                TargetPlcId = "PLC1",
                Controls = new List<HmiControlModel>
                {
                    new HmiControlModel { Name = "Btn_Line2", Type = HmiControlType.Button, ButtonBehavior = HmiButtonBehavior.SetTrue, ReadPlcId = "PLC1", ReadTagName = "M23", ShortcutKey = "F12" }
                }
            };
            await HmiService.SaveHmiConfigAsync(hmiPath2, config2);

            var plcService = CreatePlcWithDevice("PLC1");
            plcService.Cache.Set("PLC1", "M22", true, TagQuality.Good);

            var settingsService = new GlobalAppSettingsService(settingsPath, disableBackupSync: true);
            settingsService.UpdateSettings(new GlobalAppSettings
            {
                ToolEditorIsHmiVisible = false,
                ToolEditorSelectedHmiPath = hmiPath1
            });

            var toolEditorVm = new ToolEditorViewModel();
            toolEditorVm.ConfigureHmiPanelForTesting(plcService, settingsService);

            // CASE A: HMI ĐANG ẨN ngay từ lúc khởi động
            AssertEqual(false, toolEditorVm.IsHmiPanelVisible, "HMI ban đầu phải ở trạng thái ẨN");
            AssertEqual(false, toolEditorVm.HmiMonitorViewModel!.IsRunMode, "Khi HMI ẩn, IsRunMode vẫn = false để không chiếm polling");
            AssertEqual(false, plcService.IsPollingActive, "Khi HMI ẩn, không được bật PollingLock");

            // Bấm Ctrl+1 (Toggle M21) khi HMI ĐANG ẨN
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.D1, ModifierKeys.Control, isRepeat: false, focusedElement: null), "Phải nhận phím Ctrl+1 khi HMI đang ẩn");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();
            AssertEqual(true, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M21")?.CurrentValue), "M21 phải ghi True khi bấm Ctrl+1 lúc HMI ẩn");

            // Bấm F10 (Toggle M22 đã có sẵn True trong PLC Cache) khi HMI ĐANG ẨN -> phải đảo sang False
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.F10, ModifierKeys.None, isRepeat: false, focusedElement: null), "Phải nhận F10 khi HMI ẩn");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();
            AssertEqual(false, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M22")?.CurrentValue), "M22 phải đồng bộ cache True -> đảo thành False");

            // Bấm giữ và nhả F8 (Momentary M20) khi HMI ĐANG ẨN
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.F8, ModifierKeys.None, isRepeat: false, focusedElement: null), "Phải nhận F8 Momentary khi HMI ẩn");
            AssertEqual(true, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M20")?.CurrentValue), "M20 phải ON khi bấm F8 lúc HMI ẩn");
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyUp(Key.F8), "KeyUp F8 phải được xử lý khi HMI ẩn");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();
            AssertEqual(false, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M20")?.CurrentValue), "M20 phải OFF sau khi nhả F8 lúc HMI ẩn");

            // CASE B: Bật HIỆN HMI -> bấm Ctrl+1 đảo lại False
            toolEditorVm.IsHmiPanelVisible = true;
            AssertEqual(true, toolEditorVm.HmiMonitorViewModel.IsRunMode, "Khi Hiện HMI, IsRunMode phải = true");
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.D1, ModifierKeys.Control, isRepeat: false, focusedElement: null), "Phải nhận Ctrl+1 khi đang Hiện HMI");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();
            AssertEqual(false, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M21")?.CurrentValue), "M21 phải đảo về False khi HMI đang hiện");

            // CASE C: Bấm ẨN HMI trở lại -> vẫn nhận Ctrl+1
            toolEditorVm.IsHmiPanelVisible = false;
            AssertEqual(false, toolEditorVm.HmiMonitorViewModel.IsRunMode, "Sau khi ẩn lại, IsRunMode về false");
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.D1, ModifierKeys.Control, isRepeat: false, focusedElement: null), "Vẫn phải nhận Ctrl+1 sau khi chuyển từ Hiện -> Ẩn HMI");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();
            AssertEqual(true, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M21")?.CurrentValue), "M21 phải đảo lại True");

            // CASE D: Cập nhật file .hmi từ HMI Designer khi đang ẩn -> SyncHmiFromSavedDesign đồng bộ ngay phím tắt mới
            config1.Controls[1].ShortcutKey = "F11";
            await HmiService.SaveHmiConfigAsync(hmiPath1, config1);
            toolEditorVm.SyncHmiFromSavedDesign(hmiPath1);
            AssertEqual(false, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.D1, ModifierKeys.Control, isRepeat: false, focusedElement: null), "Phím cũ Ctrl+1 không còn khớp sau khi sửa");
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.F11, ModifierKeys.None, isRepeat: false, focusedElement: null), "Phím mới F11 phải nhận ngay dù HMI đang ẩn");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();
            AssertEqual(false, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M21")?.CurrentValue), "M21 phải đổi qua F11 thành công");

            // CASE E: Chọn tệp HMI khác khi HMI đang ẩn -> nạp luôn phím tắt tệp mới
            toolEditorVm.SelectHmiFileCommand.Execute(hmiPath2);
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.F12, ModifierKeys.None, isRepeat: false, focusedElement: null), "SelectHmiFile lúc đang ẩn vẫn phải nạp phím tắt của file mới");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();
            AssertEqual(true, Convert.ToBoolean(plcService.GetTagValue("PLC1", "M23")?.CurrentValue), "M23 phải được ghi True qua F12");

            // CASE F: Dừng HMI (StopHmiMonitor) khóa an toàn, Chạy lại (StartHmiMonitor) mở lại
            toolEditorVm.StopHmiMonitorCommand.Execute(null);
            AssertEqual(false, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.F12, ModifierKeys.None, isRepeat: false, focusedElement: null), "Khi đã bấm Dừng HMI, không được kích hoạt phím tắt");
            toolEditorVm.StartHmiMonitorCommand.Execute(null);
            AssertEqual(true, toolEditorVm.TryHandleHmiShortcutKeyDown(Key.F12, ModifierKeys.None, isRepeat: false, focusedElement: null), "Sau khi Chạy HMI lại, phím tắt hoạt động bình thường");
            await toolEditorVm.HmiMonitorViewModel.WaitForPendingShortcutActionsAsync();

            Console.WriteLine(" -> PASSED");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void Test5_StaWpfFocusGuard_ProtectsTextInputsAndAllowsButtonsAndFunctionKeys()
    {
        Console.WriteLine("\n[5] Testing STA WPF Focus Guard & Views Instantiation...");

        Exception? staException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var textBox = new TextBox();
                var passwordBox = new PasswordBox();
                var editableCombo = new ComboBox { IsEditable = true };
                var closedCombo = new ComboBox { IsEditable = false };
                var comboItem = new ComboBoxItem();
                var toolbarButton = new Button { Content = "Ẩn HMI" };

                // TextBox / PasswordBox / Editable ComboBox / ComboBoxItem phải chặn phím chữ, Space, Ctrl+C nhưng vẫn cho phép F7..F12
                AssertEqual(false, HmiShortcutHelper.ShouldAllowShortcutForFocus(textBox, Key.A, ModifierKeys.None), "TextBox đang focus không được kích hoạt phím A");
                AssertEqual(false, HmiShortcutHelper.ShouldAllowShortcutForFocus(textBox, Key.Space, ModifierKeys.None), "TextBox đang focus không được kích hoạt phím Space");
                AssertEqual(false, HmiShortcutHelper.ShouldAllowShortcutForFocus(textBox, Key.C, ModifierKeys.Control), "TextBox đang focus không được chặn Ctrl+C");
                AssertEqual(true, HmiShortcutHelper.ShouldAllowShortcutForFocus(textBox, Key.F7, ModifierKeys.None), "TextBox đang focus vẫn cho phép phím chức năng F7");
                AssertEqual(false, HmiShortcutHelper.ShouldAllowShortcutForFocus(passwordBox, Key.D1, ModifierKeys.None), "PasswordBox đang focus phải chặn phím số");
                AssertEqual(false, HmiShortcutHelper.ShouldAllowShortcutForFocus(editableCombo, Key.Space, ModifierKeys.None), "ComboBox IsEditable phải chặn phím Space");
                AssertEqual(false, HmiShortcutHelper.ShouldAllowShortcutForFocus(comboItem, Key.Enter, ModifierKeys.None), "ComboBoxItem (dropdown đang mở) phải chặn phím Enter");

                // Toolbar Button hoặc ComboBox đang đóng vẫn phải cho phép nhận phím tắt HMI (Space, Enter, A, F7)
                AssertEqual(true, HmiShortcutHelper.ShouldAllowShortcutForFocus(closedCombo, Key.Space, ModifierKeys.None), "ComboBox không editable & đang đóng vẫn cho phép Space");
                AssertEqual(true, HmiShortcutHelper.ShouldAllowShortcutForFocus(toolbarButton, Key.Space, ModifierKeys.None), "Button đang focus vẫn phải cho phép phím tắt Space");
                AssertEqual(true, HmiShortcutHelper.ShouldAllowShortcutForFocus(toolbarButton, Key.F7, ModifierKeys.None), "Button đang focus vẫn phải cho phép F7");

                // Khởi tạo thử View XAML đảm bảo không lỗi XAML/Binding
                var inspector = new HmiPropertyInspectorView();
                var controlView = new HmiControlView
                {
                    DataContext = new HmiControlViewModel(new HmiControlModel { Name = "B1", Type = HmiControlType.Button, ShortcutKey = "F7" })
                };
                if (inspector == null || controlView == null) throw new Exception("Không khởi tạo được HMI View");
            }
            catch (Exception ex)
            {
                staException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (staException != null)
        {
            throw new Exception($"STA WPF test lỗi: {staException.Message}", staException);
        }

        Console.WriteLine(" -> PASSED");
    }

    private static PlcManagerService CreatePlcWithDevice(string plcId)
    {
        var plcService = TestPlcConfigHelper.CreateIsolatedPlcManager();
        plcService.Plcs.Clear();
        plcService.Plcs.Add(new PlcModel
        {
            Id = plcId,
            Name = plcId,
            DriverType = PlcDriverType.Mitsubishi,
            Enabled = true
        });
        return plcService;
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"FAIL: {message} | Kỳ vọng: '{expected}', Thực tế: '{actual}'");
        }
    }
}

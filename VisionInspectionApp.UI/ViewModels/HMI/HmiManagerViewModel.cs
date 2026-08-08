using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Application.HMI;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels.HMI;

public class PlcItemDisplay
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public partial class HmiManagerViewModel : ObservableObject
{
    private readonly IPlcManagerService? _plcService;
    private readonly UndoRedoManager _undoManager = new();
    private readonly List<HmiControlModel> _clipboardModels = new();

    [ObservableProperty]
    private HmiScreenConfig _screenConfig = new();

    [ObservableProperty]
    private bool _isRunMode = false;

    [ObservableProperty]
    private string _currentFilePath = "";

    [ObservableProperty]
    private bool _isDirty = false;

    [ObservableProperty]
    private HmiControlViewModel? _selectedControl;

    [ObservableProperty]
    private bool _hasSelectedControl = false;

    [ObservableProperty]
    private bool _hasNoSelectedControl = true;

    [ObservableProperty]
    private string _selectedGlobalLibraryImage = "";

    [ObservableProperty]
    private string _statusMessage = "Chế độ Thiết kế (PAUSE) - Kéo thả phần tử và cấu hình thuộc tính.";

    [ObservableProperty]
    private Brush _statusBrush = Brushes.DodgerBlue;

    public ObservableCollection<HmiControlViewModel> Controls { get; } = new();
    public ObservableCollection<HmiControlViewModel> SelectedControls { get; } = new();

    public Array ControlTypes => Enum.GetValues(typeof(HmiControlType));
    public Array ButtonBehaviors => Enum.GetValues(typeof(HmiButtonBehavior));
    public Array ColorThemes => Enum.GetValues(typeof(HmiColorTheme));
    public Array ValueDataTypes => Enum.GetValues(typeof(PlcDataType));
    public Array TextAlignments => Enum.GetValues(typeof(HmiTextAlignment));

    public string CurrentFileName => string.IsNullOrWhiteSpace(CurrentFilePath) ? "Untitled.hmi" : Path.GetFileName(CurrentFilePath);

    public string WindowTitle
    {
        get
        {
            string fileStr = CurrentFileName;
            string dirtyMarker = IsDirty ? " *" : "";
            string modeStr = IsRunMode ? " [VẬN HÀNH / RUN MODE]" : " [THIẾT KẾ / EDIT MODE]";
            return $"🖥️ VISION AUTOMATION HMI DESIGNER - [{fileStr}{dirtyMarker}]{modeStr}";
        }
    }

    public bool CanUndo => _undoManager.CanUndo;
    public bool CanRedo => _undoManager.CanRedo;

    public ObservableCollection<PlcItemDisplay> AvailablePlcItems { get; } = new();
    public ObservableCollection<string> AvailableTags { get; } = new();
    public ObservableCollection<string> GlobalLibraryImages { get; } = new();

    public void RefreshAvailablePlcsAndTags()
    {
        // 1. Refresh AvailablePlcItems
        AvailablePlcItems.Clear();
        AvailablePlcItems.Add(new PlcItemDisplay { Id = "", DisplayName = "(Mặc định dùng PLC chính của màn hình)" });
        if (_plcService != null && _plcService.Plcs.Count > 0)
        {
            foreach (var p in _plcService.Plcs)
            {
                string status = p.Enabled ? "⚡ Online" : "⚪ Disabled";
                string name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name;
                AvailablePlcItems.Add(new PlcItemDisplay
                {
                    Id = p.Id,
                    DisplayName = $"{name} [{p.IPAddress}:{p.Port}] ({p.DriverType} - {status})"
                });
            }
        }
        else
        {
            AvailablePlcItems.Add(new PlcItemDisplay { Id = "PLC_01", DisplayName = "PLC_01 [192.168.1.10:502] (Mitsubishi - Simulation)" });
        }

        // 2. Refresh AvailableTags
        AvailableTags.Clear();
        var common = new[] { "X0", "X1", "X2", "Y0", "Y1", "Y2", "D0", "D10", "D100", "D200", "M0", "M1", "M10", "MW100" };
        foreach (var addr in common)
        {
            AvailableTags.Add(addr);
        }

        if (_plcService != null && _plcService.Tags.Count > 0)
        {
            foreach (var t in _plcService.Tags)
            {
                if (!string.IsNullOrWhiteSpace(t.Name) && !AvailableTags.Contains(t.Name)) AvailableTags.Add(t.Name);
                if (!string.IsNullOrWhiteSpace(t.Address) && !AvailableTags.Contains(t.Address)) AvailableTags.Add(t.Address);
            }
        }
    }

    public HmiManagerViewModel(IPlcManagerService? plcService = null)
    {
        _plcService = plcService;

        RefreshAvailablePlcsAndTags();

        if (_plcService != null)
        {
            _plcService.OnTagChanged += PlcService_OnTagChanged;
            _plcService.Plcs.CollectionChanged += (s, e) =>
            {
                RefreshAvailablePlcsAndTags();
            };
            _plcService.Tags.CollectionChanged += (s, e) =>
            {
                RefreshAvailablePlcsAndTags();
            };

            if (!_plcService.IsPollingActive)
            {
                _ = _plcService.StartPollingAsync();
            }
        }

        RefreshGlobalLibraryImages();
        InitDefaultLayout();

        _undoManager.ActionExecuted += (s, e) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        };
    }

    private void InitDefaultLayout()
    {
        Controls.Clear();

        var btn1 = new HmiControlModel
        {
            Name = "Btn_Start",
            Type = HmiControlType.Button,
            X = 40, Y = 40, Width = 110, Height = 90,
            LabelText = "Nút Bắt Đầu",
            ButtonBehavior = HmiButtonBehavior.Toggle,
            Theme = HmiColorTheme.Green
        };

        var lamp1 = new HmiControlModel
        {
            Name = "Lamp_RunStatus",
            Type = HmiControlType.Lamp,
            X = 180, Y = 40, Width = 90, Height = 90,
            LabelText = "Trạng Thái Băng Tải",
            Theme = HmiColorTheme.Green
        };

        var conv1 = new HmiControlModel
        {
            Name = "Conv_Main",
            Type = HmiControlType.Conveyor,
            X = 310, Y = 50, Width = 220, Height = 80,
            LabelText = "Băng Tải Chính #1",
            Theme = HmiColorTheme.Green
        };

        var cyl1 = new HmiControlModel
        {
            Name = "Cyl_Reject",
            Type = HmiControlType.Cylinder,
            X = 560, Y = 50, Width = 200, Height = 80,
            LabelText = "Xilanh Đẩy Hàng Lỗi",
            Theme = HmiColorTheme.Amber
        };

        var num1 = new HmiControlModel
        {
            Name = "Num_Speed",
            Type = HmiControlType.NumericInput,
            X = 790, Y = 50, Width = 140, Height = 80,
            LabelText = "Tốc độ (m/s)",
            DefaultText = "25.5",
            MinValue = 0, MaxValue = 100
        };

        Controls.Add(new HmiControlViewModel(btn1, _plcService, ScreenConfig));
        Controls.Add(new HmiControlViewModel(lamp1, _plcService, ScreenConfig));
        Controls.Add(new HmiControlViewModel(conv1, _plcService, ScreenConfig));
        Controls.Add(new HmiControlViewModel(cyl1, _plcService, ScreenConfig));
        Controls.Add(new HmiControlViewModel(num1, _plcService, ScreenConfig));

        SelectControl(Controls.FirstOrDefault());
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(CurrentFileName));
    }

    public void RefreshGlobalLibraryImages()
    {
        GlobalLibraryImages.Clear();
        var imgs = HmiService.GetGlobalLibraryImages();
        foreach (var img in imgs)
        {
            GlobalLibraryImages.Add(img);
        }
    }

    partial void OnCurrentFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentFileName));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnIsRunModeChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnSelectedControlChanged(HmiControlViewModel? value)
    {
        HasSelectedControl = value != null;
        HasNoSelectedControl = value == null;
    }

    partial void OnSelectedGlobalLibraryImageChanged(string value)
    {
        if (SelectedControl != null && !string.IsNullOrWhiteSpace(value) && File.Exists(value))
        {
            SelectedControl.Model.UseCustomImage = true;
            SelectedControl.Model.CustomImagePathOn = value;
            SelectedControl.Model.CustomImagePathOff = value;
            SelectedControl.UpdateVisualState();
            IsDirty = true;
        }
    }

    public void SelectControl(HmiControlViewModel? vm, bool toggleSelection = false, bool multiSelect = false)
    {
        if (vm == null)
        {
            ClearSelection();
            return;
        }

        if (multiSelect || toggleSelection)
        {
            if (SelectedControls.Contains(vm))
            {
                if (toggleSelection)
                {
                    vm.IsSelected = false;
                    SelectedControls.Remove(vm);
                }
            }
            else
            {
                vm.IsSelected = true;
                SelectedControls.Add(vm);
            }
        }
        else
        {
            ClearSelection();
            vm.IsSelected = true;
            SelectedControls.Add(vm);
        }

        SelectedControl = SelectedControls.LastOrDefault();
        HasSelectedControl = SelectedControls.Count > 0;
        HasNoSelectedControl = SelectedControls.Count == 0;
    }

    public void ClearSelection()
    {
        foreach (var c in Controls)
        {
            c.IsSelected = false;
        }
        SelectedControls.Clear();
        SelectedControl = null;
        HasSelectedControl = false;
        HasNoSelectedControl = true;
    }

    [RelayCommand]
    private void ToggleRunMode()
    {
        IsRunMode = !IsRunMode;

        if (_plcService != null)
        {
            if (IsRunMode)
            {
                _plcService.AcquirePollingLock("HmiRunMode");

                // Ensure all HMI control addresses are registered in PLC polling list
                foreach (var ctrl in Controls)
                {
                    ctrl.IsRunMode = true;
                    if (!string.IsNullOrWhiteSpace(ctrl.EffectiveReadAddress))
                    {
                        ctrl.EnsureDirectAddressRegistered(ctrl.EffectiveReadAddress);
                    }
                    if (!string.IsNullOrWhiteSpace(ctrl.EffectiveWriteAddress))
                    {
                        ctrl.EnsureDirectAddressRegistered(ctrl.EffectiveWriteAddress);
                    }
                }
            }
            else
            {
                _plcService.ReleasePollingLock("HmiRunMode");
                foreach (var ctrl in Controls)
                {
                    ctrl.IsRunMode = false;
                }
            }
        }
        else
        {
            foreach (var ctrl in Controls)
            {
                ctrl.IsRunMode = IsRunMode;
            }
        }

        if (IsRunMode)
        {
            StatusMessage = "▶ ĐANG VẬN HÀNH (RUN MODE) - Màn hình HMI đang tương tác thời gian thực với PLC.";
            StatusBrush = Brushes.ForestGreen;
        }
        else
        {
            StatusMessage = "⏸ CHẾ ĐỘ THIẾT KẾ (PAUSE) - Tự do di chuyển, căn chỉnh & thay đổi thuộc tính phần tử.";
            StatusBrush = Brushes.DodgerBlue;
        }
    }

    public void StopRunMode()
    {
        if (IsRunMode)
        {
            IsRunMode = false;
        }
        _plcService?.ReleasePollingLock("HmiRunMode");
        foreach (var ctrl in Controls)
        {
            ctrl.IsRunMode = false;
        }
    }

    [RelayCommand]
    private void AddControl(HmiControlType type)
    {
        double defaultX = 100 + (Controls.Count * 20) % 400;
        double defaultY = 100 + (Controls.Count * 20) % 300;

        var model = new HmiControlModel
        {
            Name = $"{type}_{Controls.Count + 1}",
            Type = type,
            X = defaultX,
            Y = defaultY,
            Width = type == HmiControlType.Conveyor || type == HmiControlType.Cylinder ? 180 : 100,
            Height = type == HmiControlType.Conveyor || type == HmiControlType.Cylinder ? 80 : 80,
            LabelText = $"{type} {Controls.Count + 1}"
        };

        var vm = new HmiControlViewModel(model, _plcService, ScreenConfig)
        {
            IsRunMode = IsRunMode
        };

        void doAction()
        {
            Controls.Add(vm);
            SelectControl(vm);
            IsDirty = true;
        }

        void undoAction()
        {
            Controls.Remove(vm);
            ClearSelection();
            IsDirty = true;
        }

        _undoManager.Execute(new UndoRedoManager.DelegateAction(doAction, undoAction));
        StatusMessage = $"➕ Đã thêm phần tử mới '{vm.Model.Name}'.";
        StatusBrush = Brushes.DodgerBlue;
    }

    [RelayCommand]
    private void DeleteSelectedControls()
    {
        if (SelectedControls.Count == 0 && SelectedControl == null) return;

        var toDelete = SelectedControls.Count > 0 ? SelectedControls.ToList() : new List<HmiControlViewModel> { SelectedControl! };
        var originalIndices = toDelete.Select(vm => (vm, index: Controls.IndexOf(vm))).ToList();

        void doAction()
        {
            foreach (var vm in toDelete)
            {
                Controls.Remove(vm);
            }
            ClearSelection();
            IsDirty = true;
        }

        void undoAction()
        {
            foreach (var (vm, index) in originalIndices)
            {
                if (index >= 0 && index <= Controls.Count)
                {
                    Controls.Insert(index, vm);
                }
                else
                {
                    Controls.Add(vm);
                }
            }
            ClearSelection();
            foreach (var vm in toDelete)
            {
                SelectControl(vm, multiSelect: true);
            }
            IsDirty = true;
        }

        _undoManager.Execute(new UndoRedoManager.DelegateAction(doAction, undoAction));
        StatusMessage = $"🗑️ Đã xóa {toDelete.Count} phần tử.";
        StatusBrush = Brushes.DodgerBlue;
    }

    [RelayCommand]
    private void Copy()
    {
        var targets = SelectedControls.Count > 0 ? SelectedControls.ToList() : (SelectedControl != null ? new List<HmiControlViewModel> { SelectedControl } : new List<HmiControlViewModel>());
        if (targets.Count == 0) return;

        _clipboardModels.Clear();
        foreach (var vm in targets)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(vm.Model);
            var clone = System.Text.Json.JsonSerializer.Deserialize<HmiControlModel>(json);
            if (clone != null)
            {
                _clipboardModels.Add(clone);
            }
        }

        StatusMessage = $"📋 Đã sao chép {targets.Count} phần tử vào bộ nhớ tạm (Ctrl+V để dán).";
        StatusBrush = Brushes.DodgerBlue;
    }

    [RelayCommand]
    private void Paste()
    {
        if (_clipboardModels.Count == 0) return;

        var pastedVms = new List<HmiControlViewModel>();
        foreach (var origModel in _clipboardModels)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(origModel);
            var clone = System.Text.Json.JsonSerializer.Deserialize<HmiControlModel>(json);
            if (clone != null)
            {
                clone.Id = Guid.NewGuid().ToString("N");
                clone.Name = $"{clone.Name}_Copy";
                clone.X += 20;
                clone.Y += 20;

                var vm = new HmiControlViewModel(clone, _plcService, ScreenConfig) { IsRunMode = IsRunMode };
                pastedVms.Add(vm);
            }
        }

        void doAction()
        {
            foreach (var vm in pastedVms)
            {
                Controls.Add(vm);
            }
            ClearSelection();
            foreach (var vm in pastedVms)
            {
                SelectControl(vm, multiSelect: true);
            }
            IsDirty = true;
        }

        void undoAction()
        {
            foreach (var vm in pastedVms)
            {
                Controls.Remove(vm);
            }
            ClearSelection();
            IsDirty = true;
        }

        _undoManager.Execute(new UndoRedoManager.DelegateAction(doAction, undoAction));
        StatusMessage = $"📋 Đã dán {pastedVms.Count} phần tử mới.";
        StatusBrush = Brushes.DodgerBlue;
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoManager.CanUndo)
        {
            _undoManager.Undo();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            StatusMessage = "↩️ Đã Hoàn Tác (Undo).";
            StatusBrush = Brushes.DodgerBlue;
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (_undoManager.CanRedo)
        {
            _undoManager.Redo();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            StatusMessage = "↪️ Đã Làm Lại (Redo).";
            StatusBrush = Brushes.DodgerBlue;
        }
    }

    [RelayCommand]
    private async Task SaveHmiConfigAsync()
    {
        string path = CurrentFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "HMI Design Files (*.hmi)|*.hmi|All Files (*.*)|*.*",
                DefaultExt = ".hmi",
                Title = "Lưu tệp thiết kế HMI (.hmi)"
            };

            if (dlg.ShowDialog() == true)
            {
                path = dlg.FileName;
            }
            else
            {
                return;
            }
        }

        CurrentFilePath = path;
        ScreenConfig.Controls = Controls.Select(c => c.Model).ToList();
        await HmiService.SaveHmiConfigAsync(path, ScreenConfig);
        IsDirty = false;
        StatusMessage = $"💾 Đã lưu tệp HMI: '{CurrentFileName}'";
        StatusBrush = Brushes.DodgerBlue;
    }

    [RelayCommand]
    private void LoadHmiConfig()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "HMI Design Files (*.hmi)|*.hmi|All Files (*.*)|*.*",
            DefaultExt = ".hmi",
            Title = "Mở tệp thiết kế HMI (.hmi)"
        };

        if (dlg.ShowDialog() == true)
        {
            CurrentFilePath = dlg.FileName;
            ScreenConfig = HmiService.LoadHmiConfig(dlg.FileName);

            Controls.Clear();
            foreach (var m in ScreenConfig.Controls)
            {
                Controls.Add(new HmiControlViewModel(m, _plcService, ScreenConfig) { IsRunMode = IsRunMode });
            }

            SelectControl(Controls.FirstOrDefault());
            IsDirty = false;
            _undoManager.Clear();
            StatusMessage = $"📂 Đã nạp tệp HMI: '{CurrentFileName}'";
            StatusBrush = Brushes.DodgerBlue;
        }
    }

    [RelayCommand]
    private void BrowseCustomImageOff()
    {
        if (SelectedControl == null) return;

        var dlg = new OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.svg|All Files (*.*)|*.*",
            Title = "Chọn ảnh tùy chỉnh cho trạng thái OFF"
        };

        if (dlg.ShowDialog() == true)
        {
            string savedPath = HmiService.CopyImageToLibrary(dlg.FileName);
            SelectedControl.Model.UseCustomImage = true;
            SelectedControl.Model.CustomImagePathOff = savedPath;
            SelectedControl.UpdateVisualState();
            RefreshGlobalLibraryImages();
            IsDirty = true;
        }
    }

    [RelayCommand]
    private void BrowseCustomImageOn()
    {
        if (SelectedControl == null) return;

        var dlg = new OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.svg|All Files (*.*)|*.*",
            Title = "Chọn ảnh tùy chỉnh cho trạng thái ON"
        };

        if (dlg.ShowDialog() == true)
        {
            string savedPath = HmiService.CopyImageToLibrary(dlg.FileName);
            SelectedControl.Model.UseCustomImage = true;
            SelectedControl.Model.CustomImagePathOn = savedPath;
            SelectedControl.UpdateVisualState();
            RefreshGlobalLibraryImages();
            IsDirty = true;
        }
    }

    private void PlcService_OnTagChanged(object? sender, TagChangedEventArgs e)
    {
        if (!IsRunMode || e == null) return;

        foreach (var ctrl in Controls)
        {
            ctrl.OnPlcTagUpdated(e.PlcId, e.TagName, e.NewValue);
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class LightingControllerViewModel
{
    // =====================================================================
    // Blink Pattern Subsystem Properties
    // =====================================================================

    public ObservableCollection<LightingPatternModel> Patterns { get; } = new();

    [ObservableProperty]
    private LightingPatternModel? _selectedPattern;

    // ─── 3 Event Triggers ───
    [ObservableProperty]
    private bool _enableStartupPattern = true;

    [ObservableProperty]
    private LightingPatternModel? _selectedStartupPattern;

    [ObservableProperty]
    private bool _enableShutdownPattern = true;

    [ObservableProperty]
    private LightingPatternModel? _selectedShutdownPattern;

    [ObservableProperty]
    private bool _enableNgPattern = true;

    [ObservableProperty]
    private LightingPatternModel? _selectedNgPattern;

    // ─── Editor Properties ───
    [ObservableProperty]
    private string _editorPatternName = string.Empty;

    [ObservableProperty]
    private string _editorPatternDescription = string.Empty;

    [ObservableProperty]
    private int _editorRepeatCycles = 1;

    [ObservableProperty]
    private string _editorPatternScript = string.Empty;

    [ObservableProperty]
    private bool _isValidScript = true;

    [ObservableProperty]
    private string _validationStatusText = "✅ Cú pháp hợp lệ";

    [ObservableProperty]
    private string _estimatedDurationText = string.Empty;

    // ─── Runtime Status ───
    [ObservableProperty]
    private bool _isPatternRunning;

    [ObservableProperty]
    private string _runningStatusText = "Sẵn sàng";

    // =====================================================================
    // Commands
    // =====================================================================

    public IRelayCommand CreateNewPatternCommand { get; private set; } = null!;
    public IRelayCommand ClonePatternCommand { get; private set; } = null!;
    public IRelayCommand DeletePatternCommand { get; private set; } = null!;
    public IRelayCommand SavePatternsCommand { get; private set; } = null!;
    public IAsyncRelayCommand PlayTestPatternCommand { get; private set; } = null!;
    public IRelayCommand StopTestPatternCommand { get; private set; } = null!;
    public IRelayCommand<string> ApplyTemplateCommand { get; private set; } = null!;

    // =====================================================================
    // Initialization
    // =====================================================================

    private void InitBlinkPatternSubsystem()
    {
        CreateNewPatternCommand = new RelayCommand(ExecuteCreateNewPattern);
        ClonePatternCommand = new RelayCommand(ExecuteClonePattern, () => SelectedPattern != null);
        DeletePatternCommand = new RelayCommand(ExecuteDeletePattern, () => SelectedPattern != null && !SelectedPattern.IsBuiltIn);
        SavePatternsCommand = new RelayCommand(ExecuteSavePatterns);
        PlayTestPatternCommand = new AsyncRelayCommand(ExecutePlayTestPatternAsync, () => !IsPatternRunning);
        StopTestPatternCommand = new RelayCommand(ExecuteStopTestPattern, () => IsPatternRunning);
        ApplyTemplateCommand = new RelayCommand<string>(ExecuteApplyTemplate);

        // Nạp kịch bản từ GlobalAppSettings
        var settings = _settingsService.Settings.Lighting;
        EnableStartupPattern = settings.EnableStartupPattern;
        EnableShutdownPattern = settings.EnableShutdownPattern;
        EnableNgPattern = settings.EnableNgPattern;

        Patterns.Clear();
        var list = settings.Patterns != null && settings.Patterns.Count > 0
            ? settings.Patterns
            : LightingPatternModel.CreateDefaultPatterns();

        foreach (var p in list)
        {
            Patterns.Add(p);
        }

        // Đồng bộ chọn kịch bản cho 3 sự kiện
        SelectedStartupPattern = Patterns.FirstOrDefault(p => p.Id == settings.StartupPatternId) ?? Patterns.FirstOrDefault();
        SelectedShutdownPattern = Patterns.FirstOrDefault(p => p.Id == settings.ShutdownPatternId) ?? Patterns.FirstOrDefault(p => p.Id == "pattern_shutdown") ?? Patterns.FirstOrDefault();
        SelectedNgPattern = Patterns.FirstOrDefault(p => p.Id == settings.NgPatternId) ?? Patterns.FirstOrDefault(p => p.Id == "pattern_ng_alert") ?? Patterns.FirstOrDefault();

        // Chọn kịch bản đầu tiên để hiển thị trên trình soạn thảo
        SelectedPattern = Patterns.FirstOrDefault();

        // Đăng ký sự kiện từ PatternService
        _patternService.IsRunningChanged += (_, running) =>
        {
            RunOnUI(() =>
            {
                IsPatternRunning = running;
                PlayTestPatternCommand.NotifyCanExecuteChanged();
                StopTestPatternCommand.NotifyCanExecuteChanged();
                if (!running)
                {
                    RunningStatusText = "Đã dừng.";
                }
            });
        };

        _patternService.OnStepProgress += (_, e) =>
        {
            RunOnUI(() =>
            {
                RunningStatusText = $"▶ [Chu kỳ {e.cycle}/{e.total}] {e.stepText}";
            });
        };
    }

    // =====================================================================
    // Change Handlers
    // =====================================================================

    partial void OnSelectedPatternChanged(LightingPatternModel? value)
    {
        ClonePatternCommand.NotifyCanExecuteChanged();
        DeletePatternCommand.NotifyCanExecuteChanged();

        if (value != null)
        {
            _editorPatternName = value.Name;
            _editorPatternDescription = value.Description;
            _editorRepeatCycles = Math.Max(1, value.RepeatCycles);
            _editorPatternScript = value.Script;

            OnPropertyChanged(nameof(EditorPatternName));
            OnPropertyChanged(nameof(EditorPatternDescription));
            OnPropertyChanged(nameof(EditorRepeatCycles));
            OnPropertyChanged(nameof(EditorPatternScript));

            ValidateCurrentScript();
        }
    }

    partial void OnEditorPatternNameChanged(string value)
    {
        if (SelectedPattern != null && SelectedPattern.Name != value)
        {
            SelectedPattern.Name = value;
        }
    }

    partial void OnEditorPatternDescriptionChanged(string value)
    {
        if (SelectedPattern != null)
        {
            SelectedPattern.Description = value;
        }
    }

    partial void OnEditorRepeatCyclesChanged(int value)
    {
        if (SelectedPattern != null)
        {
            SelectedPattern.RepeatCycles = Math.Max(1, value);
        }
    }

    partial void OnEditorPatternScriptChanged(string value)
    {
        if (SelectedPattern != null)
        {
            SelectedPattern.Script = value;
        }
        ValidateCurrentScript();
    }

    partial void OnEnableStartupPatternChanged(bool value)
    {
        _settingsService.Settings.Lighting.EnableStartupPattern = value;
        _settingsService.Save();
    }

    partial void OnSelectedStartupPatternChanged(LightingPatternModel? value)
    {
        if (value != null)
        {
            _settingsService.Settings.Lighting.StartupPatternId = value.Id;
            _settingsService.Save();
        }
    }

    partial void OnEnableShutdownPatternChanged(bool value)
    {
        _settingsService.Settings.Lighting.EnableShutdownPattern = value;
        _settingsService.Save();
    }

    partial void OnSelectedShutdownPatternChanged(LightingPatternModel? value)
    {
        if (value != null)
        {
            _settingsService.Settings.Lighting.ShutdownPatternId = value.Id;
            _settingsService.Save();
        }
    }

    partial void OnEnableNgPatternChanged(bool value)
    {
        _settingsService.Settings.Lighting.EnableNgPattern = value;
        _settingsService.Save();
    }

    partial void OnSelectedNgPatternChanged(LightingPatternModel? value)
    {
        if (value != null)
        {
            _settingsService.Settings.Lighting.NgPatternId = value.Id;
            _settingsService.Save();
        }
    }

    // =====================================================================
    // Validation & Execution Logic
    // =====================================================================

    private void ValidateCurrentScript()
    {
        var val = LightingPatternParser.Validate(EditorPatternScript, SelectedChannelCount);
        IsValidScript = val.IsValid;

        if (val.IsValid)
        {
            ValidationStatusText = $"✅ Cú pháp hợp lệ ({val.StepCount} bước lệnh)";
            EstimatedDurationText = $"⏱ Thời lượng: ~{val.EstimatedDurationMsPerCycle} ms/chu kỳ (Tổng: ~{val.EstimatedDurationMsPerCycle * EditorRepeatCycles} ms)";
        }
        else
        {
            ValidationStatusText = $"❌ {val.ErrorMessage}";
            EstimatedDurationText = string.Empty;
        }
    }

    private void ExecuteCreateNewPattern()
    {
        var newPattern = new LightingPatternModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Kịch bản mới #{Patterns.Count + 1}",
            Description = "Mô tả kịch bản tự tạo...",
            RepeatCycles = 1,
            Script =
@"# Kịch bản nháy đèn tùy chỉnh
ALL OFF
DELAY 100
L1 ON 255 150
L1 OFF 100
L2 ON 255 150
L2 OFF 100",
            IsBuiltIn = false
        };

        Patterns.Add(newPattern);
        SelectedPattern = newPattern;
        ExecuteSavePatterns();
    }

    private void ExecuteClonePattern()
    {
        if (SelectedPattern == null) return;
        var clone = SelectedPattern.Clone();
        Patterns.Add(clone);
        SelectedPattern = clone;
        ExecuteSavePatterns();
    }

    private void ExecuteDeletePattern()
    {
        if (SelectedPattern == null || SelectedPattern.IsBuiltIn) return;

        var toRemove = SelectedPattern;
        int idx = Patterns.IndexOf(toRemove);
        Patterns.Remove(toRemove);

        if (Patterns.Count > 0)
        {
            SelectedPattern = Patterns[Math.Clamp(idx, 0, Patterns.Count - 1)];
        }
        else
        {
            SelectedPattern = null;
        }

        ExecuteSavePatterns();
    }

    private void ExecuteSavePatterns()
    {
        if (SelectedPattern != null)
        {
            SelectedPattern.Name = EditorPatternName;
            SelectedPattern.Description = EditorPatternDescription;
            SelectedPattern.RepeatCycles = EditorRepeatCycles;
            SelectedPattern.Script = EditorPatternScript;
        }

        var settings = _settingsService.Settings.Lighting;
        settings.Patterns = Patterns.ToList();
        settings.EnableStartupPattern = EnableStartupPattern;
        settings.StartupPatternId = SelectedStartupPattern?.Id ?? "pattern_welcome";
        settings.EnableShutdownPattern = EnableShutdownPattern;
        settings.ShutdownPatternId = SelectedShutdownPattern?.Id ?? "pattern_shutdown";
        settings.EnableNgPattern = EnableNgPattern;
        settings.NgPatternId = SelectedNgPattern?.Id ?? "pattern_ng_alert";

        _settingsService.Save();
        RunningStatusText = "💾 Đã lưu cấu hình kịch bản thành công.";
    }

    private async Task ExecutePlayTestPatternAsync()
    {
        if (!IsConnected)
        {
            RunningStatusText = "⚠️ Chưa kết nối bộ điều khiển đèn! Hãy kết nối cổng COM hoặc Ethernet trước.";
            return;
        }

        ValidateCurrentScript();
        if (!IsValidScript)
        {
            RunningStatusText = "⚠️ Cú pháp kịch bản có lỗi, không thể chạy thử.";
            return;
        }

        var tempPattern = new LightingPatternModel
        {
            Id = SelectedPattern?.Id ?? "test_pattern",
            Name = string.IsNullOrWhiteSpace(EditorPatternName) ? "Chạy thử" : EditorPatternName,
            Description = EditorPatternDescription,
            RepeatCycles = Math.Max(1, EditorRepeatCycles),
            Script = EditorPatternScript
        };

        RunningStatusText = $"▶ Đang chạy thử '{tempPattern.Name}' ({tempPattern.RepeatCycles} chu kỳ)...";
        await _patternService.PlayPatternAsync(tempPattern, SelectedChannelCount);
    }

    private void ExecuteStopTestPattern()
    {
        _patternService.StopCurrentPattern();
        RunningStatusText = "⏹ Đã dừng kịch bản.";
    }

    private void ExecuteApplyTemplate(string? templateKey)
    {
        switch (templateKey?.ToLowerInvariant())
        {
            case "welcome":
                EditorPatternScript =
@"# Kịch bản Chào Mừng Khi Bật App (Welcome Chase)
ALL OFF
DELAY 100
L1 ON 255 120; L1 OFF
L2 ON 255 120; L2 OFF
L3 ON 255 120; L3 OFF
L4 ON 255 120; L4 OFF
ALL ON 255 200; ALL OFF 100
ALL ON 255 200; ALL OFF 100";
                break;

            case "shutdown":
                EditorPatternScript =
@"# Kịch bản Tạm Biệt Khi Tắt App (Shutdown Wave)
ALL ON 200 150
ALL OFF 100
L4 ON 255 100; L4 OFF
L3 ON 255 100; L3 OFF
L2 ON 255 100; L2 OFF
L1 ON 255 100; L1 OFF
ALL OFF 100";
                break;

            case "ng":
                EditorPatternScript =
@"# Kịch bản Cảnh Báo Lỗi NG (Strobe Alert 3 Lần)
STROBE ALL 70 70 3 255
DELAY 100";
                break;

            case "strobe":
                EditorPatternScript =
@"# Chớp đồng loạt liên hoàn 4 lần
STROBE ALL 50 50 4 255
DELAY 150";
                break;

            case "chase":
                EditorPatternScript =
@"# Chạy đuổi lần lượt các kênh 80ms
CHASE 80 255
DELAY 100";
                break;

            case "pingpong":
                EditorPatternScript =
@"# Quét qua lại kiểu Knight Rider
ALL OFF
L1 ON 255 80; L1 OFF
L2 ON 255 80; L2 OFF
L3 ON 255 80; L3 OFF
L4 ON 255 80; L4 OFF
L3 ON 255 80; L3 OFF
L2 ON 255 80; L2 OFF
L1 ON 255 80; L1 OFF";
                break;

            case "comma":
                EditorPatternScript =
@"# Cú pháp phân tách bằng dấu phẩy truyền thống:
L1, ON, 200, L1, OFF, 50, L2, ON, 200, L2, OFF, 50, L3, ON, 200, L3, OFF, 50, L4, ON, 200, L4, OFF, 100";
                break;
        }

        if (SelectedPattern != null)
        {
            SelectedPattern.Script = EditorPatternScript;
        }
        ValidateCurrentScript();
    }
}

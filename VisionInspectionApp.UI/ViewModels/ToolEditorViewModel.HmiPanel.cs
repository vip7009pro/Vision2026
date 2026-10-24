using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Application.HMI;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels.HMI;

namespace VisionInspectionApp.UI.ViewModels;

public class HmiFileItem : IEquatable<HmiFileItem>
{
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName => Path.GetFileName(FilePath);
    public override string ToString() => DisplayName;

    public bool Equals(HmiFileItem? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as HmiFileItem);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath ?? string.Empty);
}

public sealed partial class ToolEditorViewModel
{
    private GlobalAppSettingsService? _globalSettingsService;
    private HmiManagerViewModel? _hmiMonitorViewModel;
    private bool _isSelectingHmiInternal = false;

    [ObservableProperty]
    private bool _isHmiPanelVisible = false;

    [ObservableProperty]
    private bool _isHmiRunning = true;

    [ObservableProperty]
    private GridLength _previewImageRowHeight = new(1, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _hmiPanelRowHeight = new(0);

    [ObservableProperty]
    private string _selectedHmiFilePath = string.Empty;

    [ObservableProperty]
    private HmiFileItem? _selectedHmiFileItem;

    public ObservableCollection<HmiFileItem> AvailableHmiFileItems { get; } = new();

    public HmiManagerViewModel? HmiMonitorViewModel => _hmiMonitorViewModel;

    public string SelectedHmiFileName => string.IsNullOrWhiteSpace(SelectedHmiFilePath)
        ? "Chưa chọn tệp HMI"
        : Path.GetFileName(SelectedHmiFilePath);

    public string SelectedHmiDisplayName => string.IsNullOrWhiteSpace(SelectedHmiFilePath)
        ? "(Chưa chọn màn hình HMI)"
        : $"{Path.GetFileName(SelectedHmiFilePath)} - {(_hmiMonitorViewModel?.ScreenConfig?.ScreenName ?? "HMI")}";

    public string HmiPanelToggleButtonText => IsHmiPanelVisible ? "Ẩn HMI" : "Hiện HMI";

    public Brush HmiPanelToggleButtonBackground => IsHmiPanelVisible
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0288D1"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#455A64"));

    public string HmiLiveBadgeText => IsHmiRunning ? "LIVE (RUN)" : "ĐÃ DỪNG (STOP)";
    public string HmiLiveBadgeBackground => IsHmiRunning ? "#1510B981" : "#15EF4444";
    public string HmiLiveBadgeBorder => IsHmiRunning ? "#10B981" : "#EF4444";
    public string HmiLiveBadgeForeground => IsHmiRunning ? "#10B981" : "#EF4444";
    public string HmiLiveBadgeDot => IsHmiRunning ? "🟢 " : "⏹️ ";

    public string HmiRunStopButtonText => IsHmiRunning ? "⏹️ Dừng HMI" : "▶️ Chạy HMI";
    public string HmiRunStopButtonBackground => IsHmiRunning ? "#C62828" : "#10B981";
    public string HmiRunStopButtonToolTip => IsHmiRunning
        ? "Dừng vận hành HMI giám sát (ngắt polling thời gian thực & khóa tương tác an toàn)"
        : "Kích hoạt HMI giám sát thời gian thực với PLC";

    public Brush HmiMonitorScreenBackgroundBrush
    {
        get
        {
            try
            {
                string hex = _hmiMonitorViewModel?.ScreenConfig?.BackgroundHex ?? "#1E1E1E";
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
            }
        }
    }

    /// <summary>
    /// Khởi tạo module HMI giám sát trên cột Preview của Tool Editor
    /// </summary>
    private void InitHmiPanelMonitoring()
    {
        _globalSettingsService = _serviceProvider?.GetService(typeof(GlobalAppSettingsService)) as GlobalAppSettingsService
                                 ?? new GlobalAppSettingsService();

        // Mặc định khởi tạo HMI ở trạng thái nghỉ (IsRunMode = false), không chiếm dụng hay đọc/ghi PLC
        _hmiMonitorViewModel = new HmiManagerViewModel(_plcManagerService)
        {
            IsRunMode = false
        };

        // Kết nối sự kiện Reset Queue từ Handshake State Machine
        if (_handshakeStateMachine != null)
        {
            _handshakeStateMachine.OnResetQueueRequested += HandshakeStateMachine_OnResetQueueRequested;
        }

        string savedHmiPath = _globalSettingsService.Settings.ToolEditorSelectedHmiPath;
        RefreshAvailableHmiFiles(savedHmiPath);

        // Khôi phục đường dẫn tệp HMI đã lưu lần trước hoặc file có sẵn trong danh mục
        if (!string.IsNullOrWhiteSpace(savedHmiPath) && File.Exists(savedHmiPath))
        {
            SelectedHmiFilePath = Path.GetFullPath(savedHmiPath);
            var existing = AvailableHmiFileItems.FirstOrDefault(x => string.Equals(x.FilePath, SelectedHmiFilePath, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new HmiFileItem { FilePath = SelectedHmiFilePath };
                AvailableHmiFileItems.Insert(0, existing);
            }
            _isSelectingHmiInternal = true;
            SelectedHmiFileItem = existing;
            _isSelectingHmiInternal = false;

            // Nạp ngay tệp của người dùng để triệt tiêu các control mẫu
            _hmiMonitorViewModel.LoadFromFile(SelectedHmiFilePath);
        }
        else if (AvailableHmiFileItems.Count > 0)
        {
            SelectedHmiFilePath = AvailableHmiFileItems[0].FilePath;
            _isSelectingHmiInternal = true;
            SelectedHmiFileItem = AvailableHmiFileItems[0];
            _isSelectingHmiInternal = false;

            _hmiMonitorViewModel.LoadFromFile(SelectedHmiFilePath);
        }

        // Khôi phục trạng thái Ẩn/Hiện đã lưu trong cài đặt
        _isHmiPanelVisible = _globalSettingsService.Settings.ToolEditorIsHmiVisible;
        _isHmiRunning = true;

        UpdateHmiLayoutVisuals();
    }

    private void HandshakeStateMachine_OnResetQueueRequested(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            ResetQueueAndCounters();
            StatusBarText = "🔄 [PLC Handshake] Đã nhận tín hiệu Reset Queue từ PLC: Hàng đợi đã về 0.";
        });
    }

    partial void OnIsHmiPanelVisibleChanged(bool value)
    {
        UpdateHmiLayoutVisuals();
        OnPropertyChanged(nameof(HmiPanelToggleButtonText));
        OnPropertyChanged(nameof(HmiPanelToggleButtonBackground));

        // Lưu trạng thái Ẩn/Hiện vào settings để nhớ cho phiên làm việc tiếp theo
        if (_globalSettingsService != null)
        {
            var settings = _globalSettingsService.Settings;
            settings.ToolEditorIsHmiVisible = value;
            _globalSettingsService.UpdateSettings(settings);
        }
    }

    partial void OnSelectedHmiFileItemChanged(HmiFileItem? value)
    {
        if (_isSelectingHmiInternal || value == null) return;
        SelectHmiFile(value.FilePath);
    }

    private void UpdateHmiLayoutVisuals()
    {
        if (IsHmiPanelVisible)
        {
            HmiPanelRowHeight = new GridLength(1, GridUnitType.Star);
            PreviewImageRowHeight = new GridLength(1, GridUnitType.Star);

            if (_hmiMonitorViewModel != null)
            {
                // Chỉ nạp và khởi động HMI tương tác khi người dùng thực sự mở HMI Panel
                if (!string.IsNullOrWhiteSpace(SelectedHmiFilePath) && File.Exists(SelectedHmiFilePath))
                {
                    if (_hmiMonitorViewModel.Controls.Count == 0 || !string.Equals(_hmiMonitorViewModel.CurrentFilePath, SelectedHmiFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _hmiMonitorViewModel.LoadFromFile(SelectedHmiFilePath);
                    }
                }
                else if (AvailableHmiFileItems.Count > 0)
                {
                    SelectHmiFile(AvailableHmiFileItems[0].FilePath);
                }

                if (IsHmiRunning)
                {
                    _hmiMonitorViewModel.IsRunMode = true;
                    _plcManagerService?.AcquirePollingLock("HmiToolEditor");
                    _hmiMonitorViewModel.RefreshAllControlsFromPlc();
                }
                else
                {
                    _hmiMonitorViewModel.StopRunMode();
                    _plcManagerService?.ReleasePollingLock("HmiToolEditor");
                }
            }
        }
        else
        {
            HmiPanelRowHeight = new GridLength(0);
            PreviewImageRowHeight = new GridLength(1, GridUnitType.Star);
            
            // Dừng hoàn toàn HMI khi ẩn, giải phóng Polling Lock
            if (_hmiMonitorViewModel != null)
            {
                _hmiMonitorViewModel.StopRunMode();
            }
            _plcManagerService?.ReleasePollingLock("HmiToolEditor");
        }

        NotifyHmiRunStateChanged();
    }

    public void RefreshAvailableHmiFiles(string? preselectFilePath = null)
    {
        var foundPaths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 0. Tệp được yêu cầu nạp trước hoặc đã lưu trong cấu hình
        if (!string.IsNullOrWhiteSpace(preselectFilePath) && File.Exists(preselectFilePath))
        {
            foundPaths.Add(Path.GetFullPath(preselectFilePath));
        }

        // 1. Thư mục Thư viện HMI Global
        try
        {
            string globalDir = HmiService.GlobalLibraryDirectory;
            if (Directory.Exists(globalDir))
            {
                foreach (var f in Directory.GetFiles(globalDir, "*.hmi"))
                {
                    foundPaths.Add(Path.GetFullPath(f));
                }
            }
        }
        catch { }

        // 2. Thư mục PLC Programs và Resources của project
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string searchDir = baseDir;
            for (int i = 0; i < 4; i++)
            {
                string candidatePlc = Path.Combine(searchDir, "PLC_Programs");
                if (Directory.Exists(candidatePlc))
                {
                    foreach (var f in Directory.GetFiles(candidatePlc, "*.hmi", SearchOption.AllDirectories))
                    {
                        foundPaths.Add(Path.GetFullPath(f));
                    }
                }

                string candidateRes = Path.Combine(searchDir, "Resources", "HMI");
                if (Directory.Exists(candidateRes))
                {
                    foreach (var f in Directory.GetFiles(candidateRes, "*.hmi", SearchOption.AllDirectories))
                    {
                        foundPaths.Add(Path.GetFullPath(f));
                    }
                }

                var parent = Directory.GetParent(searchDir);
                if (parent == null) break;
                searchDir = parent.FullName;
            }
        }
        catch { }

        // 3. Tệp hiện tại đang chọn
        if (!string.IsNullOrWhiteSpace(SelectedHmiFilePath) && File.Exists(SelectedHmiFilePath))
        {
            foundPaths.Add(Path.GetFullPath(SelectedHmiFilePath));
        }

        // Cập nhật AvailableHmiFileItems
        foreach (var p in foundPaths)
        {
            if (!AvailableHmiFileItems.Any(x => string.Equals(x.FilePath, p, StringComparison.OrdinalIgnoreCase)))
            {
                AvailableHmiFileItems.Add(new HmiFileItem { FilePath = p });
            }
        }
    }

    [RelayCommand]
    private void StopHmiMonitor()
    {
        IsHmiRunning = false;
        if (_hmiMonitorViewModel != null)
        {
            _hmiMonitorViewModel.StopRunMode();
        }
        _plcManagerService?.ReleasePollingLock("HmiToolEditor");
        NotifyHmiRunStateChanged();
        StatusBarText = "⏹️ [HMI Monitor] Đã dừng HMI giám sát (Khóa tương tác & ngắt polling).";
    }

    [RelayCommand]
    private void StartHmiMonitor()
    {
        IsHmiRunning = true;
        if (_hmiMonitorViewModel != null)
        {
            if (!string.IsNullOrWhiteSpace(SelectedHmiFilePath) && File.Exists(SelectedHmiFilePath))
            {
                if (_hmiMonitorViewModel.Controls.Count == 0 || !string.Equals(_hmiMonitorViewModel.CurrentFilePath, SelectedHmiFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _hmiMonitorViewModel.LoadFromFile(SelectedHmiFilePath);
                }
            }
            _hmiMonitorViewModel.IsRunMode = true;
            _plcManagerService?.AcquirePollingLock("HmiToolEditor");
            _hmiMonitorViewModel.RefreshAllControlsFromPlc();
        }
        NotifyHmiRunStateChanged();
        StatusBarText = $"▶️ [HMI Monitor] Đang chạy HMI giám sát: '{SelectedHmiFileName}'";
    }

    [RelayCommand]
    private void ToggleHmiRunMonitor()
    {
        if (IsHmiRunning)
        {
            StopHmiMonitor();
        }
        else
        {
            StartHmiMonitor();
        }
    }

    private void NotifyHmiRunStateChanged()
    {
        OnPropertyChanged(nameof(IsHmiRunning));
        OnPropertyChanged(nameof(HmiLiveBadgeText));
        OnPropertyChanged(nameof(HmiLiveBadgeBackground));
        OnPropertyChanged(nameof(HmiLiveBadgeBorder));
        OnPropertyChanged(nameof(HmiLiveBadgeForeground));
        OnPropertyChanged(nameof(HmiLiveBadgeDot));
        OnPropertyChanged(nameof(HmiRunStopButtonText));
        OnPropertyChanged(nameof(HmiRunStopButtonBackground));
        OnPropertyChanged(nameof(HmiRunStopButtonToolTip));
    }

    [RelayCommand]
    private void ToggleHmiPanelVisibility()
    {
        IsHmiPanelVisible = !IsHmiPanelVisible;
    }

    [RelayCommand]
    private void CloseHmiPanel()
    {
        IsHmiPanelVisible = false;
    }

    [RelayCommand]
    private void BrowseHmiFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "HMI Design Files (*.hmi)|*.hmi|All Files (*.*)|*.*",
            DefaultExt = ".hmi",
            Title = "Chọn tệp thiết kế HMI để giám sát (.hmi)"
        };

        if (dlg.ShowDialog() == true)
        {
            SelectHmiFile(dlg.FileName);
        }
    }

    [RelayCommand]
    private void SelectHmiFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        SelectedHmiFilePath = Path.GetFullPath(filePath);

        _isSelectingHmiInternal = true;
        try
        {
            var existing = AvailableHmiFileItems.FirstOrDefault(x => string.Equals(x.FilePath, SelectedHmiFilePath, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new HmiFileItem { FilePath = SelectedHmiFilePath };
                AvailableHmiFileItems.Insert(0, existing);
            }
            SelectedHmiFileItem = existing;
        }
        finally
        {
            _isSelectingHmiInternal = false;
        }

        if (IsHmiPanelVisible)
        {
            _hmiMonitorViewModel?.LoadFromFile(SelectedHmiFilePath);
            if (_hmiMonitorViewModel != null && IsHmiRunning)
            {
                _hmiMonitorViewModel.IsRunMode = true;
                _plcManagerService?.AcquirePollingLock("HmiToolEditor");
                _hmiMonitorViewModel.RefreshAllControlsFromPlc();
            }
        }

        // Lưu và nhớ khi tắt bật app trở lại
        if (_globalSettingsService != null)
        {
            var settings = _globalSettingsService.Settings;
            settings.ToolEditorSelectedHmiPath = SelectedHmiFilePath;
            _globalSettingsService.UpdateSettings(settings);
        }

        OnPropertyChanged(nameof(SelectedHmiFileName));
        OnPropertyChanged(nameof(SelectedHmiDisplayName));
        OnPropertyChanged(nameof(HmiMonitorScreenBackgroundBrush));

        StatusBarText = $"🖥️ [HMI Monitor] Đã nạp giao diện HMI: '{SelectedHmiFileName}'";
    }

    [RelayCommand]
    private void ReloadHmi()
    {
        if (string.IsNullOrWhiteSpace(SelectedHmiFilePath) || !File.Exists(SelectedHmiFilePath))
        {
            BrowseHmiFile();
            return;
        }

        _hmiMonitorViewModel?.LoadFromFile(SelectedHmiFilePath);
        if (_hmiMonitorViewModel != null && IsHmiPanelVisible && IsHmiRunning)
        {
            _hmiMonitorViewModel.IsRunMode = true;
            _plcManagerService?.AcquirePollingLock("HmiToolEditor");
            _hmiMonitorViewModel.RefreshAllControlsFromPlc();
        }
        OnPropertyChanged(nameof(SelectedHmiDisplayName));
        OnPropertyChanged(nameof(HmiMonitorScreenBackgroundBrush));
        StatusBarText = $"🔄 [HMI Monitor] Đã tải lại giao diện HMI: '{SelectedHmiFileName}'";
    }
}

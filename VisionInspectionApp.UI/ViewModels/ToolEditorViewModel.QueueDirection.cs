using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel
{
    private GlobalAppSettingsService? _queueDirectionSettingsService;
    private bool _isInitializingQueueDirection;

    /// <summary>
    /// Chiều hiển thị của cả 2 thanh queue (Queue 16 nấc & Recent 20 nấc):
    /// false = Trái ➔ Phải (mặc định), true = Phải ➔ Trái.
    /// </summary>
    [ObservableProperty]
    private bool _isQueueRightToLeft;

    public FlowDirection QueueFlowDirection =>
        IsQueueRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string QueueDirectionArrowText =>
        IsQueueRightToLeft ? "←" : "→";

    public string QueueDirectionDisplayText =>
        IsQueueRightToLeft ? "Phải ➔ Trái" : "Trái ➔ Phải";

    partial void OnIsQueueRightToLeftChanged(bool value)
    {
        OnPropertyChanged(nameof(QueueFlowDirection));
        OnPropertyChanged(nameof(QueueDirectionArrowText));
        OnPropertyChanged(nameof(QueueDirectionDisplayText));

        if (!_isInitializingQueueDirection && _queueDirectionSettingsService != null)
        {
            var settings = _queueDirectionSettingsService.Settings;
            if (settings.ToolEditorQueueRightToLeft != value)
            {
                settings.ToolEditorQueueRightToLeft = value;
                _queueDirectionSettingsService.UpdateSettings(settings);
            }
        }

        UpdateQueueVisuals();
        UpdateRecentPartsVisuals();
    }

    [RelayCommand]
    public void ToggleQueueDirection()
    {
        IsQueueRightToLeft = !IsQueueRightToLeft;
        StatusBarText = IsQueueRightToLeft
            ? "⬅ Đã đảo chiều hiển thị 2 thanh Queue (16 nấc & 20 nấc): Phải ➔ Trái."
            : "➔ Đã đặt chiều hiển thị 2 thanh Queue (16 nấc & 20 nấc): Trái ➔ Phải.";
    }

    private void InitQueueDirectionSetting(GlobalAppSettingsService? settingsService)
    {
        _queueDirectionSettingsService = settingsService;
        if (_queueDirectionSettingsService == null)
        {
            return;
        }

        _isInitializingQueueDirection = true;
        try
        {
            IsQueueRightToLeft = _queueDirectionSettingsService.Settings.ToolEditorQueueRightToLeft;
        }
        finally
        {
            _isInitializingQueueDirection = false;
        }

        UpdateQueueVisuals();
        UpdateRecentPartsVisuals();
    }

    internal void ConfigureQueueDirectionSettingsForTesting(GlobalAppSettingsService settingsService)
    {
        InitQueueDirectionSetting(settingsService);
    }
}

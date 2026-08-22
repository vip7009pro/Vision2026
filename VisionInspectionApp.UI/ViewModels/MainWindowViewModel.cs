using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex = 1; // Default to OQC Scanner tab (Index 1)

    private readonly IRecentJobsService? _recentJobsService;

    public ObservableCollection<string> RecentJobs { get; } = new();

    public MainWindowViewModel(
        ToolEditorViewModel toolEditor,
        CalibrationViewModel calibration,
        ManualInspectionViewModel manualInspection,
        InspectionViewModel inspection,
        OqcScannerViewModel oqcScanner,
        CameraSettingsViewModel cameraSettings,
        IRecentJobsService? recentJobsService = null)
    {
        ToolEditor = toolEditor;
        Calibration = calibration;
        ManualInspection = manualInspection;
        Inspection = inspection;
        OqcScanner = oqcScanner;
        OqcScanner.RequestSwitchTab = idx => SelectedTabIndex = idx;
        CameraSettings = cameraSettings;
        _recentJobsService = recentJobsService;

        if (_recentJobsService != null)
        {
            _recentJobsService.RecentJobsChanged += ReloadRecentJobs;
            ReloadRecentJobs();
        }

        ToolEditor.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(ToolEditor.CurrentJobFilePath) or nameof(ToolEditor.ProductCode) or nameof(ToolEditor.IsDirty))
            {
                OnPropertyChanged(nameof(HeaderJobTitle));
                OnPropertyChanged(nameof(HeaderProductCodeTitle));
            }
        };

        CloseJobCommand = new RelayCommand(CloseJob);
        SwitchTabCommand = new RelayCommand<object>(ExecuteSwitchTab);
        ExitCommand = new RelayCommand(ExecuteExit);
        AboutCommand = new RelayCommand(ExecuteAbout);
        OpenRecentJobCommand = new RelayCommand<string>(ExecuteOpenRecentJob);
        ClearRecentJobsCommand = new RelayCommand(ExecuteClearRecentJobs);
    }

    public string HeaderJobTitle
    {
        get
        {
            var jobName = string.IsNullOrWhiteSpace(ToolEditor.CurrentJobFilePath)
                ? "[Chưa lưu]"
                : Path.GetFileName(ToolEditor.CurrentJobFilePath);
            var dirtyMark = ToolEditor.IsDirty ? " *" : "";
            return $"📁 Job: {jobName}{dirtyMark}";
        }
    }

    public string HeaderProductCodeTitle
    {
        get
        {
            var prodCode = string.IsNullOrWhiteSpace(ToolEditor.ProductCode)
                ? "--"
                : ToolEditor.ProductCode;
            var dirtyMark = ToolEditor.IsDirty ? " *" : "";
            return $"🏷️ SP: {prodCode}{dirtyMark}";
        }
    }

    public ICommand CloseJobCommand { get; }
    public ICommand SwitchTabCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand OpenRecentJobCommand { get; }
    public ICommand ClearRecentJobsCommand { get; }

    private void ReloadRecentJobs()
    {
        RecentJobs.Clear();
        if (_recentJobsService != null)
        {
            foreach (var j in _recentJobsService.GetRecentJobs())
            {
                RecentJobs.Add(j);
            }
        }
    }

    private void ExecuteOpenRecentJob(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                _recentJobsService?.RemoveRecentJob(filePath);
                MessageBox.Show($"Tệp Job không tồn tại hoặc đã bị di chuyển:\n{filePath}", "Không tìm thấy tệp", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        ToolEditor.LoadJobFromFile(filePath, autoRun: true);
        SelectedTabIndex = 0; // Chuyển sang màn hình Tool Editor
    }

    private void ExecuteClearRecentJobs()
    {
        _recentJobsService?.ClearRecentJobs();
    }

    private void ExecuteSwitchTab(object? parameter)
    {
        if (parameter is null) return;
        if (int.TryParse(parameter.ToString(), out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    private void ExecuteExit()
    {
        System.Windows.Application.Current?.MainWindow?.Close();
    }

    private void ExecuteAbout()
    {
        MessageBox.Show(
            "CMS VINA VISION SYSTEM — Enterprise Industrial Vision Platform\n" +
            "Version 2.6.0 (64-bit Edition)\n\n" +
            "© 2026 CMS VINA Co., Ltd. All rights reserved.\n" +
            "Industrial Machine Vision, Multi-camera Inspection, OQC & Automation Integration.\n\n" +
            "────────────────────────────────────────\n" +
            "Tác giả: Nguyễn Văn Hùng\n" +
            "Phone: +84971092454\n" +
            "Email: pagehungnguyen.com\n" +
            "Web: hungnguyenpage.com",
            "About CMS VINA Vision System",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CloseJob()
    {
        ToolEditor.CloseJob();
        Calibration.CloseJob();
        Inspection.CloseJob();
        
        if (System.Windows.Application.Current?.MainWindow != null)
        {
            System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM";
        }
    }

    public ToolEditorViewModel ToolEditor { get; }

    public CalibrationViewModel Calibration { get; }

    public ManualInspectionViewModel ManualInspection { get; }

    public InspectionViewModel Inspection { get; }

    public OqcScannerViewModel OqcScanner { get; }

    public CameraSettingsViewModel CameraSettings { get; }
}

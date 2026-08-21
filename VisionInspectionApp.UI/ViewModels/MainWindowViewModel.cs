using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex = 1; // Default to OQC Scanner tab (Index 1)

    public MainWindowViewModel(
        ToolEditorViewModel toolEditor,
        CalibrationViewModel calibration,
        ManualInspectionViewModel manualInspection,
        InspectionViewModel inspection,
        OqcScannerViewModel oqcScanner,
        CameraSettingsViewModel cameraSettings)
    {
        ToolEditor = toolEditor;
        Calibration = calibration;
        ManualInspection = manualInspection;
        Inspection = inspection;
        OqcScanner = oqcScanner;
        OqcScanner.RequestSwitchTab = idx => SelectedTabIndex = idx;
        CameraSettings = cameraSettings;

        CloseJobCommand = new RelayCommand(CloseJob);
        SwitchTabCommand = new RelayCommand<object>(ExecuteSwitchTab);
        ExitCommand = new RelayCommand(ExecuteExit);
        AboutCommand = new RelayCommand(ExecuteAbout);
    }

    public ICommand CloseJobCommand { get; }
    public ICommand SwitchTabCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand AboutCommand { get; }

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

using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex = 3; // Default to OQC Scanner tab

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
    }

    public ICommand CloseJobCommand { get; }

    private void CloseJob()
    {
        ToolEditor.CloseJob();
        Calibration.CloseJob();
        Inspection.CloseJob();
        
        System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM";
    }

    public ToolEditorViewModel ToolEditor { get; }

    public CalibrationViewModel Calibration { get; }

    public ManualInspectionViewModel ManualInspection { get; }

    public InspectionViewModel Inspection { get; }

    public OqcScannerViewModel OqcScanner { get; }

    public CameraSettingsViewModel CameraSettings { get; }
}

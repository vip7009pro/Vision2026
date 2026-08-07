using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VisionInspectionApp.UI.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        ToolEditorViewModel toolEditor,
        CalibrationViewModel calibration,
        ManualInspectionViewModel manualInspection,
        InspectionViewModel inspection,
        LiveCameraViewModel liveCamera,
        CameraSettingsViewModel cameraSettings)
    {
        ToolEditor = toolEditor;
        Calibration = calibration;
        ManualInspection = manualInspection;
        Inspection = inspection;
        LiveCamera = liveCamera;
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

    public LiveCameraViewModel LiveCamera { get; }

    public CameraSettingsViewModel CameraSettings { get; }
}

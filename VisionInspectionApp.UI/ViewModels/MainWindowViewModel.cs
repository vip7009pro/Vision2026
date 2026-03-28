using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspectionApp.UI.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        ToolEditorViewModel toolEditor,
        CalibrationViewModel calibration,
        ManualInspectionViewModel manualInspection,
        InspectionViewModel inspection,
        LiveCameraViewModel liveCamera,
        BatchProcessingViewModel batchProcessing,
        PlcViewModel plc)
    {
        ToolEditor = toolEditor;
        Calibration = calibration;
        ManualInspection = manualInspection;
        Inspection = inspection;
        LiveCamera = liveCamera;
        BatchProcessing = batchProcessing;
        Plc = plc;
    }


    public ToolEditorViewModel ToolEditor { get; }

    public CalibrationViewModel Calibration { get; }

    public ManualInspectionViewModel ManualInspection { get; }

    public InspectionViewModel Inspection { get; }

    public LiveCameraViewModel LiveCamera { get; }

    public BatchProcessingViewModel BatchProcessing { get; }

    public PlcViewModel Plc { get; }
}

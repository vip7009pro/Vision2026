using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspectionApp.UI.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(TeachViewModel teach, ToolEditorViewModel toolEditor, CalibrationViewModel calibration, ManualInspectionViewModel manualInspection, InspectionViewModel inspection)
    {
        Teach = teach;
        ToolEditor = toolEditor;
        Calibration = calibration;
        ManualInspection = manualInspection;
        Inspection = inspection;
    }

    public TeachViewModel Teach { get; }

    public ToolEditorViewModel ToolEditor { get; }

    public CalibrationViewModel Calibration { get; }

    public ManualInspectionViewModel ManualInspection { get; }

    public InspectionViewModel Inspection { get; }
}

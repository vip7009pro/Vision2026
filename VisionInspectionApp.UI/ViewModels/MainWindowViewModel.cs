using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspectionApp.UI.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(TeachViewModel teach, CalibrationViewModel calibration, InspectionViewModel inspection)
    {
        Teach = teach;
        Calibration = calibration;
        Inspection = inspection;
    }

    public TeachViewModel Teach { get; }

    public CalibrationViewModel Calibration { get; }

    public InspectionViewModel Inspection { get; }
}

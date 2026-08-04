using System.Windows;
using VisionInspectionApp.UI.ViewModels.PLC;

namespace VisionInspectionApp.UI.Views.PLC;

public partial class PlcMonitorWindow : Window
{
    public PlcMonitorWindow(PlcMonitorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

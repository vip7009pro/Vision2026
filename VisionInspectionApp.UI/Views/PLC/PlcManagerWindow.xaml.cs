using System.Windows;
using VisionInspectionApp.UI.ViewModels.PLC;

namespace VisionInspectionApp.UI.Views.PLC;

public partial class PlcManagerWindow : Window
{
    public PlcManagerWindow(PlcManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class RollDefectMapWindow : Window
{
    public RollDefectMapWindow(RollDefectMapViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }
}

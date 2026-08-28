using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class LightingControllerWindow : Window
{
    public LightingControllerWindow(LightingControllerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Closed += (_, _) =>
        {
            viewModel.StopDebounceTimer();
        };
    }
}

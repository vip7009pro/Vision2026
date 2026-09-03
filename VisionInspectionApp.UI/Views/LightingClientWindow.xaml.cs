using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class LightingClientWindow : Window
{
    private readonly LightingClientViewModel _viewModel;

    public LightingClientWindow(LightingClientViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Closed += (_, _) =>
        {
            _viewModel.Cleanup();
        };
    }
}

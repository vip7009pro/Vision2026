using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class LightingServerWindow : Window
{
    private readonly LightingServerViewModel _viewModel;

    public LightingServerWindow(LightingServerViewModel viewModel)
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

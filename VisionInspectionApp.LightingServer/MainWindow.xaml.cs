using System.Windows;

namespace VisionInspectionApp.LightingServer;

public partial class MainWindow : Window
{
    private readonly LightingServerStandaloneViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new LightingServerStandaloneViewModel();
        DataContext = _viewModel;

        Closed += (_, _) =>
        {
            _viewModel.Cleanup();
        };
    }
}

using System.Windows;
using System.Windows.Controls;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

/// <summary>
/// Interaction logic for CameraSettingsView.xaml
/// </summary>
public partial class CameraSettingsView : UserControl
{
    private CameraSettingsViewModel? _vm;

    public CameraSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestFitView -= OnFitViewRequested;
        }

        _vm = e.NewValue as CameraSettingsViewModel;

        if (_vm != null)
        {
            _vm.RequestFitView += OnFitViewRequested;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CameraSettingsViewModel vm && _vm != vm)
        {
            if (_vm != null) _vm.RequestFitView -= OnFitViewRequested;
            _vm = vm;
            _vm.RequestFitView += OnFitViewRequested;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestFitView -= OnFitViewRequested;
            _vm = null;
        }
    }

    private void OnFitViewRequested()
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            CameraImageViewer?.ResetView();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnFitViewClicked(object sender, RoutedEventArgs e)
    {
        CameraImageViewer?.ResetView();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcScannerView : UserControl
{
    private bool _hasInitialAutoFit = false;

    public OqcScannerView()
    {
        InitializeComponent();
        Loaded += OqcScannerView_Loaded;
        PreviewKeyDown += OqcScannerView_PreviewKeyDown;
    }

    private void OqcScannerView_Loaded(object sender, RoutedEventArgs e)
    {
        ScanInputTextBox.Focus();
        if (!_hasInitialAutoFit)
        {
            _hasInitialAutoFit = true;
            ScheduleAutoFit();
        }
    }

    private void ScheduleAutoFit()
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            OqcImageViewer?.ResetView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BtnFitImagePreview_Click(object sender, RoutedEventArgs e)
    {
        OqcImageViewer?.ResetView();
    }

    private void OqcScannerView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.OqcScannerViewModel vm) return;

        if (e.Key == Key.Space)
        {
            if (vm.ScanFromCameraCommand.CanExecute(null))
            {
                vm.ScanFromCameraCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F5)
        {
            if (vm.EnableLiveCameraCommand.CanExecute(null))
            {
                vm.EnableLiveCameraCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}

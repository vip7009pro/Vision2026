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
            if (vm.UseExternalScanner)
            {
                // Khi dùng đầu scan ngoài: phím Space dùng để RUN JOB
                if (vm.RunJobCommand.CanExecute(null))
                {
                    vm.RunJobCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else
            {
                // Khi không dùng đầu scan ngoài: phím Space dùng để quét mã từ Camera
                if (vm.ScanFromCameraCommand.CanExecute(null))
                {
                    vm.ScanFromCameraCommand.Execute(null);
                    e.Handled = true;
                }
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

    private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.OqcScannerViewModel vm && HistoryDataGrid.SelectedItem is Models.OqcScanHistoryEntry selected)
        {
            vm.ExecuteOpenScanDetail(selected);
        }
    }

    private void ViewDetailBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.OqcScannerViewModel vm)
        {
            var selected = HistoryDataGrid.SelectedItem as Models.OqcScanHistoryEntry;
            vm.ExecuteOpenScanDetail(selected);
        }
    }

    private void ViewOutputImageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Models.OqcScanHistoryEntry entry && DataContext is ViewModels.OqcScannerViewModel vm)
        {
            vm.ExecuteOpenScanDetail(entry);
        }
    }
}

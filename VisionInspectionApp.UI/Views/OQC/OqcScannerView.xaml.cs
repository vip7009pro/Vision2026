using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcScannerView : UserControl
{
    public OqcScannerView()
    {
        InitializeComponent();
        Loaded += OqcScannerView_Loaded;
        PreviewKeyDown += OqcScannerView_PreviewKeyDown;
    }

    private void OqcScannerView_Loaded(object sender, RoutedEventArgs e)
    {
        ScanInputTextBox.Focus();
    }

    private void OqcScannerView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (DataContext is ViewModels.OqcScannerViewModel vm)
            {
                if (vm.ScanFromCameraCommand.CanExecute(null))
                {
                    vm.ScanFromCameraCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}

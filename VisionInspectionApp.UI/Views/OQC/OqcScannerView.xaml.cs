using System.Windows;
using System.Windows.Controls;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcScannerView : UserControl
{
    public OqcScannerView()
    {
        InitializeComponent();
        Loaded += OqcScannerView_Loaded;
    }

    private void OqcScannerView_Loaded(object sender, RoutedEventArgs e)
    {
        ScanInputTextBox.Focus();
    }
}

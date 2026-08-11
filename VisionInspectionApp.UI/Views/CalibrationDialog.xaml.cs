using System.Windows;

namespace VisionInspectionApp.UI.Views;

public partial class CalibrationDialog : Window
{
    public CalibrationDialog()
    {
        InitializeComponent();
    }

    private void OnApplyAndCloseClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

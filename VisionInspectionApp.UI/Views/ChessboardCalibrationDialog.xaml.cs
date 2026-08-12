using System.Windows;

namespace VisionInspectionApp.UI.Views;

public partial class ChessboardCalibrationDialog : Window
{
    public ChessboardCalibrationDialog()
    {
        InitializeComponent();
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e)
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

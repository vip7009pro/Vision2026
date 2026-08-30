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
        try { DialogResult = true; } catch { }
        Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}

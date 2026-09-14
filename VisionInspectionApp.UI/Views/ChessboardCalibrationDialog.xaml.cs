using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class ChessboardCalibrationDialog : Window
{
    public ChessboardCalibrationDialog()
    {
        InitializeComponent();
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChessboardCalibrationViewModel vm)
        {
            await vm.StartLiveStreamAsync();
        }
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is ChessboardCalibrationViewModel vm)
        {
            await vm.StopLiveStreamAsync();
        }
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

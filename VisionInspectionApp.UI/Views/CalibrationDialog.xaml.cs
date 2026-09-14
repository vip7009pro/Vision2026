using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class CalibrationDialog : Window
{
    public CalibrationDialog()
    {
        InitializeComponent();
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CalibrationViewModel vm)
        {
            await vm.StartLiveStreamAsync();
        }
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is CalibrationViewModel vm)
        {
            await vm.StopLiveStreamAsync();
        }
    }

    private void OnApplyAndCloseClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is CalibrationViewModel vm)
        {
            vm.SavePixelsPerMm();
        }
        try { DialogResult = true; } catch { }
        Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}

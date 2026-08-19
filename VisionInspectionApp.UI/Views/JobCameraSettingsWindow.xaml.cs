using System;
using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class JobCameraSettingsWindow : Window
{
    public JobCameraSettingsWindow(JobCameraSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose = () =>
        {
            Dispatcher.Invoke(() =>
            {
                DialogResult = true;
                Close();
            });
        };

        Closed += (s, e) =>
        {
            viewModel.Dispose();
        };
    }
}

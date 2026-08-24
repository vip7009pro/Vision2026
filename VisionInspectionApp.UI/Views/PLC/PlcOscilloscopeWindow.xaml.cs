using System;
using System.Windows;
using VisionInspectionApp.UI.ViewModels.PLC;

namespace VisionInspectionApp.UI.Views.PLC;

public partial class PlcOscilloscopeWindow : Window
{
    public PlcOscilloscopeViewModel? ViewModel => DataContext as PlcOscilloscopeViewModel;

    public PlcOscilloscopeWindow(PlcOscilloscopeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Closed += (s, e) =>
        {
            viewModel.Dispose();
        };
    }
}

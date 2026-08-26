using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class InspectionLogWindow : Window
{
    public InspectionLogWindow(InspectionLogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

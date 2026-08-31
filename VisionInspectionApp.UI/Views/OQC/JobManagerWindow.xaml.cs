using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views.OQC;

/// <summary>
/// Interaction logic for JobManagerWindow.xaml
/// </summary>
public partial class JobManagerWindow : Window
{
    public JobManagerWindow()
    {
        InitializeComponent();
    }

    public JobManagerWindow(JobManagerViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

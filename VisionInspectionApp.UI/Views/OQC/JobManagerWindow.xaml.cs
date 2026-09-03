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
        viewModel.RequestClose += () =>
        {
            Dispatcher.Invoke(Close);
        };
    }

    private void OnDataGridRowMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is JobManagerViewModel vm && vm.OpenJobFromListCommand.CanExecute(null))
        {
            vm.OpenJobFromListCommand.Execute(null);
        }
    }
}

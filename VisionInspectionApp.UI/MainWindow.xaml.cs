using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            bool isDirty = vm.ToolEditor.IsDirty || vm.Calibration.IsDirty;
            if (isDirty)
            {
                var result = MessageBox.Show("There are unsaved changes. Do you want to save them before closing?", 
                                             "Unsaved Changes", 
                                             MessageBoxButton.YesNoCancel, 
                                             MessageBoxImage.Warning);
                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
                else if (result == MessageBoxResult.Yes)
                {
                    if (vm.ToolEditor.IsDirty && vm.ToolEditor.SaveJobCommand.CanExecute(null))
                        vm.ToolEditor.SaveJobCommand.Execute(null);


                    if (vm.Calibration.IsDirty && vm.Calibration.SaveJobCommand.CanExecute(null))
                        vm.Calibration.SaveJobCommand.Execute(null);
                }
            }
        }
    }
}
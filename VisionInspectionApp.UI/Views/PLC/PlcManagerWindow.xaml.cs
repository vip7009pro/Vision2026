using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionInspectionApp.UI.ViewModels.PLC;

namespace VisionInspectionApp.UI.Views.PLC;

public partial class PlcManagerWindow : Window
{
    public PlcManagerWindow(PlcManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ComboBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox cb && cb.IsEditable && !cb.IsDropDownOpen)
        {
            var source = e.OriginalSource as DependencyObject;
            if (source is TextBox || source is TextBlock || source?.GetType().Name.Contains("Text") == true)
            {
                cb.IsDropDownOpen = true;
            }
        }
    }
}

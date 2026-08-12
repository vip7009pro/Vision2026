using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VisionInspectionApp.UI.Views.HMI;

public partial class HmiPropertyInspectorView : UserControl
{
    public HmiPropertyInspectorView()
    {
        InitializeComponent();
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

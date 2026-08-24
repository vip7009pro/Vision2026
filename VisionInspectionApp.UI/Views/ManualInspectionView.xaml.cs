using System.Windows.Controls;

namespace VisionInspectionApp.UI.Views;

public partial class ManualInspectionView : UserControl
{
    public ManualInspectionView()
    {
        InitializeComponent();
    }

    private void OnFitViewClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        PART_Viewer?.ResetView();
    }
}

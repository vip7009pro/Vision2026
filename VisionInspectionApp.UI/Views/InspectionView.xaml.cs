using System.Windows.Controls;

namespace VisionInspectionApp.UI.Views;

public partial class InspectionView : UserControl
{
    public InspectionView()
    {
        InitializeComponent();
    }

    private void BtnFitImagePreview_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        InspectionImageViewer?.ResetView();
    }
}

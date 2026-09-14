using System.Windows;
using VisionInspectionApp.UI.ViewModels.Licensing;

namespace VisionInspectionApp.UI.Views.Licensing;

public partial class LicenseDialog : Window
{
    public LicenseDialog() : this(new LicenseViewModel(new VisionInspectionApp.Application.Licensing.LicenseService()))
    {
    }

    public LicenseDialog(LicenseViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

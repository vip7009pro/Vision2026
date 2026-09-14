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
        Closed += (s, e) => (DataContext as LicenseViewModel)?.Cleanup();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

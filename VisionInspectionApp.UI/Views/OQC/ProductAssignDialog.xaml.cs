using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class ProductAssignDialog : Window
{
    public ProductAssignDialog(OqcScannerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (s, e) =>
        {
            if (viewModel.ProductListTable == null)
            {
                await viewModel.SearchProductsCommand.ExecuteAsync(null);
            }
        };
    }

    private void BrowseJobFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Job Files (*.job)|*.job|All Files (*.*)|*.*",
            Title = "Chọn tệp Job cần liên kết"
        };

        if (dialog.ShowDialog() == true)
        {
            if (DataContext is OqcScannerViewModel vm)
            {
                vm.AssignJobFilePath = dialog.FileName;
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

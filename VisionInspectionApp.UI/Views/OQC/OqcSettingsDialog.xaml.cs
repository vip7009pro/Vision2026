using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcSettingsDialog : Window
{
    public OqcSettingsDialog(OqcScannerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void BrowseJobFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục gốc lưu các tệp Job (.job)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            if (DataContext is OqcScannerViewModel vm)
            {
                vm.JobRootDirectory = dialog.SelectedPath;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OqcScannerViewModel vm)
        {
            vm.SaveConfigCommand.Execute(null);
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

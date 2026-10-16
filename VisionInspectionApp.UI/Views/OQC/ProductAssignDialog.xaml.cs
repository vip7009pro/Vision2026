using System.Windows;
using System.Windows.Controls;
using System.Data;
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

    private void ProductsDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.IsReadOnly = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ProductsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is OqcScannerViewModel vm && sender is DataGrid grid)
        {
            vm.SetSelectedProductRows(grid.SelectedItems);
        }
    }

    private void ProductsDataGrid_CheckBoxPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not DataRowView row || e.ChangedButton != System.Windows.Input.MouseButton.Left)
        {
            return;
        }

        if (ProductsDataGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow dataGridRow)
        {
            e.Handled = true;
            dataGridRow.IsSelected = !dataGridRow.IsSelected;
        }
    }
}

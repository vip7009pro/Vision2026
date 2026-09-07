using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcScanHistoryWindow : Window
{
    private readonly OqcScannerViewModel _viewModel;
    private ICollectionView? _historyView;

    public OqcScanHistoryWindow(OqcScannerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        Loaded += OqcScanHistoryWindow_Loaded;
    }

    private void OqcScanHistoryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _historyView = CollectionViewSource.GetDefaultView(_viewModel.ScanHistory);
        if (_historyView != null)
        {
            _historyView.Filter = FilterHistory;
        }
        UpdateCount();
    }

    private bool FilterHistory(object obj)
    {
        if (obj is not OqcScanHistoryEntry entry) return false;
        var filter = TxtSearchFilter.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filter)) return true;

        return (entry.ScannedCode?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.ProductName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.InspectResult?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        _historyView?.Refresh();
        UpdateCount();
    }

    private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
    {
        TxtSearchFilter.Text = string.Empty;
        _historyView?.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (_historyView == null) return;
        int count = 0;
        foreach (var _ in _historyView) count++;
        TxtRecordCount.Text = count.ToString();
    }

    private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is OqcScanHistoryEntry selected)
        {
            _viewModel.ExecuteOpenScanDetail(selected);
        }
    }

    private void BtnViewDetail_Click(object sender, RoutedEventArgs e)
    {
        var selected = HistoryDataGrid.SelectedItem as OqcScanHistoryEntry;
        _viewModel.ExecuteOpenScanDetail(selected);
    }

    private void ViewOutputImageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OqcScanHistoryEntry entry)
        {
            _viewModel.ExecuteOpenScanDetail(entry);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

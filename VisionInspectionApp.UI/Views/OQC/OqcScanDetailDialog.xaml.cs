using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcScanDetailDialog : Window
{
    private readonly OqcScanHistoryEntry _entry;

    public OqcScanDetailDialog(OqcScanHistoryEntry entry)
    {
        InitializeComponent();
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        DataContext = _entry;

        Loaded += (_, _) =>
        {
            LoadOutputImage();
            ImageViewer?.ResetView();
        };
    }

    private void LoadOutputImage()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_entry.OutputImagePath) && File.Exists(_entry.OutputImagePath))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(_entry.OutputImagePath, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();

                ImageViewer.ImageSource = bi;
                ImageViewer.Visibility = Visibility.Visible;
                TxtNoImageMessage.Visibility = Visibility.Collapsed;
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load output image: {ex.Message}");
        }

        ImageViewer.ImageSource = null;
        ImageViewer.Visibility = Visibility.Collapsed;
        TxtNoImageMessage.Visibility = Visibility.Visible;
    }

    private void FitView_Click(object sender, RoutedEventArgs e)
    {
        ImageViewer?.ResetView();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ImageViewer?.ZoomIn();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ImageViewer?.ZoomOut();
    }

    private void Reset100_Click(object sender, RoutedEventArgs e)
    {
        ImageViewer?.ResetView(1.0);
    }

    private void OpenImageExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_entry.OutputImagePath) && File.Exists(_entry.OutputImagePath))
            {
                Process.Start(new ProcessStartInfo(_entry.OutputImagePath) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("Tệp ảnh không tồn tại trên ổ đĩa.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở tệp ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = JsonSerializer.Serialize(_entry, new JsonSerializerOptions { WriteIndented = true });
            Clipboard.SetText(json);
            MessageBox.Show("✅ Đã sao chép toàn bộ dữ liệu chi tiết vào Clipboard!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi sao chép dữ liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

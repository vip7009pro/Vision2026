using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.Views.PLC;

public partial class DotnetRuntimePromptDialog : Window
{
    private readonly IDotnetRuntimeService _dotnetRuntimeService;
    private readonly GlobalAppSettingsService? _settingsService;
    private bool _isCompleted = false;

    public DotnetRuntimePromptDialog(IDotnetRuntimeService dotnetRuntimeService, GlobalAppSettingsService? settingsService = null)
    {
        InitializeComponent();
        _dotnetRuntimeService = dotnetRuntimeService ?? DotnetRuntimeService.Instance;
        _settingsService = settingsService;

        if (_settingsService != null)
        {
            ChkSuppressPrompt.IsChecked = _settingsService.Settings.Plc.SuppressDotnetX86Prompt;
        }

        ChkSuppressPrompt.Checked += OnSuppressCheckChanged;
        ChkSuppressPrompt.Unchecked += OnSuppressCheckChanged;
    }

    private void OnSuppressCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsService != null)
        {
            _settingsService.Settings.Plc.SuppressDotnetX86Prompt = ChkSuppressPrompt.IsChecked == true;
            _settingsService.Save();
        }
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnBrowser_Click(object sender, RoutedEventArgs e)
    {
        _dotnetRuntimeService.OpenDownloadPageInBrowser();
        Close();
    }

    private async void BtnAutoInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            Close();
            return;
        }

        BtnAutoInstall.IsEnabled = false;
        BtnBrowser.IsEnabled = false;
        BtnSkip.IsEnabled = false;
        AlertContainer.Visibility = Visibility.Collapsed;
        ProgressContainer.Visibility = Visibility.Visible;

        TxtProgressTitle.Text = "📥 Đang tải bộ cài đặt .NET 8.0 x86 từ Microsoft CDN...";
        TxtProgressPercent.Text = "0%";
        PbDownloadProgress.Value = 0;
        TxtProgressDetail.Text = "Đang kết nối tới máy chủ Microsoft...";

        var progress = new Progress<FileDownloadProgressInfo>(info =>
        {
            PbDownloadProgress.Value = info.Percentage;
            TxtProgressPercent.Text = $"{info.Percentage:F0}%";
            TxtProgressDetail.Text = info.TotalBytes.HasValue && info.TotalBytes.Value > 0
                ? $"{info.ProgressFormatted} • {info.SpeedFormatted}"
                : $"Đã tải: {(info.BytesDownloaded / (1024.0 * 1024.0)):F1} MB • {info.SpeedFormatted}";
        });

        using var cts = new CancellationTokenSource();
        var (downloadOk, installerPath, downloadErr) = await _dotnetRuntimeService.DownloadX86InstallerAsync(progress, cts.Token);

        if (!downloadOk)
        {
            ProgressContainer.Visibility = Visibility.Collapsed;
            ShowAlert(false, $"❌ Lỗi khi tải bộ cài đặt: {downloadErr}\nBạn có thể nhấn 'Tải Bằng Trình Duyệt' để tải thủ công.");
            BtnAutoInstall.IsEnabled = true;
            BtnBrowser.IsEnabled = true;
            BtnSkip.IsEnabled = true;
            return;
        }

        TxtProgressTitle.Text = "⚙️ Đang thực thi bộ cài đặt .NET Desktop Runtime (x86)...";
        TxtProgressPercent.Text = "100%";
        TxtProgressDetail.Text = "Vui lòng xác nhận quyền Quản trị viên (UAC) nếu hộp thoại Windows xuất hiện...";

        var (installOk, installErr) = await _dotnetRuntimeService.RunInstallerAsync(installerPath, passive: true);

        // Kiểm tra lại tình trạng runtime
        bool isInstalled = _dotnetRuntimeService.IsX86RuntimeInstalled();

        if (isInstalled)
        {
            _isCompleted = true;
            ProgressContainer.Visibility = Visibility.Collapsed;
            ShowAlert(true, "✅ Đã cài đặt thành công .NET Desktop Runtime 8.0 (x86)!\nModule PLC Bridge đã sẵn sàng hoạt động cùng PLC Mitsubishi.");

            BtnAutoInstall.Content = "Đóng & Tiếp Tục";
            BtnAutoInstall.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
            BtnAutoInstall.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            BtnAutoInstall.IsEnabled = true;
            BtnBrowser.Visibility = Visibility.Collapsed;
            BtnSkip.Visibility = Visibility.Collapsed;
        }
        else
        {
            ProgressContainer.Visibility = Visibility.Collapsed;
            string msg = !string.IsNullOrWhiteSpace(installErr)
                ? $"⚠️ Quá trình cài đặt chưa thành công ({installErr}). Vui lòng nhấn 'Tải Bằng Trình Duyệt' để cài đặt thủ công."
                : "⚠️ Chưa phát hiện .NET Desktop Runtime 8.0 (x86) sau khi cài đặt. Vui lòng cài đặt thủ công từ trang Microsoft.";
            ShowAlert(false, msg);

            BtnAutoInstall.IsEnabled = true;
            BtnBrowser.IsEnabled = true;
            BtnSkip.IsEnabled = true;
        }
    }

    private void ShowAlert(bool isSuccess, string message)
    {
        AlertContainer.Visibility = Visibility.Visible;
        if (isSuccess)
        {
            AlertContainer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#052E16"));
            AlertContainer.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            AlertContainer.BorderThickness = new Thickness(1);
            TxtAlertMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6EE7B7"));
        }
        else
        {
            AlertContainer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A0E0E"));
            AlertContainer.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            AlertContainer.BorderThickness = new Thickness(1);
            TxtAlertMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
        }
        TxtAlertMessage.Text = message;
    }
}

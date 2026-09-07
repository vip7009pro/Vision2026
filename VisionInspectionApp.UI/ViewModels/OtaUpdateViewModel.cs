using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models.Ota;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public partial class OtaUpdateViewModel : ObservableObject
{
    private readonly IOtaUpdateService _otaService;
    private readonly GlobalAppSettingsService _settingsService;
    private CancellationTokenSource? _downloadCts;
    private UpdateManifest? _currentManifest;
    private string _downloadedZipPath = "";

    [ObservableProperty]
    private string _currentVersionText = "1.0.0.0";

    [ObservableProperty]
    private string _latestVersionText = "---";

    [ObservableProperty]
    private string _releaseNotes = "Chưa có thông tin ghi chú phát hành.";

    [ObservableProperty]
    private string _releaseDateText = "---";

    [ObservableProperty]
    private string _packageSizeText = "---";

    [ObservableProperty]
    private bool _hasUpdate = false;

    [ObservableProperty]
    private bool _isChecking = false;

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private bool _isDownloaded = false;

    [ObservableProperty]
    private bool _isMandatory = false;

    [ObservableProperty]
    private double _downloadProgress = 0.0;

    [ObservableProperty]
    private string _downloadProgressText = "";

    [ObservableProperty]
    private string _downloadSpeedText = "";

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng kiểm tra phiên bản mới.";

    [ObservableProperty]
    private string _statusColorHex = "#94A3B8";

    [ObservableProperty]
    private string _serverUrl = "";

    [ObservableProperty]
    private string _selectedSourceType = "Auto";

    [ObservableProperty]
    private bool _autoCheckOnStartup = true;

    public event Action? RequestClose;

    public OtaUpdateViewModel(IOtaUpdateService otaService, GlobalAppSettingsService settingsService)
    {
        _otaService = otaService;
        _settingsService = settingsService;

        CurrentVersionText = $"v{_otaService.CurrentVersion}";
        var otaCfg = _settingsService.Settings.Ota;
        ServerUrl = otaCfg.UpdateServerUrl;
        SelectedSourceType = otaCfg.UpdateSourceType;
        AutoCheckOnStartup = otaCfg.AutoCheckOnStartup;
    }

    [RelayCommand]
    public async Task CheckForUpdateAsync()
    {
        if (IsChecking || IsDownloading) return;

        IsChecking = true;
        StatusMessage = "Đang kết nối máy chủ kiểm tra phiên bản...";
        StatusColorHex = "#38BDF8";

        try
        {
            // Lưu lại URL và loại nguồn nếu người dùng có sửa đổi
            _settingsService.Settings.Ota.UpdateServerUrl = ServerUrl;
            _settingsService.Settings.Ota.UpdateSourceType = SelectedSourceType;
            _settingsService.Settings.Ota.AutoCheckOnStartup = AutoCheckOnStartup;
            _settingsService.Settings.Ota.LastCheckedTime = DateTime.UtcNow;
            _settingsService.Save();

            var result = await _otaService.CheckForUpdateAsync(ServerUrl, SelectedSourceType).ConfigureAwait(true);

            if (result.Status == UpdateCheckStatus.Error)
            {
                StatusMessage = $"⚠️ {result.ErrorMessage}";
                StatusColorHex = "#F87171";
                HasUpdate = false;
                return;
            }

            if (result.HasUpdate && result.Manifest != null)
            {
                _currentManifest = result.Manifest;
                LatestVersionText = $"v{result.Manifest.Version}";
                ReleaseNotes = !string.IsNullOrWhiteSpace(result.Manifest.ReleaseNotes) 
                    ? result.Manifest.ReleaseNotes 
                    : "Bản cập nhật tính năng mới và tối ưu hiệu suất.";
                ReleaseDateText = result.Manifest.ReleaseDate?.ToString("yyyy-MM-dd HH:mm") ?? "Mới nhất";
                PackageSizeText = result.Manifest.FileSize > 0 
                    ? $"{(result.Manifest.FileSize / (1024.0 * 1024.0)):F2} MB" 
                    : "Không xác định";
                IsMandatory = result.Manifest.IsMandatory;
                HasUpdate = true;
                IsDownloaded = false;

                StatusMessage = result.Status == UpdateCheckStatus.MandatoryUpdate
                    ? "🚨 CÓ BẢN CẬP NHẬT BẮT BUỘC! Vui lòng cập nhật ngay."
                    : "🎉 Đã phát hiện phiên bản mới! Bạn có thể tải về ngay.";
                StatusColorHex = result.Status == UpdateCheckStatus.MandatoryUpdate ? "#EF4444" : "#4ADE80";
            }
            else
            {
                HasUpdate = false;
                LatestVersionText = $"v{_otaService.CurrentVersion}";
                StatusMessage = $"✅ Bạn đang sử dụng phiên bản mới nhất ({CurrentVersionText}).";
                StatusColorHex = "#4ADE80";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi kiểm tra cập nhật: {ex.Message}";
            StatusColorHex = "#F87171";
            HasUpdate = false;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    public async Task DownloadAndInstallAsync()
    {
        if (_currentManifest == null || IsDownloading) return;

        // Nếu đã tải xong trước đó -> Chỉ việc khởi chạy updater và tắt app
        if (IsDownloaded && !string.IsNullOrWhiteSpace(_downloadedZipPath) && File.Exists(_downloadedZipPath))
        {
            LaunchInstaller();
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadProgressText = "Đang khởi tạo tiến trình tải...";
        DownloadSpeedText = "";
        StatusMessage = "Đang tải gói cập nhật về máy...";
        StatusColorHex = "#38BDF8";

        _downloadCts = new CancellationTokenSource();

        try
        {
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updates");
            Directory.CreateDirectory(tempDir);

            var progress = new Progress<UpdateProgressInfo>(info =>
            {
                DownloadProgress = info.Percentage;
                DownloadProgressText = info.ProgressFormatted;
                DownloadSpeedText = info.SpeedFormatted;
            });

            _downloadedZipPath = await _otaService.DownloadUpdatePackageAsync(_currentManifest, tempDir, progress, _downloadCts.Token).ConfigureAwait(true);

            // Kiểm tra mã băm SHA-256
            StatusMessage = "Đang xác thực tính toàn vẹn (SHA-256 Checksum)...";
            bool isHashValid = _otaService.VerifyPackageChecksum(_downloadedZipPath, _currentManifest.Sha256);

            if (!isHashValid)
            {
                try { File.Delete(_downloadedZipPath); } catch { }
                _downloadedZipPath = "";
                StatusMessage = "❌ Lỗi xác thực SHA-256: Tệp cập nhật bị hỏng hoặc tải thiếu. Đã hủy bỏ.";
                StatusColorHex = "#EF4444";
                IsDownloaded = false;
                return;
            }

            IsDownloaded = true;
            StatusMessage = "✅ Tải & xác thực thành công! Bấm 'Cài Đặt & Khởi Động Lại' để hoàn tất.";
            StatusColorHex = "#4ADE80";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã hủy tải bản cập nhật.";
            StatusColorHex = "#94A3B8";
            IsDownloaded = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi tải bản cập nhật: {ex.Message}";
            StatusColorHex = "#EF4444";
            IsDownloaded = false;
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    public void LaunchInstaller()
    {
        if (string.IsNullOrWhiteSpace(_downloadedZipPath) || !File.Exists(_downloadedZipPath))
        {
            StatusMessage = "Không tìm thấy gói cập nhật để cài đặt.";
            StatusColorHex = "#EF4444";
            return;
        }

        StatusMessage = "Đang khởi chạy VisionUpdater và thoát ứng dụng...";
        bool ok = _otaService.LaunchUpdaterAndShutdown(_downloadedZipPath, restartAfterUpdate: true);

        if (ok)
        {
            RequestClose?.Invoke();
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            StatusMessage = "Không thể khởi động trình cập nhật VisionUpdater.exe. Vui lòng kiểm tra quyền Administrator hoặc tệp tin.";
            StatusColorHex = "#EF4444";
        }
    }

    [RelayCommand]
    public void IgnoreThisVersion()
    {
        if (_currentManifest != null)
        {
            _settingsService.Settings.Ota.IgnoredVersion = _currentManifest.Version;
            _settingsService.Save();
        }
        RequestClose?.Invoke();
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _settingsService.Settings.Ota.UpdateServerUrl = ServerUrl;
        _settingsService.Settings.Ota.UpdateSourceType = SelectedSourceType;
        _settingsService.Settings.Ota.AutoCheckOnStartup = AutoCheckOnStartup;
        _settingsService.Save();
        StatusMessage = "Đã lưu cài đặt OTA Update thành công.";
        StatusColorHex = "#4ADE80";
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }
}

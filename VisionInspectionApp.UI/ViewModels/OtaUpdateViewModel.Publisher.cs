using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models.Ota;

namespace VisionInspectionApp.UI.ViewModels;

public partial class OtaUpdateViewModel
{
    private readonly IOtaPublisherService _publisherService;
    private CancellationTokenSource? _publishCts;
    private string? _detectedCsprojPath;

    [ObservableProperty]
    private string _publishSourceDirectory = "";

    [ObservableProperty]
    private string _publishNewVersion = "1.0.0.1";

    [ObservableProperty]
    private string _publishReleaseChannel = "Stable";

    [ObservableProperty]
    private bool _publishIsMandatory = false;

    [ObservableProperty]
    private string _publishReleaseNotes = "Bản cập nhật tính năng mới và tối ưu hóa hệ thống.";

    [ObservableProperty]
    private string _publishServerUrl = "http://192.168.1.100/ota_server.php";

    [ObservableProperty]
    private string _publishServerStorageFolder = "uploads/ota_packages";

    [ObservableProperty]
    private string _publishApiToken = "";

    [ObservableProperty]
    private bool _publishAutoUpdateCsproj = true;

    [ObservableProperty]
    private bool _isPublishing = false;

    [ObservableProperty]
    private double _publishProgress = 0.0;

    [ObservableProperty]
    private string _publishProgressText = "";

    [ObservableProperty]
    private string _publishSpeedText = "";

    [ObservableProperty]
    private string _publishStatusMessage = "Sẵn sàng đóng gói và tải lên server.";

    [ObservableProperty]
    private string _publishStatusColorHex = "#94A3B8";

    [ObservableProperty]
    private string _publishLogs = "";

    private void InitializePublisher()
    {
        var otaCfg = _settingsService.Settings.Ota;

        if (!string.IsNullOrWhiteSpace(otaCfg.PublishServerUploadUrl))
            PublishServerUrl = otaCfg.PublishServerUploadUrl;
        if (!string.IsNullOrWhiteSpace(otaCfg.PublishServerStorageFolder))
            PublishServerStorageFolder = otaCfg.PublishServerStorageFolder;
        PublishApiToken = otaCfg.PublishApiToken;
        PublishAutoUpdateCsproj = otaCfg.PublishAutoUpdateCsproj;
        if (!string.IsNullOrWhiteSpace(otaCfg.PublishReleaseChannel))
            PublishReleaseChannel = otaCfg.PublishReleaseChannel;

        // Tự động tìm đường dẫn file .csproj
        DetectCsprojPath();

        // Tự động xác định thư mục nguồn build
        DetectSourceDirectory(otaCfg.PublishSourceDirectory);

        // Tự động đề xuất phiên bản mới kế tiếp (tăng patch)
        SuggestNextVersion();
    }

    private void DetectCsprojPath()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Thử tìm theo cấu trúc thư mục phát triển
            var candidatePaths = new[]
            {
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VisionInspectionApp.UI", "VisionInspectionApp.UI.csproj")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "VisionInspectionApp.UI.csproj")),
                Path.GetFullPath(Path.Combine(baseDir, "VisionInspectionApp.UI.csproj")),
                @"G:\NODEJS\Vision2026\VisionInspectionApp.UI\VisionInspectionApp.UI.csproj"
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    _detectedCsprojPath = path;
                    break;
                }
            }
        }
        catch { }
    }

    private void DetectSourceDirectory(string? savedDirectory)
    {
        if (!string.IsNullOrWhiteSpace(savedDirectory) && Directory.Exists(savedDirectory))
        {
            PublishSourceDirectory = savedDirectory;
            return;
        }

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Ưu tiên tìm thư mục Release nếu đang mở từ project
            var candidateRelease = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VisionInspectionApp.UI", "bin", "Release", "net8.0-windows"));
            if (Directory.Exists(candidateRelease))
            {
                PublishSourceDirectory = candidateRelease;
                return;
            }

            var candidateDebug = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VisionInspectionApp.UI", "bin", "Debug", "net8.0-windows"));
            if (Directory.Exists(candidateDebug))
            {
                PublishSourceDirectory = candidateDebug;
                return;
            }

            // Mặc định là thư mục hiện tại của ứng dụng
            PublishSourceDirectory = baseDir;
        }
        catch
        {
            PublishSourceDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }
    }

    private void SuggestNextVersion()
    {
        var cur = _otaService.CurrentVersion;
        int major = Math.Max(1, cur.Major);
        int minor = Math.Max(0, cur.Minor);
        int build = Math.Max(0, cur.Build);
        int revision = Math.Max(0, cur.Revision);

        // Mặc định tăng revision (patch) lên 1
        PublishNewVersion = $"{major}.{minor}.{build}.{revision + 1}";
    }

    [RelayCommand]
    public void BrowseSourceDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục chứa mã chạy ứng dụng để đóng gói (.zip)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(PublishSourceDirectory) && Directory.Exists(PublishSourceDirectory))
        {
            dialog.SelectedPath = PublishSourceDirectory;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            PublishSourceDirectory = dialog.SelectedPath;
            AppendPublishLog($"📁 Đã chọn thư mục nguồn: {PublishSourceDirectory}");
        }
    }

    [RelayCommand]
    public void IncrementPatchVersion()
    {
        if (Version.TryParse(PublishNewVersion, out var v))
        {
            int rev = Math.Max(0, v.Revision) + 1;
            PublishNewVersion = $"{Math.Max(1, v.Major)}.{Math.Max(0, v.Minor)}.{Math.Max(0, v.Build)}.{rev}";
        }
    }

    [RelayCommand]
    public void IncrementMinorVersion()
    {
        if (Version.TryParse(PublishNewVersion, out var v))
        {
            PublishNewVersion = $"{Math.Max(1, v.Major)}.{Math.Max(0, v.Minor) + 1}.0.0";
        }
    }

    [RelayCommand]
    public void IncrementMajorVersion()
    {
        if (Version.TryParse(PublishNewVersion, out var v))
        {
            PublishNewVersion = $"{Math.Max(1, v.Major) + 1}.0.0.0";
        }
    }

    [RelayCommand]
    public async Task PackageAndPublishAsync()
    {
        if (IsPublishing) return;

        // 1. Kiểm tra tính hợp lệ
        if (string.IsNullOrWhiteSpace(PublishSourceDirectory) || !Directory.Exists(PublishSourceDirectory))
        {
            PublishStatusMessage = "❌ Thư mục nguồn không tồn tại. Vui lòng kiểm tra lại.";
            PublishStatusColorHex = "#EF4444";
            AppendPublishLog($"❌ Lỗi: Thư mục nguồn không tồn tại '{PublishSourceDirectory}'.");
            return;
        }

        if (!Version.TryParse(PublishNewVersion, out var targetVer))
        {
            PublishStatusMessage = "❌ Định dạng phiên bản không hợp lệ (Ví dụ hợp lệ: 1.0.0.1).";
            PublishStatusColorHex = "#EF4444";
            AppendPublishLog($"❌ Lỗi: Phiên bản '{PublishNewVersion}' không đúng định dạng.");
            return;
        }

        if (string.IsNullOrWhiteSpace(PublishServerUrl))
        {
            PublishStatusMessage = "❌ Chưa nhập URL máy chủ upload (ota_server.php).";
            PublishStatusColorHex = "#EF4444";
            AppendPublishLog("❌ Lỗi: URL máy chủ upload trống.");
            return;
        }

        IsPublishing = true;
        PublishProgress = 0;
        PublishProgressText = "Đang khởi tạo tiến trình đóng gói...";
        PublishSpeedText = "";
        PublishStatusMessage = "Đang xử lý đóng gói bản cập nhật...";
        PublishStatusColorHex = "#38BDF8";
        PublishLogs = "";

        _publishCts = new CancellationTokenSource();
        string tempZipDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_publish");
        string zipFileName = $"VisionUpdate_v{PublishNewVersion}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
        string zipFilePath = Path.Combine(tempZipDir, zipFileName);

        try
        {
            // Lưu lại cấu hình xuất bản
            _settingsService.Settings.Ota.PublishServerUploadUrl = PublishServerUrl;
            _settingsService.Settings.Ota.PublishServerStorageFolder = PublishServerStorageFolder;
            _settingsService.Settings.Ota.PublishApiToken = PublishApiToken;
            _settingsService.Settings.Ota.PublishSourceDirectory = PublishSourceDirectory;
            _settingsService.Settings.Ota.PublishAutoUpdateCsproj = PublishAutoUpdateCsproj;
            _settingsService.Settings.Ota.PublishReleaseChannel = PublishReleaseChannel;
            _settingsService.Save();

            AppendPublishLog($"=======================================================");
            AppendPublishLog($"🚀 BẮT ĐẦU ĐÓNG GÓI & PHÁT HÀNH BẢN CẬP NHẬT v{PublishNewVersion}");
            AppendPublishLog($"=======================================================");
            AppendPublishLog($"📁 Thư mục nguồn: {PublishSourceDirectory}");
            AppendPublishLog($"🏢 Máy chủ upload: {PublishServerUrl}");
            AppendPublishLog($"📂 Thư mục trên server: {PublishServerStorageFolder}");
            AppendPublishLog($"🏷️ Kênh phát hành: {PublishReleaseChannel} | Bắt buộc: {PublishIsMandatory}");

            // BƯỚC 1: Tự động cập nhật phiên bản vào file .csproj (nếu được bật)
            if (PublishAutoUpdateCsproj && !string.IsNullOrWhiteSpace(_detectedCsprojPath) && File.Exists(_detectedCsprojPath))
            {
                AppendPublishLog($"📝 Đang cập nhật số phiên bản mới vào: {Path.GetFileName(_detectedCsprojPath)}...");
                bool csprojUpdated = await _publisherService.UpdateCsprojVersionAsync(_detectedCsprojPath, PublishNewVersion).ConfigureAwait(true);
                if (csprojUpdated)
                {
                    AppendPublishLog($"  ✓ Đã cập nhật Version = {PublishNewVersion} vào file .csproj thành công.");
                }
            }

            // BƯỚC 2: Nén thư mục nguồn thành tệp .zip
            AppendPublishLog($"📦 Đang nén các tệp ứng dụng thành gói .zip...");
            PublishStatusMessage = "Đang nén thư mục ứng dụng thành tệp .zip...";
            var zipProgress = new Progress<double>(pct =>
            {
                PublishProgress = pct * 0.4; // 0 - 40% cho bước nén
                PublishProgressText = $"Đang nén tệp: {pct:F0}%";
            });

            await _publisherService.BuildZipPackageAsync(PublishSourceDirectory, zipFilePath, zipProgress, _publishCts.Token).ConfigureAwait(true);

            long fileSizeBytes = new FileInfo(zipFilePath).Length;
            double sizeMb = fileSizeBytes / (1024.0 * 1024.0);
            AppendPublishLog($"  ✓ Nén hoàn tất: {sizeMb:F2} MB ({fileSizeBytes:N0} bytes)");

            // BƯỚC 3: Tính toán mã băm SHA-256
            AppendPublishLog($"🔒 Đang tính toán mã băm SHA-256 xác thực tính toàn vẹn...");
            PublishProgress = 45;
            PublishProgressText = "Đang tính toán SHA-256...";
            string sha256 = _publisherService.ComputeSha256(zipFilePath);
            AppendPublishLog($"  ✓ SHA-256: {sha256}");

            // BƯỚC 4: Tạo cấu hình Manifest
            var manifest = new UpdateManifest
            {
                AppName = "CMS VINA Vision System",
                Version = PublishNewVersion,
                ReleaseDate = DateTime.UtcNow,
                Channel = PublishReleaseChannel,
                IsMandatory = PublishIsMandatory,
                FileSize = fileSizeBytes,
                Sha256 = sha256,
                ReleaseNotes = PublishReleaseNotes
            };

            var publishConfig = new OtaPublishConfig
            {
                ServerUploadUrl = PublishServerUrl,
                ServerStorageFolder = PublishServerStorageFolder,
                ApiToken = PublishApiToken,
                SourceDirectory = PublishSourceDirectory,
                TargetVersion = PublishNewVersion,
                ReleaseChannel = PublishReleaseChannel,
                ReleaseNotes = PublishReleaseNotes,
                IsMandatory = PublishIsMandatory,
                AutoUpdateCsproj = PublishAutoUpdateCsproj
            };

            // BƯỚC 5: Tải gói zip và dữ liệu manifest lên máy chủ qua HTTP POST multipart
            AppendPublishLog($"📤 Đang tải tệp zip và cập nhật version.json lên máy chủ...");
            PublishStatusMessage = "Đang tải tệp cập nhật lên máy chủ...";

            var uploadProgress = new Progress<UpdateProgressInfo>(info =>
            {
                // 50% - 100% cho bước upload
                PublishProgress = 50.0 + (info.Percentage * 0.5);
                string phaseText = !string.IsNullOrWhiteSpace(info.StatusText) ? $"{info.StatusText}: " : "Tải lên: ";
                PublishProgressText = $"{phaseText}{info.Percentage:F1}% ({info.BytesDownloaded / (1024.0 * 1024.0):F2} MB / {info.TotalBytes / (1024.0 * 1024.0):F2} MB)";
                PublishSpeedText = info.SpeedFormatted;
            });

            var result = await _publisherService.PublishToServerAsync(publishConfig, zipFilePath, manifest, uploadProgress, _publishCts.Token).ConfigureAwait(true);

            if (result.Success)
            {
                PublishProgress = 100;
                PublishProgressText = "100% — Đã hoàn thành!";
                PublishSpeedText = "";
                PublishStatusMessage = $"✅ Phát hành bản cập nhật v{PublishNewVersion} lên máy chủ thành công!";
                PublishStatusColorHex = "#4ADE80";

                AppendPublishLog($"=======================================================");
                AppendPublishLog($"🎉 PHÁT HÀNH BẢN CẬP NHẬT THÀNH CÔNG!");
                AppendPublishLog($"  - Phiên bản: v{result.Version}");
                if (!string.IsNullOrWhiteSpace(result.DownloadUrl))
                    AppendPublishLog($"  - Đường dẫn gói zip: {result.DownloadUrl}");
                if (!string.IsNullOrWhiteSpace(result.ServerFolder))
                    AppendPublishLog($"  - Thư mục trên server: {result.ServerFolder}");
                if (!string.IsNullOrWhiteSpace(result.ManifestUrl))
                    AppendPublishLog($"  - Đường dẫn version.json: {result.ManifestUrl}");
                AppendPublishLog($"=======================================================");

                // Tự động cập nhật lại thông tin ở Tab 1
                LatestVersionText = $"v{PublishNewVersion}";
                ReleaseNotes = PublishReleaseNotes;
            }
            else
            {
                PublishStatusMessage = $"❌ Lỗi phát hành: {result.ErrorMessage}";
                PublishStatusColorHex = "#EF4444";
                AppendPublishLog($"❌ THẤT BẠI: {result.ErrorMessage}");
                if (!string.IsNullOrWhiteSpace(result.ServerMessage))
                {
                    AppendPublishLog($"Chi tiết phản hồi từ máy chủ: {result.ServerMessage}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            PublishStatusMessage = "Đã hủy bỏ tiến trình phát hành.";
            PublishStatusColorHex = "#94A3B8";
            AppendPublishLog("⚠️ Tiến trình đã bị người dùng hủy bỏ.");
        }
        catch (Exception ex)
        {
            PublishStatusMessage = $"Lỗi: {ex.Message}";
            PublishStatusColorHex = "#EF4444";
            AppendPublishLog($"❌ NGOẠI LỆ: {ex.Message}");
        }
        finally
        {
            IsPublishing = false;
            _publishCts?.Dispose();
            _publishCts = null;

            // Xóa file zip tạm
            try
            {
                if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
            }
            catch { }
        }
    }

    [RelayCommand]
    public void CancelPublish()
    {
        _publishCts?.Cancel();
    }

    private void AppendPublishLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        PublishLogs = string.IsNullOrEmpty(PublishLogs)
            ? $"[{timestamp}] {message}"
            : $"{PublishLogs}\n[{timestamp}] {message}";
    }
}

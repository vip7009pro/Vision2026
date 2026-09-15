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
    private bool _publishAutoBuildProject = true;

    [ObservableProperty]
    private bool _isBuildingProject = false;

    [ObservableProperty]
    private bool _isPublishing = false;

    public bool IsBusyPublishingOrBuilding => IsPublishing || IsBuildingProject;

    partial void OnIsPublishingChanged(bool value) => OnPropertyChanged(nameof(IsBusyPublishingOrBuilding));
    partial void OnIsBuildingProjectChanged(bool value) => OnPropertyChanged(nameof(IsBusyPublishingOrBuilding));

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
        PublishAutoBuildProject = otaCfg.PublishAutoBuildProject;
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
            _settingsService.Settings.Ota.PublishAutoBuildProject = PublishAutoBuildProject;
            _settingsService.Settings.Ota.PublishReleaseChannel = PublishReleaseChannel;
            _settingsService.Save();

            AppendPublishLog($"=======================================================");
            AppendPublishLog($"🚀 BẮT ĐẦU ĐÓNG GÓI & PHÁT HÀNH BẢN CẬP NHẬT v{PublishNewVersion}");
            AppendPublishLog($"=======================================================");
            AppendPublishLog($"📁 Thư mục nguồn ban đầu: {PublishSourceDirectory}");
            AppendPublishLog($"🏢 Máy chủ upload: {PublishServerUrl}");
            AppendPublishLog($"📂 Thư mục trên server: {PublishServerStorageFolder}");
            AppendPublishLog($"🏷️ Kênh phát hành: {PublishReleaseChannel} | Bắt buộc: {PublishIsMandatory}");
            AppendPublishLog($"⚙️ Tùy chọn: AutoUpdateCsproj={PublishAutoUpdateCsproj}, AutoBuildProject={PublishAutoBuildProject}");

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

            string actualSourceDirectory = PublishSourceDirectory;
            string? stagedBuildDir = null;

            // BƯỚC 1.1: Tự động biên dịch dự án (.NET Publish) với phiên bản mới vào thư mục Staging
            if (PublishAutoBuildProject && !string.IsNullOrWhiteSpace(_detectedCsprojPath) && File.Exists(_detectedCsprojPath))
            {
                stagedBuildDir = Path.Combine(tempZipDir, "staged_build");
                PublishStatusMessage = $"Đang biên dịch dự án với phiên bản v{PublishNewVersion}...";
                PublishProgress = 5;
                PublishProgressText = "Đang biên dịch dự án (.NET Publish)...";
                AppendPublishLog($"🔨 Đang tự động biên dịch dự án (dotnet publish) với phiên bản v{PublishNewVersion} vào thư mục Staging...");

                var buildProgress = new Progress<string>(msg => AppendPublishLog(msg));
                var buildResult = await _publisherService.BuildAndStageProjectAsync(
                    _detectedCsprojPath,
                    PublishNewVersion,
                    stagedBuildDir,
                    buildProgress,
                    _publishCts.Token).ConfigureAwait(true);

                if (!buildResult.Success)
                {
                    PublishStatusMessage = "❌ Biên dịch dự án thất bại. Đã dừng đóng gói.";
                    PublishStatusColorHex = "#EF4444";
                    AppendPublishLog($"❌ DỪNG PHÁT HÀNH: {buildResult.ErrorMessage}");
                    return;
                }

                actualSourceDirectory = stagedBuildDir;
                AppendPublishLog($"  ✓ Đã hoàn tất biên dịch! Thư mục đóng gói cập nhật: {actualSourceDirectory}");
            }
            else
            {
                // Nếu không auto-build, kiểm tra xem DLL trong PublishSourceDirectory có bị lệch version không
                string checkDll = Path.Combine(PublishSourceDirectory, "VisionInspectionApp.UI.dll");
                if (File.Exists(checkDll))
                {
                    var binVer = _publisherService.GetBinaryAssemblyVersion(checkDll);
                    string binVerStr = binVer.AssemblyVersion?.ToString() ?? binVer.FileVersion ?? "không xác định";
                    if (!string.Equals(binVerStr, PublishNewVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendPublishLog($"⚠️ CẢNH BÁO: Phiên bản nhị phân hiện tại trong thư mục ({binVerStr}) KHÔNG KHỚP với phiên bản phát hành ({PublishNewVersion})!");
                        AppendPublishLog($"   Để phiên bản mới có hiệu lực trong app, hãy bật tùy chọn 'Tự động biên dịch dự án' hoặc nhấn 'Biên Dịch Dự Án Ngay' trước khi đóng gói.");
                    }
                }
            }

            // BƯỚC 2: Nén thư mục nguồn thành tệp .zip
            AppendPublishLog($"📦 Đang nén các tệp ứng dụng thành gói .zip từ: {actualSourceDirectory}...");
            PublishStatusMessage = "Đang nén thư mục ứng dụng thành tệp .zip...";
            var zipProgress = new Progress<double>(pct =>
            {
                PublishProgress = 15.0 + (pct * 0.3); // 15 - 45% cho bước nén
                PublishProgressText = $"Đang nén tệp: {pct:F0}%";
            });

            await _publisherService.BuildZipPackageAsync(actualSourceDirectory, zipFilePath, zipProgress, _publishCts.Token).ConfigureAwait(true);

            long fileSizeBytes = new FileInfo(zipFilePath).Length;
            double sizeMb = fileSizeBytes / (1024.0 * 1024.0);
            AppendPublishLog($"  ✓ Nén hoàn tất: {sizeMb:F2} MB ({fileSizeBytes:N0} bytes)");

            // BƯỚC 3: Tính toán mã băm SHA-256
            AppendPublishLog($"🔒 Đang tính toán mã băm SHA-256 xác thực tính toàn vẹn...");
            PublishProgress = 48;
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
                SourceDirectory = actualSourceDirectory,
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

            // Dọn dẹp thư mục staging nếu có
            try
            {
                string stagedDir = Path.Combine(tempZipDir, "staged_build");
                if (Directory.Exists(stagedDir))
                {
                    Directory.Delete(stagedDir, true);
                }
            }
            catch { }
        }
    }

    [RelayCommand]
    public async Task BuildProjectOnlyAsync()
    {
        if (IsPublishing || IsBuildingProject) return;

        if (string.IsNullOrWhiteSpace(_detectedCsprojPath) || !File.Exists(_detectedCsprojPath))
        {
            PublishStatusMessage = "❌ Không tìm thấy tệp dự án VisionInspectionApp.UI.csproj.";
            PublishStatusColorHex = "#EF4444";
            AppendPublishLog("❌ Lỗi: Không tìm thấy tệp dự án .csproj để biên dịch.");
            return;
        }

        if (!Version.TryParse(PublishNewVersion, out _))
        {
            PublishStatusMessage = "❌ Số phiên bản mới không đúng định dạng.";
            PublishStatusColorHex = "#EF4444";
            AppendPublishLog($"❌ Phiên bản '{PublishNewVersion}' không hợp lệ.");
            return;
        }

        IsBuildingProject = true;
        PublishProgress = 0;
        PublishProgressText = "Đang bắt đầu biên dịch dự án...";
        PublishStatusMessage = $"Đang biên dịch dự án với phiên bản v{PublishNewVersion}...";
        PublishStatusColorHex = "#38BDF8";
        PublishLogs = "";

        _publishCts = new CancellationTokenSource();
        string tempZipDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_publish");
        string targetStagingDir = Path.Combine(tempZipDir, "build_output");

        try
        {
            // Cập nhật csproj trước nếu được bật
            if (PublishAutoUpdateCsproj)
            {
                AppendPublishLog($"📝 Cập nhật Version vào .csproj: {Path.GetFileName(_detectedCsprojPath)}...");
                await _publisherService.UpdateCsprojVersionAsync(_detectedCsprojPath, PublishNewVersion).ConfigureAwait(true);
            }

            AppendPublishLog($"=======================================================");
            AppendPublishLog($"🔨 BẮT ĐẦU BIÊN DỊCH DỰ ÁN (.NET PUBLISH) v{PublishNewVersion}");
            AppendPublishLog($"=======================================================");

            var progress = new Progress<string>(msg => AppendPublishLog(msg));
            var result = await _publisherService.BuildAndStageProjectAsync(_detectedCsprojPath, PublishNewVersion, targetStagingDir, progress, _publishCts.Token).ConfigureAwait(true);

            if (result.Success)
            {
                PublishSourceDirectory = targetStagingDir;
                PublishProgress = 100;
                PublishProgressText = "100% — Biên dịch hoàn tất!";
                PublishStatusMessage = $"✅ Biên dịch thành công phiên bản v{PublishNewVersion}!";
                PublishStatusColorHex = "#4ADE80";
                AppendPublishLog($"=======================================================");
                AppendPublishLog($"🎉 BIÊN DỊCH DỰ ÁN THÀNH CÔNG!");
                AppendPublishLog($"📁 Thư mục xuất bản đã gán vào nguồn đóng gói: {targetStagingDir}");
                AppendPublishLog($"=======================================================");
            }
            else
            {
                PublishStatusMessage = $"❌ Biên dịch thất bại: {result.ErrorMessage}";
                PublishStatusColorHex = "#EF4444";
                AppendPublishLog($"❌ BIÊN DỊCH THẤT BẠI: {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            PublishStatusMessage = "Đã hủy tiến trình biên dịch.";
            PublishStatusColorHex = "#94A3B8";
            AppendPublishLog("⚠️ Tiến trình biên dịch đã bị người dùng hủy.");
        }
        catch (Exception ex)
        {
            PublishStatusMessage = $"Lỗi: {ex.Message}";
            PublishStatusColorHex = "#EF4444";
            AppendPublishLog($"❌ NGOẠI LỆ: {ex.Message}");
        }
        finally
        {
            IsBuildingProject = false;
            _publishCts?.Dispose();
            _publishCts = null;
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

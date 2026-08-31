using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.Persistence;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Views.OQC;

namespace VisionInspectionApp.UI.ViewModels;

public partial class JobManagerViewModel : ObservableObject
{
    private readonly IOqcScannerService _oqcService;
    private readonly IDbManagerService _dbManager;
    private readonly IRemoteServerService _remoteServerService;
    private readonly CameraService _cameraService;
    private readonly SharedImageContext _sharedImage;
    private readonly ToolEditorViewModel _toolEditorViewModel;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IJobService _jobService;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _currentPageIndex = 0;

    [ObservableProperty]
    private string _pageIndicatorText = "Trang 1";

    [ObservableProperty]
    private ObservableCollection<JobManagerItem> _items = new();

    [ObservableProperty]
    private JobManagerItem? _selectedItem;

    [ObservableProperty]
    private string _serverStatusText = "Chưa kiểm tra kết nối";

    [ObservableProperty]
    private Brush _serverStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private bool _isServerOnline;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng.";

    [ObservableProperty]
    private Brush _statusBrush = Brushes.Gray;

    [ObservableProperty]
    private BitmapSource? _selectedTeachImagePreview;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private string _serverApiUrl = "http://localhost/vision_upload.php";

    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand NextPageCommand { get; }
    public IAsyncRelayCommand PrevPageCommand { get; }
    public IAsyncRelayCommand RefreshListCommand { get; }
    public IAsyncRelayCommand PingServerCommand { get; }
    public IAsyncRelayCommand UploadTeachFromCameraCommand { get; }
    public IAsyncRelayCommand UploadTeachFromFileCommand { get; }
    public IAsyncRelayCommand RemoteTeachCommand { get; }
    public IAsyncRelayCommand UploadCurrentJobCommand { get; }
    public IAsyncRelayCommand DownloadJobCommand { get; }
    public IAsyncRelayCommand AssignLocalJobCommand { get; }
    public IAsyncRelayCommand AssignCurrentActiveJobCommand { get; }
    public IRelayCommand OpenProductAssignCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }

    public JobManagerViewModel(
        IOqcScannerService oqcService,
        IDbManagerService dbManager,
        IRemoteServerService remoteServerService,
        CameraService cameraService,
        SharedImageContext sharedImage,
        ToolEditorViewModel toolEditorViewModel,
        MainWindowViewModel mainWindowViewModel,
        IJobService jobService)
    {
        _oqcService = oqcService;
        _dbManager = dbManager;
        _remoteServerService = remoteServerService;
        _cameraService = cameraService;
        _sharedImage = sharedImage;
        _toolEditorViewModel = toolEditorViewModel;
        _mainWindowViewModel = mainWindowViewModel;
        _jobService = jobService;

        ServerApiUrl = _oqcService.Config.ServerApiUrl;

        SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
        NextPageCommand = new AsyncRelayCommand(ExecuteNextPageAsync);
        PrevPageCommand = new AsyncRelayCommand(ExecutePrevPageAsync);
        RefreshListCommand = new AsyncRelayCommand(ExecuteRefreshListAsync);
        PingServerCommand = new AsyncRelayCommand(ExecutePingServerAsync);
        UploadTeachFromCameraCommand = new AsyncRelayCommand(ExecuteUploadTeachFromCameraAsync);
        UploadTeachFromFileCommand = new AsyncRelayCommand(ExecuteUploadTeachFromFileAsync);
        RemoteTeachCommand = new AsyncRelayCommand(ExecuteRemoteTeachAsync);
        UploadCurrentJobCommand = new AsyncRelayCommand(ExecuteUploadCurrentJobAsync);
        DownloadJobCommand = new AsyncRelayCommand(ExecuteDownloadJobAsync);
        AssignLocalJobCommand = new AsyncRelayCommand(ExecuteAssignLocalJobAsync);
        AssignCurrentActiveJobCommand = new AsyncRelayCommand(ExecuteAssignCurrentActiveJobAsync);
        OpenProductAssignCommand = new RelayCommand(ExecuteOpenProductAssign);
        OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);

        // Auto ping on init
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            await ExecutePingServerAsync();
            await ExecuteRefreshListAsync();
        });
    }

    partial void OnSelectedItemChanged(JobManagerItem? value)
    {
        if (value == null)
        {
            SelectedTeachImagePreview = null;
            return;
        }

        _ = LoadTeachImagePreviewAsync(value.TeachImagePath);
    }

    private async Task LoadTeachImagePreviewAsync(string teachImagePath)
    {
        if (string.IsNullOrWhiteSpace(teachImagePath))
        {
            SelectedTeachImagePreview = null;
            return;
        }

        try
        {
            string urlOrPath = teachImagePath.Trim();

            // Case 1: Local file
            if (File.Exists(urlOrPath))
            {
                using var mat = Cv2.ImRead(urlOrPath);
                if (mat != null && !mat.Empty())
                {
                    var bmp = mat.ToBitmapSource();
                    bmp.Freeze();
                    SelectedTeachImagePreview = bmp;
                    return;
                }
            }

            // Case 2: Full URL or Relative Server Path
            string fullUrl = urlOrPath;
            if (!urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string baseUrl = GetServerBaseUrl();
                fullUrl = $"{baseUrl}/{urlOrPath.TrimStart('/')}";
            }

            var (success, data, err) = await _remoteServerService.DownloadFileAsync(fullUrl);
            if (success && data != null && data.Length > 0)
            {
                using var mat = Cv2.ImDecode(data, ImreadModes.Color);
                if (mat != null && !mat.Empty())
                {
                    var bmp = mat.ToBitmapSource();
                    bmp.Freeze();
                    SelectedTeachImagePreview = bmp;
                    return;
                }
            }

            SelectedTeachImagePreview = null;
        }
        catch
        {
            SelectedTeachImagePreview = null;
        }
    }

    public async Task ExecutePingServerAsync()
    {
        ServerApiUrl = _oqcService.Config.ServerApiUrl;
        var (success, msg) = await _remoteServerService.PingServerAsync(ServerApiUrl);
        IsServerOnline = success;
        if (success)
        {
            ServerStatusText = "🟢 Server Online";
            ServerStatusBrush = Brushes.LimeGreen;
            StatusMessage = msg;
            StatusBrush = Brushes.Green;
        }
        else
        {
            ServerStatusText = "🔴 Server Offline";
            ServerStatusBrush = Brushes.Red;
            StatusMessage = $"⚠️ {msg}";
            StatusBrush = Brushes.OrangeRed;
        }
    }

    public async Task ExecuteSearchAsync()
    {
        CurrentPageIndex = 0;
        await FetchJobManagerListAsync();
    }

    public async Task ExecuteRefreshListAsync()
    {
        await FetchJobManagerListAsync();
    }

    public async Task ExecuteNextPageAsync()
    {
        CurrentPageIndex++;
        await FetchJobManagerListAsync();
    }

    public async Task ExecutePrevPageAsync()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            await FetchJobManagerListAsync();
        }
    }

    private async Task FetchJobManagerListAsync()
    {
        IsBusy = true;
        BusyMessage = "Đang nạp danh sách sản phẩm & Job...";
        StatusMessage = "🔍 Đang nạp danh sách từ CSDL...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            var (success, table, error) = await _oqcService.GetJobManagerListAsync(SearchText, CurrentPageIndex, _dbManager);
            if (success && table != null)
            {
                var cfg = _oqcService.Config;
                var list = new ObservableCollection<JobManagerItem>();

                string colCode = !string.IsNullOrWhiteSpace(cfg.JobManagerProductCodeColumn) ? cfg.JobManagerProductCodeColumn : "ProductCode";
                string colName = !string.IsNullOrWhiteSpace(cfg.JobManagerProductNameColumn) ? cfg.JobManagerProductNameColumn : "ProductName";
                string colJob = !string.IsNullOrWhiteSpace(cfg.JobManagerJobFileColumn) ? cfg.JobManagerJobFileColumn : "JobFilePath";
                string colTeach = !string.IsNullOrWhiteSpace(cfg.JobManagerTeachImageColumn) ? cfg.JobManagerTeachImageColumn : "TeachImagePath";
                string colUpdated = !string.IsNullOrWhiteSpace(cfg.JobManagerUpdatedColumn) ? cfg.JobManagerUpdatedColumn : "UpdatedAt";

                foreach (DataRow row in table.Rows)
                {
                    string pCode = table.Columns.Contains(colCode) ? row[colCode]?.ToString() ?? "" : (table.Columns.Contains("G_CODE") ? row["G_CODE"]?.ToString() ?? "" : row[0]?.ToString() ?? "");
                    string pName = table.Columns.Contains(colName) ? row[colName]?.ToString() ?? "" : (table.Columns.Contains("G_NAME_KD") ? row["G_NAME_KD"]?.ToString() ?? "" : "");
                    string jobPath = table.Columns.Contains(colJob) ? row[colJob]?.ToString() ?? "" : "";
                    string teachPath = table.Columns.Contains(colTeach) ? row[colTeach]?.ToString() ?? "" : "";
                    string updated = table.Columns.Contains(colUpdated) ? row[colUpdated]?.ToString() ?? "" : "";

                    list.Add(new JobManagerItem
                    {
                        ProductCode = pCode,
                        ProductName = pName,
                        JobFilePath = jobPath,
                        TeachImagePath = teachPath,
                        UpdatedAt = updated,
                        HasJobFile = !string.IsNullOrWhiteSpace(jobPath),
                        HasTeachImage = !string.IsNullOrWhiteSpace(teachPath)
                    });
                }

                Items = list;
                PageIndicatorText = $"Trang {CurrentPageIndex + 1} ({table.Rows.Count} dòng)";
                StatusMessage = $"✅ Đã tải trang {CurrentPageIndex + 1} ({table.Rows.Count} sản phẩm).";
                StatusBrush = Brushes.Green;

                // Select first item if exists
                if (Items.Count > 0 && (SelectedItem == null || !Items.Any(x => x.ProductCode == SelectedItem.ProductCode)))
                {
                    SelectedItem = Items[0];
                }
            }
            else
            {
                Items.Clear();
                PageIndicatorText = $"Trang {CurrentPageIndex + 1}";
                StatusMessage = $"❌ Lỗi nạp danh sách: {error}";
                StatusBrush = Brushes.Red;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ngoại lệ: {ex.Message}";
            StatusBrush = Brushes.Red;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Chụp ảnh trực tiếp từ Camera hiện tại và tải lên Server làm Teach Image cho sản phẩm được chọn.
    /// </summary>
    public async Task ExecuteUploadTeachFromCameraAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi chụp ảnh mẫu!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        BusyMessage = "Đang chụp ảnh từ Camera & tải lên Server...";
        StatusMessage = $"📸 Đang chụp ảnh mẫu cho mã '{SelectedItem.ProductCode}'...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            byte[]? imageBytes = null;

            // 1. Thử lấy ảnh trực tiếp từ Camera
            if (_cameraService.IsRunning)
            {
                using var frame = _cameraService.TryGetLatestFrameClone();
                if (frame != null && !frame.Empty())
                {
                    imageBytes = frame.ToBytes(".png");
                }
            }

            // 2. Fallback từ SharedImageContext
            if (imageBytes == null || imageBytes.Length == 0)
            {
                using var snap = _sharedImage.GetSnapshot();
                if (snap != null && !snap.Empty())
                {
                    imageBytes = snap.ToBytes(".png");
                }
            }

            if (imageBytes == null || imageBytes.Length == 0)
            {
                MessageBox.Show("Không thể lấy ảnh từ Camera hoặc màn hình kiểm tra hiện tại. Hãy bật Camera hoặc mở ảnh trước!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "❌ Không có dữ liệu ảnh để tải lên.";
                StatusBrush = Brushes.Red;
                return;
            }

            // Upload lên Server
            string fileName = $"teach_{SelectedItem.ProductCode}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var (uploadOk, fullUrl, relPath, uploadErr) = await _remoteServerService.UploadImageAsync(
                imageBytes, fileName, SelectedItem.ProductCode, _oqcService.Config.ServerApiUrl);

            if (!uploadOk)
            {
                MessageBox.Show($"Lỗi tải ảnh lên máy chủ:\n{uploadErr}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"❌ Lỗi Upload: {uploadErr}";
                StatusBrush = Brushes.Red;
                return;
            }

            // Cập nhật CSDL
            string teachPathToSave = !string.IsNullOrWhiteSpace(relPath) ? relPath : fullUrl;
            var (assignOk, assignMsg) = await _oqcService.UpdateTeachImagePathAsync(
                SelectedItem.ProductCode, teachPathToSave, _dbManager);

            if (assignOk)
            {
                SelectedItem.TeachImagePath = teachPathToSave;
                SelectedItem.HasTeachImage = true;
                SelectedItem.StatusMessage = "Đã cập nhật ảnh Teach";

                await LoadTeachImagePreviewAsync(teachPathToSave);

                StatusMessage = $"✅ Tải ảnh Teach Image thành công ({fileName}) và cập nhật CSDL!";
                StatusBrush = Brushes.Green;
                MessageBox.Show($"✅ Tải ảnh Teach Image thành công!\nURL: {fullUrl}\nĐã cập nhật vào CSDL cho mã '{SelectedItem.ProductCode}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"⚠️ Ảnh đã upload nhưng lỗi ghi CSDL: {assignMsg}";
                StatusBrush = Brushes.Orange;
                MessageBox.Show($"Ảnh đã tải lên Server thành công nhưng lỗi cập nhật CSDL:\n{assignMsg}", "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ngoại lệ Upload: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Ngoại lệ khi upload ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Chọn ảnh từ máy tính (File) và tải lên Server làm Teach Image cho sản phẩm được chọn.
    /// </summary>
    public async Task ExecuteUploadTeachFromFileAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi chọn ảnh!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ofd = new OpenFileDialog
        {
            Title = $"Chọn Ảnh Mẫu (Teach Image) cho '{SelectedItem.ProductCode}'",
            Filter = "Tệp Ảnh (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Tất cả tệp (*.*)|*.*"
        };

        if (ofd.ShowDialog() != true) return;

        IsBusy = true;
        BusyMessage = "Đang đọc & tải ảnh lên Server...";

        try
        {
            byte[] imageBytes = await File.ReadAllBytesAsync(ofd.FileName);
            string fileName = Path.GetFileName(ofd.FileName);

            var (uploadOk, fullUrl, relPath, uploadErr) = await _remoteServerService.UploadImageAsync(
                imageBytes, fileName, SelectedItem.ProductCode, _oqcService.Config.ServerApiUrl);

            if (!uploadOk)
            {
                MessageBox.Show($"Lỗi tải ảnh lên máy chủ:\n{uploadErr}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"❌ Lỗi Upload: {uploadErr}";
                StatusBrush = Brushes.Red;
                return;
            }

            string teachPathToSave = !string.IsNullOrWhiteSpace(relPath) ? relPath : fullUrl;
            var (assignOk, assignMsg) = await _oqcService.UpdateTeachImagePathAsync(
                SelectedItem.ProductCode, teachPathToSave, _dbManager);

            if (assignOk)
            {
                SelectedItem.TeachImagePath = teachPathToSave;
                SelectedItem.HasTeachImage = true;
                SelectedItem.StatusMessage = "Đã cập nhật ảnh Teach";

                await LoadTeachImagePreviewAsync(teachPathToSave);

                StatusMessage = $"✅ Tải ảnh Teach Image thành công ({fileName}) và cập nhật CSDL!";
                StatusBrush = Brushes.Green;
                MessageBox.Show($"✅ Tải ảnh Teach Image thành công!\nURL: {fullUrl}\nĐã cập nhật vào CSDL cho mã '{SelectedItem.ProductCode}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"⚠️ Ảnh đã upload nhưng lỗi ghi CSDL: {assignMsg}";
                StatusBrush = Brushes.Orange;
                MessageBox.Show($"Ảnh đã tải lên Server thành công nhưng lỗi cập nhật CSDL:\n{assignMsg}", "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ngoại lệ Upload: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Ngoại lệ khi upload ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Huấn luyện từ xa (Remote Teach): Tải Job từ Server về thư mục Teaching (cùng thư mục chương trình),
    /// nạp Job vào Tool Editor, và tự động chuyển node ImageSource sang URL ảnh mẫu Teach Image từ Server.
    /// </summary>
    public async Task ExecuteRemoteTeachAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi huấn luyện từ xa!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedItem.TeachImagePath))
        {
            MessageBox.Show($"Sản phẩm '{SelectedItem.ProductCode}' chưa có ảnh mẫu (Teach Image) trên Server!\nHãy chụp hoặc tải ảnh mẫu lên Server trước.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        BusyMessage = "Đang chuẩn bị môi trường Huấn luyện từ xa (tải Job & nạp ảnh mẫu)...";

        try
        {
            // 1. Chuẩn bị thư mục Teaching (cùng thư mục chương trình)
            string teachingDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Teaching");
            Directory.CreateDirectory(teachingDir);

            // 2. Nếu sản phẩm đã có tệp Job trên Server -> Tải về thư mục Teaching và nạp vào Tool Editor
            string? localTeachingJobPath = null;
            if (!string.IsNullOrWhiteSpace(SelectedItem.JobFilePath))
            {
                string jobPath = SelectedItem.JobFilePath.Trim();
                string fullJobUrl = jobPath;
                if (!jobPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !jobPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !File.Exists(jobPath))
                {
                    string baseUrl = GetServerBaseUrl();
                    fullJobUrl = $"{baseUrl}/{jobPath.TrimStart('/')}";
                }

                byte[]? jobBytes = null;
                if (File.Exists(jobPath))
                {
                    jobBytes = await File.ReadAllBytesAsync(jobPath);
                }
                else
                {
                    var (dlOk, data, dlErr) = await _remoteServerService.DownloadFileAsync(fullJobUrl);
                    if (dlOk && data != null && data.Length > 0)
                    {
                        jobBytes = data;
                    }
                }

                if (jobBytes != null && jobBytes.Length > 0)
                {
                    string fileName = Path.GetFileName(jobPath);
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        fileName = $"job_{SelectedItem.ProductCode}.job";
                    }

                    localTeachingJobPath = Path.Combine(teachingDir, fileName);
                    await File.WriteAllBytesAsync(localTeachingJobPath, jobBytes);

                    // Nạp Job này vào Tool Editor
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _toolEditorViewModel.LoadJobFromFile(localTeachingJobPath);
                    });
                }
            }

            // 3. Chuẩn bị URL ảnh mẫu Teach Image đầy đủ
            string teachPath = SelectedItem.TeachImagePath.Trim();
            string fullTeachUrl = teachPath;
            if (!teachPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !teachPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string baseUrl = GetServerBaseUrl();
                fullTeachUrl = $"{baseUrl}/{teachPath.TrimStart('/')}";
            }

            // 4. Đồng bộ mã sản phẩm vào Tool Editor
            _toolEditorViewModel.ProductCode = !string.IsNullOrWhiteSpace(SelectedItem.ProductName) ? SelectedItem.ProductName : SelectedItem.ProductCode;

            // 5. Nạp URL ảnh mẫu vào node ImageSource của Tool Editor và tải ảnh về hiển thị
            await _toolEditorViewModel.FetchAndApplyImageUrlAsync(fullTeachUrl);

            // 6. Chuyển sang Tab Tool Editor
            _mainWindowViewModel.SelectedTabIndex = 0;

            StatusMessage = $"✅ Đã chuẩn bị môi trường Huấn luyện từ xa cho '{SelectedItem.ProductCode}'!";
            StatusBrush = Brushes.Green;

            string jobInfo = localTeachingJobPath != null
                ? $"\n- Tệp Job đã tải về: {localTeachingJobPath}"
                : "\n- Chưa có tệp Job trên Server (bắt đầu từ cấu hình hiện tại)";

            MessageBox.Show($"🌐 Đã nạp môi trường Huấn Luyện Từ Xa cho sản phẩm '{SelectedItem.ProductCode}':{jobInfo}\n- Nguồn ảnh ImageSource: {fullTeachUrl}\n\nBạn có thể tiến hành đặt ROI, Train Template Origin và cấu hình các tool kiểm tra.", "Huấn Luyện Từ Xa", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi nạp môi trường huấn luyện: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Lỗi nạp môi trường huấn luyện: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Tải tệp Job (.job) hiện tại lên Server và tự động cập nhật đường dẫn vào CSDL.
    /// </summary>
    public async Task ExecuteUploadCurrentJobAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi tải Job lên Server!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? jobFilePath = _toolEditorViewModel.CurrentJobFilePath;
        if (string.IsNullOrWhiteSpace(jobFilePath) || !File.Exists(jobFilePath))
        {
            var ofd = new OpenFileDialog
            {
                Title = $"Chọn tệp Job (.job) để tải lên Server cho '{SelectedItem.ProductCode}'",
                Filter = "Vision Job Files (*.job)|*.job|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() != true) return;
            jobFilePath = ofd.FileName;
        }

        // Tự động chuẩn bị Job cho sản xuất: Nếu đang ở chế độ ảnh mẫu URL (Remote Teach), tự động chuyển về Camera OQC gốc
        bool autoSwitchedToCamera = false;
        if (!string.IsNullOrWhiteSpace(_toolEditorViewModel.CurrentJobFilePath) &&
            string.Equals(_toolEditorViewModel.CurrentJobFilePath, jobFilePath, StringComparison.OrdinalIgnoreCase))
        {
            autoSwitchedToCamera = _toolEditorViewModel.PrepareJobForProductionUpload();
        }

        IsBusy = true;
        BusyMessage = "Đang tải tệp Job lên Server XAMPP...";
        StatusMessage = $"📤 Đang tải tệp Job '{Path.GetFileName(jobFilePath)}' lên Server...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            var (uploadOk, fullUrl, relPath, uploadErr) = await _remoteServerService.UploadJobAsync(
                jobFilePath, SelectedItem.ProductCode, _oqcService.Config.ServerApiUrl);

            if (!uploadOk)
            {
                MessageBox.Show($"Lỗi tải tệp Job lên máy chủ:\n{uploadErr}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"❌ Lỗi Upload Job: {uploadErr}";
                StatusBrush = Brushes.Red;
                return;
            }

            string jobPathToSave = !string.IsNullOrWhiteSpace(relPath) ? relPath : fullUrl;
            var (assignOk, assignMsg) = await _oqcService.AssignProductJobAsync(
                SelectedItem.ProductCode, jobPathToSave, _dbManager, SelectedItem.TeachImagePath);

            if (assignOk)
            {
                SelectedItem.JobFilePath = jobPathToSave;
                SelectedItem.HasJobFile = true;
                SelectedItem.StatusMessage = "Đã đồng bộ Job lên Server";

                string cameraNotice = autoSwitchedToCamera
                    ? "\n\n⚡ Nguồn ảnh đã được tự động cấu hình về Camera OQC gốc (bảo lưu toàn bộ thông số Camera & Đèn) để kích hoạt camera thực tế khi chạy kiểm tra dưới chuyền."
                    : "";

                StatusMessage = $"✅ Tải tệp Job lên Server thành công và cập nhật CSDL cho '{SelectedItem.ProductCode}'!";
                StatusBrush = Brushes.Green;
                MessageBox.Show($"✅ Tải tệp Job lên Server thành công!\nURL: {fullUrl}\nĐã cập nhật liên kết trong CSDL cho mã '{SelectedItem.ProductCode}'.{cameraNotice}", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"⚠️ Tệp Job đã upload nhưng lỗi ghi CSDL: {assignMsg}";
                StatusBrush = Brushes.Orange;
                MessageBox.Show($"Tệp Job đã tải lên Server thành công nhưng lỗi cập nhật CSDL:\n{assignMsg}", "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ngoại lệ Upload Job: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Ngoại lệ khi upload Job: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Tải file Job từ Server về máy và mở trong Tool Editor (cho phép người dùng chọn vị trí lưu).
    /// </summary>
    public async Task ExecuteDownloadJobAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedItem.JobFilePath))
        {
            MessageBox.Show($"Sản phẩm '{SelectedItem.ProductCode}' chưa có tệp Job trên Server!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        BusyMessage = "Đang tải tệp Job từ Server về máy...";

        try
        {
            string jobPath = SelectedItem.JobFilePath.Trim();
            string fullUrl = jobPath;
            if (!jobPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !jobPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(jobPath))
            {
                string baseUrl = GetServerBaseUrl();
                fullUrl = $"{baseUrl}/{jobPath.TrimStart('/')}";
            }

            byte[]? jobBytes = null;
            if (File.Exists(jobPath))
            {
                jobBytes = await File.ReadAllBytesAsync(jobPath);
            }
            else
            {
                var (dlOk, data, dlErr) = await _remoteServerService.DownloadFileAsync(fullUrl);
                if (!dlOk || data == null || data.Length == 0)
                {
                    MessageBox.Show($"Không thể tải tệp Job từ URL:\n{fullUrl}\nLỗi: {dlErr}", "Lỗi Tải Job", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                jobBytes = data;
            }

            // Hiển thị SaveFileDialog cho phép người dùng chọn thư mục và tên tệp lưu
            string initialDir = !string.IsNullOrWhiteSpace(_oqcService.Config.JobRootDirectory) && Directory.Exists(_oqcService.Config.JobRootDirectory)
                ? _oqcService.Config.JobRootDirectory
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jobs");
            Directory.CreateDirectory(initialDir);

            string defaultFileName = Path.GetFileName(SelectedItem.JobFilePath);
            if (string.IsNullOrWhiteSpace(defaultFileName) || defaultFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                defaultFileName = $"job_{SelectedItem.ProductCode}.job";
            }

            var sfd = new SaveFileDialog
            {
                Title = $"Lưu tệp Job (.job) cho sản phẩm '{SelectedItem.ProductCode}'",
                FileName = defaultFileName,
                InitialDirectory = initialDir,
                Filter = "Vision Job Files (*.job)|*.job|All Files (*.*)|*.*",
                DefaultExt = ".job"
            };

            if (sfd.ShowDialog() != true) return;

            string localJobPath = sfd.FileName;
            await File.WriteAllBytesAsync(localJobPath, jobBytes);

            // Nạp Job vào Tool Editor
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _toolEditorViewModel.LoadJobFromFile(localJobPath);
                _mainWindowViewModel.SelectedTabIndex = 0;
            });

            StatusMessage = $"✅ Đã tải và nạp Job '{Path.GetFileName(localJobPath)}' vào Tool Editor!";
            StatusBrush = Brushes.Green;
            MessageBox.Show($"✅ Đã tải và nạp tệp Job thành công!\nĐường dẫn cục bộ: {localJobPath}", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi tải Job: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Lỗi tải Job: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Gán tệp Job cục bộ cho sản phẩm đang chọn trong CSDL.
    /// </summary>
    public async Task ExecuteAssignLocalJobAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ofd = new OpenFileDialog
        {
            Title = $"Chọn tệp Job (.job) gán cho '{SelectedItem.ProductCode}'",
            Filter = "Vision Job Files (*.job)|*.job|All Files (*.*)|*.*"
        };

        if (ofd.ShowDialog() != true) return;

        var (assignOk, assignMsg) = await _oqcService.AssignProductJobAsync(
            SelectedItem.ProductCode, ofd.FileName, _dbManager, SelectedItem.TeachImagePath);

        if (assignOk)
        {
            SelectedItem.JobFilePath = ofd.FileName;
            SelectedItem.HasJobFile = true;
            StatusMessage = $"✅ Đã gán Job '{Path.GetFileName(ofd.FileName)}' cho '{SelectedItem.ProductCode}'!";
            StatusBrush = Brushes.Green;
            MessageBox.Show($"✅ Đã gán Job thành công cho mã '{SelectedItem.ProductCode}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            StatusMessage = $"❌ {assignMsg}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Lỗi gán Job: {assignMsg}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Mở hộp thoại Gán Mã Sản Phẩm Mới (Product Assign Dialog) trực tiếp từ cửa sổ Quản Lý Job & Huấn Luyện Từ Xa.
    /// Tự động làm mới danh sách Job sau khi gán mã thành công.
    /// </summary>
    public void ExecuteOpenProductAssign()
    {
        var oqcVm = _mainWindowViewModel.OqcScanner ?? (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(OqcScannerViewModel)) as OqcScannerViewModel;
        if (oqcVm != null)
        {
            // Tự động điền đường dẫn Job: ưu tiên Job đang chọn trong bảng hoặc Job đang mở trong Tool Editor
            string jobToAssign = SelectedItem?.JobFilePath ?? _toolEditorViewModel.CurrentJobFilePath ?? "";
            if (!string.IsNullOrWhiteSpace(jobToAssign))
            {
                oqcVm.AssignJobFilePath = jobToAssign;
            }

            var dialog = new ProductAssignDialog(oqcVm)
            {
                Owner = System.Windows.Application.Current?.Windows.OfType<JobManagerWindow>().FirstOrDefault() 
                        ?? System.Windows.Application.Current?.MainWindow
            };
            dialog.Closed += async (s, e) =>
            {
                // Sau khi đóng hộp thoại gán mã, tự động làm mới danh sách để hiển thị mã mới
                await ExecuteRefreshListAsync();
            };
            dialog.ShowDialog();
        }
        else
        {
            MessageBox.Show("Không tìm thấy dịch vụ OqcScannerViewModel!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Gán trực tiếp tệp Job đang mở trong Tool Editor cho sản phẩm đang chọn trong danh sách CSDL.
    /// </summary>
    public async Task ExecuteAssignCurrentActiveJobAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi gán Job!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? currentJobPath = _toolEditorViewModel.CurrentJobFilePath;
        if (string.IsNullOrWhiteSpace(currentJobPath) || !File.Exists(currentJobPath))
        {
            MessageBox.Show("Hiện tại chưa có tệp Job nào được mở hoặc lưu trong Tool Editor!\nVui lòng mở hoặc lưu Job trước khi gán.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (assignOk, assignMsg) = await _oqcService.AssignProductJobAsync(
            SelectedItem.ProductCode, currentJobPath, _dbManager, SelectedItem.TeachImagePath);

        if (assignOk)
        {
            SelectedItem.JobFilePath = currentJobPath;
            SelectedItem.HasJobFile = true;
            SelectedItem.StatusMessage = "Đã gán Job đang mở";

            // Cập nhật ProductCode vào ToolEditor
            string nameForToolEditor = !string.IsNullOrWhiteSpace(SelectedItem.ProductName) ? SelectedItem.ProductName : SelectedItem.ProductCode;
            _toolEditorViewModel.ApplyAssignedProductCode(nameForToolEditor, currentJobPath);

            StatusMessage = $"✅ Đã gán tệp Job đang mở '{Path.GetFileName(currentJobPath)}' cho sản phẩm '{SelectedItem.ProductCode}'!";
            StatusBrush = Brushes.Green;
            MessageBox.Show($"✅ Đã gán tệp Job đang mở cho sản phẩm '{SelectedItem.ProductCode}' thành công!\nTệp Job: {currentJobPath}", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            StatusMessage = $"❌ Lỗi gán Job: {assignMsg}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Lỗi gán Job:\n{assignMsg}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ExecuteOpenSettings()
    {
        var oqcVm = _mainWindowViewModel.OqcScanner ?? (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(OqcScannerViewModel)) as OqcScannerViewModel;
        if (oqcVm != null)
        {
            var win = new OqcSettingsDialog(oqcVm)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            win.Show();
        }
    }

    private string GetServerBaseUrl()
    {
        string apiUrl = _oqcService.Config.ServerApiUrl;
        if (string.IsNullOrWhiteSpace(apiUrl)) return "http://localhost";

        try
        {
            var uri = new Uri(apiUrl);
            string baseUri = $"{uri.Scheme}://{uri.Authority}";
            string path = uri.AbsolutePath;
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash > 0)
            {
                string dir = path.Substring(0, lastSlash);
                return $"{baseUri}{dir}";
            }
            return baseUri;
        }
        catch
        {
            return "http://localhost";
        }
    }
}

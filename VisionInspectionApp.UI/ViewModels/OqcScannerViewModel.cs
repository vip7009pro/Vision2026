using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;

using OpenCvSharp;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public partial class OqcScannerViewModel : ObservableObject
{
    private readonly IOqcScannerService _oqcService;
    private readonly IDbManagerService _dbManager;
    private readonly IJobService _jobService;
    private readonly InspectionViewModel _inspectionViewModel;
    private readonly ToolEditorViewModel _toolEditorViewModel;
    private readonly CameraService _cameraService;
    private readonly VisionInspectionApp.Application.Services.IRemoteServerService _remoteServerService;
    private readonly VisionInspectionApp.Application.LightingController.LightingPatternService? _lightingPatternService;
    private readonly GlobalAppSettingsService? _globalAppSettings;

    [ObservableProperty]
    private string _scannedCode = "";

    [ObservableProperty]
    private string _currentProductName = "-";

    [ObservableProperty]
    private string _currentJobFilePath = "-";

    [ObservableProperty]
    private int _currentJobTestedCount = 0;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng quét mã QR/Barcode sản phẩm.";

    [ObservableProperty]
    private Brush _statusBrush = Brushes.Gray;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private bool _autoRunJob = true;

    [ObservableProperty]
    private bool _steelPunchMode = true;

    [ObservableProperty]
    private string _currentSessionProductCode = "";

    public bool HasLoadedJob => IsJobLoadedFromManager || (!string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job");

    public bool IsSameAsCurrentSessionCode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return true;
        if (string.IsNullOrWhiteSpace(CurrentSessionProductCode)) return false;

        var (valid, processedCode, _, _) = _oqcService.ProcessRawCodeString(input);
        string codeToCompare = valid ? processedCode : input.Trim();

        return string.Equals(codeToCompare, CurrentSessionProductCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sự kiện yêu cầu View đưa focus về TextBox ô quét mã và bôi đen toàn bộ (SelectAll),
    /// cho phép đầu quét barcode/QR tiếp theo tự động ghi đè mà không cần xóa tay.
    /// </summary>
    public event Action? RequestFocusAndSelectInput;

    public void TriggerFocusAndSelectInput()
    {
        RequestFocusAndSelectInput?.Invoke();
    }

    [ObservableProperty]
    private bool _isJobLoadedFromManager = false;

    [ObservableProperty]
    private bool _isLoadingPopupVisible = false;

    [ObservableProperty]
    private string _loadingMessage = "🔍 Đang phân tích & nhận diện mã 360° đa tầng... Vui lòng chờ trong giây lát!";

    [ObservableProperty]
    private bool _isShowingLiveCamera = true;

    // ─── Image & Overlay Preview Properties for ResultView ───
    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private IEnumerable<OverlayItem>? _overlayItems;

    [ObservableProperty]
    private bool _showResultOverlay = true;

    [ObservableProperty]
    private bool _showRois = true;

    [ObservableProperty]
    private bool _showCrosshair;

    partial void OnShowCrosshairChanged(bool value)
    {
        if (_globalAppSettings != null)
        {
            _globalAppSettings.Settings.ShowCrosshair = value;
            _globalAppSettings.Save();
        }
    }

    [ObservableProperty]
    private bool _isOriginalQualityPreview = MatExtensions.UseOriginalQualityPreview;

    // ─── Origin Live Guide & Template Preview ───
    [ObservableProperty]
    private BitmapSource? _originTemplateImage;

    [ObservableProperty]
    private bool _hasOriginTemplate;

    [ObservableProperty]
    private string _originGuideDetails = string.Empty;

    [ObservableProperty]
    private string _originName = string.Empty;

    private readonly ObservableCollection<OverlayItem> _originLiveGuideOverlays = new();

    private List<OverlayItem>? _allOverlayItemsCache;
    private ImageSource? _lastOqcPreviewImage;
    private List<OverlayItem>? _lastOqcOverlayItems;
    private bool _isOqcRunInProgress = false;
    private bool _isRenderingLiveFrame = false;
    private readonly WriteableBitmapRenderer _liveRenderer = new();
    private string _lastScannedRawCode = "";
    private string _lastScannedProcessedCode = "";

    // ─── Big Result Display & Measurement Details (50/50 Layout) ───
    [ObservableProperty]
    private OqcScanHistoryEntry? _latestScanEntry;

    [ObservableProperty]
    private ObservableCollection<OqcMeasurementDetail> _currentMeasurementDetails = new();

    [ObservableProperty]
    private string _bigResultStatusText = "READY";

    [ObservableProperty]
    private Brush _bigResultBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));

    [ObservableProperty]
    private Brush _bigResultForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

    [ObservableProperty]
    private Brush _bigResultBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));

    [ObservableProperty]
    private string _lastResultSummary = "Sẵn sàng quét mã sản phẩm để bắt đầu đo kiểm.";

    [ObservableProperty]
    private string _lastNgDetails = "";

    [ObservableProperty]
    private bool _hasLastNgDetails = false;

    public ObservableCollection<OqcScanHistoryEntry> ScanHistory { get; } = new();

    public Action<int>? RequestSwitchTab { get; set; }

    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand ScanOrRunJobCommand { get; }
    public IAsyncRelayCommand ScanFromCameraCommand { get; }
    public IAsyncRelayCommand TriggerInspectOrLiveCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand OpenProductAssignCommand { get; }
    public IRelayCommand ManualOpenJobCommand { get; }
    public IRelayCommand ClearHistoryCommand { get; }
    public IRelayCommand ExportToExcelCommand { get; }
    public IRelayCommand<OqcScanHistoryEntry> OpenScanDetailCommand { get; }
    public IRelayCommand OpenScanHistoryWindowCommand { get; }
    public IRelayCommand ViewLatestOutputImageCommand { get; }
    public IRelayCommand ViewLatestScanDetailCommand { get; }
    public IRelayCommand SwitchToToolEditorCommand { get; }
    public IRelayCommand ToggleLiveCameraCommand { get; }
    public IRelayCommand OpenJobManagerCommand { get; }
    public IAsyncRelayCommand QuickCaptureAndUploadTeachImageCommand { get; }

    public string ScanButtonText
    {
        get
        {
            if (IsJobLoadedFromManager)
            {
                return (UseExternalScanner || true) ? "▶ CHẠY JOB (SPACE / Ctrl+F8)" : "▶ CHẠY JOB";
            }

            if (SteelPunchMode && HasLoadedJob)
            {
                return "▶ CHẠY JOB (SPACE / Ctrl+F8)";
            }

            if (!AutoRunJob && !string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
            {
                return UseExternalScanner ? "▶ CHẠY JOB (SPACE / Ctrl+F8)" : "▶ CHẠY JOB";
            }
            return "🔍 QUÉT / TÌM";
        }
    }

    public string CameraScanButtonText => UseExternalScanner
        ? "📷 QUÉT CAMERA"
        : "📷 QUÉT CAMERA (SPACE / Ctrl+F8)";

    public string PreviewHeaderTitle => IsShowingLiveCamera 
        ? "📷 LIVE CAMERA (Căn chỉnh sản phẩm - Space / Ctrl+F8 để kiểm tra)" 
        : "🖼️ XEM TRƯỚC KẾT QUẢ FINAL (Space / Ctrl+F8 / F5 để bật Live Cam)";

    public string LiveToggleButtonText => IsShowingLiveCamera 
        ? "🖼️ Xem Kết Quả Final" 
        : "📷 Live Camera (F5 / Space / Ctrl+F8)";

    public OqcScannerViewModel(
        IOqcScannerService oqcService,
        IDbManagerService dbManager,
        IJobService jobService,
        InspectionViewModel inspectionViewModel,
        ToolEditorViewModel toolEditorViewModel,
        CameraService cameraService,
        VisionInspectionApp.Application.Services.IRemoteServerService? remoteServerService = null,
        VisionInspectionApp.Application.LightingController.LightingPatternService? lightingPatternService = null,
        GlobalAppSettingsService? globalAppSettings = null)
    {
        _oqcService = oqcService;
        _dbManager = dbManager;
        _jobService = jobService;
        _inspectionViewModel = inspectionViewModel;
        _toolEditorViewModel = toolEditorViewModel;
        _cameraService = cameraService;
        _remoteServerService = remoteServerService ?? new VisionInspectionApp.Application.Services.RemoteServerService();
        _lightingPatternService = lightingPatternService;
        _globalAppSettings = globalAppSettings;
        _showCrosshair = _globalAppSettings?.Settings.ShowCrosshair ?? false;
        _steelPunchMode = _oqcService?.Config?.SteelPunchMode ?? true;

        ScanCommand = new AsyncRelayCommand(ExecuteScanAsync);
        ScanOrRunJobCommand = new AsyncRelayCommand(ExecuteScanOrRunJobAsync);
        ScanFromCameraCommand = new AsyncRelayCommand(ExecuteScanFromCameraAsync);
        OpenSettingsCommand = new RelayCommand(OpenSettingsDialog);
        OpenProductAssignCommand = new RelayCommand(OpenProductAssignDialog);
        OpenJobManagerCommand = new RelayCommand(OpenJobManagerWindow);
        QuickCaptureAndUploadTeachImageCommand = new AsyncRelayCommand(ExecuteQuickCaptureAndUploadTeachImageAsync);
        ManualOpenJobCommand = new RelayCommand(ExecuteManualOpenJob);
        ClearHistoryCommand = new RelayCommand(ExecuteClearHistory);
        ExportToExcelCommand = new RelayCommand(ExecuteExportToExcel);
        OpenScanDetailCommand = new RelayCommand<OqcScanHistoryEntry>(ExecuteOpenScanDetail);
        OpenScanHistoryWindowCommand = new RelayCommand(ExecuteOpenScanHistoryWindow);
        ViewLatestOutputImageCommand = new RelayCommand(ExecuteViewLatestOutputImage);
        ViewLatestScanDetailCommand = new RelayCommand(() => ExecuteOpenScanDetail(LatestScanEntry));
        SwitchToToolEditorCommand = new RelayCommand(() => RequestSwitchTab?.Invoke(0));
        ToggleLiveCameraCommand = new RelayCommand(ToggleLiveCamera);
        TriggerInspectOrLiveCommand = new AsyncRelayCommand(ExecuteTriggerInspectOrLiveAsync);

        // Load Scan History from local persistence
        LoadSavedScanHistory();
        if (IsShowingLiveCamera)
        {
            SetWaitingForInspectionState();
        }

        _inspectionViewModel.InspectionCompletedAsync += HandleInspectionCompletedAsync;
        _toolEditorViewModel.InspectionCompletedAsync += HandleInspectionCompletedAsync;
        _toolEditorViewModel.PropertyChanged += OnToolEditorPropertyChanged;

        // Subscribe to CameraService frame stream for live alignment preview
        _cameraService.FrameCaptured += OnCameraFrameCaptured;
        if (!_cameraService.IsRunning)
        {
            _ = _cameraService.StartSavedCameraAsync();
        }
        else if (IsShowingLiveCamera)
        {
            _ = _cameraService.RequestLiveStreamAsync("OQCScanner", true);
        }

        // Initialize Settings properties
        InitSettingsProperties();
    }

    private void OnToolEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolEditorViewModel.Origin_TemplatePreviewImage) ||
            e.PropertyName == "CurrentJobFilePath")
        {
            RefreshOriginTemplateFromJob(_toolEditorViewModel.Config, _toolEditorViewModel.CurrentTempWorkingDir);
        }
        else if (e.PropertyName == nameof(ToolEditorViewModel.IsOriginalQualityPreview))
        {
            if (IsOriginalQualityPreview != _toolEditorViewModel.IsOriginalQualityPreview)
            {
                IsOriginalQualityPreview = _toolEditorViewModel.IsOriginalQualityPreview;
            }
        }
    }

    private async Task RunTaskWith1SecLoadingTimeoutAsync(Func<Task> asyncAction, string loadingMsg)
    {
        using var cts = new CancellationTokenSource();
        var timeoutTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, cts.Token);
                await System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    LoadingMessage = loadingMsg;
                    IsLoadingPopupVisible = true;
                });
            }
            catch (TaskCanceledException) { }
            catch { }
        });

        try
        {
            await asyncAction();
        }
        finally
        {
            cts.Cancel();
            await System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                IsLoadingPopupVisible = false;
            });
        }
    }

    partial void OnAutoRunJobChanged(bool value)
    {
        if (!_isSuppressingConfigSave && _oqcService?.Config != null)
        {
            _oqcService.Config.AutoRunJob = value;
            _oqcService.SaveConfig(_oqcService.Config);
        }
        OnPropertyChanged(nameof(ScanButtonText));
    }

    partial void OnSteelPunchModeChanged(bool value)
    {
        if (!_isSuppressingConfigSave && _oqcService?.Config != null)
        {
            _oqcService.Config.SteelPunchMode = value;
            _oqcService.SaveConfig(_oqcService.Config);
        }
        OnPropertyChanged(nameof(ScanButtonText));
    }

    private string _lastLoadedJobFilePath = "";

    partial void OnCurrentJobFilePathChanged(string value)
    {
        if (!string.Equals(_lastLoadedJobFilePath, value, StringComparison.OrdinalIgnoreCase))
        {
            _lastLoadedJobFilePath = value ?? "";
            CurrentJobTestedCount = 0;
        }
        OnPropertyChanged(nameof(HasLoadedJob));
        OnPropertyChanged(nameof(ScanButtonText));
    }



    partial void OnUseExternalScannerChanged(bool value)
    {
        if (!_isSuppressingConfigSave && _oqcService?.Config != null)
        {
            _oqcService.Config.UseExternalScanner = value;
            _oqcService.SaveConfig(_oqcService.Config);
        }
        OnPropertyChanged(nameof(ScanButtonText));
        OnPropertyChanged(nameof(CameraScanButtonText));
    }

    partial void OnOnlyOriginModeChanged(bool value)
    {
        if (!_isSuppressingConfigSave && _oqcService?.Config != null)
        {
            _oqcService.Config.OnlyOriginMode = value;
            _oqcService.SaveConfig(_oqcService.Config);
        }
    }

    partial void OnIsShowingLiveCameraChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewHeaderTitle));
        OnPropertyChanged(nameof(LiveToggleButtonText));
        if (value)
        {
            ShowRois = true;
            ShowCrosshair = true;
            OverlayItems = (_originLiveGuideOverlays.Count > 0) ? _originLiveGuideOverlays : null;
            _ = _cameraService?.RequestLiveStreamAsync("OQCScanner", true);
            SetWaitingForInspectionState();
        }
        else
        {
            _ = _cameraService?.RequestLiveStreamAsync("OQCScanner", false);
            PreviewImage = _lastOqcPreviewImage;
            _allOverlayItemsCache = _lastOqcOverlayItems != null ? new List<OverlayItem>(_lastOqcOverlayItems) : new List<OverlayItem>();
            UpdatePreviewOverlays();
        }
    }

    partial void OnIsJobLoadedFromManagerChanged(bool value)
    {
        OnPropertyChanged(nameof(HasLoadedJob));
        OnPropertyChanged(nameof(ScanButtonText));
    }

    /// <summary>
    /// Nạp cấu hình Job từ danh sách Quản lý Job.
    /// Giữ nguyên cấu hình Job, để trống ô ScannedCode và yêu cầu người dùng nhập/quét LABEL ID trước khi chạy.
    /// </summary>
    public void SetJobLoadedFromManager(string jobPath, string productName, string productCode)
    {
        CurrentJobFilePath = jobPath;
        CurrentJobTestedCount = 0;
        CurrentProductName = !string.IsNullOrWhiteSpace(productName) ? productName : productCode;
        CurrentSessionProductCode = productCode;
        ScannedCode = ""; // Để trống ô textfield theo yêu cầu của người dùng
        IsJobLoadedFromManager = true;
        _lastScannedProcessedCode = null;
        _lastScannedRawCode = null;
        StatusMessage = $"📁 Đã nạp Job: {Path.GetFileName(jobPath)} ({CurrentProductName}). Hãy nhập LABEL ID trước khi chạy job.";
        StatusBrush = Brushes.DodgerBlue;

        if (_toolEditorViewModel?.Config != null)
        {
            _inspectionViewModel.CurrentJobFilePath = jobPath;
            _inspectionViewModel.CurrentTempWorkingDir = _toolEditorViewModel.CurrentTempWorkingDir;
            _inspectionViewModel.ProductCode = productCode;
            _inspectionViewModel.SetConfig(_toolEditorViewModel.Config);
            RefreshOriginTemplateFromJob(_toolEditorViewModel.Config, _toolEditorViewModel.CurrentTempWorkingDir);
        }

        // Đảm bảo Live View từ camera luôn luôn được kích hoạt mượt mà khi mở Job từ Quản lý Job
        _isRenderingLiveFrame = false;
        ShowRois = true;
        ShowCrosshair = true;
        if (!IsShowingLiveCamera)
        {
            EnableLiveCamera();
        }
        else
        {
            _ = _cameraService.RequestLiveStreamAsync("OQCScanner", true);
            if (!_cameraService.IsRunning)
            {
                _ = _cameraService.StartSavedCameraAsync();
            }
            OverlayItems = (_originLiveGuideOverlays.Count > 0) ? _originLiveGuideOverlays : null;
        }

        SetWaitingForInspectionState($"Đã nạp Job: {Path.GetFileName(jobPath)} ({CurrentProductName}). Căn chỉnh sản phẩm và nhập LABEL ID trước khi chạy.");

        OnPropertyChanged(nameof(PreviewHeaderTitle));
        OnPropertyChanged(nameof(LiveToggleButtonText));
        OnPropertyChanged(nameof(ScanButtonText));

        TriggerFocusAndSelectInput();
    }

    [RelayCommand]
    public void ResetJobTestedCount()
    {
        CurrentJobTestedCount = 0;
    }

    [RelayCommand]
    public void RunJob()
    {
        string rawInput = ScannedCode?.Trim() ?? "";

        // Kiểm tra trường hợp Job mở từ danh sách Quản Lý Job
        if (IsJobLoadedFromManager)
        {
            if (string.IsNullOrWhiteSpace(_lastScannedProcessedCode))
            {
                if (string.IsNullOrWhiteSpace(rawInput))
                {
                    StatusMessage = "⚠️ Hãy nhập LABEL ID trước khi chạy job.";
                    StatusBrush = Brushes.Orange;
                    return;
                }

                var (valid, processedCode, extractedRawCode, filterError) = _oqcService.ProcessRawCodeString(rawInput);
                if (!valid)
                {
                    StatusMessage = $"❌ Mã LABEL ID '{extractedRawCode}' không hợp lệ: {filterError}";
                    StatusBrush = Brushes.Red;
                    return;
                }

                _lastScannedProcessedCode = processedCode;
                _lastScannedRawCode = extractedRawCode;
                _toolEditorViewModel.ProductCode = processedCode;
                _inspectionViewModel.ProductCode = processedCode;
            }
            else if (!string.IsNullOrWhiteSpace(rawInput) && rawInput != _lastScannedRawCode && rawInput != _lastScannedProcessedCode)
            {
                var (valid, processedCode, extractedRawCode, filterError) = _oqcService.ProcessRawCodeString(rawInput);
                if (!valid)
                {
                    StatusMessage = $"❌ Mã LABEL ID '{extractedRawCode}' không hợp lệ: {filterError}";
                    StatusBrush = Brushes.Red;
                    return;
                }

                _lastScannedProcessedCode = processedCode;
                _lastScannedRawCode = extractedRawCode;
                _toolEditorViewModel.ProductCode = processedCode;
                _inspectionViewModel.ProductCode = processedCode;
            }
        }
        else if (SteelPunchMode && !string.IsNullOrWhiteSpace(rawInput))
        {
            // Chế độ Cú đấm thép: Đọc chuỗi scan trên textbox để cập nhật mã sản phẩm đang kiểm tra
            if (rawInput != _lastScannedRawCode && rawInput != _lastScannedProcessedCode)
            {
                var (valid, processedCode, extractedRawCode, filterError) = _oqcService.ProcessRawCodeString(rawInput);
                if (!valid)
                {
                    StatusMessage = $"❌ Mã quét '{extractedRawCode}' không hợp lệ: {filterError}";
                    StatusBrush = Brushes.Red;
                    return;
                }

                _lastScannedProcessedCode = processedCode;
                _lastScannedRawCode = extractedRawCode;
                _toolEditorViewModel.ProductCode = processedCode;
                _inspectionViewModel.ProductCode = processedCode;
            }
        }
        else if (!string.IsNullOrWhiteSpace(rawInput))
        {
            if (rawInput != _lastScannedRawCode && rawInput != _lastScannedProcessedCode)
            {
                var (valid, processedCode, extractedRawCode, filterError) = _oqcService.ProcessRawCodeString(rawInput);
                if (valid)
                {
                    _lastScannedProcessedCode = processedCode;
                    _lastScannedRawCode = extractedRawCode;
                    _toolEditorViewModel.ProductCode = processedCode;
                    _inspectionViewModel.ProductCode = processedCode;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
        {
            string effectiveCode = !string.IsNullOrWhiteSpace(_lastScannedProcessedCode)
                ? _lastScannedProcessedCode
                : (!string.IsNullOrWhiteSpace(_toolEditorViewModel.ProductCode)
                    ? _toolEditorViewModel.ProductCode
                    : CurrentProductName);

            // Kiểm tra xem đã có dòng pending nào ("Đang kiểm tra..." hoặc "Đã nạp Job") cho mã này chưa
            var existingPending = ScanHistory.FirstOrDefault(e =>
                (string.Equals(e.ScannedCode, effectiveCode, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrWhiteSpace(_lastScannedRawCode) && string.Equals(e.ScannedCode, _lastScannedRawCode, StringComparison.OrdinalIgnoreCase))) &&
                (e.InspectResult == "Đang kiểm tra..." || e.InspectResult == "Đã nạp Job"));

            if (existingPending != null)
            {
                existingPending.Time = DateTime.Now;
                existingPending.InspectResult = "Đang kiểm tra...";
                existingPending.ResultBrushHex = "#1E88E5";
            }
            else
            {
                // Thêm dòng mới trạng thái "Đang kiểm tra..." cho lần kiểm tra này (đảm bảo mỗi lần kiểm tra đều có bản ghi lịch sử)
                var historyEntry = new OqcScanHistoryEntry
                {
                    Time = DateTime.Now,
                    ScannedCode = effectiveCode,
                    ProductName = CurrentProductName,
                    JobFilePath = CurrentJobFilePath,
                    InspectResult = "Đang kiểm tra...",
                    ResultBrushHex = "#1E88E5",
                    Success = true,
                    Message = "OK"
                };
                AddHistory(historyEntry);
            }

            IsShowingLiveCamera = false;
            StatusMessage = $"⌛ Đang chạy kiểm tra Job cho sản phẩm '{CurrentProductName}'...";
            StatusBrush = Brushes.DodgerBlue;

            _isOqcRunInProgress = true;
            _toolEditorViewModel.OnRunOnceClicked();
        }
        else
        {
            StatusMessage = "⚠️ Chưa có Job nào được nạp. Vui lòng quét mã sản phẩm trước!";
            StatusBrush = Brushes.Orange;
        }
    }

    [RelayCommand]
    private void EnableLiveCamera()
    {
        if (!_cameraService.IsRunning)
        {
            _ = _cameraService.StartSavedCameraAsync();
        }
        if (!IsShowingLiveCamera)
        {
            IsShowingLiveCamera = true;
        }
        else
        {
            SetWaitingForInspectionState();
        }

        TriggerFocusAndSelectInput();
    }

    private void ToggleLiveCamera()
    {
        if (!IsShowingLiveCamera)
        {
            EnableLiveCamera();
        }
        else
        {
            IsShowingLiveCamera = false;
            RefreshPreviewFromToolEditor();
        }
    }

    public async Task ExecuteTriggerInspectOrLiveAsync()
    {
        if (IsScanning || _isOqcRunInProgress) return;

        if (IsShowingLiveCamera)
        {
            // 1. Đang ở chế độ Live View -> Kích hoạt kiểm tra hàng
            if (HasLoadedJob)
            {
                RunJob();
            }
            else
            {
                await ExecuteScanFromCameraAsync();
            }
        }
        else
        {
            // 2. Đang ở chế độ hiển thị kết quả kiểm tra (không Live) -> Kích hoạt quay trở lại Live View
            EnableLiveCamera();
        }
    }

    public void UpdateOriginLiveGuideOverlays(VisionConfig? cfg = null)
    {
        cfg ??= _toolEditorViewModel.Config;
        _originLiveGuideOverlays.Clear();

        if (cfg?.Origin is not null)
        {
            var origin = cfg.Origin;
            OriginName = origin.Name;

            // 1. Search ROI (Khung tìm kiếm - Viền nét đứt xanh biển)
            if (origin.SearchRoi.Width > 0 && origin.SearchRoi.Height > 0)
            {
                _originLiveGuideOverlays.Add(new OverlayRectItem
                {
                    X = origin.SearchRoi.X,
                    Y = origin.SearchRoi.Y,
                    Width = origin.SearchRoi.Width,
                    Height = origin.SearchRoi.Height,
                    Angle = origin.SearchRoi.Angle,
                    Stroke = Brushes.DeepSkyBlue,
                    StrokeThickness = 1.5,
                    Label = $"{origin.Name} Search ROI"
                });
            }

            // 2. Template ROI (Khung đặt mẫu chuẩn - Viền màu vàng nổi bật)
            if (origin.TemplateRoi.Width > 0 && origin.TemplateRoi.Height > 0)
            {
                _originLiveGuideOverlays.Add(new OverlayRectItem
                {
                    X = origin.TemplateRoi.X,
                    Y = origin.TemplateRoi.Y,
                    Width = origin.TemplateRoi.Width,
                    Height = origin.TemplateRoi.Height,
                    Angle = origin.TemplateRoi.Angle,
                    Stroke = Brushes.Gold,
                    StrokeThickness = 2.5,
                    Label = "🎯 VỊ TRÍ ĐẶT MẪU (ORIGIN)"
                });
            }

            // 3. Center Crosshair (Dấu chữ thập định vị tâm Origin)
            var cx = (origin.WorldPosition.X != 0 || origin.WorldPosition.Y != 0)
                ? origin.WorldPosition.X
                : (origin.TemplateRoi.X + origin.TemplateRoi.Width / 2.0);
            var cy = (origin.WorldPosition.X != 0 || origin.WorldPosition.Y != 0)
                ? origin.WorldPosition.Y
                : (origin.TemplateRoi.Y + origin.TemplateRoi.Height / 2.0);

            if (cx > 0 && cy > 0)
            {
                const double arm = 30.0;
                _originLiveGuideOverlays.Add(new OverlayLineItem { X1 = cx - arm, Y1 = cy, X2 = cx + arm, Y2 = cy, Stroke = Brushes.Gold, StrokeThickness = 2.0 });
                _originLiveGuideOverlays.Add(new OverlayLineItem { X1 = cx, Y1 = cy - arm, X2 = cx, Y2 = cy + arm, Stroke = Brushes.Gold, StrokeThickness = 2.0 });
                _originLiveGuideOverlays.Add(new OverlayPointItem { X = cx, Y = cy, Radius = 4.0, Fill = Brushes.Gold, Stroke = Brushes.Black, Label = "🎯 Tâm Chuẩn" });
            }
        }
    }

    public void RefreshOriginTemplateFromJob(VisionConfig? cfg = null, string? tempDir = null)
    {
        cfg ??= _toolEditorViewModel.Config;
        if (cfg?.Origin == null)
        {
            OriginTemplateImage = null;
            HasOriginTemplate = false;
            OriginGuideDetails = "Chưa cấu hình Origin trong Job này.";
            OriginName = string.Empty;
            UpdateOriginLiveGuideOverlays(cfg);
            return;
        }

        var origin = cfg.Origin;
        UpdateOriginLiveGuideOverlays(cfg);

        OriginGuideDetails = $"🏷️ Node: {origin.Name} | Loại: {origin.OriginAlgorithm}\n📐 Khung mẫu: {origin.TemplateRoi.Width} × {origin.TemplateRoi.Height} px\n🎯 Tâm chuẩn: ({origin.WorldPosition.X:F0}, {origin.WorldPosition.Y:F0})";

        try
        {
            var resolvedFile = _toolEditorViewModel?.ResolveTemplatePath(origin.TemplateImageFile, "origin.png", "origin*.png");
            if (!string.IsNullOrWhiteSpace(resolvedFile) && File.Exists(resolvedFile))
            {
                origin.TemplateImageFile = resolvedFile;
                using var mat = Cv2.ImRead(resolvedFile, ImreadModes.Color);
                if (mat != null && !mat.Empty())
                {
                    OriginTemplateImage = mat.ToBitmapSourceSafe();
                    HasOriginTemplate = true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OqcScannerViewModel] Error loading origin template: {ex.Message}");
        }

        // Fallback to ToolEditorViewModel.Origin_TemplatePreviewImage if already loaded
        if (_toolEditorViewModel?.Origin_TemplatePreviewImage != null)
        {
            OriginTemplateImage = _toolEditorViewModel.Origin_TemplatePreviewImage;
            HasOriginTemplate = true;
        }
        else
        {
            OriginTemplateImage = null;
            HasOriginTemplate = false;
        }
    }

    private void OnCameraFrameCaptured(object? sender, Mat frame)
    {
        if (!IsShowingLiveCamera || frame == null || frame.IsDisposed || frame.Empty())
        {
            return;
        }

        if (_isRenderingLiveFrame)
        {
            return;
        }

        _isRenderingLiveFrame = true;
        Mat? frameCopy = null;
        try
        {
            frameCopy = frame.Clone();
        }
        catch
        {
            _isRenderingLiveFrame = false;
            return;
        }

        try
        {
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                try
                {
                    if (IsShowingLiveCamera && frameCopy != null && !frameCopy.IsDisposed && !frameCopy.Empty())
                    {
                        var bitmap = _liveRenderer.UpdateFromMat(frameCopy, 1920, 1080);
                        if (bitmap != null && !ReferenceEquals(PreviewImage, bitmap))
                        {
                            PreviewImage = bitmap;
                        }
                        OverlayItems = (ShowRois && _originLiveGuideOverlays.Count > 0) ? _originLiveGuideOverlays : null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OqcScanner] Live render error: {ex.Message}");
                }
                finally
                {
                    frameCopy?.Dispose();
                    _isRenderingLiveFrame = false;
                }
            }));
        }
        catch
        {
            frameCopy?.Dispose();
            _isRenderingLiveFrame = false;
        }
    }

    private async Task ExecuteScanFromCameraAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        int timeoutMs = _oqcService.Config.ScanTimeoutMs > 0 ? _oqcService.Config.ScanTimeoutMs : 3000;
        StatusMessage = $"📷 Đang nhận diện mã QR/Barcode từ Camera (Timeout: {timeoutMs / 1000.0:F1}s)...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            var startTime = DateTime.UtcNow;
            CameraCodeScanResult? result = null;

            await RunTaskWith1SecLoadingTimeoutAsync(async () =>
            {
                // 1. Chụp 1 ảnh từ camera tại thời điểm bấm Space
                using var snapshot = _cameraService.TryGetLatestFrameClone() ?? await _cameraService.CaptureSnapshotAsync();
                if (snapshot == null || snapshot.Empty())
                {
                    return;
                }

                // 2. Chạy thuật toán nhận diện mã với giới hạn thời gian Timeout
                var decodeTask = Task.Run(() => _oqcService.DecodeCodeFromImage(snapshot, _oqcService.Config));
                var completedTask = await Task.WhenAny(decodeTask, Task.Delay(timeoutMs));

                if (completedTask == decodeTask)
                {
                    result = await decodeTask;
                }
            }, $"🔍 Đang phân tích & nhận diện mã 360° đa tầng... (Timeout: {timeoutMs / 1000.0:F1}s)");

            double elapsedSec = (DateTime.UtcNow - startTime).TotalSeconds;

            if (result != null && result.Success && !string.IsNullOrWhiteSpace(result.ProcessedCode))
            {
                // Nhận diện mã thành công trong thời gian timeout: Mã đã được bóc tách theo quy tắc từ ảnh
                ScannedCode = result.ProcessedCode;
                StatusMessage = $"📷 Đã đọc mã từ Camera: '{result.ProcessedCode}' (Mã gốc: '{result.RawCode}', Loại: {result.CodeType}). Đang tra DB...";
                StatusBrush = Brushes.DodgerBlue;

                await ExecuteScanInternalAsync(directProcessedCode: result.ProcessedCode, directRawCode: result.RawCode);
            }
            else
            {
                // Quá thời gian timeout hoặc không nhận diện được mã -> TỰ ĐỘNG TRẢ VỀ FAIL!
                string reasonMsg = (result == null)
                    ? $"Hết thời gian chờ ({elapsedSec:F1}s / Timeout {timeoutMs}ms) khi nhận diện mã"
                    : (string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Không tìm thấy mã QR/Barcode hợp lệ trong ảnh" : result.ErrorMessage);

                StatusMessage = $"❌ Nhận diện mã thất bại: {reasonMsg}!";
                StatusBrush = Brushes.Red;

                // Ghi nhận bản ghi FAIL vào lịch sử quét mã
                var failEntry = new OqcScanHistoryEntry
                {
                    Time = DateTime.Now,
                    ScannedCode = "NO_READ",
                    ProductName = "Không tìm thấy mã",
                    JobFilePath = "-",
                    Success = false,
                    InspectResult = "FAIL",
                    ResultBrushHex = "#E53935",
                    Message = reasonMsg,
                    InspectDetails = $"Nhận diện mã không thành công sau {elapsedSec:F1}s (Timeout cấu hình: {timeoutMs}ms)."
                };

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ScanHistory.Insert(0, failEntry);
                    if (ScanHistory.Count > 100) ScanHistory.RemoveAt(ScanHistory.Count - 1);
                });

                // Nếu có cấu hình ghi log lên DB thì ghi nhận kết quả thất bại
                if (_oqcService.Config.LogResultToDb && !string.IsNullOrWhiteSpace(_oqcService.Config.LogResultDbId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var failResult = new InspectionResult
                            {
                                Pass = false
                            };
                            failResult.Timings.TotalMs = (int)(elapsedSec * 1000);
                            await _oqcService.LogInspectionResultAsync("NO_READ", Guid.NewGuid().ToString("N"), "-", failResult, new VisionConfig { ProductName = "Timeout No Read Fail" }, _dbManager);
                        }
                        catch { }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi nhận diện mã từ Camera: {ex.Message}";
            StatusBrush = Brushes.Red;
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ExecuteScanOrRunJobAsync()
    {
        if (HasLoadedJob)
        {
            RunJob();
        }
        else
        {
            await ExecuteScanAsync();
        }
    }

    private async Task ExecuteScanAsync()
    {
        if (IsScanning || _isOqcRunInProgress) return;

        IsScanning = true;
        try
        {
            await ExecuteScanInternalAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi xử lý mã: {ex.Message}";
            StatusBrush = Brushes.Red;
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ExecuteScanInternalAsync(string? directProcessedCode = null, string? directRawCode = null)
    {
        // Dừng ngay kịch bản nháy đèn đang chạy (nếu có) để trả lại ánh sáng ổn định cho camera
        _lightingPatternService?.StopCurrentPattern();

        string code;
        string rawCode;

        if (!string.IsNullOrWhiteSpace(directProcessedCode))
        {
            // Mã được đọc trực tiếp từ Camera: ĐÃ được bóc tách và lọc theo quy tắc, không áp dụng lọc/cắt lại lần 2
            code = directProcessedCode.Trim();
            rawCode = !string.IsNullOrWhiteSpace(directRawCode) ? directRawCode.Trim() : code;
        }
        else
        {
            string rawInput = ScannedCode?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                if (!AutoRunJob && HasLoadedJob)
                {
                    RunJob();
                    return;
                }

                StatusMessage = "⚠️ Vui lòng nhập hoặc quét mã sản phẩm!";
                StatusBrush = Brushes.Orange;
                return;
            }

            // Áp dụng bộ lọc độ dài và cắt chuỗi cấu hình cho mã nhập/quét từ đầu đọc ngoài
            var (valid, processedCode, extractedRawCode, filterError) = _oqcService.ProcessRawCodeString(rawInput);
            if (!valid)
            {
                StatusMessage = $"❌ Mã quét '{extractedRawCode}' không hợp lệ: {filterError}";
                StatusBrush = Brushes.Red;

                var invalidEntry = new OqcScanHistoryEntry
                {
                    Time = DateTime.Now,
                    ScannedCode = extractedRawCode,
                    ProductName = "Mã không hợp lệ",
                    JobFilePath = "-",
                    Success = false,
                    InspectResult = "LỖI BỘ LỌC MÃ",
                    ResultBrushHex = "#D32F2F",
                    Message = filterError,
                    InspectDetails = $"Mã gốc '{extractedRawCode}' bị loại bởi bộ lọc: {filterError}"
                };
                AddHistory(invalidEntry);
                ScannedCode = "";
                return;
            }

            code = processedCode;
            rawCode = extractedRawCode;
        }

        // ─── TRƯỜNG HỢP QUÉT LẠI MÃ TEM CÙNG PHIÊN ĐỂ BẮT ĐẦU PHIÊN MỚI / RESET COUNTING ───
        if (HasLoadedJob && IsSameAsCurrentSessionCode(rawCode) && !string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
        {
            // Reset số lượng mẫu đã kiểm tra về 0 cho phiên mới
            CurrentJobTestedCount = 0;
            _lastScannedProcessedCode = code;
            _lastScannedRawCode = rawCode;
            CurrentSessionProductCode = code;

            if (SteelPunchMode)
            {
                ScannedCode = code;
            }

            // Dọn dẹp các dòng pending cũ nếu có
            var pendingOld = ScanHistory.Where(e => e.InspectResult == "Đang kiểm tra..." || e.InspectResult == "Đã nạp Job").ToList();
            if (pendingOld.Count > 0)
            {
                RemoveHistoryEntries(ScanHistory, pendingOld);
            }

            // Thêm bản ghi mới cho phiên làm việc mới
            var resetHistoryEntry = new OqcScanHistoryEntry
            {
                Time = DateTime.Now,
                ScannedCode = code,
                ProductName = CurrentProductName,
                JobFilePath = CurrentJobFilePath,
                InspectResult = AutoRunJob ? "Đang kiểm tra..." : "Đã nạp Job",
                ResultBrushHex = "#1E88E5",
                Success = true,
                Message = "OK"
            };
            AddHistory(resetHistoryEntry);

            if (AutoRunJob)
            {
                IsShowingLiveCamera = false;
                StatusMessage = $"📁 Đã làm mới phiên Job '{Path.GetFileName(CurrentJobFilePath)}' cho mã '{code}' và chạy kiểm tra...";
                StatusBrush = Brushes.DodgerBlue;
                _isOqcRunInProgress = true;
                _toolEditorViewModel.LoadJobFromFile(CurrentJobFilePath, autoRun: true);
            }
            else
            {
                _isOqcRunInProgress = false;
                ShowRois = true;
                ShowCrosshair = true;
                IsShowingLiveCamera = true;
                SetWaitingForInspectionState($"Đã làm mới phiên Job '{Path.GetFileName(CurrentJobFilePath)}' cho mã '{code}'. Đã reset đếm mẫu về 0. Căn chỉnh sản phẩm và nhấn '▶ CHẠY JOB' (Space) để kiểm tra.");
                StatusMessage = $"🔄 Đã nạp lại Job phiên mới cho mã '{code}'. Đã reset số đếm mẫu về 0. Bấm Space để kiểm tra.";
                StatusBrush = Brushes.DodgerBlue;
            }

            OnPropertyChanged(nameof(ScanButtonText));
            OnPropertyChanged(nameof(PreviewHeaderTitle));
            OnPropertyChanged(nameof(LiveToggleButtonText));
            TriggerFocusAndSelectInput();
            return;
        }

        // ─── TRƯỜNG HỢP NẠP JOB MỚI (MÃ KHÁC HOẶC CHƯA CÓ JOB) ───
        _lastScannedProcessedCode = code;
        _lastScannedRawCode = rawCode;
        CurrentSessionProductCode = code;
        IsJobLoadedFromManager = false; // Giải phóng cờ mở từ Manager khi scan mã mới

        // Dọn dẹp các dòng pending cũ của Job trước (nếu có)
        var oldPendingEntries = ScanHistory.Where(e => e.InspectResult == "Đang kiểm tra..." || e.InspectResult == "Đã nạp Job").ToList();
        if (oldPendingEntries.Count > 0)
        {
            RemoveHistoryEntries(ScanHistory, oldPendingEntries);
        }

        // Text scanned được cắt và hiển thị theo quy tắc quy định trong bảng cấu hình OQC, không xóa textbox
        if (SteelPunchMode)
        {
            ScannedCode = code;
        }

        StatusMessage = $"🔍 Đang tra cứu cơ sở dữ liệu cho mã '{code}'" + (code != rawCode ? $" (Mã gốc: '{rawCode}')" : "") + "...";
        StatusBrush = Brushes.DodgerBlue;

        string displayProductName = code;
        var historyEntry = new OqcScanHistoryEntry
        {
            Time = DateTime.Now,
            ScannedCode = code,
            ProductName = displayProductName,
            InspectResult = AutoRunJob ? "Đang kiểm tra..." : "Đã nạp Job",
            ResultBrushHex = "#1E88E5"
        };

        try
        {
            if (_oqcService.Config.EnableProductNameLookup)
            {
                var (nameFound, resolvedName, _) = await _oqcService.LookupProductNameAsync(code, _dbManager);
                if (nameFound && !string.IsNullOrWhiteSpace(resolvedName))
                {
                    displayProductName = resolvedName;
                }
            }

            CurrentProductName = displayProductName;
            historyEntry.ProductName = displayProductName;

            var (found, jobPath, error) = await _oqcService.LookupJobAsync(code, _dbManager, _remoteServerService);
            if (!found)
            {
                StatusMessage = $"❌ Lỗi tra cứu DB: {error}";
                StatusBrush = Brushes.Red;
                CurrentJobFilePath = "Chưa có Job";

                historyEntry.Success = false;
                historyEntry.Message = error;
                historyEntry.JobFilePath = jobPath;
                historyEntry.InspectResult = "LỖI TRA CỨU DB";
                historyEntry.ResultBrushHex = "#D32F2F";
                AddHistory(historyEntry);
                return;
            }

            CurrentJobFilePath = jobPath;
            historyEntry.Success = true;
            historyEntry.JobFilePath = jobPath;
            historyEntry.Message = "OK";

            // Add history entry to UI
            AddHistory(historyEntry);

            var cfg = _jobService.LoadJob(jobPath, out var tempDir);
            cfg.ProductCode = code;
            cfg.ProductName = displayProductName;

            _inspectionViewModel.CurrentJobFilePath = jobPath;
            _inspectionViewModel.CurrentTempWorkingDir = tempDir;
            _inspectionViewModel.ProductCode = code;
            _inspectionViewModel.SetConfig(cfg);

            if (System.Windows.Application.Current?.MainWindow != null)
            {
                System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - [OQC] " + Path.GetFileName(jobPath);
            }

            _toolEditorViewModel.ProductCode = code;

            // QUAN TRỌNG: Reset số lượng mẫu đã kiểm tra về 0 cho Job mới nạp
            CurrentJobTestedCount = 0;

            if (AutoRunJob)
            {
                // Auto Run mode: Load job and run graph automatically
                IsShowingLiveCamera = false;
                StatusMessage = $"📁 Đang nạp tệp Job: '{Path.GetFileName(jobPath)}' và chạy kiểm tra...";
                _isOqcRunInProgress = true;
                _toolEditorViewModel.LoadJobFromFile(jobPath, autoRun: true);
            }
            else
            {
                // Manual Run mode: Load job only, keep Live Camera active for product alignment
                _isOqcRunInProgress = false;
                ShowRois = true;
                ShowCrosshair = true;
                IsShowingLiveCamera = true;
                SetWaitingForInspectionState($"Đã nạp Job '{Path.GetFileName(jobPath)}' cho mã '{code}'. Căn chỉnh sản phẩm và nhấn '▶ CHẠY JOB' để kiểm tra.");
                StatusMessage = $"✅ Đã nạp Job '{Path.GetFileName(jobPath)}' cho mã '{code}'. Căn chỉnh sản phẩm và nhấn '▶ CHẠY JOB' để kiểm tra.";
                StatusBrush = Brushes.DodgerBlue;
                _toolEditorViewModel.LoadJobFromFile(jobPath, autoRun: false);
            }

            RefreshOriginTemplateFromJob(cfg, tempDir);

            OnPropertyChanged(nameof(ScanButtonText));
            OnPropertyChanged(nameof(PreviewHeaderTitle));
            OnPropertyChanged(nameof(LiveToggleButtonText));

            // Cập nhật giá trị chuỗi mã quét trên TextBox
            if (SteelPunchMode)
            {
                ScannedCode = code; // Text scanned được cắt và hiển thị theo quy tắc quy định trong bảng cấu hình OQC, không tự xóa textbox
            }
            else
            {
                ScannedCode = "";
            }

            // Tự động bôi đen toàn bộ mã trong TextBox để lần scan tiếp theo tự động ghi đè
            TriggerFocusAndSelectInput();
        }
        catch (Exception ex)
        {
            _isOqcRunInProgress = false;
            StatusMessage = $"❌ Lỗi tra cứu/mở Job: {ex.Message}";
            StatusBrush = Brushes.Red;

            historyEntry.Success = false;
            historyEntry.Message = ex.Message;
            historyEntry.InspectResult = "LỖI NẠP JOB";
            historyEntry.ResultBrushHex = "#D32F2F";
            if (!ScanHistory.Contains(historyEntry))
            {
                AddHistory(historyEntry);
            }
        }
        finally
        {
            IsScanning = false;
            TriggerFocusAndSelectInput();
        }
    }

    public async Task HandleInspectionCompletedAsync(InspectionResult result, VisionConfig config)
    {
        if (result == null || !_isOqcRunInProgress) return;
        _isOqcRunInProgress = false;

        IsShowingLiveCamera = false;
        OnPropertyChanged(nameof(PreviewHeaderTitle));
        OnPropertyChanged(nameof(LiveToggleButtonText));

        _lastOqcPreviewImage = _toolEditorViewModel.FinalPreviewImage;
        var finalOverlays = _toolEditorViewModel.FinalOverlayItems;
        _lastOqcOverlayItems = finalOverlays != null ? new List<OverlayItem>(finalOverlays) : new List<OverlayItem>();
        _allOverlayItemsCache = new List<OverlayItem>(_lastOqcOverlayItems);

        string processedCode = !string.IsNullOrWhiteSpace(_lastScannedProcessedCode)
            ? _lastScannedProcessedCode
            : (!string.IsNullOrWhiteSpace(_toolEditorViewModel.ProductCode) ? _toolEditorViewModel.ProductCode : config?.ProductCode ?? CurrentProductName);

        string rawCode = !string.IsNullOrWhiteSpace(_lastScannedRawCode) ? _lastScannedRawCode : processedCode;

        if (string.IsNullOrWhiteSpace(_lastScannedProcessedCode))
        {
            _lastScannedProcessedCode = processedCode;
            _lastScannedRawCode = rawCode;
        }

        string productName = CurrentProductName;
        string path = CurrentJobFilePath;
        string uuid = Guid.NewGuid().ToString("N");

        string outputImagePath = "";
        if (result.ImageOutputs != null && result.ImageOutputs.Count > 0)
        {
            outputImagePath = result.ImageOutputs.FirstOrDefault(x => !string.IsNullOrEmpty(x.SavedFilePath))?.SavedFilePath ?? "";
        }

        var measurementDetails = (config != null) 
            ? _oqcService.ExtractMeasurementDetails(result, config) 
            : new List<OqcMeasurementDetail>();

        bool effectivePass = result.Pass;
        string details = ExtractDetailedReasons(result);

        if (OnlyOriginMode)
        {
            bool isOriginPass = result.Origin != null && result.Origin.Pass;
            effectivePass = isOriginPass;
            if (isOriginPass)
            {
                details = result.Pass
                    ? "Bắt Origin đạt tiêu chuẩn (PASS)."
                    : $"Bắt Origin đạt tiêu chuẩn (Score: {result.Origin?.Score:F3}) - Chế độ chỉ bắt Origin.";
            }
            else
            {
                details = result.Origin != null
                    ? $"Origin không đạt tiêu chuẩn (Score: {result.Origin.Score:F3})!"
                    : "Không tìm thấy Origin / Job chưa có cấu hình Origin!";
            }
        }

        string statusStr = effectivePass ? "PASS (OK)" : "NG (LỖI)";
        string colorHex = effectivePass ? "#2E7D32" : "#D32F2F";
        Brush statusBrush = effectivePass ? Brushes.ForestGreen : Brushes.Crimson;

        OqcScanHistoryEntry currentEntry = null!;

        void UpdateUiAndHistory()
        {
            CurrentJobTestedCount++;

            // Tìm dòng pending (đang chờ kết quả) trong lịch sử:
            // Là dòng có ScannedCode khớp với mã sản phẩm này VÀ có InspectResult là "Đang kiểm tra..." hoặc "Đã nạp Job"
            var pendingEntry = ScanHistory.FirstOrDefault(e =>
                (string.Equals(e.ScannedCode, processedCode, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrWhiteSpace(rawCode) && string.Equals(e.ScannedCode, rawCode, StringComparison.OrdinalIgnoreCase))) &&
                (e.InspectResult == "Đang kiểm tra..." || e.InspectResult == "Đã nạp Job"));

            if (pendingEntry != null)
            {
                // Cập nhật kết quả vào dòng pending hiện có
                pendingEntry.Time = DateTime.Now;
                pendingEntry.ScannedCode = processedCode;
                pendingEntry.ProductName = productName;
                pendingEntry.JobFilePath = path;
                pendingEntry.Uuid = uuid;
                pendingEntry.InspectResult = statusStr;
                pendingEntry.InspectDetails = details;
                pendingEntry.ResultBrushHex = colorHex;
                pendingEntry.OutputImagePath = outputImagePath;
                pendingEntry.MeasurementDetails = measurementDetails;
                pendingEntry.Success = true;
                pendingEntry.Message = "OK";
                currentEntry = pendingEntry;
            }
            else
            {
                // Không có dòng pending (ví dụ: các lần chụp và kiểm tiếp theo cho cùng 1 phiên Job đang mở)
                // BẮT BUỘC TẠO DÒNG MỚI ĐỂ LƯU TOÀN BỘ LỊCH SỬ TỪNG LẦN KIỂM TRA!
                currentEntry = new OqcScanHistoryEntry
                {
                    Time = DateTime.Now,
                    ScannedCode = processedCode,
                    ProductName = productName,
                    JobFilePath = path,
                    Uuid = uuid,
                    InspectResult = statusStr,
                    InspectDetails = details,
                    ResultBrushHex = colorHex,
                    OutputImagePath = outputImagePath,
                    MeasurementDetails = measurementDetails,
                    Success = true,
                    Message = "OK"
                };
                AddHistory(currentEntry);
            }

            StatusMessage = effectivePass
                ? $"✅ SẢN PHẨM '{productName}' ({processedCode}) -> KẾT QUẢ: PASS (OK)"
                : $"❌ SẢN PHẨM '{productName}' ({processedCode}) -> KẾT QUẢ: NG! Lý do: {details}";
            StatusBrush = statusBrush;

            LatestScanEntry = currentEntry;

            // Update Big Result Display & Measurement Details (50/50 Layout)
            BigResultStatusText = effectivePass ? "PASS" : "NG";
            BigResultBackgroundBrush = effectivePass
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B5E20"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B71C1C"));
            BigResultBorderBrush = effectivePass
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
            BigResultForegroundBrush = Brushes.White;

            if (OnlyOriginMode)
            {
                LastResultSummary = effectivePass
                    ? $"✅ SP: {productName} | Mã: {processedCode} | Origin: ĐẠT ({result.Origin?.Score:F3}) | Chế độ chỉ bắt Origin | Lúc: {DateTime.Now:HH:mm:ss}"
                    : $"❌ SP: {productName} | Mã: {processedCode} | Origin: KHÔNG ĐẠT | Lúc: {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                int passCount = measurementDetails.Count(d => d.Pass);
                int totalCount = measurementDetails.Count;
                LastResultSummary = effectivePass
                    ? $"✅ SP: {productName} | Mã: {processedCode} | Đạt: {passCount}/{totalCount} phép đo | Lúc: {DateTime.Now:HH:mm:ss}"
                    : $"❌ SP: {productName} | Mã: {processedCode} | Lỗi: {totalCount - passCount}/{totalCount} phép đo | Lúc: {DateTime.Now:HH:mm:ss}";
            }

            HasLastNgDetails = !effectivePass && !string.IsNullOrWhiteSpace(details);
            LastNgDetails = details;

            // Đồng bộ kết quả chung của result theo effectivePass khi bật chế độ chỉ bắt Origin
            result.Pass = effectivePass;

            CurrentMeasurementDetails?.Clear();
            if (CurrentMeasurementDetails != null)
            {
                foreach (var m in measurementDetails)
                {
                    CurrentMeasurementDetails.Add(m);
                }
            }

            if (IsJobLoadedFromManager)
            {
                if (!SteelPunchMode)
                {
                    _lastScannedProcessedCode = null;
                    _lastScannedRawCode = null;
                    ScannedCode = ""; // Xóa ô nhập liệu để sẵn sàng cho lần quét LABEL ID tiếp theo
                }
            }

            // Tự động tắt ROI và Crosshair sau khi chạy xong để người dùng dễ dàng quan sát kết quả
            ShowRois = false;
            ShowCrosshair = false;

            _oqcService.SaveScanHistory(ScanHistory);
            if (!IsShowingLiveCamera)
            {
                PreviewImage = _lastOqcPreviewImage;
                UpdatePreviewOverlays();
            }

            TriggerFocusAndSelectInput();
        }

        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(UpdateUiAndHistory);
        }
        else
        {
            UpdateUiAndHistory();
        }

        // Log result to Database if enabled
        if ((_oqcService.Config.LogResultToDb || _oqcService.Config.LogDetailResultToDb) && config != null)
        {
            var (dbSuccess, dbMsg) = await _oqcService.LogInspectionResultAsync(processedCode, uuid, path, result, config, _dbManager, measurementDetails, rawCode);

            void UpdateDbStatus()
            {
                if (currentEntry != null)
                {
                    currentEntry.DbLogStatus = dbSuccess ? "DB: OK" : "DB: LỖI";
                }
                if (!dbSuccess)
                {
                    StatusMessage += $" | ⚠️ Ghi DB thất bại: {dbMsg}";
                }
                else
                {
                    StatusMessage += $" | 💾 {dbMsg}";
                }
                _oqcService.SaveScanHistory(ScanHistory);
            }

            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(UpdateDbStatus);
            }
            else
            {
                UpdateDbStatus();
            }
        }

        // Kích hoạt kịch bản nháy đèn cảnh báo NG (NG Blink Pattern) nếu kết quả kiểm tra NG
        if (!result.Pass && _lightingPatternService != null)
        {
            var lSettings = _globalAppSettings?.Settings?.Lighting;
            if (lSettings != null && lSettings.EnableNgPattern)
            {
                _ = _lightingPatternService.PlayNgPatternAsync(
                    lSettings.EnableNgPattern,
                    lSettings.NgPatternId,
                    lSettings.Patterns,
                    lSettings.ChannelCount);
            }
        }
    }

    public void RefreshPreviewFromToolEditor()
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            PreviewImage = _lastOqcPreviewImage;
            _allOverlayItemsCache = _lastOqcOverlayItems != null ? new List<OverlayItem>(_lastOqcOverlayItems) : new List<OverlayItem>();
            UpdatePreviewOverlays();
        });
    }

    partial void OnShowResultOverlayChanged(bool value)
    {
        if (IsShowingLiveCamera) return;
        UpdatePreviewOverlays();
    }

    partial void OnShowRoisChanged(bool value)
    {
        if (IsShowingLiveCamera)
        {
            OverlayItems = (value && _originLiveGuideOverlays.Count > 0) ? _originLiveGuideOverlays : null;
        }
        else
        {
            UpdatePreviewOverlays();
        }
    }

    partial void OnIsOriginalQualityPreviewChanged(bool value)
    {
        MatExtensions.UseOriginalQualityPreview = value;
        if (_toolEditorViewModel != null && _toolEditorViewModel.IsOriginalQualityPreview != value)
        {
            _toolEditorViewModel.IsOriginalQualityPreview = value;
        }
        if (!IsShowingLiveCamera && _toolEditorViewModel != null)
        {
            // Đọc FinalPreviewImage ngay sau đó => phải làm mới ĐỒNG BỘ.
            _toolEditorViewModel.RefreshPreviewsNow();
            _lastOqcPreviewImage = _toolEditorViewModel.FinalPreviewImage;
            PreviewImage = _lastOqcPreviewImage;
        }
    }

    private void UpdatePreviewOverlays()
    {
        if (_allOverlayItemsCache == null || _allOverlayItemsCache.Count == 0)
        {
            OverlayItems = null;
            return;
        }

        var filtered = new List<OverlayItem>();
        foreach (var item in _allOverlayItemsCache)
        {
            bool isRoiBox = item is OverlayRectItem;

            if (isRoiBox)
            {
                if (ShowRois)
                {
                    filtered.Add(item);
                }
            }
            else
            {
                if (ShowResultOverlay)
                {
                    filtered.Add(item);
                }
            }
        }

        OverlayItems = filtered;
    }

    private static string ExtractDetailedReasons(InspectionResult result)
    {
        return OqcScannerService.ExtractNgReasons(result);
    }

    private void ExecuteManualOpenJob()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Job Files (*.job)|*.job|All Files (*.*)|*.*",
            Title = "Mở tệp Vision Job thủ công"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _isOqcRunInProgress = false;
                _toolEditorViewModel.LoadJobFromFile(dialog.FileName, autoRun: false);

                var cfg = _jobService.LoadJob(dialog.FileName, out var tempDir);
                _inspectionViewModel.CurrentJobFilePath = dialog.FileName;
                _inspectionViewModel.CurrentTempWorkingDir = tempDir;
                _inspectionViewModel.SetConfig(cfg);

                RefreshOriginTemplateFromJob(cfg, tempDir);

                // Đảm bảo Live View từ camera luôn luôn được kích hoạt mượt mà khi mở Job thủ công
                _isRenderingLiveFrame = false;
                if (!IsShowingLiveCamera)
                {
                    EnableLiveCamera();
                }
                else
                {
                    _ = _cameraService.RequestLiveStreamAsync("OQCScanner", true);
                    if (!_cameraService.IsRunning)
                    {
                        _ = _cameraService.StartSavedCameraAsync();
                    }
                    OverlayItems = (ShowRois && _originLiveGuideOverlays.Count > 0) ? _originLiveGuideOverlays : null;
                }

                SetWaitingForInspectionState($"Đã nạp Job: {Path.GetFileName(dialog.FileName)}. Căn chỉnh sản phẩm và bấm 'CHẠY JOB'.");

                OnPropertyChanged(nameof(PreviewHeaderTitle));
                OnPropertyChanged(nameof(LiveToggleButtonText));

                CurrentJobFilePath = dialog.FileName;
                CurrentProductName = Path.GetFileNameWithoutExtension(dialog.FileName);
                StatusMessage = $"✅ Đã mở thủ công Job: {Path.GetFileName(dialog.FileName)}";
                StatusBrush = Brushes.Green;
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Lỗi mở Job thủ công: {ex.Message}";
                StatusBrush = Brushes.Red;
            }
        }
    }

    private void LoadSavedScanHistory()
    {
        try
        {
            var list = _oqcService.LoadScanHistory();
            if (list != null && list.Count > 0)
            {
                ScanHistory.Clear();
                foreach (var item in list)
                {
                    ScanHistory.Add(item);
                }

                // Hiển thị kết quả của lần đo gần nhất lên Big Result & Current Measurement Details
                var top = ScanHistory[0];
                LatestScanEntry = top;
                bool isPass = top.InspectResult.Contains("PASS", StringComparison.OrdinalIgnoreCase) || top.InspectResult.Contains("OK", StringComparison.OrdinalIgnoreCase);
                bool isNg = top.InspectResult.Contains("NG", StringComparison.OrdinalIgnoreCase);

                if (isPass)
                {
                    BigResultStatusText = "PASS";
                    BigResultBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B5E20"));
                    BigResultBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                    BigResultForegroundBrush = Brushes.White;
                }
                else if (isNg)
                {
                    BigResultStatusText = "NG";
                    BigResultBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B71C1C"));
                    BigResultBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                    BigResultForegroundBrush = Brushes.White;
                }

                LastResultSummary = $"Gần nhất: {top.ProductName} ({top.ScannedCode}) | Lúc {top.Time:HH:mm:ss dd/MM}";
                HasLastNgDetails = isNg && !string.IsNullOrWhiteSpace(top.InspectDetails);
                LastNgDetails = top.InspectDetails;

                CurrentMeasurementDetails.Clear();
                if (top.MeasurementDetails != null)
                {
                    foreach (var m in top.MeasurementDetails)
                    {
                        CurrentMeasurementDetails.Add(m);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadSavedScanHistory error: {ex.Message}");
        }
    }

    /// <summary>
    /// Chuyển hiển thị kết quả sang trạng thái Chờ kiểm tra (READY) khi bắt đầu Live View,
    /// xóa kết quả và chi tiết của lần đo trước để không gây nhầm lẫn cho người vận hành.
    /// </summary>
    public void SetWaitingForInspectionState(string? customSummary = null)
    {
        BigResultStatusText = "READY";
        BigResultBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        BigResultBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
        BigResultForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

        if (!string.IsNullOrWhiteSpace(customSummary))
        {
            LastResultSummary = customSummary;
        }
        else if (!string.IsNullOrWhiteSpace(CurrentProductName) && CurrentProductName != "-")
        {
            LastResultSummary = $"Chờ kiểm tra sản phẩm: {CurrentProductName}. Căn chỉnh camera và bấm 'CHẠY JOB'.";
        }
        else
        {
            LastResultSummary = "Chế độ Live Camera: Sẵn sàng quét mã sản phẩm để bắt đầu đo kiểm.";
        }

        HasLastNgDetails = false;
        LastNgDetails = "";
        CurrentMeasurementDetails?.Clear();
    }

    private void AddHistory(OqcScanHistoryEntry entry)
    {
        ScanHistory.Insert(0, entry);
        while (ScanHistory.Count > 500)
        {
            ScanHistory.RemoveAt(ScanHistory.Count - 1);
        }
        _oqcService.SaveScanHistory(ScanHistory);
    }

    /// <summary>
    /// Kiểm tra xem mật khẩu nhập vào có trùng khớp với mật khẩu xóa dữ liệu lịch sử OQC đã cấu hình không.
    /// Mặc định: "1234"
    /// </summary>
    public bool VerifyDeleteHistoryPassword(string? inputPassword)
    {
        string currentPwd = !string.IsNullOrWhiteSpace(DeleteHistoryPassword) ? DeleteHistoryPassword : "1234";
        return string.Equals(currentPwd, inputPassword, StringComparison.Ordinal);
    }

    /// <summary>
    /// Kiểm tra kết quả kiểm tra có phải là PASS (đạt) hay không (phục vụ lọc PASS, ẩn NG).
    /// Hỗ trợ các định dạng "PASS (OK)", "PASS", "OK" (không phân biệt hoa thường).
    /// Các kết quả khác như "NG (LỖI)", "NG", "FAIL", "LỖI...", "Đang kiểm tra...", "Đã nạp Job" trả về false.
    /// </summary>
    public static bool IsPassResult(string? inspectResult)
    {
        if (string.IsNullOrWhiteSpace(inspectResult)) return false;
        var trimmed = inspectResult.Trim();
        return trimmed.StartsWith("PASS", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("OK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Thực hiện xóa sạch toàn bộ lịch sử quét sau khi đã xác thực mật khẩu.
    /// </summary>
    public void ClearScanHistoryDirect()
    {
        ScanHistory.Clear();
        _oqcService.SaveScanHistory(ScanHistory);

        CurrentMeasurementDetails?.Clear();
        LatestScanEntry = null;
        BigResultStatusText = "READY";
        BigResultBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        BigResultBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
        BigResultForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
        LastResultSummary = "Sẵn sàng quét mã sản phẩm để bắt đầu đo kiểm.";
        HasLastNgDetails = false;
        LastNgDetails = "";

        StatusMessage = "🗑️ Đã xóa toàn bộ lịch sử quét OQC.";
        StatusBrush = Brushes.Gray;
    }

    private void ExecuteClearHistory()
    {
        if (ScanHistory.Count == 0) return;
        if (MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ lịch sử quét mã không?", "Xác Nhận Xóa Lịch Sử", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ClearScanHistoryDirect();
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Trạng thái "Đã nạp Job" = Job đã nạp nhưng CHƯA từng được kiểm tra.
    // Các dòng này nằm mãi trong Lịch sử quét mã OQC => cần nút "Đóng Job" để dọn.
    // ────────────────────────────────────────────────────────────────────────
    private const string PendingJobResultText = "Đã nạp Job";

    /// <summary>True nếu dòng lịch sử đang ở trạng thái "mới nạp Job nhưng chưa kiểm tra".</summary>
    public static bool IsPendingJobEntry(OqcScanHistoryEntry? entry)
    {
        return entry is not null
            && !string.IsNullOrWhiteSpace(entry.InspectResult)
            && entry.InspectResult.Trim().Equals(PendingJobResultText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lọc ra các dòng lịch sử đang ở trạng thái "Đã nạp Job" (thuần logic, không phụ thuộc UI/DB).
    /// </summary>
    public static List<OqcScanHistoryEntry> FindPendingJobEntries(IEnumerable<OqcScanHistoryEntry>? history)
    {
        return history is null
            ? new List<OqcScanHistoryEntry>()
            : history.Where(IsPendingJobEntry).ToList();
    }

    /// <summary>
    /// Xóa các dòng chỉ định khỏi danh sách lịch sử (thuần logic). Trả về số dòng đã xóa thực tế.
    /// </summary>
    public static int RemoveHistoryEntries(
        IList<OqcScanHistoryEntry>? history,
        IEnumerable<OqcScanHistoryEntry>? targets)
    {
        if (history is null || targets is null) return 0;

        var toRemove = targets
            .Where(t => t is not null && history.Contains(t))
            .Distinct()
            .ToList();

        foreach (var entry in toRemove)
        {
            history.Remove(entry);
        }

        return toRemove.Count;
    }

    /// <summary>
    /// Nút "Đóng Job" trên tab OQC Scanner: đóng Job đang nạp và XÓA các dòng lịch sử
    /// <summary>
    /// Đóng Job đang nạp, hủy phiên làm việc, xóa các dòng pending và xóa sạch ô nhập mã.
    /// </summary>
    public void ExecuteCloseLoadedJob(bool askConfirmation = true)
    {
        var pendingEntries = FindPendingJobEntries(ScanHistory);

        if (askConfirmation)
        {
            var confirmMessage = pendingEntries.Count > 0
                ? $"Đóng Job đang nạp và xóa {pendingEntries.Count} dòng lịch sử ở trạng thái \"{PendingJobResultText}\"?\n\n" +
                  "Các dòng này sẽ bị xóa khỏi 'Lịch sử quét mã OQC'."
                : "Đóng Job đang nạp?\n\n" +
                  $"(Hiện không có dòng lịch sử nào ở trạng thái \"{PendingJobResultText}\" để xóa.)";

            if (MessageBox.Show(confirmMessage, "Xác Nhận Đóng Job", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (pendingEntries.Count > 0)
        {
            RemoveHistoryEntries(ScanHistory, pendingEntries);
            _oqcService.SaveScanHistory(ScanHistory);

            // Nếu dòng đang hiển thị ở Big Result bị xóa thì chuyển sang dòng mới nhất còn lại.
            if (LatestScanEntry is not null && pendingEntries.Contains(LatestScanEntry))
            {
                LatestScanEntry = ScanHistory.FirstOrDefault();
            }
        }

        // Giải phóng Job khỏi OQC Scanner (không còn Job nào được nạp).
        IsJobLoadedFromManager = false;
        CurrentJobFilePath = "Chưa có Job";
        CurrentProductName = "-";
        CurrentJobTestedCount = 0;
        _lastScannedProcessedCode = null;
        _lastScannedRawCode = null;
        CurrentSessionProductCode = "";

        // Xóa sạch ô nhập mã ScannedCode trên TextBox theo yêu cầu
        ScannedCode = "";

        SetWaitingForInspectionState("Sẵn sàng quét mã sản phẩm để bắt đầu đo kiểm.");

        StatusMessage = pendingEntries.Count > 0
            ? $"🔒 Đã đóng Job và xóa {pendingEntries.Count} dòng lịch sử \"{PendingJobResultText}\"."
            : "🔒 Đã đóng Job đang nạp và xóa ô nhập mã.";
        StatusBrush = Brushes.Gray;

        OnPropertyChanged(nameof(HasLoadedJob));
        OnPropertyChanged(nameof(ScanButtonText));
        OnPropertyChanged(nameof(PreviewHeaderTitle));
        OnPropertyChanged(nameof(LiveToggleButtonText));

        TriggerFocusAndSelectInput();
    }

    /// <summary>
    /// Nút "Đóng Job" trên tab OQC Scanner: đóng Job đang nạp và XÓA các dòng lịch sử
    /// còn nằm ở trạng thái "Đã nạp Job" (nạp Job nhưng không bao giờ được kiểm tra).
    /// </summary>
    [RelayCommand]
    public void CloseLoadedJob()
    {
        ExecuteCloseLoadedJob(askConfirmation: true);
    }

    /// <summary>
    /// Phím tắt ESC: Đóng Job (không popup hỏi xác nhận để công nhân thao tác 1 chạm nhanh gọn)
    /// và xóa sạch ô nhập mã Textbox (kể cả khi chưa nạp Job nhưng ô nhập đang có ký tự gõ dở).
    /// </summary>
    [RelayCommand]
    public void EscapeCloseJobAndClearText()
    {
        if (HasLoadedJob)
        {
            ExecuteCloseLoadedJob(askConfirmation: false);
        }
        else
        {
            ScannedCode = "";
            StatusMessage = "Sẵn sàng quét mã QR/Barcode sản phẩm.";
            StatusBrush = Brushes.Gray;
        }

        TriggerFocusAndSelectInput();
    }

    /// <summary>
    /// Xóa CÁC dòng lịch sử quét được chọn (dùng cho nút xóa từng dòng / xóa dòng đã chọn
    /// trong cửa sổ "Lịch Sử Quét Mã OQC"). Trả về số dòng đã xóa.
    /// </summary>
    public int DeleteHistoryEntries(IEnumerable<OqcScanHistoryEntry>? entries, bool askConfirmation = true)
    {
        var targets = entries?
            .Where(e => e is not null && ScanHistory.Contains(e))
            .Distinct()
            .ToList() ?? new List<OqcScanHistoryEntry>();

        if (targets.Count == 0) return 0;

        if (askConfirmation)
        {
            var message = targets.Count == 1
                ? $"Xóa dòng lịch sử quét của mã '{targets[0].ScannedCode}' ({targets[0].InspectResult})?\n\nHành động này không thể hoàn tác."
                : $"Xóa {targets.Count} dòng lịch sử quét đã chọn?\n\nHành động này không thể hoàn tác.";

            if (MessageBox.Show(message, "Xác Nhận Xóa Dòng Lịch Sử", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return 0;
            }
        }

        var removed = RemoveHistoryEntries(ScanHistory, targets);
        if (removed == 0) return 0;

        _oqcService.SaveScanHistory(ScanHistory);

        // Nếu dòng đang hiển thị ở Big Result bị xóa thì chuyển sang dòng mới nhất còn lại.
        if (LatestScanEntry is not null && targets.Contains(LatestScanEntry))
        {
            LatestScanEntry = ScanHistory.FirstOrDefault();
        }

        StatusMessage = $"🗑️ Đã xóa {removed} dòng lịch sử quét OQC.";
        StatusBrush = Brushes.Gray;

        return removed;
    }

    private static Views.OQC.OqcScanDetailDialog? _scanDetailDialogInstance;
    private static Views.OQC.OqcScanHistoryWindow? _scanHistoryWindowInstance;
    private static Views.OQC.OqcSettingsDialog? _settingsDialogInstance;
    private static Views.OQC.ProductAssignDialog? _productAssignDialogInstance;

    public void ExecuteOpenScanHistoryWindow()
    {
        if (_scanHistoryWindowInstance != null && _scanHistoryWindowInstance.IsLoaded)
        {
            _scanHistoryWindowInstance.Activate();
            if (_scanHistoryWindowInstance.WindowState == WindowState.Minimized)
                _scanHistoryWindowInstance.WindowState = WindowState.Normal;
            return;
        }

        var mainWin = System.Windows.Application.Current?.MainWindow;
        _scanHistoryWindowInstance = new Views.OQC.OqcScanHistoryWindow(this)
        {
            Owner = mainWin
        };
        _scanHistoryWindowInstance.Closed += (s, e) => _scanHistoryWindowInstance = null;
        _scanHistoryWindowInstance.Show();
    }

    private void ExecuteViewLatestOutputImage()
    {
        if (LatestScanEntry != null)
        {
            ExecuteOpenScanDetail(LatestScanEntry);
        }
        else if (ScanHistory.Count > 0)
        {
            ExecuteOpenScanDetail(ScanHistory[0]);
        }
        else
        {
            MessageBox.Show("Chưa có bản ghi đo nào để xem ảnh.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void ExecuteOpenScanDetail(OqcScanHistoryEntry? entry)
    {
        if (entry == null)
        {
            if (ScanHistory.Count > 0)
            {
                entry = ScanHistory[0];
            }
            else
            {
                MessageBox.Show("Chưa có bản ghi lịch sử nào được chọn.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        if (_scanDetailDialogInstance != null && _scanDetailDialogInstance.IsLoaded)
        {
            _scanDetailDialogInstance.Activate();
            if (_scanDetailDialogInstance.WindowState == WindowState.Minimized)
                _scanDetailDialogInstance.WindowState = WindowState.Normal;
            return;
        }

        var mainWin = System.Windows.Application.Current?.MainWindow;
        _scanDetailDialogInstance = new Views.OQC.OqcScanDetailDialog(entry)
        {
            Owner = mainWin
        };
        _scanDetailDialogInstance.Closed += (s, e) => _scanDetailDialogInstance = null;
        _scanDetailDialogInstance.Show();
    }

    private void ExecuteExportToExcel()
    {
        try
        {
            if (ScanHistory.Count == 0)
            {
                MessageBox.Show("Bảng lịch sử quét hiện đang trống!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Xuất Lịch Sử Quét OQC ra Excel (CSV)",
                Filter = "Tệp CSV (Excel) (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"OqcScanHistory_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Thời Gian,Mã Quét,UUID,Tên Sản Phẩm,Tệp Job,Kết Quả,Lý Do NG / Chi Tiết,Đường Dẫn Ảnh Output");

                foreach (var item in ScanHistory)
                {
                    string timeStr = item.Time.ToString("yyyy-MM-dd HH:mm:ss");
                    string code = EscapeCsv(item.ScannedCode);
                    string uuid = EscapeCsv(item.Uuid);
                    string name = EscapeCsv(item.ProductName);
                    string job = EscapeCsv(item.JobFilePath);
                    string result = EscapeCsv(item.InspectResult);
                    string details = EscapeCsv(item.InspectDetails);
                    string imgPath = EscapeCsv(item.OutputImagePath);

                    sb.AppendLine($"{timeStr},{code},{uuid},{name},{job},{result},{details},{imgPath}");
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
                MessageBox.Show($"✅ Đã xuất {ScanHistory.Count} bản ghi ra tệp Excel thành công!\nĐường dẫn: {sfd.FileName}", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi xuất Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return $"\"{field}\"";
    }

    private void OpenSettingsDialog()
    {
        if (_settingsDialogInstance != null && _settingsDialogInstance.IsLoaded)
        {
            _settingsDialogInstance.Activate();
            if (_settingsDialogInstance.WindowState == WindowState.Minimized)
                _settingsDialogInstance.WindowState = WindowState.Normal;
            return;
        }

        LoadSettingsFromConfig();
        var mainWin = System.Windows.Application.Current?.MainWindow;
        _settingsDialogInstance = new Views.OQC.OqcSettingsDialog(this)
        {
            Owner = mainWin
        };
        _settingsDialogInstance.Closed += (s, e) => _settingsDialogInstance = null;
        _settingsDialogInstance.Show();
    }

    private void OpenProductAssignDialog()
    {
        if (_productAssignDialogInstance != null && _productAssignDialogInstance.IsLoaded)
        {
            _productAssignDialogInstance.Activate();
            if (_productAssignDialogInstance.WindowState == WindowState.Minimized)
                _productAssignDialogInstance.WindowState = WindowState.Normal;
            return;
        }

        AssignJobFilePath = CurrentJobFilePath != "-" ? CurrentJobFilePath : "";
        var mainWin = System.Windows.Application.Current?.MainWindow;
        _productAssignDialogInstance = new Views.OQC.ProductAssignDialog(this)
        {
            Owner = mainWin
        };
        _productAssignDialogInstance.Closed += (s, e) => _productAssignDialogInstance = null;
        _productAssignDialogInstance.Show();
    }

    private static Views.OQC.JobManagerWindow? _jobManagerWindowInstance;

    public void OpenJobManagerWindow()
    {
        if (_jobManagerWindowInstance != null && _jobManagerWindowInstance.IsLoaded)
        {
            _jobManagerWindowInstance.Activate();
            if (_jobManagerWindowInstance.WindowState == WindowState.Minimized)
                _jobManagerWindowInstance.WindowState = WindowState.Normal;
            return;
        }

        var mainWin = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
                      ?? System.Windows.Application.Current?.MainWindow;
        var mainVm = mainWin?.DataContext as MainWindowViewModel;
        var jobVm = new JobManagerViewModel(
            _oqcService,
            _dbManager,
            _remoteServerService,
            _cameraService,
            _toolEditorViewModel.SharedImageContext,
            _toolEditorViewModel,
            mainVm ?? (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel ?? new MainWindowViewModel(_toolEditorViewModel, null!, null!, _inspectionViewModel, this, null!),
            _jobService
        );

        _jobManagerWindowInstance = new Views.OQC.JobManagerWindow(jobVm);
        if (mainWin != null && mainWin != _jobManagerWindowInstance && mainWin.IsLoaded)
        {
            _jobManagerWindowInstance.Owner = mainWin;
        }
        _jobManagerWindowInstance.Closed += (s, e) => _jobManagerWindowInstance = null;
        _jobManagerWindowInstance.Show();
    }

    public async Task ExecuteQuickCaptureAndUploadTeachImageAsync()
    {
        string productCode = !string.IsNullOrWhiteSpace(ScannedCode) ? ScannedCode.Trim() : (!string.IsNullOrWhiteSpace(CurrentProductName) && CurrentProductName != "-" ? CurrentProductName.Trim() : "");
        if (string.IsNullOrWhiteSpace(productCode))
        {
            MessageBox.Show("Vui lòng quét hoặc nhập mã sản phẩm trước khi chụp ảnh mẫu!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        byte[]? imageBytes = null;
        if (_cameraService.IsRunning)
        {
            using var frame = _cameraService.TryGetLatestFrameClone();
            if (frame != null && !frame.Empty())
            {
                imageBytes = frame.ToBytes(".png");
            }
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            using var snap = _toolEditorViewModel.SharedImageContext.GetSnapshot();
            if (snap != null && !snap.Empty())
            {
                imageBytes = snap.ToBytes(".png");
            }
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            MessageBox.Show("Không thể lấy khung hình ảnh từ Camera. Hãy bật Camera trước!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusMessage = $"📸 Đang tải ảnh mẫu cho mã '{productCode}' lên Server...";
        StatusBrush = Brushes.DodgerBlue;

        string safeCode = RemoteServerService.SanitizeIdentifier(productCode);
        string safeName = RemoteServerService.SanitizeIdentifier(CurrentProductName != "-" ? CurrentProductName : null);
        string id = !string.IsNullOrWhiteSpace(safeName) ? $"{safeCode}_{safeName}" : safeCode;
        string fileName = $"teach_{id}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var (uploadOk, fullUrl, relPath, uploadErr) = await _remoteServerService.UploadImageAsync(
            imageBytes, fileName, productCode, _oqcService.Config.ServerApiUrl, CurrentProductName != "-" ? CurrentProductName : null);

        if (!uploadOk)
        {
            StatusMessage = $"❌ Lỗi Upload: {uploadErr}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Lỗi tải ảnh mẫu lên Server:\n{uploadErr}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string teachPathToSave = !string.IsNullOrWhiteSpace(relPath) ? relPath : fullUrl;
        var (assignOk, assignMsg) = await _oqcService.UpdateTeachImagePathAsync(productCode, teachPathToSave, _dbManager);

        if (assignOk)
        {
            StatusMessage = $"✅ Đã tải ảnh mẫu '{fileName}' lên Server và cập nhật CSDL cho '{productCode}'!";
            StatusBrush = Brushes.Green;
            MessageBox.Show($"✅ Đã tải ảnh mẫu lên Server thành công!\nURL: {fullUrl}\nĐã lưu vào CSDL cho mã '{productCode}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            StatusMessage = $"⚠️ Ảnh đã upload nhưng lỗi ghi CSDL: {assignMsg}";
            StatusBrush = Brushes.Orange;
            MessageBox.Show($"Ảnh đã tải lên Server nhưng lỗi cập nhật CSDL:\n{assignMsg}", "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

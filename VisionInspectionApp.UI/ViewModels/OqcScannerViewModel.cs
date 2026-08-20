using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Application.OQC;
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

    [ObservableProperty]
    private string _scannedCode = "";

    [ObservableProperty]
    private string _currentProductName = "-";

    [ObservableProperty]
    private string _currentJobFilePath = "-";

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng quét mã QR/Barcode sản phẩm.";

    [ObservableProperty]
    private Brush _statusBrush = Brushes.Gray;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private bool _autoRunJob = true;

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

    private List<OverlayItem>? _allOverlayItemsCache;
    private bool _isRenderingLiveFrame = false;
    private string _lastScannedRawCode = "";

    public ObservableCollection<OqcScanHistoryEntry> ScanHistory { get; } = new();

    public Action<int>? RequestSwitchTab { get; set; }

    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand ScanFromCameraCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand OpenProductAssignCommand { get; }
    public IRelayCommand ManualOpenJobCommand { get; }
    public IRelayCommand ClearHistoryCommand { get; }
    public IRelayCommand ExportToExcelCommand { get; }
    public IRelayCommand<OqcScanHistoryEntry> OpenScanDetailCommand { get; }
    public IRelayCommand SwitchToToolEditorCommand { get; }
    public IRelayCommand ToggleLiveCameraCommand { get; }

    public string ScanButtonText
    {
        get
        {
            if (!AutoRunJob && !string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
            {
                return UseExternalScanner ? "▶ CHẠY JOB (SPACE)" : "▶ CHẠY JOB";
            }
            return "🔍 QUÉT / TÌM";
        }
    }

    public string CameraScanButtonText => UseExternalScanner
        ? "📷 QUÉT CAMERA"
        : "📷 QUÉT CAMERA (SPACE)";

    public string PreviewHeaderTitle => IsShowingLiveCamera 
        ? "📷 LIVE CAMERA (Căn chỉnh sản phẩm - F5)" 
        : "🖼️ XEM TRƯỚC KẾT QUẢ FINAL (ResultView - Nút F5 để bật Live Cam)";

    public string LiveToggleButtonText => IsShowingLiveCamera 
        ? "🖼️ Xem Kết Quả Final" 
        : "📷 Live Camera (F5)";

    public OqcScannerViewModel(
        IOqcScannerService oqcService,
        IDbManagerService dbManager,
        IJobService jobService,
        InspectionViewModel inspectionViewModel,
        ToolEditorViewModel toolEditorViewModel,
        CameraService cameraService)
    {
        _oqcService = oqcService;
        _dbManager = dbManager;
        _jobService = jobService;
        _inspectionViewModel = inspectionViewModel;
        _toolEditorViewModel = toolEditorViewModel;
        _cameraService = cameraService;

        ScanCommand = new AsyncRelayCommand(ExecuteScanAsync);
        ScanFromCameraCommand = new AsyncRelayCommand(ExecuteScanFromCameraAsync);
        OpenSettingsCommand = new RelayCommand(OpenSettingsDialog);
        OpenProductAssignCommand = new RelayCommand(OpenProductAssignDialog);
        ManualOpenJobCommand = new RelayCommand(ExecuteManualOpenJob);
        ClearHistoryCommand = new RelayCommand(ExecuteClearHistory);
        ExportToExcelCommand = new RelayCommand(ExecuteExportToExcel);
        OpenScanDetailCommand = new RelayCommand<OqcScanHistoryEntry>(ExecuteOpenScanDetail);
        SwitchToToolEditorCommand = new RelayCommand(() => RequestSwitchTab?.Invoke(0));
        ToggleLiveCameraCommand = new RelayCommand(ToggleLiveCamera);

        // Load Scan History from local persistence
        LoadSavedScanHistory();

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
        if (e.PropertyName == nameof(ToolEditorViewModel.FinalOverlayItems) ||
            e.PropertyName == nameof(ToolEditorViewModel.FinalPreviewImage) ||
            e.PropertyName == nameof(ToolEditorViewModel.SelectedNodePreviewImage) ||
            e.PropertyName == nameof(ToolEditorViewModel.SelectedNodeOverlayItems))
        {
            if (!IsShowingLiveCamera)
            {
                RefreshPreviewFromToolEditor();
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
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    LoadingMessage = loadingMsg;
                    IsLoadingPopupVisible = true;
                });
            }
            catch (TaskCanceledException) { }
        });

        try
        {
            await asyncAction();
        }
        finally
        {
            cts.Cancel();
            IsLoadingPopupVisible = false;
        }
    }

    partial void OnAutoRunJobChanged(bool value)
    {
        OnPropertyChanged(nameof(ScanButtonText));
    }

    partial void OnCurrentJobFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(ScanButtonText));
    }

    partial void OnUseExternalScannerChanged(bool value)
    {
        _oqcService.Config.UseExternalScanner = value;
        _oqcService.SaveConfig(_oqcService.Config);
        OnPropertyChanged(nameof(ScanButtonText));
        OnPropertyChanged(nameof(CameraScanButtonText));
    }

    partial void OnIsShowingLiveCameraChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewHeaderTitle));
        OnPropertyChanged(nameof(LiveToggleButtonText));
        if (value)
        {
            OverlayItems = null;
            _ = _cameraService.RequestLiveStreamAsync("OQCScanner", true);
        }
        else
        {
            _ = _cameraService.RequestLiveStreamAsync("OQCScanner", false);
        }
    }

    [RelayCommand]
    public void RunJob()
    {
        if (!string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
        {
            IsShowingLiveCamera = false;
            StatusMessage = $"⌛ Đang chạy kiểm tra Job cho sản phẩm '{CurrentProductName}'...";
            StatusBrush = Brushes.DodgerBlue;

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
        IsShowingLiveCamera = true;
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

    private void OnCameraFrameCaptured(object? sender, Mat frame)
    {
        if (!IsShowingLiveCamera || frame == null || frame.Empty())
        {
            return;
        }

        if (_isRenderingLiveFrame)
        {
            return;
        }

        _isRenderingLiveFrame = true;

        try
        {
            var bitmap = frame.ToBitmapSourceForDisplay(1920, 1080);

            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (IsShowingLiveCamera)
                    {
                        PreviewImage = bitmap;
                        OverlayItems = null;
                    }
                }
                finally
                {
                    _isRenderingLiveFrame = false;
                }
            }));
        }
        catch
        {
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
                // Nhận diện mã thành công trong thời gian timeout
                ScannedCode = result.ProcessedCode;
                StatusMessage = $"📷 Đã đọc mã từ Camera: '{result.ProcessedCode}' (Mã gốc: '{result.RawCode}', Loại: {result.CodeType}). Đang tra DB...";
                StatusBrush = Brushes.DodgerBlue;

                await ExecuteScanInternalAsync();
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
            StatusMessage = $"❌ Lỗi đọc mã từ camera: {ex.Message}";
            StatusBrush = Brushes.Red;
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ExecuteScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        try
        {
            await RunTaskWith1SecLoadingTimeoutAsync(async () =>
            {
                await ExecuteScanInternalAsync();
            }, "⚡ Đang tra cứu cơ sở dữ liệu & nạp Job... Vui lòng chờ!");
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

    private async Task ExecuteScanInternalAsync()
    {
        string rawInput = ScannedCode?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            if (!AutoRunJob && !string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
            {
                RunJob();
                return;
            }

            StatusMessage = "⚠️ Vui lòng nhập hoặc quét mã sản phẩm!";
            StatusBrush = Brushes.Orange;
            return;
        }

        // Áp dụng bộ lọc độ dài và cắt chuỗi cấu hình cho mã nhập/quét từ đầu đọc ngoài
        var (valid, processedCode, rawCode, filterError) = _oqcService.ProcessRawCodeString(rawInput);
        if (!valid)
        {
            StatusMessage = $"❌ Mã quét '{rawCode}' không hợp lệ: {filterError}";
            StatusBrush = Brushes.Red;

            var invalidEntry = new OqcScanHistoryEntry
            {
                Time = DateTime.Now,
                ScannedCode = rawCode,
                ProductName = "Mã không hợp lệ",
                JobFilePath = "-",
                Success = false,
                InspectResult = "LỖI BỘ LỌC MÃ",
                ResultBrushHex = "#D32F2F",
                Message = filterError,
                InspectDetails = $"Mã gốc '{rawCode}' bị loại bởi bộ lọc: {filterError}"
            };
            AddHistory(invalidEntry);
            ScannedCode = "";
            return;
        }

        string code = processedCode;
        _lastScannedRawCode = rawCode;
        StatusMessage = $"🔍 Đang tra cứu cơ sở dữ liệu cho mã '{code}'" + (code != rawCode ? $" (Mã gốc: '{rawCode}')" : "") + "...";
        StatusBrush = Brushes.DodgerBlue;

        string displayProductName = code;
        if (_oqcService.Config.EnableProductNameLookup)
        {
            var (nameFound, resolvedName, _) = await _oqcService.LookupProductNameAsync(code, _dbManager);
            if (nameFound && !string.IsNullOrWhiteSpace(resolvedName))
            {
                displayProductName = resolvedName;
            }
        }

        CurrentProductName = displayProductName;

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
            var (found, jobPath, error) = await _oqcService.LookupJobAsync(code, _dbManager);
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

            System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - [OQC] " + Path.GetFileName(jobPath);

            _toolEditorViewModel.ProductCode = code;

            if (AutoRunJob)
            {
                // Auto Run mode: Load job and run graph automatically
                IsShowingLiveCamera = false;
                StatusMessage = $"📁 Đang nạp tệp Job: '{Path.GetFileName(jobPath)}' và chạy kiểm tra...";
                _toolEditorViewModel.LoadJobFromFile(jobPath, autoRun: true);

                if (_toolEditorViewModel.LastResult != null)
                {
                    await HandleInspectionCompletedAsync(_toolEditorViewModel.LastResult, cfg);
                }
            }
            else
            {
                // Manual Run mode: Load job only, keep Live Camera active for product alignment
                IsShowingLiveCamera = true;
                StatusMessage = $"✅ Đã nạp Job '{Path.GetFileName(jobPath)}' cho mã '{code}'. Căn chỉnh sản phẩm và nhấn '▶ CHẠY JOB' để kiểm tra.";
                StatusBrush = Brushes.DodgerBlue;
                _toolEditorViewModel.LoadJobFromFile(jobPath, autoRun: false);
            }

            OnPropertyChanged(nameof(ScanButtonText));
            OnPropertyChanged(nameof(PreviewHeaderTitle));
            OnPropertyChanged(nameof(LiveToggleButtonText));

            // Auto select code in input field for quick re-scanning
            ScannedCode = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi mở Job: {ex.Message}";
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
        }
    }

    public async Task HandleInspectionCompletedAsync(InspectionResult result, VisionConfig config)
    {
        if (result == null) return;

        IsShowingLiveCamera = false;
        OnPropertyChanged(nameof(PreviewHeaderTitle));
        OnPropertyChanged(nameof(LiveToggleButtonText));

        string rawCode = !string.IsNullOrWhiteSpace(_lastScannedRawCode)
            ? _lastScannedRawCode
            : (!string.IsNullOrWhiteSpace(_toolEditorViewModel.ProductCode) ? _toolEditorViewModel.ProductCode : config?.ProductCode ?? CurrentProductName);

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

        string details = ExtractDetailedReasons(result);
        string statusStr = result.Pass ? "PASS (OK)" : "NG (LỖI)";
        string colorHex = result.Pass ? "#2E7D32" : "#D32F2F";
        Brush statusBrush = result.Pass ? Brushes.ForestGreen : Brushes.Crimson;

        // Always update UI Scan History entry & Refresh Preview Image
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (ScanHistory.Count > 0)
            {
                var entry = ScanHistory.FirstOrDefault(e => string.Equals(e.ScannedCode, rawCode, StringComparison.OrdinalIgnoreCase)) 
                            ?? ScanHistory[0];

                entry.Uuid = uuid;
                entry.InspectResult = statusStr;
                entry.InspectDetails = details;
                entry.ResultBrushHex = colorHex;
                entry.OutputImagePath = outputImagePath;
                entry.MeasurementDetails = measurementDetails;

                StatusMessage = result.Pass
                    ? $"✅ SẢN PHẨM '{productName}' ({rawCode}) -> KẾT QUẢ: PASS (OK)"
                    : $"❌ SẢN PHẨM '{productName}' ({rawCode}) -> KẾT QUẢ: NG! Lý do: {details}";
                StatusBrush = statusBrush;
            }

            _oqcService.SaveScanHistory(ScanHistory);
            RefreshPreviewFromToolEditor();
        });

        // Log result to Database if enabled
        if ((_oqcService.Config.LogResultToDb || _oqcService.Config.LogDetailResultToDb) && config != null)
        {
            await _oqcService.LogInspectionResultAsync(rawCode, uuid, path, result, config, _dbManager, measurementDetails);
        }
    }

    public void RefreshPreviewFromToolEditor()
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            PreviewImage = _toolEditorViewModel.FinalPreviewImage ?? _toolEditorViewModel.SelectedNodePreviewImage;
            var finalOverlays = _toolEditorViewModel.FinalOverlayItems ?? _toolEditorViewModel.SelectedNodeOverlayItems;
            _allOverlayItemsCache = finalOverlays != null ? new List<OverlayItem>(finalOverlays) : new List<OverlayItem>();
            UpdatePreviewOverlays();
        });
    }

    partial void OnShowResultOverlayChanged(bool value) => UpdatePreviewOverlays();
    partial void OnShowRoisChanged(bool value) => UpdatePreviewOverlays();

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
                _toolEditorViewModel.LoadJobFromFile(dialog.FileName);

                var cfg = _jobService.LoadJob(dialog.FileName, out var tempDir);
                _inspectionViewModel.CurrentJobFilePath = dialog.FileName;
                _inspectionViewModel.CurrentTempWorkingDir = tempDir;
                _inspectionViewModel.SetConfig(cfg);

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
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadSavedScanHistory error: {ex.Message}");
        }
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

    private void ExecuteClearHistory()
    {
        if (ScanHistory.Count == 0) return;
        if (MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ lịch sử quét mã không?", "Xác Nhận Xóa Lịch Sử", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ScanHistory.Clear();
            _oqcService.SaveScanHistory(ScanHistory);
            StatusMessage = "🗑️ Đã xóa toàn bộ lịch sử quét OQC.";
            StatusBrush = Brushes.Gray;
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

        var dlg = new Views.OQC.OqcScanDetailDialog(entry)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dlg.ShowDialog();
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
        LoadSettingsFromConfig();
        var dlg = new Views.OQC.OqcSettingsDialog(this)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dlg.ShowDialog();
    }

    private void OpenProductAssignDialog()
    {
        AssignJobFilePath = CurrentJobFilePath != "-" ? CurrentJobFilePath : "";
        var dlg = new Views.OQC.ProductAssignDialog(this)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dlg.ShowDialog();
    }
}

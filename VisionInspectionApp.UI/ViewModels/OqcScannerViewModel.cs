using System;
using System.Collections.ObjectModel;
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
    public IRelayCommand SwitchToToolEditorCommand { get; }
    public IRelayCommand ToggleLiveCameraCommand { get; }

    public string ScanButtonText
    {
        get
        {
            if (!AutoRunJob && !string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
            {
                return "▶ CHẠY JOB";
            }
            return "🔍 QUÉT / TÌM";
        }
    }

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
        ClearHistoryCommand = new RelayCommand(() => ScanHistory.Clear());
        SwitchToToolEditorCommand = new RelayCommand(() => RequestSwitchTab?.Invoke(0));
        ToggleLiveCameraCommand = new RelayCommand(ToggleLiveCamera);

        _inspectionViewModel.InspectionCompletedAsync += HandleInspectionCompletedAsync;

        // Subscribe to CameraService frame stream for live alignment preview
        _cameraService.FrameCaptured += OnCameraFrameCaptured;
        if (!_cameraService.IsRunning)
        {
            _ = _cameraService.StartSavedCameraAsync();
        }

        // Initialize Settings properties
        InitSettingsProperties();
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

    [RelayCommand]
    private void EnableLiveCamera()
    {
        IsShowingLiveCamera = true;
        OverlayItems = null; // Clear inspection overlays during live stream
        if (!_cameraService.IsRunning)
        {
            _ = _cameraService.StartSavedCameraAsync();
        }
        OnPropertyChanged(nameof(PreviewHeaderTitle));
        OnPropertyChanged(nameof(LiveToggleButtonText));
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
            OnPropertyChanged(nameof(PreviewHeaderTitle));
            OnPropertyChanged(nameof(LiveToggleButtonText));
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
            using var frameClone = frame.Clone();
            var bitmap = frameClone.ToBitmapSourceSafe();

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
        StatusMessage = "📷 Đang chụp ảnh và nhận diện mã QR/Barcode từ Camera...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            await RunTaskWith1SecLoadingTimeoutAsync(async () =>
            {
                using var snapshot = await _cameraService.CaptureSnapshotAsync();
                if (snapshot == null || snapshot.Empty())
                {
                    StatusMessage = "❌ Không lấy được hình ảnh từ Camera! Vui lòng kiểm tra kết nối Camera.";
                    StatusBrush = Brushes.Red;
                    return;
                }

                var result = await Task.Run(() => _oqcService.DecodeCodeFromImage(snapshot, _oqcService.Config));
                if (!result.Success || string.IsNullOrWhiteSpace(result.ProcessedCode))
                {
                    StatusMessage = $"❌ {result.ErrorMessage}";
                    StatusBrush = Brushes.Orange;
                    return;
                }

                ScannedCode = result.ProcessedCode;
                StatusMessage = $"📷 Đã đọc mã từ Camera: '{result.ProcessedCode}' (Mã gốc: '{result.RawCode}', Loại: {result.CodeType}). Đang tra DB...";
                StatusBrush = Brushes.DodgerBlue;

                await ExecuteScanInternalAsync();
            }, "🔍 Đang phân tích & nhận diện mã 360° đa tầng... Vui lòng chờ trong giây lát!");
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
        string code = ScannedCode?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code))
        {
            if (!AutoRunJob && !string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" && CurrentJobFilePath != "Chưa có Job")
            {
                IsShowingLiveCamera = false;
                OnPropertyChanged(nameof(PreviewHeaderTitle));
                OnPropertyChanged(nameof(LiveToggleButtonText));
                StatusMessage = $"⌛ Đang chạy kiểm tra cho sản phẩm '{CurrentProductName}'...";
                StatusBrush = Brushes.DodgerBlue;

                _toolEditorViewModel.OnRunOnceClicked();
                return;
            }

            StatusMessage = "⚠️ Vui lòng nhập hoặc quét mã sản phẩm!";
            StatusBrush = Brushes.Orange;
            return;
        }

        _lastScannedRawCode = code;
        StatusMessage = $"🔍 Đang tra cứu cơ sở dữ liệu cho mã '{code}'...";
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

                entry.InspectResult = statusStr;
                entry.InspectDetails = details;
                entry.ResultBrushHex = colorHex;

                StatusMessage = result.Pass
                    ? $"✅ SẢN PHẨM '{productName}' ({rawCode}) -> KẾT QUẢ: PASS (OK)"
                    : $"❌ SẢN PHẨM '{productName}' ({rawCode}) -> KẾT QUẢ: NG! Lý do: {details}";
                StatusBrush = statusBrush;
            }

            RefreshPreviewFromToolEditor();
        });

        // Log result to Database if enabled
        if (_oqcService.Config.LogResultToDb && config != null)
        {
            await _oqcService.LogInspectionResultAsync(rawCode, path, result, config, _dbManager);
        }
    }

    public void RefreshPreviewFromToolEditor()
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            PreviewImage = _toolEditorViewModel.FinalPreviewImage ?? _toolEditorViewModel.SelectedNodePreviewImage;
            var finalOverlays = _toolEditorViewModel.FinalOverlayItems;
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

    private void AddHistory(OqcScanHistoryEntry entry)
    {
        ScanHistory.Insert(0, entry);
        while (ScanHistory.Count > 50)
        {
            ScanHistory.RemoveAt(ScanHistory.Count - 1);
        }
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

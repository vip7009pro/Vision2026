using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class BatchProcessingViewModel : ObservableObject
{
    private readonly BatchProcessingService _batchProcessingService;
    private readonly IConfigService _configService;
    private CancellationTokenSource? _cancellationTokenSource;
    private List<(string FileName, List<InspectionResult> Results)> _batchResults = new();

    public BatchProcessingViewModel(
        BatchProcessingService batchProcessingService,
        IConfigService configService)
    {
        _batchProcessingService = batchProcessingService;
        _configService = configService;

        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        StartBatchCommand = new AsyncRelayCommand(StartBatchAsync);
        CancelBatchCommand = new RelayCommand(CancelBatch);
        LoadConfigCommand = new RelayCommand(LoadConfig);
        RefreshConfigsCommand = new RelayCommand(RefreshConfigs);
        ExportResultsCommand = new AsyncRelayCommand(ExportResultsAsync);

        AvailableConfigs = new ObservableCollection<string>();
        ProcessingLog = new ObservableCollection<string>();
        ResultSummary = new ObservableCollection<BatchResultRow>();

        // Subscribe batch processing events
        _batchProcessingService.ImageProcessed += OnImageProcessed;
        _batchProcessingService.ProcessingCompleted += OnProcessingCompleted;
        _batchProcessingService.ErrorOccurred += OnBatchError;

        RefreshConfigs();
    }

    public sealed record BatchResultRow(
        string FileName,
        int TotalMeasurements,
        int PassCount,
        string Status);

    [ObservableProperty]
    private string _productCode = "ProductA";

    [ObservableProperty]
    private string? _selectedConfig;

    partial void OnSelectedConfigChanged(string? value)
    {
        if (value != null)
        {
            LoadConfig();
        }
    }

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private int _progress = 0;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _fileInfo = "Chưa chọn folder";

    [ObservableProperty]
    private int _totalFiles = 0;

    [ObservableProperty]
    private int _totalPass = 0;

    [ObservableProperty]
    private int _totalFail = 0;

    public ObservableCollection<string> AvailableConfigs { get; }

    public ObservableCollection<string> ProcessingLog { get; }

    public ObservableCollection<BatchResultRow> ResultSummary { get; }

    public ICommand BrowseFolderCommand { get; }

    public ICommand StartBatchCommand { get; }

    public ICommand CancelBatchCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand RefreshConfigsCommand { get; }

    public ICommand ExportResultsCommand { get; }

    /// <summary>
    /// Chọn folder
    /// </summary>
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn folder chứa ảnh cần xử lý"
        };

        if (dialog.ShowDialog() == true)
        {
            FolderPath = dialog.FolderName;

            // Đếm số ảnh
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
            var imageCount = Directory.GetFiles(FolderPath)
                .Count(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()));

            TotalFiles = imageCount;
            FileInfo = $"{imageCount} ảnh found in {FolderPath}";
        }
    }

    /// <summary>
    /// Bắt đầu batch processing
    /// </summary>
    private async Task StartBatchAsync()
    {
        if (string.IsNullOrEmpty(FolderPath))
        {
            StatusMessage = "Vui lòng chọn folder";
            return;
        }

        if (SelectedConfig == null)
        {
            StatusMessage = "Vui lòng chọn config";
            return;
        }

        IsProcessing = true;
        Progress = 0;
        ProcessingLog.Clear();
        ResultSummary.Clear();
        _batchResults.Clear();
        TotalPass = 0;
        TotalFail = 0;

        AddLog($"------- Batch Processing Started -------");
        AddLog($"Folder: {FolderPath}");
        AddLog($"Config: {SelectedConfig}");
        AddLog($"Total files: {TotalFiles}");
        AddLog("");

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            await _batchProcessingService.ProcessBatchAsync(
                FolderPath,
                ProductCode,
                _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi: {ex.Message}";
        }
    }

    /// <summary>
    /// Hủy batch processing
    /// </summary>
    private void CancelBatch()
    {
        _cancellationTokenSource?.Cancel();
        IsProcessing = false;
        StatusMessage = "Batch processing cancelled";
    }

    /// <summary>
    /// Xử lý khi ảnh được xử lý xong
    /// </summary>
    private void OnImageProcessed(object? sender, (string FileName, Mat Image, List<InspectionResult> Results, int Progress) data)
    {
        Progress = data.Progress;

        var (fileName, image, results, progress) = data;

        // Đếm pass/fail
        int passCount = results.Count(r => r.Pass);
        int failCount = results.Count - passCount;

        if (failCount > 0)
        {
            TotalFail += failCount;
        }
        if (passCount > 0)
        {
            TotalPass += passCount;
        }

        // Add log
        var status = failCount == 0 ? "✓ PASS" : "✗ FAIL";
        AddLog($"[{progress}%] {fileName}: {passCount}/{results.Count} measurements passed {status}");

        // Add to result summary
        ResultSummary.Add(new BatchResultRow(
            fileName,
            results.Count,
            passCount,
            status));

        // Store results
        _batchResults.Add((fileName, results));

        // Cleanup
        image.Dispose();

        StatusMessage = $"Processing... {progress}% ({ResultSummary.Count}/{TotalFiles})";
    }

    /// <summary>
    /// Xử lý khi batch processing hoàn thành
    /// </summary>
    private void OnProcessingCompleted(object? sender, string message)
    {
        AddLog("");
        AddLog(message);
        AddLog($"Statistics: {TotalPass} PASS, {TotalFail} FAIL");
        AddLog($"------- Batch Processing Completed -------");

        IsProcessing = false;
        StatusMessage = "Batch processing completed";
    }

    /// <summary>
    /// Xử lý lỗi batch processing
    /// </summary>
    private void OnBatchError(object? sender, string error)
    {
        AddLog($"[ERROR] {error}");
        StatusMessage = error;
    }

    /// <summary>
    /// Load config
    /// </summary>
    private void LoadConfig()
    {
        if (SelectedConfig == null)
            return;

        try
        {
            var config = _configService.LoadConfig(SelectedConfig);
            StatusMessage = config != null ? $"Config loaded: {SelectedConfig}" : "Failed to load config";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi load config: {ex.Message}";
        }
    }

    /// <summary>
    /// Refresh danh sách config
    /// </summary>
    private void RefreshConfigs()
    {
        AvailableConfigs.Clear();
        try
        {
            var configRoot = "configs";
            if (Directory.Exists(configRoot))
            {
                foreach (var file in Directory.EnumerateFiles(configRoot, "*.json"))
                {
                    var productCode = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(productCode))
                    {
                        AvailableConfigs.Add(productCode);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi load config: {ex.Message}";
        }
    }

    /// <summary>
    /// Export kết quả batch processing
    /// </summary>
    private async Task ExportResultsAsync()
    {
        if (_batchResults.Count == 0)
        {
            StatusMessage = "Không có kết quả để export";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Batch Results",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = $"batch_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                AddLog($"Exporting results to {dialog.FileName}...");
                await _batchProcessingService.ExportResultsAsync(dialog.FileName, _batchResults);
                StatusMessage = "Results exported successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export error: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Thêm log message
    /// </summary>
    private void AddLog(string message)
    {
        ProcessingLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

        // Giới hạn log entries (keep last 1000)
        if (ProcessingLog.Count > 1000)
        {
            ProcessingLog.RemoveAt(0);
        }
    }
}

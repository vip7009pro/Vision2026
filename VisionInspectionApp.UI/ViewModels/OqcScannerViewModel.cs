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

namespace VisionInspectionApp.UI.ViewModels;

public partial class OqcScannerViewModel : ObservableObject
{
    private readonly IOqcScannerService _oqcService;
    private readonly IDbManagerService _dbManager;
    private readonly IJobService _jobService;
    private readonly InspectionViewModel _inspectionViewModel;
    private readonly ToolEditorViewModel _toolEditorViewModel;

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

    public ObservableCollection<OqcScanHistoryEntry> ScanHistory { get; } = new();

    public Action<int>? RequestSwitchTab { get; set; }

    public IAsyncRelayCommand ScanCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand OpenProductAssignCommand { get; }
    public IRelayCommand ManualOpenJobCommand { get; }
    public IRelayCommand ClearHistoryCommand { get; }
    public IRelayCommand SwitchToToolEditorCommand { get; }

    public OqcScannerViewModel(
        IOqcScannerService oqcService,
        IDbManagerService dbManager,
        IJobService jobService,
        InspectionViewModel inspectionViewModel,
        ToolEditorViewModel toolEditorViewModel)
    {
        _oqcService = oqcService;
        _dbManager = dbManager;
        _jobService = jobService;
        _inspectionViewModel = inspectionViewModel;
        _toolEditorViewModel = toolEditorViewModel;

        ScanCommand = new AsyncRelayCommand(ExecuteScanAsync);
        OpenSettingsCommand = new RelayCommand(OpenSettingsDialog);
        OpenProductAssignCommand = new RelayCommand(OpenProductAssignDialog);
        ManualOpenJobCommand = new RelayCommand(ExecuteManualOpenJob);
        ClearHistoryCommand = new RelayCommand(() => ScanHistory.Clear());
        SwitchToToolEditorCommand = new RelayCommand(() => RequestSwitchTab?.Invoke(0));

        _inspectionViewModel.InspectionCompletedAsync += HandleInspectionCompletedAsync;

        // Initialize Settings properties
        InitSettingsProperties();
    }

    private async Task ExecuteScanAsync()
    {
        string code = ScannedCode?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code))
        {
            StatusMessage = "⚠️ Vui lòng nhập hoặc quét mã sản phẩm!";
            StatusBrush = Brushes.Orange;
            return;
        }

        IsScanning = true;
        StatusMessage = $"🔍 Đang tra cứu cơ sở dữ liệu cho mã '{code}'...";
        StatusBrush = Brushes.DodgerBlue;

        CurrentProductName = code;

        var historyEntry = new OqcScanHistoryEntry
        {
            Time = DateTime.Now,
            ScannedCode = code,
            InspectResult = "Đang kiểm tra...",
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

            // Add history entry to UI before executing inspection so it can be updated by event
            AddHistory(historyEntry);

            // Load job into Tool Editor & Inspection Engine (which triggers OnRunOnceClicked)
            StatusMessage = $"📁 Đang nạp tệp Job: '{Path.GetFileName(jobPath)}' vào Tool Editor...";
            
            _toolEditorViewModel.LoadJobFromFile(jobPath);

            var cfg = _jobService.LoadJob(jobPath, out var tempDir);
            _inspectionViewModel.CurrentJobFilePath = jobPath;
            _inspectionViewModel.CurrentTempWorkingDir = tempDir;
            _inspectionViewModel.ProductCode = code;
            _inspectionViewModel.SetConfig(cfg);

            System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - [OQC] " + Path.GetFileName(jobPath);

            // Check if ToolEditor produced an inspection result
            if (_toolEditorViewModel.LastResult != null)
            {
                await HandleInspectionCompletedAsync(_toolEditorViewModel.LastResult, cfg);
            }
            else
            {
                StatusMessage = $"✅ Đã nạp Job '{Path.GetFileName(jobPath)}' cho mã '{code}'. Sẵn sàng kiểm tra.";
                StatusBrush = Brushes.Green;
            }

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

        string code = CurrentProductName;
        string path = CurrentJobFilePath;

        string details = ExtractDetailedReasons(result);
        string statusStr = result.Pass ? "PASS (OK)" : "NG (LỖI)";
        string colorHex = result.Pass ? "#2E7D32" : "#D32F2F";
        Brush statusBrush = result.Pass ? Brushes.ForestGreen : Brushes.Crimson;

        // Always update UI Scan History entry
        if (ScanHistory.Count > 0)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var entry = ScanHistory.FirstOrDefault(e => string.Equals(e.ScannedCode, code, StringComparison.OrdinalIgnoreCase)) 
                            ?? ScanHistory[0];

                entry.InspectResult = statusStr;
                entry.InspectDetails = details;
                entry.ResultBrushHex = colorHex;

                StatusMessage = result.Pass
                    ? $"✅ SẢN PHẨM '{code}' -> KẾT QUẢ: PASS (OK)"
                    : $"❌ SẢN PHẨM '{code}' -> KẾT QUẢ: NG! Lý do: {details}";
                StatusBrush = statusBrush;
            });
        }

        // Log result to Database if enabled
        if (_oqcService.Config.LogResultToDb && config != null)
        {
            await _oqcService.LogInspectionResultAsync(code, path, result, config, _dbManager);
        }
    }

    private static string ExtractDetailedReasons(InspectionResult result)
    {
        if (result == null) return "Chưa có kết quả";
        if (result.Pass) return "Tất cả công cụ kiểm tra đạt yêu cầu (PASS).";

        var reasons = new System.Collections.Generic.List<string>();

        if (result.Origin != null && !result.Origin.Pass)
        {
            reasons.Add($"Origin NG (Score: {result.Origin.Score:F3})");
        }

        foreach (var d in result.Distances)
        {
            if (!d.Pass)
            {
                reasons.Add($"Distance [{d.Name}] NG: {d.Value:F3}mm (Tiêu chuẩn: {d.Nominal:F3}, Dung sai: +{d.TolPlus}/-{d.TolMinus})");
            }
        }

        foreach (var l2l in result.LineToLineDistances)
        {
            if (!l2l.Pass)
            {
                reasons.Add($"LineToLine [{l2l.Name}] NG: {l2l.Value:F3}mm (Tiêu chuẩn: {l2l.Nominal:F3})");
            }
        }

        foreach (var p2l in result.PointToLineDistances)
        {
            if (!p2l.Pass)
            {
                reasons.Add($"PointToLine [{p2l.Name}] NG: {p2l.Value:F3}mm (Tiêu chuẩn: {p2l.Nominal:F3})");
            }
        }

        foreach (var seg in result.SegmentLineDistances)
        {
            if (!seg.Pass)
            {
                reasons.Add($"SegmentLine [{seg.Name}] NG: {seg.Value:F3}mm");
            }
        }

        foreach (var ang in result.Angles)
        {
            if (!ang.Pass)
            {
                reasons.Add($"Angle [{ang.Name}] NG: {ang.ValueDeg:F2}° (Tiêu chuẩn: {ang.Nominal:F2}°)");
            }
        }

        foreach (var ep in result.EdgePairs)
        {
            if (!ep.Pass)
            {
                reasons.Add($"EdgePair [{ep.Name}] NG: {ep.Value:F3}mm");
            }
        }

        foreach (var epd in result.EdgePairDetections)
        {
            if (!epd.Pass)
            {
                reasons.Add($"EdgePairDetect [{epd.Name}] NG: {epd.Value:F3}mm");
            }
        }

        foreach (var dia in result.Diameters)
        {
            if (!dia.Pass)
            {
                reasons.Add($"Diameter [{dia.Name}] NG: {dia.Value:F3}mm");
            }
        }

        foreach (var c in result.Conditions)
        {
            if (!c.Pass)
            {
                reasons.Add($"Condition [{c.Name}] NG ({c.Expression})");
            }
        }

        foreach (var sc in result.SurfaceCompares)
        {
            if (!sc.Pass)
            {
                reasons.Add($"Ngoại quan [{sc.Name}] NG ({sc.Count} vết lỗi)");
            }
        }

        foreach (var cc in result.ContourCompares)
        {
            if (!cc.Pass)
            {
                reasons.Add($"ContourCompare [{cc.Name}] NG (Score: {cc.MatchScore:F3}, MaxDist: {cc.MaxDistancePx:F1}px)");
            }
        }

        foreach (var cd in result.CodeDetections)
        {
            if (!cd.Found)
            {
                reasons.Add($"CodeDetect [{cd.Name}] NG (Không đọc được mã)");
            }
        }

        if (reasons.Count == 0) return "NG (Không đạt tiêu chí kiểm tra chung)";
        return string.Join(" | ", reasons);
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

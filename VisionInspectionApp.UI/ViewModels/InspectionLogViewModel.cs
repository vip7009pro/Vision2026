using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels;

public partial class InspectionLogViewModel : ObservableObject
{
    private readonly IInspectionLogService _logService;
    private List<InspectionPartRecord> _allCurrentSessionParts = new();

    public InspectionLogViewModel(IInspectionLogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));

        _logService.SessionUpdated += OnSessionUpdated;
        _logService.PartLogged += OnPartLogged;

        LoadSessionsCommand = new AsyncRelayCommand(LoadSessionsAsync);
        ExportExcelCommand = new RelayCommand(ExportExcel);
        ExportCsvCommand = new RelayCommand(ExportCsv);
        ExportJsonCommand = new RelayCommand(ExportJson);
        ToggleCpkPanelCommand = new RelayCommand(() => IsCpkPanelVisible = !IsCpkPanelVisible);
        DeleteSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync);
        ClearAllCommand = new AsyncRelayCommand(ClearAllHistoryAsync);

        _ = LoadSessionsAsync();
    }

    // ─── Observable Properties ───────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<InspectionSessionRecord> _sessions = new();

    [ObservableProperty]
    private InspectionSessionRecord? _selectedSession;

    [ObservableProperty]
    private string _searchSessionText = "";

    [ObservableProperty]
    private ObservableCollection<InspectionPartRecord> _parts = new();

    [ObservableProperty]
    private InspectionPartRecord? _selectedPart;

    [ObservableProperty]
    private ObservableCollection<InspectionItemMeasurement> _measurements = new();

    [ObservableProperty]
    private bool _showOnlyNg;

    [ObservableProperty]
    private string _searchItemText = "";

    [ObservableProperty]
    private bool _isCpkPanelVisible = true;

    [ObservableProperty]
    private ObservableCollection<string> _availableMeasurements = new();

    [ObservableProperty]
    private string? _selectedMeasurement;

    [ObservableProperty]
    private int _subgroupSizeN = 32;

    [ObservableProperty]
    private SpcAnalysisResult? _spcResult;

    [ObservableProperty]
    private string _spcHeaderProduct = "Sản phẩm: -";

    [ObservableProperty]
    private string _spcHeaderMaterial = "Vật liệu: -";

    [ObservableProperty]
    private string _spcHeaderItem = "Hạng mục test: -";

    [ObservableProperty]
    private string _spcHeaderTotalRows = "Total: 0 rows";

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng.";

    // ─── Chart Geometries for Canvas Binding ─────────────────────────

    [ObservableProperty]
    private ObservableCollection<HistogramBarVisual> _histogramBars = new();

    [ObservableProperty]
    private PointCollection _histogramGaussPoints = new();

    [ObservableProperty]
    private PointCollection _xbarPoints = new();

    [ObservableProperty]
    private double _xbarClY = 50;

    [ObservableProperty]
    private double _xbarUclY = 20;

    [ObservableProperty]
    private double _xbarLclY = 80;

    [ObservableProperty]
    private PointCollection _rChartPoints = new();

    [ObservableProperty]
    private double _rClY = 50;

    [ObservableProperty]
    private double _rUclY = 20;

    [ObservableProperty]
    private double _rLclY = 80;

    [ObservableProperty]
    private PointCollection _cpkTrendPoints = new();

    [ObservableProperty]
    private double _cpk133Y = 40;

    [ObservableProperty]
    private double _cpk167Y = 20;

    // Chart Axis Labels
    [ObservableProperty] private string _histXMinLabel = "0.0";
    [ObservableProperty] private string _histXMaxLabel = "0.0";
    [ObservableProperty] private string _histYMaxLabel = "0";

    [ObservableProperty] private string _xbarYMaxLabel = "0.0";
    [ObservableProperty] private string _xbarYMinLabel = "0.0";
    [ObservableProperty] private string _xbarXMaxLabel = "0";

    [ObservableProperty] private string _rYMaxLabel = "0.0";
    [ObservableProperty] private string _rYMinLabel = "0.0";
    [ObservableProperty] private string _rXMaxLabel = "0";

    [ObservableProperty] private string _cpkYMaxLabel = "2.0";
    [ObservableProperty] private string _cpkYMinLabel = "0.0";
    [ObservableProperty] private string _cpkXMaxLabel = "0";

    // ─── Commands ────────────────────────────────────────────────────

    public IAsyncRelayCommand LoadSessionsCommand { get; }
    public IRelayCommand ExportExcelCommand { get; }
    public IRelayCommand ExportCsvCommand { get; }
    public IRelayCommand ExportJsonCommand { get; }
    public IRelayCommand ToggleCpkPanelCommand { get; }
    public IAsyncRelayCommand DeleteSessionCommand { get; }
    public IAsyncRelayCommand ClearAllCommand { get; }

    // ─── Change Handlers ─────────────────────────────────────────────

    partial void OnSelectedSessionChanged(InspectionSessionRecord? value)
    {
        if (value != null)
        {
            _ = LoadPartsForSessionAsync(value.Id);
        }
        else
        {
            _allCurrentSessionParts.Clear();
            Parts.Clear();
            Measurements.Clear();
            AvailableMeasurements.Clear();
            SelectedMeasurement = null;
            SpcResult = null;
            ClearCharts();
        }
    }

    partial void OnShowOnlyNgChanged(bool value) => ApplyFilter();
    partial void OnSearchItemTextChanged(string value) => ApplyFilter();
    partial void OnSearchSessionTextChanged(string value) => _ = LoadSessionsAsync();

    partial void OnSelectedPartChanged(InspectionPartRecord? value)
    {
        Measurements.Clear();
        if (value?.Measurements != null)
        {
            foreach (var m in value.Measurements)
            {
                Measurements.Add(m);
            }
        }
    }

    partial void OnSelectedMeasurementChanged(string? value)
    {
        UpdateSpcAnalysis();
    }

    partial void OnSubgroupSizeNChanged(int value)
    {
        if (value < 2) SubgroupSizeN = 2;
        else UpdateSpcAnalysis();
    }

    // ─── Data Loading ────────────────────────────────────────────────

    public async Task LoadSessionsAsync()
    {
        try
        {
            var list = await _logService.GetAllSessionsAsync();
            var search = SearchSessionText?.Trim().ToLowerInvariant() ?? "";

            var filtered = string.IsNullOrEmpty(search)
                ? list
                : list.Where(s => (s.ProductName?.ToLowerInvariant().Contains(search) ?? false) ||
                                  (s.SessionCode?.ToLowerInvariant().Contains(search) ?? false) ||
                                  (s.FormattedStartTime.Contains(search))).ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var prevSelectedId = SelectedSession?.Id;
                Sessions.Clear();
                foreach (var s in filtered)
                {
                    Sessions.Add(s);
                }

                if (!string.IsNullOrEmpty(prevSelectedId))
                {
                    SelectedSession = Sessions.FirstOrDefault(x => x.Id == prevSelectedId);
                }

                if (SelectedSession == null && Sessions.Count > 0)
                {
                    SelectedSession = Sessions[0];
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi nạp phiên: {ex.Message}";
        }
    }

    private async Task LoadPartsForSessionAsync(string sessionId)
    {
        try
        {
            var parts = await _logService.GetPartsForSessionAsync(sessionId);
            _allCurrentSessionParts = parts.ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyFilter();
                UpdateAvailableMeasurements();
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi nạp chi tiết con hàng: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allCurrentSessionParts.AsEnumerable();

        if (ShowOnlyNg)
        {
            filtered = filtered.Where(p => !p.Pass);
        }

        var search = SearchItemText?.Trim().ToLowerInvariant() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(p => p.Measurements != null && p.Measurements.Any(m => m.ItemName.ToLowerInvariant().Contains(search)));
        }

        Parts.Clear();
        foreach (var p in filtered)
        {
            Parts.Add(p);
        }

        if (SelectedPart == null && Parts.Count > 0)
        {
            SelectedPart = Parts[0];
        }
    }

    private void UpdateAvailableMeasurements()
    {
        var itemNames = _allCurrentSessionParts
            .Where(p => p.Measurements != null)
            .SelectMany(p => p.Measurements.Select(m => m.ItemName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        var prev = SelectedMeasurement;
        AvailableMeasurements.Clear();
        foreach (var name in itemNames)
        {
            AvailableMeasurements.Add(name);
        }

        if (!string.IsNullOrEmpty(prev) && AvailableMeasurements.Contains(prev))
        {
            SelectedMeasurement = prev;
        }
        else if (AvailableMeasurements.Count > 0)
        {
            SelectedMeasurement = AvailableMeasurements[0];
        }
        else
        {
            SelectedMeasurement = null;
            SpcResult = null;
            ClearCharts();
        }
    }

    // ─── SPC & CPK Analysis + Chart Drawing ──────────────────────────

    public void UpdateSpcAnalysis()
    {
        if (SelectedSession == null || string.IsNullOrEmpty(SelectedMeasurement) || _allCurrentSessionParts.Count == 0)
        {
            SpcResult = null;
            ClearCharts();
            return;
        }

        // Lấy tất cả giá trị đo của hạng mục được chọn
        var measList = new List<InspectionItemMeasurement>();
        foreach (var part in _allCurrentSessionParts)
        {
            if (part.Measurements == null) continue;
            var m = part.Measurements.FirstOrDefault(x => string.Equals(x.ItemName, SelectedMeasurement, StringComparison.OrdinalIgnoreCase));
            if (m != null)
            {
                measList.Add(m);
            }
        }

        if (measList.Count == 0)
        {
            SpcResult = null;
            ClearCharts();
            return;
        }

        var first = measList[0];
        var values = measList.Select(m => m.MeasuredValue).ToList();

        SpcResult = SpcEngine.Analyze(
            SelectedMeasurement,
            values,
            first.Nominal,
            first.TolPlus,
            first.TolMinus,
            first.Unit,
            SubgroupSizeN);

        // Cập nhật Header
        SpcHeaderProduct = $"Sản phẩm: {SelectedSession.ProductName}";
        SpcHeaderMaterial = $"Vật liệu: {SelectedSession.Material}";
        SpcHeaderItem = $"Hạng mục test: {SelectedMeasurement}";
        SpcHeaderTotalRows = $"Total: {values.Count} rows (n={SpcResult.SubgroupSizeN})";

        // Render dữ liệu đồ họa cho 4 biểu đồ
        RenderCharts(SpcResult);
    }

    private void RenderCharts(SpcAnalysisResult spc)
    {
        const double ChartW = 240.0;
        const double ChartH = 120.0;
        const double PaddingTop = 15.0;
        const double PaddingBottom = 15.0;
        const double DrawH = ChartH - PaddingTop - PaddingBottom;

        // ════ 1. HISTOGRAM CHART ════
        HistogramBars.Clear();
        var gaussPts = new PointCollection();

        if (spc.HistogramBins.Count > 0)
        {
            int maxCount = Math.Max(1, spc.HistogramBins.Max(b => Math.Max(b.Count, (int)Math.Ceiling(b.NormalCurveHeight))));
            HistYMaxLabel = maxCount.ToString();
            HistXMinLabel = $"{spc.HistogramBins.First().BinStart:F2}";
            HistXMaxLabel = $"{spc.HistogramBins.Last().BinEnd:F2}";

            double barSlotW = ChartW / spc.HistogramBins.Count;
            double barW = Math.Max(2, barSlotW - 2);

            for (int i = 0; i < spc.HistogramBins.Count; i++)
            {
                var bin = spc.HistogramBins[i];
                double barH = (bin.Count / (double)maxCount) * DrawH;
                double x = i * barSlotW + (barSlotW - barW) / 2.0;
                double y = ChartH - PaddingBottom - barH;

                HistogramBars.Add(new HistogramBarVisual
                {
                    X = x,
                    Y = y,
                    Width = barW,
                    Height = Math.Max(1, barH),
                    FillBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    Count = bin.Count,
                    RangeText = bin.FormattedRange
                });

                // Gauss curve point
                double gH = (bin.NormalCurveHeight / maxCount) * DrawH;
                double gX = i * barSlotW + barSlotW / 2.0;
                double gY = Math.Max(PaddingTop, ChartH - PaddingBottom - gH);
                gaussPts.Add(new Point(gX, gY));
            }
        }
        HistogramGaussPoints = gaussPts;

        // ════ 2. XBAR CHART ════
        var xbarPts = new PointCollection();
        if (spc.Subgroups.Count > 0)
        {
            double minY = Math.Min(spc.Subgroups.Min(g => g.Mean), spc.Xbar_LCL);
            double maxY = Math.Max(spc.Subgroups.Max(g => g.Mean), spc.Xbar_UCL);
            double rangeY = Math.Max(1e-4, maxY - minY);

            XbarYMinLabel = $"{minY:F2}";
            XbarYMaxLabel = $"{maxY:F2}";
            XbarXMaxLabel = spc.Subgroups.Count.ToString();

            Func<double, double> mapY = v => ChartH - PaddingBottom - ((v - minY) / rangeY) * DrawH;

            XbarClY = mapY(spc.Xbar_CL);
            XbarUclY = mapY(spc.Xbar_UCL);
            XbarLclY = mapY(spc.Xbar_LCL);

            double stepX = ChartW / Math.Max(1, spc.Subgroups.Count - 1);
            for (int i = 0; i < spc.Subgroups.Count; i++)
            {
                double x = i * stepX;
                double y = mapY(spc.Subgroups[i].Mean);
                xbarPts.Add(new Point(x, y));
            }
        }
        XbarPoints = xbarPts;

        // ════ 3. R CHART ════
        var rPts = new PointCollection();
        if (spc.Subgroups.Count > 0)
        {
            double minY = Math.Min(0, spc.R_LCL);
            double maxY = Math.Max(spc.Subgroups.Max(g => g.Range), spc.R_UCL);
            double rangeY = Math.Max(1e-4, maxY - minY);

            RYMinLabel = $"{minY:F2}";
            RYMaxLabel = $"{maxY:F2}";
            RXMaxLabel = spc.Subgroups.Count.ToString();

            Func<double, double> mapY = v => ChartH - PaddingBottom - ((v - minY) / rangeY) * DrawH;

            RClY = mapY(spc.R_CL);
            RUclY = mapY(spc.R_UCL);
            RLclY = mapY(spc.R_LCL);

            double stepX = ChartW / Math.Max(1, spc.Subgroups.Count - 1);
            for (int i = 0; i < spc.Subgroups.Count; i++)
            {
                double x = i * stepX;
                double y = mapY(spc.Subgroups[i].Range);
                rPts.Add(new Point(x, y));
            }
        }
        RChartPoints = rPts;

        // ════ 4. CPK TREND CHART ════
        var cpkPts = new PointCollection();
        if (spc.Subgroups.Count > 0)
        {
            double minY = 0.0;
            double maxY = Math.Max(2.0, spc.Subgroups.Max(g => g.Cpk) + 0.3);
            double rangeY = Math.Max(1e-4, maxY - minY);

            CpkYMinLabel = $"{minY:F1}";
            CpkYMaxLabel = $"{maxY:F1}";
            CpkXMaxLabel = spc.Subgroups.Count.ToString();

            Func<double, double> mapY = v => ChartH - PaddingBottom - ((v - minY) / rangeY) * DrawH;

            Cpk133Y = mapY(1.33);
            Cpk167Y = mapY(1.67);

            double stepX = ChartW / Math.Max(1, spc.Subgroups.Count - 1);
            for (int i = 0; i < spc.Subgroups.Count; i++)
            {
                double x = i * stepX;
                double y = Math.Clamp(mapY(spc.Subgroups[i].Cpk), PaddingTop, ChartH - PaddingBottom);
                cpkPts.Add(new Point(x, y));
            }
        }
        CpkTrendPoints = cpkPts;
    }

    private void ClearCharts()
    {
        HistogramBars.Clear();
        HistogramGaussPoints = new PointCollection();
        XbarPoints = new PointCollection();
        RChartPoints = new PointCollection();
        CpkTrendPoints = new PointCollection();
    }

    // ─── Export Operations ───────────────────────────────────────────

    private void ExportExcel()
    {
        if (SelectedSession == null)
        {
            MessageBox.Show("Vui lòng chọn một phiên kiểm tra để xuất Excel.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xls;*.xml)|*.xls;*.xml|Tất cả tệp (*.*)|*.*",
            FileName = $"Inspection_Log_{SelectedSession.SessionCode}_{DateTime.Now:yyyyMMdd_HHmm}.xls"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                InspectionLogExporter.ExportToExcel(SelectedSession, _allCurrentSessionParts, SpcResult, dlg.FileName);
                StatusMessage = $"Đã xuất Excel thành công: {Path.GetFileName(dlg.FileName)}";
                MessageBox.Show($"Đã xuất báo cáo Excel thành công ra:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExportCsv()
    {
        if (SelectedSession == null)
        {
            MessageBox.Show("Vui lòng chọn một phiên kiểm tra để xuất CSV.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV File (*.csv)|*.csv|Tất cả tệp (*.*)|*.*",
            FileName = $"Inspection_Log_{SelectedSession.SessionCode}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                InspectionLogExporter.ExportToCsv(SelectedSession, _allCurrentSessionParts, dlg.FileName);
                StatusMessage = $"Đã xuất CSV thành công: {Path.GetFileName(dlg.FileName)}";
                MessageBox.Show($"Đã xuất file CSV thành công ra:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất CSV: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExportJson()
    {
        if (SelectedSession == null)
        {
            MessageBox.Show("Vui lòng chọn một phiên kiểm tra để xuất JSON.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "JSON File (*.json)|*.json|Tất cả tệp (*.*)|*.*",
            FileName = $"Inspection_Log_{SelectedSession.SessionCode}_{DateTime.Now:yyyyMMdd_HHmm}.json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                InspectionLogExporter.ExportToJson(SelectedSession, _allCurrentSessionParts, SpcResult, dlg.FileName);
                StatusMessage = $"Đã xuất JSON thành công: {Path.GetFileName(dlg.FileName)}";
                MessageBox.Show($"Đã xuất file JSON thành công ra:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất JSON: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession == null) return;
        var r = MessageBox.Show($"Bạn có chắc chắn muốn xóa phiên '{SelectedSession.SessionCode}' khỏi lịch sử?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
        {
            await _logService.DeleteSessionAsync(SelectedSession.Id);
            await LoadSessionsAsync();
        }
    }

    private async Task ClearAllHistoryAsync()
    {
        var r = MessageBox.Show("Bạn có chắc chắn muốn xóa TOÀN BỘ lịch sử kiểm tra?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Yes)
        {
            await _logService.ClearAllHistoryAsync();
            await LoadSessionsAsync();
        }
    }

    private void OnSessionUpdated(object? sender, InspectionSessionRecord session)
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            var existing = Sessions.FirstOrDefault(s => s.Id == session.Id);
            if (existing != null)
            {
                int index = Sessions.IndexOf(existing);
                Sessions[index] = session;
            }
            else
            {
                Sessions.Insert(0, session);
            }
        }));
    }

    private void OnPartLogged(object? sender, InspectionPartRecord part)
    {
        if (SelectedSession == null || SelectedSession.Id != part.SessionId) return;

        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            _allCurrentSessionParts.Add(part);
            if (!ShowOnlyNg || !part.Pass)
            {
                Parts.Add(part);
            }
            UpdateAvailableMeasurements();
            UpdateSpcAnalysis();
        }));
    }
}

public sealed class HistogramBarVisual
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Brush FillBrush { get; set; } = Brushes.DodgerBlue;
    public int Count { get; set; }
    public string RangeText { get; set; } = "";
}

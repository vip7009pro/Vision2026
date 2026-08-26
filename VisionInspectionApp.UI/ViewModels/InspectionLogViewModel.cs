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
    private ObservableCollection<ChartDataPointVisual> _xbarMarkers = new();

    [ObservableProperty]
    private double _xbarClY = 50;

    [ObservableProperty]
    private double _xbarUclY = 20;

    [ObservableProperty]
    private double _xbarLclY = 80;

    [ObservableProperty]
    private PointCollection _rChartPoints = new();

    [ObservableProperty]
    private ObservableCollection<ChartDataPointVisual> _rChartMarkers = new();

    [ObservableProperty]
    private double _rClY = 50;

    [ObservableProperty]
    private double _rUclY = 20;

    [ObservableProperty]
    private double _rLclY = 80;

    [ObservableProperty]
    private PointCollection _cpkTrendPoints = new();

    [ObservableProperty]
    private ObservableCollection<ChartDataPointVisual> _cpkTrendMarkers = new();

    [ObservableProperty]
    private double _cpk133Y = 40;

    [ObservableProperty]
    private double _cpk167Y = 20;

    // Chart Axis Labels
    [ObservableProperty] private string _histXMinLabel = "0.0";
    [ObservableProperty] private string _histXMidLabel = "0.0";
    [ObservableProperty] private string _histXMaxLabel = "0.0";
    [ObservableProperty] private string _histYMaxLabel = "0";
    [ObservableProperty] private string _histYMidLabel = "0";

    [ObservableProperty] private string _xbarYMaxLabel = "0.0";
    [ObservableProperty] private string _xbarYMidLabel = "0.0";
    [ObservableProperty] private string _xbarYMinLabel = "0.0";
    [ObservableProperty] private string _xbarXMaxLabel = "0";
    [ObservableProperty] private string _xbarClLabel = "0.0";
    [ObservableProperty] private string _xbarUclLabel = "0.0";
    [ObservableProperty] private string _xbarLclLabel = "0.0";

    [ObservableProperty] private string _rYMaxLabel = "0.0";
    [ObservableProperty] private string _rYMidLabel = "0.0";
    [ObservableProperty] private string _rYMinLabel = "0.0";
    [ObservableProperty] private string _rXMaxLabel = "0";
    [ObservableProperty] private string _rClLabel = "0.0";
    [ObservableProperty] private string _rUclLabel = "0.0";
    [ObservableProperty] private string _rLclLabel = "0.0";

    [ObservableProperty] private string _cpkYMaxLabel = "2.5";
    [ObservableProperty] private string _cpkYMidLabel = "1.3";
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
        var allValues = measList.Select(m => m.MeasuredValue).ToList();
        var validValues = allValues.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        int nanCount = allValues.Count - validValues.Count;

        if (validValues.Count == 0)
        {
            SpcResult = null;
            ClearCharts();
            SpcHeaderProduct = $"Sản phẩm: {SelectedSession.ProductName}";
            SpcHeaderMaterial = $"Vật liệu: {SelectedSession.Material}";
            SpcHeaderItem = $"Hạng mục test: {SelectedMeasurement}";
            SpcHeaderTotalRows = $"Total: {allValues.Count} rows (Tất cả {nanCount} mẫu là NaN/NG - Không đủ dữ liệu tính SPC)";
            StatusMessage = "Tất cả các con hàng đều ra kết quả NaN/NG cho hạng mục được chọn.";
            return;
        }

        SpcResult = SpcEngine.Analyze(
            SelectedMeasurement,
            validValues,
            first.Nominal,
            first.TolPlus,
            first.TolMinus,
            first.Unit,
            SubgroupSizeN);

        // Cập nhật Header
        SpcHeaderProduct = $"Sản phẩm: {SelectedSession.ProductName}";
        SpcHeaderMaterial = $"Vật liệu: {SelectedSession.Material}";
        SpcHeaderItem = $"Hạng mục test: {SelectedMeasurement}";
        SpcHeaderTotalRows = nanCount > 0
            ? $"Total: {allValues.Count} rows (Hợp lệ: {validValues.Count}, Bỏ qua NaN: {nanCount}) (n={SpcResult.SubgroupSizeN})"
            : $"Total: {validValues.Count} rows (n={SpcResult.SubgroupSizeN})";

        // Render dữ liệu đồ họa cho 4 biểu đồ
        RenderCharts(SpcResult);
    }

    private void RenderCharts(SpcAnalysisResult spc)
    {
        const double PlotLeft = 38.0;
        const double PlotRight = 310.0;
        const double PlotTop = 14.0;
        const double PlotBottom = 150.0;
        const double PlotW = PlotRight - PlotLeft; // 272.0
        const double PlotH = PlotBottom - PlotTop; // 136.0

        // ════ 1. HISTOGRAM CHART ════
        HistogramBars.Clear();
        var gaussPts = new PointCollection();

        if (spc.HistogramBins.Count > 0)
        {
            int maxCount = Math.Max(1, spc.HistogramBins.Max(b => Math.Max(b.Count, (int)Math.Ceiling(b.NormalCurveHeight))));
            int midCount = maxCount / 2;
            HistYMaxLabel = maxCount.ToString();
            HistYMidLabel = midCount.ToString();

            double minX = spc.HistogramBins.First().BinStart;
            double maxX = spc.HistogramBins.Last().BinEnd;
            double midX = (minX + maxX) / 2.0;

            HistXMinLabel = $"{minX:F2}";
            HistXMidLabel = $"{midX:F2}";
            HistXMaxLabel = $"{maxX:F2}";

            int totalSamples = spc.HistogramBins.Sum(b => b.Count);
            double barSlotW = PlotW / spc.HistogramBins.Count;
            double barW = Math.Max(3, barSlotW - 2);

            for (int i = 0; i < spc.HistogramBins.Count; i++)
            {
                var bin = spc.HistogramBins[i];
                double barH = Math.Max(1, (bin.Count / (double)maxCount) * PlotH);
                double x = PlotLeft + i * barSlotW + (barSlotW - barW) / 2.0;
                double y = PlotBottom - barH;

                double pct = totalSamples > 0 ? (bin.Count * 100.0 / totalSamples) : 0;
                HistogramBars.Add(new HistogramBarVisual
                {
                    X = x,
                    Y = y,
                    Width = barW,
                    Height = barH,
                    FillBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    Count = bin.Count,
                    RangeText = bin.FormattedRange,
                    ToolTipText = $"📊 Khoảng: {bin.FormattedRange}\n🔢 Số lượng: {bin.Count} con hàng ({pct:F1}%)\n📈 Gauss density: {bin.NormalCurveHeight:F1}"
                });

                // Gauss curve point
                double gH = (bin.NormalCurveHeight / maxCount) * PlotH;
                double gX = PlotLeft + i * barSlotW + barSlotW / 2.0;
                double gY = Math.Clamp(PlotBottom - gH, PlotTop, PlotBottom);
                gaussPts.Add(new Point(gX, gY));
            }
        }
        HistogramGaussPoints = gaussPts;

        // ════ 2. XBAR CHART ════
        var xbarPts = new PointCollection();
        XbarMarkers.Clear();
        if (spc.Subgroups.Count > 0)
        {
            double rawMin = Math.Min(spc.Subgroups.Min(g => g.Mean), spc.Xbar_LCL);
            double rawMax = Math.Max(spc.Subgroups.Max(g => g.Mean), spc.Xbar_UCL);
            double span = Math.Max(1e-4, rawMax - rawMin);
            double pad = span * 0.15;
            double minY = rawMin - pad;
            double maxY = rawMax + pad;
            double rangeY = Math.Max(1e-4, maxY - minY);

            XbarYMinLabel = $"{rawMin:F2}";
            XbarYMidLabel = $"{((rawMax + rawMin) / 2.0):F2}";
            XbarYMaxLabel = $"{rawMax:F2}";
            XbarXMaxLabel = spc.Subgroups.Count.ToString();

            XbarClLabel = $"{spc.Xbar_CL:F2}";
            XbarUclLabel = $"{spc.Xbar_UCL:F2}";
            XbarLclLabel = $"{spc.Xbar_LCL:F2}";

            Func<double, double> mapY = v => PlotBottom - ((v - minY) / rangeY) * PlotH;

            XbarClY = Math.Clamp(mapY(spc.Xbar_CL), PlotTop, PlotBottom);
            XbarUclY = Math.Clamp(mapY(spc.Xbar_UCL), PlotTop, PlotBottom);
            XbarLclY = Math.Clamp(mapY(spc.Xbar_LCL), PlotTop, PlotBottom);

            double stepX = spc.Subgroups.Count > 1 ? PlotW / (spc.Subgroups.Count - 1) : 0;
            for (int i = 0; i < spc.Subgroups.Count; i++)
            {
                var g = spc.Subgroups[i];
                double x = PlotLeft + (spc.Subgroups.Count == 1 ? PlotW / 2.0 : i * stepX);
                double y = Math.Clamp(mapY(g.Mean), PlotTop, PlotBottom);
                xbarPts.Add(new Point(x, y));

                XbarMarkers.Add(new ChartDataPointVisual
                {
                    X = x - 4,
                    Y = y - 4,
                    Value = g.Mean,
                    StrokeBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                    FillBrush = Brushes.White,
                    ToolTipText = $"📍 Nhóm #{i + 1} (n={spc.SubgroupSizeN})\n• Trung bình (AVG): {g.Mean:F3} {spc.Unit}\n• Min trong nhóm: {g.Values.Min():F3}\n• Max trong nhóm: {g.Values.Max():F3}\n• Giới hạn UCL: {spc.Xbar_UCL:F3}\n• Giới hạn LCL: {spc.Xbar_LCL:F3}\n• Đường chuẩn CL: {spc.Xbar_CL:F3}"
                });
            }
        }
        XbarPoints = xbarPts;

        // ════ 3. R CHART ════
        var rPts = new PointCollection();
        RChartMarkers.Clear();
        if (spc.Subgroups.Count > 0)
        {
            double rawMin = Math.Min(0, spc.R_LCL);
            double rawMax = Math.Max(spc.Subgroups.Max(g => g.Range), spc.R_UCL);
            double span = Math.Max(1e-4, rawMax - rawMin);
            double pad = span * 0.15;
            double minY = Math.Max(0, rawMin - pad);
            double maxY = rawMax + pad;
            double rangeY = Math.Max(1e-4, maxY - minY);

            RYMinLabel = $"{rawMin:F2}";
            RYMidLabel = $"{((rawMax + rawMin) / 2.0):F2}";
            RYMaxLabel = $"{rawMax:F2}";
            RXMaxLabel = spc.Subgroups.Count.ToString();

            RClLabel = $"{spc.R_CL:F2}";
            RUclLabel = $"{spc.R_UCL:F2}";
            RLclLabel = $"{spc.R_LCL:F2}";

            Func<double, double> mapY = v => PlotBottom - ((v - minY) / rangeY) * PlotH;

            RClY = Math.Clamp(mapY(spc.R_CL), PlotTop, PlotBottom);
            RUclY = Math.Clamp(mapY(spc.R_UCL), PlotTop, PlotBottom);
            RLclY = Math.Clamp(mapY(spc.R_LCL), PlotTop, PlotBottom);

            double stepX = spc.Subgroups.Count > 1 ? PlotW / (spc.Subgroups.Count - 1) : 0;
            for (int i = 0; i < spc.Subgroups.Count; i++)
            {
                var g = spc.Subgroups[i];
                double x = PlotLeft + (spc.Subgroups.Count == 1 ? PlotW / 2.0 : i * stepX);
                double y = Math.Clamp(mapY(g.Range), PlotTop, PlotBottom);
                rPts.Add(new Point(x, y));

                RChartMarkers.Add(new ChartDataPointVisual
                {
                    X = x - 4,
                    Y = y - 4,
                    Value = g.Range,
                    StrokeBrush = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
                    FillBrush = Brushes.White,
                    ToolTipText = $"📍 Nhóm #{i + 1} (n={spc.SubgroupSizeN})\n• Độ biến thiên (R): {g.Range:F3} {spc.Unit}\n• Max: {g.Values.Max():F3}\n• Min: {g.Values.Min():F3}\n• Giới hạn R_UCL: {spc.R_UCL:F3}\n• Giới hạn R_LCL: {spc.R_LCL:F3}\n• Đường chuẩn R_CL: {spc.R_CL:F3}"
                });
            }
        }
        RChartPoints = rPts;

        // ════ 4. CPK TREND CHART ════
        var cpkPts = new PointCollection();
        CpkTrendMarkers.Clear();
        if (spc.Subgroups.Count > 0)
        {
            double maxCpkVal = spc.Subgroups.Max(g => g.Cpk);
            double rawMax = Math.Max(2.5, maxCpkVal + 0.4);
            double minY = 0.0;
            double maxY = rawMax;
            double rangeY = Math.Max(1e-4, maxY - minY);

            CpkYMinLabel = "0.0";
            CpkYMidLabel = $"{(maxY / 2.0):F1}";
            CpkYMaxLabel = $"{maxY:F1}";
            CpkXMaxLabel = spc.Subgroups.Count.ToString();

            Func<double, double> mapY = v => PlotBottom - ((v - minY) / rangeY) * PlotH;

            Cpk133Y = Math.Clamp(mapY(1.33), PlotTop, PlotBottom);
            Cpk167Y = Math.Clamp(mapY(1.67), PlotTop, PlotBottom);

            double stepX = spc.Subgroups.Count > 1 ? PlotW / (spc.Subgroups.Count - 1) : 0;
            for (int i = 0; i < spc.Subgroups.Count; i++)
            {
                var g = spc.Subgroups[i];
                double x = PlotLeft + (spc.Subgroups.Count == 1 ? PlotW / 2.0 : i * stepX);
                double y = Math.Clamp(mapY(g.Cpk), PlotTop, PlotBottom);
                cpkPts.Add(new Point(x, y));

                var markerColor = g.Cpk >= 1.67 ? Color.FromRgb(16, 185, 129) : (g.Cpk >= 1.33 ? Color.FromRgb(59, 130, 246) : Color.FromRgb(239, 68, 68));
                string assess = g.Cpk >= 1.67 ? "🌟 Xuất sắc (Cpk >= 1.67 • 5-6 Sigma)" : (g.Cpk >= 1.33 ? "✅ Đạt chuẩn (Cpk >= 1.33 • 4 Sigma)" : (g.Cpk >= 1.0 ? "⚠️ Tạm chấp nhận (1.0 <= Cpk < 1.33)" : "❌ Không đạt (Cpk < 1.0 • Rủi ro NG cao)"));

                CpkTrendMarkers.Add(new ChartDataPointVisual
                {
                    X = x - 4,
                    Y = y - 4,
                    Value = g.Cpk,
                    StrokeBrush = new SolidColorBrush(markerColor),
                    FillBrush = new SolidColorBrush(markerColor),
                    ToolTipText = $"📍 Nhóm #{i + 1} (n={spc.SubgroupSizeN})\n• Chỉ số Cpk: {g.Cpk:F2}\n• Đánh giá: {assess}\n• Ngưỡng 1 (Chuẩn 4 Sigma): 1.33\n• Ngưỡng 2 (Chuẩn 5 Sigma): 1.67"
                });
            }
        }
        CpkTrendPoints = cpkPts;
    }

    private void ClearCharts()
    {
        HistogramBars.Clear();
        HistogramGaussPoints = new PointCollection();
        XbarPoints = new PointCollection();
        XbarMarkers.Clear();
        RChartPoints = new PointCollection();
        RChartMarkers.Clear();
        CpkTrendPoints = new PointCollection();
        CpkTrendMarkers.Clear();
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
                MessageBox.Show($"Đã xuất file Excel thành công ra:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
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
    public string ToolTipText { get; set; } = "";
}

public sealed class ChartDataPointVisual
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Value { get; set; }
    public string ToolTipText { get; set; } = "";
    public Brush StrokeBrush { get; set; } = Brushes.ForestGreen;
    public Brush FillBrush { get; set; } = Brushes.White;
}

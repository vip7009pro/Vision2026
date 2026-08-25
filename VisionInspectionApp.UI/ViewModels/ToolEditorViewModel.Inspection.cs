using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        public sealed record SpecResultRow(
            string Tool,
            string Name,
            string RefA,
            string RefB,
            double Value,
            double Nominal,
            double TolPlus,
            double TolMinus,
            bool Pass,
            string Unit = "mm");

        public sealed record CodeDetectionRow(string Name, bool Found, string Text);

        public sealed record SurfaceCompareDebugPick(int Index, string DisplayName);

        public sealed record ToolTimingRow(string Name, int TimeMs);

        public ObservableCollection<SpecResultRow> SpecResults { get; } = new();

        public ObservableCollection<CodeDetectionRow> CodeDetectionResults { get; } = new();

        public ObservableCollection<SurfaceCompareDebugPick> SurfaceCompareDebugItems { get; } = new();

        public ObservableCollection<ToolTimingRow> ToolTimings { get; } = new();

        private InspectionResult? _lastResult;
        public InspectionResult? LastResult
        {
            get => _lastResult;
            set
            {
                _lastResult = value;
                OnPropertyChanged(nameof(LastResult));
                OnLastResultChanged(value);
            }
        }

        private string _resultStatusText = "Chờ kiểm tra";
        public string ResultStatusText
        {
            get => _resultStatusText;
            set => SetProperty(ref _resultStatusText, value);
        }

        private Brush _resultBackgroundBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        public Brush ResultBackgroundBrush
        {
            get => _resultBackgroundBrush;
            set => SetProperty(ref _resultBackgroundBrush, value);
        }

        private Brush _resultBorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
        public Brush ResultBorderBrush
        {
            get => _resultBorderBrush;
            set => SetProperty(ref _resultBorderBrush, value);
        }

        private Brush _resultForegroundBrush = Brushes.Gray;
        public Brush ResultForegroundBrush
        {
            get => _resultForegroundBrush;
            set => SetProperty(ref _resultForegroundBrush, value);
        }

        private string _ngReasonsText = "";
        public string NgReasonsText
        {
            get => _ngReasonsText;
            set => SetProperty(ref _ngReasonsText, value);
        }

        private bool _isNg = false;
        public bool IsNg
        {
            get => _isNg;
            set => SetProperty(ref _isNg, value);
        }

        private string _originScoreText = "";
        public string OriginScoreText
        {
            get => _originScoreText;
            set => SetProperty(ref _originScoreText, value);
        }

        private SurfaceCompareDebugPick? _selectedSurfaceCompareDebugPick;
        public SurfaceCompareDebugPick? SelectedSurfaceCompareDebugPick
        {
            get => _selectedSurfaceCompareDebugPick;
            set
            {
                if (SetProperty(ref _selectedSurfaceCompareDebugPick, value))
                {
                    if (!_surfaceCompareDebugRebuildInProgress)
                    {
                        UpdateSurfaceCompareDebugImages();
                    }
                }
            }
        }

        private ImageSource? _debugTemplate;
        public ImageSource? DebugTemplate
        {
            get => _debugTemplate;
            set => SetProperty(ref _debugTemplate, value);
        }

        private ImageSource? _debugCurrent;
        public ImageSource? DebugCurrent
        {
            get => _debugCurrent;
            set => SetProperty(ref _debugCurrent, value);
        }

        private ImageSource? _debugBinary;
        public ImageSource? DebugBinary
        {
            get => _debugBinary;
            set => SetProperty(ref _debugBinary, value);
        }

        private ImageSource? _debugDiff;
        public ImageSource? DebugDiff
        {
            get => _debugDiff;
            set => SetProperty(ref _debugDiff, value);
        }

        private bool _surfaceCompareDebugRebuildInProgress;

        public event Func<InspectionResult, VisionConfig, Task>? InspectionCompletedAsync;

        private void OnLastResultChanged(InspectionResult? value)
        {
            RefreshInspectionDashboard(value);

            if (value != null && _config != null && InspectionCompletedAsync != null)
            {
                _ = InspectionCompletedAsync.Invoke(value, _config);
            }
        }

        private void RefreshInspectionDashboard(InspectionResult? res)
        {
            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => RefreshInspectionDashboard(res));
                return;
            }

            RefreshSpecResults(res);
            RefreshCodeDetectionResults(res);
            RefreshTimings(res);
            RebuildSurfaceCompareDebugSelector(res);
            UpdateResultSummary(res);
        }

        private void RefreshTimings(InspectionResult? res)
        {
            ToolTimings.Clear();
            if (res?.Timings?.NodeTimings is null) return;

            foreach (var kvp in res.Timings.NodeTimings.OrderByDescending(x => x.Value))
            {
                ToolTimings.Add(new ToolTimingRow(kvp.Key, kvp.Value));
            }
        }

        private void UpdateResultSummary(InspectionResult? res)
        {
            if (res is null)
            {
                ResultStatusText = "Chờ kiểm tra";
                ResultBackgroundBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                ResultBorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                ResultForegroundBrush = Brushes.Gray;
                NgReasonsText = "";
                IsNg = false;
                OriginScoreText = "";
                return;
            }

            if (res.Origin is not null)
            {
                OriginScoreText = $"Origin Score: {res.Origin.Score:F3} (Min: {res.Origin.Threshold:F2}) -> {(res.Origin.Pass ? "PASS" : "FAIL")}";
            }
            else
            {
                OriginScoreText = "Origin: N/A";
            }

            if (res.Pass)
            {
                ResultStatusText = "OK";
                ResultBackgroundBrush = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                ResultBorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                ResultForegroundBrush = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                NgReasonsText = "";
                IsNg = false;
            }
            else
            {
                ResultStatusText = "NG";
                ResultBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                ResultBorderBrush = new SolidColorBrush(Color.FromRgb(239, 83, 80));
                ResultForegroundBrush = new SolidColorBrush(Color.FromRgb(198, 40, 40));
                IsNg = true;

                var reasons = new List<string>();
                if (res.Origin is not null && !res.Origin.Pass)
                {
                    reasons.Add($"• Lỗi Origin '{res.Origin.Name}': Score {res.Origin.Score:F3} < Ngưỡng {res.Origin.Threshold:F2}");
                }

                if (res.Points is not null)
                {
                    foreach (var p in res.Points.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi Point '{p.Name}': Score {p.Score:F3} < Ngưỡng {p.Threshold:F2}");
                    }
                }

                if (res.Lines is not null)
                {
                    foreach (var l in res.Lines.Where(x => !x.Found))
                    {
                        reasons.Add($"• Lỗi Line '{l.Name}': Không tìm thấy đủ cạnh");
                    }
                }

                if (res.Distances is not null)
                {
                    foreach (var d in res.Distances.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi Distance '{d.Name}': Giá trị {d.Value:F3}mm nằm ngoài khoảng [{d.Nominal + d.TolMinus:F3}, {d.Nominal + d.TolPlus:F3}]");
                    }
                }

                if (res.LineToLineDistances is not null)
                {
                    foreach (var l2l in res.LineToLineDistances.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi LineLineDist '{l2l.Name}': Giá trị {l2l.Value:F3}mm nằm ngoài khoảng [{l2l.Nominal + l2l.TolMinus:F3}, {l2l.Nominal + l2l.TolPlus:F3}]");
                    }
                }

                if (res.PointToLineDistances is not null)
                {
                    foreach (var p2l in res.PointToLineDistances.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi PointLineDist '{p2l.Name}': Giá trị {p2l.Value:F3}mm nằm ngoài khoảng [{p2l.Nominal + p2l.TolMinus:F3}, {p2l.Nominal + p2l.TolPlus:F3}]");
                    }
                }

                if (res.SegmentLineDistances is not null)
                {
                    foreach (var sld in res.SegmentLineDistances.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi SegmentLineDist '{sld.Name}': Giá trị {sld.Value:F3}mm nằm ngoài khoảng [{sld.Nominal + sld.TolMinus:F3}, {sld.Nominal + sld.TolPlus:F3}]");
                    }
                }

                if (res.Angles is not null)
                {
                    foreach (var a in res.Angles.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi Angle '{a.Name}': Giá trị {a.ValueDeg:F3}° nằm ngoài khoảng [{a.Nominal + a.TolMinus:F3}, {a.Nominal + a.TolPlus:F3}]");
                    }
                }

                if (res.EdgePairs is not null)
                {
                    foreach (var ep in res.EdgePairs.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi EdgePair '{ep.Name}': Giá trị {ep.Value:F3}mm nằm ngoài khoảng [{ep.Nominal + ep.TolMinus:F3}, {ep.Nominal + ep.TolPlus:F3}]");
                    }
                }

                if (res.EdgePairDetections is not null)
                {
                    foreach (var epd in res.EdgePairDetections.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi EdgePairDetect '{epd.Name}': Giá trị {epd.Value:F3}mm nằm ngoài khoảng [{epd.Nominal + epd.TolMinus:F3}, {epd.Nominal + epd.TolPlus:F3}]");
                    }
                }

                if (res.LinePairDetections is not null)
                {
                    foreach (var lpd in res.LinePairDetections.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi LinePairDetect '{lpd.Name}': Giá trị {lpd.Value:F3}mm nằm ngoài khoảng [{lpd.Nominal + lpd.TolMinus:F3}, {lpd.Nominal + lpd.TolPlus:F3}]");
                    }
                }

                if (res.Diameters is not null)
                {
                    foreach (var dia in res.Diameters.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi Diameter '{dia.Name}': Giá trị {dia.Value:F3}mm nằm ngoài khoảng [{dia.Nominal + dia.TolMinus:F3}, {dia.Nominal + dia.TolPlus:F3}]");
                    }
                }

                if (res.Conditions is not null)
                {
                    foreach (var c in res.Conditions.Where(x => !x.Pass))
                    {
                        var errStr = string.IsNullOrWhiteSpace(c.Error) ? "" : $" ({c.Error})";
                        reasons.Add($"• Lỗi Condition '{c.Name}': Biểu thức [{c.Expression}] sai{errStr}");
                    }
                }

                if (res.SurfaceCompares is not null)
                {
                    foreach (var sc in res.SurfaceCompares.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi SurfaceCompare '{sc.Name}': Phát hiện {sc.Count} điểm lỗi (Diện tích tối đa: {sc.MaxArea} px)");
                    }
                }

                if (res.CodeDetections is not null)
                {
                    foreach (var cd in res.CodeDetections.Where(x => !x.Found))
                    {
                        reasons.Add($"• Lỗi CodeDetection '{cd.Name}': Không đọc được mã");
                    }
                }

                if (res.ColorDiffs is not null)
                {
                    foreach (var cd in res.ColorDiffs.Where(x => !x.Pass))
                    {
                        reasons.Add($"• Lỗi ColorDiff '{cd.Name}': ΔE = {cd.DeltaE:F2} > Ngưỡng tối đa {cd.MaxDeltaE:F2}");
                    }
                }

                NgReasonsText = string.Join("\n", reasons);
            }

            PushRecentPartInspectionResult(res.Pass);
        }

        public string SpecResultsValueHeader => (_config is not null && _config.PixelsPerMm > 0 && Math.Abs(_config.PixelsPerMm - 1.0) > 1e-6) ? "Value (mm)" : "Value (px)";

        private void RefreshSpecResults(InspectionResult? res)
        {
            SpecResults.Clear();
            OnPropertyChanged(nameof(SpecResultsValueHeader));
            if (res is null) return;

            var isCalibrated = _config is not null && _config.PixelsPerMm > 0 && Math.Abs(_config.PixelsPerMm - 1.0) > 1e-6;
            var distUnit = isCalibrated ? "mm" : "px";

            if (res.Distances is not null)
            {
                foreach (var d in res.Distances)
                {
                    SpecResults.Add(new SpecResultRow(
                        "Distance",
                        d.Name,
                        d.PointA,
                        d.PointB,
                        d.Value,
                        d.Nominal,
                        d.TolPlus,
                        d.TolMinus,
                        d.Pass,
                        distUnit));
                }
            }

            if (res.LineToLineDistances is not null)
            {
                foreach (var l2l in res.LineToLineDistances)
                {
                    SpecResults.Add(new SpecResultRow(
                        "LineLineDist",
                        l2l.Name,
                        l2l.RefA,
                        l2l.RefB,
                        l2l.Value,
                        l2l.Nominal,
                        l2l.TolPlus,
                        l2l.TolMinus,
                        l2l.Pass,
                        distUnit));
                }
            }

            if (res.PointToLineDistances is not null)
            {
                foreach (var p2l in res.PointToLineDistances)
                {
                    SpecResults.Add(new SpecResultRow(
                        "PointLineDist",
                        p2l.Name,
                        p2l.RefA,
                        p2l.RefB,
                        p2l.Value,
                        p2l.Nominal,
                        p2l.TolPlus,
                        p2l.TolMinus,
                        p2l.Pass,
                        distUnit));
                }
            }

            if (res.SegmentLineDistances is not null)
            {
                foreach (var sld in res.SegmentLineDistances)
                {
                    SpecResults.Add(new SpecResultRow(
                        "SegmentLineDist",
                        sld.Name,
                        sld.RefA,
                        sld.RefB,
                        sld.Value,
                        sld.Nominal,
                        sld.TolPlus,
                        sld.TolMinus,
                        sld.Pass,
                        distUnit));
                }
            }

            if (res.Angles is not null)
            {
                foreach (var ang in res.Angles)
                {
                    SpecResults.Add(new SpecResultRow(
                        "Angle",
                        ang.Name,
                        ang.LineA,
                        ang.LineB,
                        ang.ValueDeg,
                        ang.Nominal,
                        ang.TolPlus,
                        ang.TolMinus,
                        ang.Pass,
                        "°"));
                }
            }

            if (res.EdgePairs is not null)
            {
                foreach (var ep in res.EdgePairs)
                {
                    SpecResults.Add(new SpecResultRow(
                        "EdgePair",
                        ep.Name,
                        ep.RefA,
                        ep.RefB,
                        ep.Value,
                        ep.Nominal,
                        ep.TolPlus,
                        ep.TolMinus,
                        ep.Pass,
                        distUnit));
                }
            }

            if (res.EdgePairDetections is not null)
            {
                foreach (var epd in res.EdgePairDetections)
                {
                    SpecResults.Add(new SpecResultRow(
                        "EdgePairDetect",
                        epd.Name,
                        "-",
                        "-",
                        epd.Value,
                        epd.Nominal,
                        epd.TolPlus,
                        epd.TolMinus,
                        epd.Pass,
                        distUnit));
                }
            }

            if (res.LinePairDetections is not null)
            {
                foreach (var lpd in res.LinePairDetections)
                {
                    SpecResults.Add(new SpecResultRow(
                        "LinePairDetect",
                        lpd.Name,
                        "-",
                        "-",
                        lpd.Value,
                        lpd.Nominal,
                        lpd.TolPlus,
                        lpd.TolMinus,
                        lpd.Pass,
                        distUnit));
                }
            }

            if (res.Diameters is not null)
            {
                foreach (var dia in res.Diameters)
                {
                    SpecResults.Add(new SpecResultRow(
                        "Diameter",
                        dia.Name,
                        dia.CircleRef,
                        "-",
                        dia.Value,
                        dia.Nominal,
                        dia.TolPlus,
                        dia.TolMinus,
                        dia.Pass,
                        distUnit));
                }
            }

            if (res.ColorDiffs is not null)
            {
                foreach (var cd in res.ColorDiffs)
                {
                    SpecResults.Add(new SpecResultRow(
                        "ColorDiff",
                        cd.Name,
                        "Sample",
                        "Ref",
                        cd.DeltaE,
                        0.0,
                        cd.MaxDeltaE,
                        0.0,
                        cd.Pass,
                        "ΔE"));
                }
            }
        }

        private void RefreshCodeDetectionResults(InspectionResult? res)
        {
            CodeDetectionResults.Clear();
            if (res?.CodeDetections is null) return;

            foreach (var item in res.CodeDetections)
            {
                CodeDetectionResults.Add(new CodeDetectionRow(item.Name, item.Found, item.Text ?? "-"));
            }
        }

        private void RebuildSurfaceCompareDebugSelector(InspectionResult? res)
        {
            _surfaceCompareDebugRebuildInProgress = true;
            try
            {
                SurfaceCompareDebugItems.Clear();
                if (res?.SurfaceCompares is null) return;

                int index = 0;
                foreach (var sc in res.SurfaceCompares)
                {
                    SurfaceCompareDebugItems.Add(new SurfaceCompareDebugPick(index, sc.Name));
                    index++;
                }

                if (SurfaceCompareDebugItems.Count > 0)
                {
                    SelectedSurfaceCompareDebugPick = SurfaceCompareDebugItems[0];
                }
                else
                {
                    SelectedSurfaceCompareDebugPick = null;
                }
            }
            finally
            {
                _surfaceCompareDebugRebuildInProgress = false;
            }
            UpdateSurfaceCompareDebugImages();
        }

        private void UpdateSurfaceCompareDebugImages()
        {
            var pick = SelectedSurfaceCompareDebugPick;
            var list = _lastResult?.SurfaceCompares;
            if (pick is null || list is null || pick.Index < 0 || pick.Index >= list.Count)
            {
                DebugTemplate = null;
                DebugCurrent = null;
                DebugBinary = null;
                DebugDiff = null;
                return;
            }

            var item = list[pick.Index];
            DebugTemplate = ByteToBitmapSource(item.TemplateImage);
            DebugCurrent = ByteToBitmapSource(item.CurrentImage);
            DebugBinary = ByteToBitmapSource(item.BinaryImage);
            DebugDiff = ByteToBitmapSource(item.DiffImage);
        }

        private static ImageSource? ByteToBitmapSource(byte[]? bytes)
        {
            if (bytes is null || bytes.Length == 0) return null;
            try
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}

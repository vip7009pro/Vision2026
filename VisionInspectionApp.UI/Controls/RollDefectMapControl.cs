using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Controls;

/// <summary>
/// Giao diện Bản đồ Khuyết tật Cuộn thời gian thực (Real-time Roll Defect Map Visualizer)
/// Hiển thị trực quan toàn bộ dải cuộn từ 0m đến N mét kèm vị trí chính xác của từng vết lỗi.
/// </summary>
public sealed class RollDefectMapControl : FrameworkElement
{
    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(
            nameof(Session),
            typeof(RollSession),
            typeof(RollDefectMapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentWebPositionMmProperty =
        DependencyProperty.Register(
            nameof(CurrentWebPositionMm),
            typeof(double),
            typeof(RollDefectMapControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public RollSession? Session
    {
        get => (RollSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public double CurrentWebPositionMm
    {
        get => (double)GetValue(CurrentWebPositionMmProperty);
        set => SetValue(CurrentWebPositionMmProperty, value);
    }

    private readonly Typeface _typeface = new("Segoe UI");
    private readonly ToolTip _toolTip = new();

    public RollDefectMapControl()
    {
        ToolTip = _toolTip;
        _toolTip.IsOpen = false;
        MouseMove += HandleMouseMove;
        MouseLeave += (_, _) => _toolTip.IsOpen = false;
    }

    private void HandleMouseMove(object sender, MouseEventArgs e)
    {
        var session = Session;
        if (session == null || session.Defects.Count == 0 || ActualHeight <= 40 || ActualWidth <= 60)
        {
            _toolTip.IsOpen = false;
            return;
        }

        var pt = e.GetPosition(this);
        double leftMargin = 50.0;
        double rightMargin = 20.0;
        double topMargin = 20.0;
        double bottomMargin = 30.0;

        double plotW = Math.Max(10, ActualWidth - leftMargin - rightMargin);
        double plotH = Math.Max(10, ActualHeight - topMargin - bottomMargin);
        double totalMeters = Math.Max(1.0, session.TotalLengthMeters);
        double rollWidth = Math.Max(100.0, session.RollWidthMm);

        RollDefectItem? hoveredDefect = null;
        double minDistance = 12.0; // Bán kính bắt dính hover 12px

        foreach (var defect in session.Defects)
        {
            double defectMeter = defect.WebY_Mm / 1000.0;
            double posX = leftMargin + ((defect.WebX_Mm / rollWidth) * plotW);
            double posY = topMargin + ((defectMeter / totalMeters) * plotH);

            double dist = Math.Sqrt(Math.Pow(pt.X - posX, 2) + Math.Pow(pt.Y - posY, 2));
            if (dist < minDistance)
            {
                minDistance = dist;
                hoveredDefect = defect;
            }
        }

        if (hoveredDefect != null)
        {
            _toolTip.Content = $"[{hoveredDefect.DefectType}] - {hoveredDefect.Severity}\n" +
                               $"Vị trí dọc: {(hoveredDefect.WebY_Mm / 1000.0):F3} m ({hoveredDefect.WebY_Mm:F1} mm)\n" +
                               $"Vị trí ngang: {hoveredDefect.WebX_Mm:F1} mm\n" +
                               $"Kích thước: {hoveredDefect.Width_Mm:F2} x {hoveredDefect.Length_Mm:F2} mm (Diện tích: {hoveredDefect.Area_Mm2:F2} mm²)\n" +
                               $"Trạng thái: {(hoveredDefect.RejectTriggered ? "🔴 Đã Reject" : "🟢 PASS")}";
            _toolTip.IsOpen = true;
        }
        else
        {
            _toolTip.IsOpen = false;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double leftMargin = 50.0;
        double rightMargin = 20.0;
        double topMargin = 20.0;
        double bottomMargin = 30.0;

        double plotW = Math.Max(10, w - leftMargin - rightMargin);
        double plotH = Math.Max(10, h - topMargin - bottomMargin);

        // 1. Nền tổng thể dải cuộn (Web Background)
        var bgBrush = (Brush?)TryFindResource("PanelBackgroundBrush") ?? new SolidColorBrush(Color.FromRgb(15, 23, 42));
        var ribbonBrush = (Brush?)TryFindResource("PanelAltBackgroundBrush") ?? new SolidColorBrush(Color.FromRgb(30, 41, 59));
        var borderPen = new Pen((Brush?)TryFindResource("BorderBrush") ?? new SolidColorBrush(Color.FromRgb(51, 65, 85)), 1.0);
        var textBrush = (Brush?)TryFindResource("TextMutedBrush") ?? new SolidColorBrush(Color.FromRgb(148, 163, 184));

        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, w, h));
        dc.DrawRectangle(ribbonBrush, borderPen, new Rect(leftMargin, topMargin, plotW, plotH));

        var session = Session;
        double totalMeters = session != null && session.TotalLengthMeters > 0 ? session.TotalLengthMeters : 10.0;
        double rollWidth = session != null && session.RollWidthMm > 0 ? session.RollWidthMm : 500.0;

        // 2. Vẽ các vạch chia mét dài (Y-axis Grid)
        int meterSteps = Math.Max(2, (int)Math.Ceiling(totalMeters / 5.0));
        double stepValue = totalMeters / meterSteps;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.0);
        for (int i = 0; i <= meterSteps; i++)
        {
            double m = i * stepValue;
            double yPos = topMargin + ((m / totalMeters) * plotH);

            dc.DrawLine(gridPen, new Point(leftMargin, yPos), new Point(leftMargin + plotW, yPos));

            var ft = new FormattedText(
                $"{m:F1}m",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                10.0,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(ft, new Point(5, yPos - (ft.Height / 2)));
        }

        // 3. Vẽ vạch chỉ vị trí quét hiện tại (Current Scanner Position)
        double currentMeter = CurrentWebPositionMm / 1000.0;
        if (currentMeter >= 0 && currentMeter <= totalMeters)
        {
            double currentY = topMargin + ((currentMeter / totalMeters) * plotH);
            var scannerPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2.0);
            dc.DrawLine(scannerPen, new Point(leftMargin - 5, currentY), new Point(leftMargin + plotW + 5, currentY));
        }

        // 4. Vẽ các chấm vết khuyết tật trên dải cuộn
        if (session != null && session.Defects != null)
        {
            var rejectBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Đỏ
            var warnBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));   // Vàng
            var defectPen = new Pen(new SolidColorBrush(Colors.White), 1.0);

            foreach (var defect in session.Defects)
            {
                double defectMeter = defect.WebY_Mm / 1000.0;
                double posX = leftMargin + ((defect.WebX_Mm / rollWidth) * plotW);
                double posY = topMargin + ((defectMeter / totalMeters) * plotH);

                var fillBrush = defect.Severity >= DefectSeverity.Reject ? rejectBrush : warnBrush;
                double radius = defect.Severity >= DefectSeverity.Reject ? 4.5 : 3.5;

                dc.DrawEllipse(fillBrush, defectPen, new Point(posX, posY), radius, radius);
            }
        }
    }
}

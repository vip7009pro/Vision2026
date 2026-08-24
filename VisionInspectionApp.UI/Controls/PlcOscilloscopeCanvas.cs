using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Controls;

public sealed class OscilloscopeChannelRenderData
{
    public int ChannelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsBit { get; set; } = true;
    public Color Color { get; set; } = Colors.LimeGreen;
    public IReadOnlyList<PlcOscilloscopeSample> Samples { get; set; } = Array.Empty<PlcOscilloscopeSample>();
    public double CurrentValue { get; set; }
    public double MinValue { get; set; } = 0;
    public double MaxValue { get; set; } = 1;
}

public sealed class PlcOscilloscopeCanvas : FrameworkElement
{
    public static readonly DependencyProperty ChannelsProperty =
        DependencyProperty.Register(
            nameof(Channels),
            typeof(IReadOnlyList<OscilloscopeChannelRenderData>),
            typeof(PlcOscilloscopeCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimeWindowMsProperty =
        DependencyProperty.Register(
            nameof(TimeWindowMs),
            typeof(double),
            typeof(PlcOscilloscopeCanvas),
            new FrameworkPropertyMetadata(1000.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewOffsetMsProperty =
        DependencyProperty.Register(
            nameof(ViewOffsetMs),
            typeof(double),
            typeof(PlcOscilloscopeCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CursorAPosMsProperty =
        DependencyProperty.Register(
            nameof(CursorAPosMs),
            typeof(double?),
            typeof(PlcOscilloscopeCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CursorBPosMsProperty =
        DependencyProperty.Register(
            nameof(CursorBPosMs),
            typeof(double?),
            typeof(PlcOscilloscopeCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ShowCursorsProperty =
        DependencyProperty.Register(
            nameof(ShowCursors),
            typeof(bool),
            typeof(PlcOscilloscopeCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxSessionTimeMsProperty =
        DependencyProperty.Register(
            nameof(MaxSessionTimeMs),
            typeof(double),
            typeof(PlcOscilloscopeCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<OscilloscopeChannelRenderData>? Channels
    {
        get => (IReadOnlyList<OscilloscopeChannelRenderData>?)GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    public double TimeWindowMs
    {
        get => (double)GetValue(TimeWindowMsProperty);
        set => SetValue(TimeWindowMsProperty, value);
    }

    public double ViewOffsetMs
    {
        get => (double)GetValue(ViewOffsetMsProperty);
        set => SetValue(ViewOffsetMsProperty, value);
    }

    public double? CursorAPosMs
    {
        get => (double?)GetValue(CursorAPosMsProperty);
        set => SetValue(CursorAPosMsProperty, value);
    }

    public double? CursorBPosMs
    {
        get => (double?)GetValue(CursorBPosMsProperty);
        set => SetValue(CursorBPosMsProperty, value);
    }

    public bool ShowCursors
    {
        get => (bool)GetValue(ShowCursorsProperty);
        set => SetValue(ShowCursorsProperty, value);
    }

    public double MaxSessionTimeMs
    {
        get => (double)GetValue(MaxSessionTimeMsProperty);
        set => SetValue(MaxSessionTimeMsProperty, value);
    }

    private readonly Typeface _typeface = new("Segoe UI");
    private readonly Typeface _monoTypeface = new("Consolas");

    private bool _isDraggingCursorA;
    private bool _isDraggingCursorB;

    public event Action<double>? OnTimeWindowChanged;

    public PlcOscilloscopeCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        MouseDown += HandleMouseDown;
        MouseMove += HandleMouseMove;
        MouseUp += HandleMouseUp;
        MouseWheel += HandleMouseWheel;
    }

    private void HandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ShowCursors) return;

        var pt = e.GetPosition(this);
        double leftMargin = 120.0;
        double rightMargin = 20.0;
        double plotW = Math.Max(10, ActualWidth - leftMargin - rightMargin);

        if (pt.X < leftMargin || pt.X > ActualWidth - rightMargin) return;

        double windowMs = Math.Max(10.0, TimeWindowMs);
        double startMs = ViewOffsetMs;
        double clickedTimeMs = startMs + ((pt.X - leftMargin) / plotW) * windowMs;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            CursorAPosMs = clickedTimeMs;
            _isDraggingCursorA = true;
            CaptureMouse();
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            CursorBPosMs = clickedTimeMs;
            _isDraggingCursorB = true;
            CaptureMouse();
        }
    }

    private void HandleMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingCursorA && !_isDraggingCursorB) return;

        var pt = e.GetPosition(this);
        double leftMargin = 120.0;
        double rightMargin = 20.0;
        double plotW = Math.Max(10, ActualWidth - leftMargin - rightMargin);
        double windowMs = Math.Max(10.0, TimeWindowMs);
        double startMs = ViewOffsetMs;

        double clampedX = Math.Clamp(pt.X, leftMargin, ActualWidth - rightMargin);
        double timeMs = startMs + ((clampedX - leftMargin) / plotW) * windowMs;

        if (_isDraggingCursorA)
        {
            CursorAPosMs = timeMs;
        }
        else if (_isDraggingCursorB)
        {
            CursorBPosMs = timeMs;
        }
    }

    private void HandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingCursorA || _isDraggingCursorB)
        {
            _isDraggingCursorA = false;
            _isDraggingCursorB = false;
            ReleaseMouseCapture();
        }
    }

    private void HandleMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 0.8 : 1.25;
        double newWindow = Math.Clamp(TimeWindowMs * factor, 10.0, 60000.0);
        TimeWindowMs = newWindow;
        OnTimeWindowChanged?.Invoke(newWindow);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 100 || h < 60) return;

        // 1. Background (Deep Dark Scope Canvas)
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 16, 20)), null, new Rect(0, 0, w, h));

        double leftMargin = 125.0;
        double rightMargin = 20.0;
        double topMargin = 30.0;
        double bottomMargin = 30.0;

        double plotW = Math.Max(10, w - leftMargin - rightMargin);
        double plotH = Math.Max(10, h - topMargin - bottomMargin);

        var activeChannels = Channels?.Where(c => c.Enabled).ToList() ?? new List<OscilloscopeChannelRenderData>();
        int channelCount = Math.Max(1, activeChannels.Count);
        double channelTrackH = plotH / channelCount;

        // 2. Oscilloscope Grid Lines (Major & Minor)
        var majorGridPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 0, 229, 255)), 1);
        majorGridPen.Freeze();
        var minorGridPen = new Pen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), 0.5)
        {
            DashStyle = DashStyles.Dot
        };
        minorGridPen.Freeze();

        // Vertical Time Division Grid (10 major divisions)
        int timeDivisions = 10;
        for (int i = 0; i <= timeDivisions; i++)
        {
            double gx = leftMargin + (i * (plotW / timeDivisions));
            dc.DrawLine(i == 0 || i == timeDivisions ? majorGridPen : minorGridPen, new Point(gx, topMargin), new Point(gx, topMargin + plotH));
        }

        // Horizontal Track Separators
        for (int i = 0; i <= channelCount; i++)
        {
            double gy = topMargin + (i * channelTrackH);
            dc.DrawLine(majorGridPen, new Point(leftMargin, gy), new Point(leftMargin + plotW, gy));
        }

        // Time Range Info
        double windowMs = Math.Max(10.0, TimeWindowMs);
        double startMs = ViewOffsetMs;
        double endMs = startMs + windowMs;

        // 3. Render Channels
        for (int chIdx = 0; chIdx < activeChannels.Count; chIdx++)
        {
            var ch = activeChannels[chIdx];
            double trackTop = topMargin + (chIdx * channelTrackH);
            double trackBottom = trackTop + channelTrackH;
            double trackMid = trackTop + (channelTrackH / 2.0);

            // Channel Label & Badge on Left Pane
            var chBrush = new SolidColorBrush(ch.Color);
            chBrush.Freeze();
            var chPen = new Pen(chBrush, 2.0);
            chPen.Freeze();

            // Background badge for channel header
            var badgeRect = new Rect(8, trackTop + 6, leftMargin - 16, Math.Min(40, channelTrackH - 12));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(30, ch.Color.R, ch.Color.G, ch.Color.B)), new Pen(chBrush, 1.0), badgeRect, 4, 4);

            string chTitle = $"CH{ch.ChannelId} [{(!string.IsNullOrWhiteSpace(ch.Address) ? ch.Address : ch.Name)}]";
            var titleText = new FormattedText(
                chTitle,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                11,
                chBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(titleText, new Point(badgeRect.Left + 6, badgeRect.Top + 3));

            string liveValStr = ch.IsBit ? (ch.CurrentValue > 0.5 ? "🔴 HIGH (1)" : "⚪ LOW (0)") : $"{ch.CurrentValue:F2}";
            var valBrush = ch.IsBit && ch.CurrentValue > 0.5 ? chBrush : new SolidColorBrush(Color.FromRgb(180, 180, 180));
            valBrush.Freeze();
            var valText = new FormattedText(
                liveValStr,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                10,
                valBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(valText, new Point(badgeRect.Left + 6, badgeRect.Top + 18));

            // Render Waveform Samples
            if (ch.Samples != null && ch.Samples.Count > 0)
            {
                var samples = ch.Samples;
                double padY = 8.0;
                double highY = trackTop + padY;
                double lowY = trackBottom - padY;

                var streamGeom = new StreamGeometry();
                var fillGeom = new StreamGeometry();

                using (var ctx = streamGeom.Open())
                using (var fillCtx = fillGeom.Open())
                {
                    bool isFirst = true;
                    double lastPx = leftMargin;
                    double lastPy = lowY;
                    Point firstFillPoint = new Point(leftMargin, lowY);

                    for (int sIdx = 0; sIdx < samples.Count; sIdx++)
                    {
                        var s = samples[sIdx];
                        if (s.TimestampMs < startMs && sIdx + 1 < samples.Count && samples[sIdx + 1].TimestampMs < startMs)
                        {
                            continue;
                        }
                        if (s.TimestampMs > endMs && isFirst)
                        {
                            break;
                        }

                        double normX = (s.TimestampMs - startMs) / windowMs;
                        double px = leftMargin + (normX * plotW);

                        double py;
                        if (ch.IsBit)
                        {
                            py = s.Value > 0.5 ? highY : lowY;
                        }
                        else
                        {
                            double minV = ch.MinValue;
                            double maxV = Math.Max(minV + 0.001, ch.MaxValue);
                            double normV = Math.Clamp((s.Value - minV) / (maxV - minV), 0.0, 1.0);
                            py = lowY - (normV * (lowY - highY));
                        }

                        if (isFirst)
                        {
                            ctx.BeginFigure(new Point(px, py), false, false);
                            fillCtx.BeginFigure(new Point(px, lowY), true, true);
                            fillCtx.LineTo(new Point(px, py), true, false);
                            firstFillPoint = new Point(px, lowY);
                            isFirst = false;
                        }
                        else
                        {
                            if (ch.IsBit)
                            {
                                // Digital Square Step Waveform
                                ctx.LineTo(new Point(px, lastPy), true, false);
                                ctx.LineTo(new Point(px, py), true, false);

                                fillCtx.LineTo(new Point(px, lastPy), true, false);
                                fillCtx.LineTo(new Point(px, py), true, false);
                            }
                            else
                            {
                                ctx.LineTo(new Point(px, py), true, false);
                                fillCtx.LineTo(new Point(px, py), true, false);
                            }
                        }

                        lastPx = px;
                        lastPy = py;

                        if (s.TimestampMs > endMs) break;
                    }

                    if (!isFirst)
                    {
                        fillCtx.LineTo(new Point(lastPx, lowY), true, false);
                    }
                }

                streamGeom.Freeze();
                fillGeom.Freeze();

                if (ch.IsBit)
                {
                    var glowBrush = new SolidColorBrush(Color.FromArgb(25, ch.Color.R, ch.Color.G, ch.Color.B));
                    glowBrush.Freeze();
                    dc.DrawGeometry(glowBrush, null, fillGeom);
                }

                dc.DrawGeometry(null, chPen, streamGeom);
            }
        }

        // 4. Time Axis Labels at Bottom
        for (int i = 0; i <= timeDivisions; i++)
        {
            double gx = leftMargin + (i * (plotW / timeDivisions));
            double tValMs = startMs + (i * (windowMs / timeDivisions));
            string timeStr = tValMs >= 1000 ? $"{(tValMs / 1000.0):F2}s" : $"{tValMs:F0}ms";

            var tText = new FormattedText(
                timeStr,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _monoTypeface,
                10,
                new SolidColorBrush(Color.FromRgb(150, 160, 175)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(tText, new Point(gx - (tText.Width / 2.0), topMargin + plotH + 6));
        }

        // 5. Cursors A & B Measurement Overlay
        if (ShowCursors)
        {
            double? curA = CursorAPosMs;
            double? curB = CursorBPosMs;

            var cursorAPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 171, 0)), 1.5) { DashStyle = DashStyles.Dash };
            cursorAPen.Freeze();
            var cursorBPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 229, 255)), 1.5) { DashStyle = DashStyles.Dash };
            cursorBPen.Freeze();

            double? xA = null;
            double? xB = null;

            if (curA.HasValue)
            {
                double normA = (curA.Value - startMs) / windowMs;
                if (normA >= 0 && normA <= 1.0)
                {
                    xA = leftMargin + (normA * plotW);
                    dc.DrawLine(cursorAPen, new Point(xA.Value, topMargin), new Point(xA.Value, topMargin + plotH));

                    // Cursor A Flag
                    var flagRect = new Rect(xA.Value - 14, topMargin - 18, 28, 16);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 171, 0)), null, flagRect, 3, 3);
                    var textA = new FormattedText("A", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(textA, new Point(flagRect.Left + 9, flagRect.Top + 1));
                }
            }

            if (curB.HasValue)
            {
                double normB = (curB.Value - startMs) / windowMs;
                if (normB >= 0 && normB <= 1.0)
                {
                    xB = leftMargin + (normB * plotW);
                    dc.DrawLine(cursorBPen, new Point(xB.Value, topMargin), new Point(xB.Value, topMargin + plotH));

                    // Cursor B Flag
                    var flagRect = new Rect(xB.Value - 14, topMargin - 18, 28, 16);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0, 229, 255)), null, flagRect, 3, 3);
                    var textB = new FormattedText("B", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(textB, new Point(flagRect.Left + 9, flagRect.Top + 1));
                }
            }

            // Top-Right Measurement HUD Banner
            if (curA.HasValue && curB.HasValue)
            {
                double dtMs = Math.Abs(curB.Value - curA.Value);
                double freqHz = dtMs > 0.001 ? (1000.0 / dtMs) : 0;
                string hudStr = $"📏 A: {curA.Value:F1}ms  |  B: {curB.Value:F1}ms  |  Δt = {dtMs:F2} ms  |  f = {freqHz:F1} Hz";

                var hudText = new FormattedText(
                    hudStr,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    11.5,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                var hudBg = new Rect(w - rightMargin - hudText.Width - 16, 6, hudText.Width + 16, 20);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(200, 20, 24, 32)), new Pen(new SolidColorBrush(Color.FromRgb(60, 70, 90)), 1), hudBg, 4, 4);
                dc.DrawText(hudText, new Point(hudBg.Left + 8, hudBg.Top + 2));
            }
        }
    }
}

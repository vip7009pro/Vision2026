using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;

namespace VisionInspectionApp.UI.ViewModels.PLC;

public partial class PlcOscilloscopeChannelVM : ObservableObject
{
    [ObservableProperty]
    private int _channelId = 1;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _name = "Channel 1";

    [ObservableProperty]
    private string _address = "X0";

    [ObservableProperty]
    private string _plcId = "PLC1";

    [ObservableProperty]
    private PlcDataType _dataType = PlcDataType.Bool;

    [ObservableProperty]
    private Color _color = Color.FromRgb(0, 230, 118); // Neon Green

    [ObservableProperty]
    private double _currentValue;

    [ObservableProperty]
    private double _pulseWidthMs;

    [ObservableProperty]
    private double _periodMs;

    [ObservableProperty]
    private double _frequencyHz;

    [ObservableProperty]
    private long _transitionCount;

    [ObservableProperty]
    private double _lastTransitionTimeMs;

    [ObservableProperty]
    private double _highDurationMs;

    [ObservableProperty]
    private double _lowDurationMs;

    public string DisplayTitle => $"CH{ChannelId}: {Name} ({Address})";

    public List<PlcOscilloscopeSample> Samples { get; } = new(5000);
    public readonly object SampleLock = new();

    public double LastRisingTimeMs { get; set; } = -1;
    public double LastFallingTimeMs { get; set; } = -1;
    public double LastStateChangeTimeMs { get; set; } = 0;
    public double LastStateValue { get; set; } = 0;

    partial void OnAddressChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
    }
}

public partial class PlcOscilloscopeViewModel : ObservableObject, IDisposable
{
    private readonly IPlcManagerService _plcService;
    private readonly Stopwatch _sessionStopwatch = new();
    private CancellationTokenSource? _samplingCts;
    private Task? _samplingTask;
    private readonly DispatcherTimer _uiRefreshTimer;
    private bool _disposed;

    [ObservableProperty]
    private PlcModel? _selectedPlc;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _samplingIntervalMs = 10; // 10ms (100Hz)

    [ObservableProperty]
    private double _timeWindowMs = 2000.0; // 2 seconds visible window

    [ObservableProperty]
    private double _viewOffsetMs = 0.0;

    [ObservableProperty]
    private double _maxSessionTimeMs = 0.0;

    [ObservableProperty]
    private OscilloscopeTriggerMode _triggerMode = OscilloscopeTriggerMode.FreeRun;

    [ObservableProperty]
    private int _triggerChannelId = 1;

    [ObservableProperty]
    private bool _showCursors = true;

    [ObservableProperty]
    private double? _cursorAPosMs = 200.0;

    [ObservableProperty]
    private double? _cursorBPosMs = 400.0;

    [ObservableProperty]
    private double _deltaTimeMs = 200.0;

    [ObservableProperty]
    private double _deltaFreqHz = 5.0;

    [ObservableProperty]
    private long _totalCapturedSamples;

    [ObservableProperty]
    private string _statusText = "🟢 Sẵn sàng ghi nhận tín hiệu Oscilloscope.";

    public ObservableCollection<PlcModel> Plcs => _plcService.Plcs;

    public ObservableCollection<PlcOscilloscopeChannelVM> Channels { get; } = new();

    public ObservableCollection<PlcOscilloscopeEvent> Events { get; } = new();

    [ObservableProperty]
    private PlcOscilloscopeEvent? _selectedEvent;

    [ObservableProperty]
    private IReadOnlyList<OscilloscopeChannelRenderData>? _renderChannels;

    public Array DataTypes => Enum.GetValues(typeof(PlcDataType));
    public Array TriggerModes => Enum.GetValues(typeof(OscilloscopeTriggerMode));

    public int[] AvailableIntervals { get; } = new[] { 1, 2, 5, 10, 20, 50, 100 };
    public double[] AvailableTimeWindows { get; } = new[] { 500.0, 1000.0, 2000.0, 5000.0, 10000.0, 30000.0, 60000.0 };

    public ObservableCollection<string> AvailableTagNames { get; } = new();

    public PlcOscilloscopeViewModel(IPlcManagerService plcService)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));

        SelectedPlc = Plcs.FirstOrDefault();
        InitializeDefaultChannels();
        RefreshAvailableTags();

        _uiRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS UI render update
        };
        _uiRefreshTimer.Tick += OnUiRefreshTick;
        _uiRefreshTimer.Start();

        _plcService.OnTagChanged += HandlePlcTagChanged;

        // Auto start capture
        StartCapture();
    }

    private void InitializeDefaultChannels()
    {
        Channels.Clear();

        // CH1: X0 (Trigger / Sensor) - Neon Green
        Channels.Add(new PlcOscilloscopeChannelVM
        {
            ChannelId = 1,
            Name = "Trigger Sensor",
            Address = "X0",
            DataType = PlcDataType.Bool,
            Color = Color.FromRgb(0, 230, 118),
            Enabled = true
        });

        // CH2: Y1 (Vision Busy) - Neon Cyan
        Channels.Add(new PlcOscilloscopeChannelVM
        {
            ChannelId = 2,
            Name = "Vision Busy",
            Address = "Y1",
            DataType = PlcDataType.Bool,
            Color = Color.FromRgb(0, 229, 255),
            Enabled = true
        });

        // CH3: Y2 (Vision Done) - Neon Yellow
        Channels.Add(new PlcOscilloscopeChannelVM
        {
            ChannelId = 3,
            Name = "Vision Done",
            Address = "Y2",
            DataType = PlcDataType.Bool,
            Color = Color.FromRgb(255, 214, 0),
            Enabled = true
        });

        // CH4: Y0 (Reject Piston) - Neon Pink
        Channels.Add(new PlcOscilloscopeChannelVM
        {
            ChannelId = 4,
            Name = "Reject Piston",
            Address = "Y0",
            DataType = PlcDataType.Bool,
            Color = Color.FromRgb(255, 64, 129),
            Enabled = true
        });
    }

    public void RefreshAvailableTags()
    {
        AvailableTagNames.Clear();
        var common = new[] { "X0", "X1", "X2", "X3", "X4", "Y0", "Y1", "Y2", "Y3", "Y4", "M0", "M1", "M100", "D100", "D200" };
        foreach (var c in common) AvailableTagNames.Add(c);

        foreach (var t in _plcService.Tags)
        {
            if (!string.IsNullOrWhiteSpace(t.Address) && !AvailableTagNames.Contains(t.Address)) AvailableTagNames.Add(t.Address);
            if (!string.IsNullOrWhiteSpace(t.Name) && !AvailableTagNames.Contains(t.Name)) AvailableTagNames.Add(t.Name);
        }
    }

    [RelayCommand]
    public void StartCapture()
    {
        if (IsRunning) return;

        IsRunning = true;
        _sessionStopwatch.Start();

        _samplingCts = new CancellationTokenSource();
        var token = _samplingCts.Token;

        _samplingTask = Task.Run(() => SamplingLoopAsync(token), token);
        StatusText = $"▶ Đang chạy ghi nhận tín hiệu thời gian thực (Chu kỳ quét: {SamplingIntervalMs} ms)...";
    }

    [RelayCommand]
    public void PauseCapture()
    {
        if (!IsRunning) return;

        IsRunning = false;
        try
        {
            _samplingCts?.Cancel();
        }
        catch { }

        StatusText = "⏸ Đã đóng băng sóng (Frozen Waveform). Bạn có thể kéo Cursor A/B để đo khoảng cách thời gian.";
    }

    [RelayCommand]
    public void ClearBuffer()
    {
        lock (_sessionStopwatch)
        {
            _sessionStopwatch.Reset();
            if (IsRunning) _sessionStopwatch.Start();
        }

        foreach (var ch in Channels)
        {
            lock (ch.SampleLock)
            {
                ch.Samples.Clear();
                ch.TransitionCount = 0;
                ch.PulseWidthMs = 0;
                ch.PeriodMs = 0;
                ch.FrequencyHz = 0;
                ch.LastRisingTimeMs = -1;
                ch.LastFallingTimeMs = -1;
                ch.LastStateChangeTimeMs = 0;
            }
        }

        Events.Clear();
        TotalCapturedSamples = 0;
        MaxSessionTimeMs = 0;
        ViewOffsetMs = 0;

        CursorAPosMs = 50;
        CursorBPosMs = 150;

        UpdateRenderData();
        StatusText = "🧹 Đã xóa toàn bộ bộ đệm sóng và lịch sử sự kiện.";
    }

    [RelayCommand]
    public void AutoFitTimeScale()
    {
        if (MaxSessionTimeMs > 100)
        {
            TimeWindowMs = Math.Clamp(MaxSessionTimeMs, 100.0, 60000.0);
            ViewOffsetMs = 0;
        }
    }

    private async Task SamplingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            var swLoop = Stopwatch.StartNew();
            double currentMs = _sessionStopwatch.Elapsed.TotalMilliseconds;
            var now = DateTime.Now;

            var enabledChs = Channels.Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.Address)).ToList();
            if (enabledChs.Count > 0)
            {
                string targetPlcId = SelectedPlc?.Id ?? "PLC1";
                var driver = _plcService.GetDriver(targetPlcId);

                // Build tag list for driver batch read
                var tagList = enabledChs.Select(c => new PlcTag
                {
                    PlcId = targetPlcId,
                    Name = c.Address,
                    Address = c.Address,
                    DataType = c.DataType
                }).ToList();

                IDictionary<string, object?>? readResults = null;
                if (driver != null && driver.IsConnected)
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(Math.Max(500, SamplingIntervalMs * 5));
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                        readResults = await driver.ReadBatchAsync(tagList, linked.Token);
                    }
                    catch { }
                }

                foreach (var ch in enabledChs)
                {
                    double val = 0.0;
                    if (readResults != null && readResults.TryGetValue(ch.Address, out var objVal) && objVal != null)
                    {
                        val = ConvertToDouble(objVal);
                    }
                    else
                    {
                        var cached = _plcService.GetTagValue(targetPlcId, ch.Address);
                        if (cached?.CurrentValue != null)
                        {
                            val = ConvertToDouble(cached.CurrentValue);
                        }
                    }

                    RecordSample(ch, currentMs, now, val);
                }

                TotalCapturedSamples += enabledChs.Count;
            }

            MaxSessionTimeMs = currentMs;
            if (IsRunning && currentMs > TimeWindowMs)
            {
                ViewOffsetMs = currentMs - TimeWindowMs;
            }

            swLoop.Stop();
            int delayMs = SamplingIntervalMs - (int)swLoop.ElapsedMilliseconds;
            if (delayMs > 0)
            {
                try
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    private void RecordSample(PlcOscilloscopeChannelVM ch, double timestampMs, DateTime wallClockTime, double val)
    {
        lock (ch.SampleLock)
        {
            // Detect transitions
            bool isBit = ch.DataType == PlcDataType.Bool;
            double oldVal = ch.LastStateValue;
            bool stateChanged = ch.Samples.Count == 0 || (isBit ? (val > 0.5 != oldVal > 0.5) : Math.Abs(val - oldVal) > 0.0001);

            ch.CurrentValue = val;
            ch.LastStateValue = val;

            if (stateChanged && ch.Samples.Count > 0)
            {
                double duration = timestampMs - ch.LastStateChangeTimeMs;
                ch.TransitionCount++;

                double pulseWidth = 0.0;
                string transType;

                if (isBit)
                {
                    if (val > 0.5)
                    {
                        // Rising Edge (0 -> 1)
                        transType = "🟢 Sườn lên (0→1)";
                        if (ch.LastRisingTimeMs >= 0)
                        {
                            ch.PeriodMs = timestampMs - ch.LastRisingTimeMs;
                            ch.FrequencyHz = ch.PeriodMs > 0.1 ? (1000.0 / ch.PeriodMs) : 0;
                        }
                        ch.LastRisingTimeMs = timestampMs;
                        ch.LowDurationMs = duration;
                    }
                    else
                    {
                        // Falling Edge (1 -> 0)
                        transType = "🔴 Sườn xuống (1→0)";
                        pulseWidth = ch.LastRisingTimeMs >= 0 ? (timestampMs - ch.LastRisingTimeMs) : duration;
                        ch.PulseWidthMs = pulseWidth;
                        ch.LastFallingTimeMs = timestampMs;
                        ch.HighDurationMs = duration;
                    }
                }
                else
                {
                    transType = $"⚡ Biến thiên ({oldVal:F2}→{val:F2})";
                }

                ch.LastStateChangeTimeMs = timestampMs;
                ch.LastTransitionTimeMs = timestampMs;

                // Push to Event log
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    var evt = new PlcOscilloscopeEvent
                    {
                        Index = Events.Count + 1,
                        Timestamp = wallClockTime,
                        ChannelId = ch.ChannelId,
                        ChannelName = ch.Name,
                        Address = ch.Address,
                        OldState = isBit ? (oldVal > 0.5 ? "1 (ON)" : "0 (OFF)") : oldVal.ToString("F2"),
                        NewState = isBit ? (val > 0.5 ? "1 (ON)" : "0 (OFF)") : val.ToString("F2"),
                        DurationMs = duration,
                        PulseWidthMs = pulseWidth,
                        TransitionType = transType
                    };

                    Events.Insert(0, evt);
                    if (Events.Count > 500)
                    {
                        Events.RemoveAt(Events.Count - 1);
                    }
                }));
            }

            ch.Samples.Add(new PlcOscilloscopeSample(timestampMs, wallClockTime, val, isBit));
            if (ch.Samples.Count > 25000)
            {
                ch.Samples.RemoveRange(0, 5000);
            }
        }
    }

    private void HandlePlcTagChanged(object? sender, TagChangedEventArgs e)
    {
        if (e == null) return;
        double currentMs = _sessionStopwatch.Elapsed.TotalMilliseconds;
        var ch = Channels.FirstOrDefault(c => c.Enabled && (string.Equals(c.Address, e.TagName, StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(c.Name, e.TagName, StringComparison.OrdinalIgnoreCase)));
        if (ch != null)
        {
            double val = ConvertToDouble(e.NewValue);
            RecordSample(ch, currentMs, e.Timestamp, val);
        }
    }

    private void OnUiRefreshTick(object? sender, EventArgs e)
    {
        UpdateRenderData();
        UpdateCursorCalculations();
    }

    private void UpdateRenderData()
    {
        var list = new List<OscilloscopeChannelRenderData>();
        foreach (var ch in Channels)
        {
            if (!ch.Enabled) continue;

            PlcOscilloscopeSample[] sampleCopy;
            lock (ch.SampleLock)
            {
                sampleCopy = ch.Samples.ToArray();
            }

            double minV = 0.0;
            double maxV = 1.0;
            if (ch.DataType != PlcDataType.Bool && sampleCopy.Length > 0)
            {
                minV = sampleCopy.Min(s => s.Value);
                maxV = sampleCopy.Max(s => s.Value);
                if (Math.Abs(maxV - minV) < 0.001) { minV -= 1; maxV += 1; }
            }

            list.Add(new OscilloscopeChannelRenderData
            {
                ChannelId = ch.ChannelId,
                Name = ch.Name,
                Address = ch.Address,
                Enabled = ch.Enabled,
                IsBit = ch.DataType == PlcDataType.Bool,
                Color = ch.Color,
                Samples = sampleCopy,
                CurrentValue = ch.CurrentValue,
                MinValue = minV,
                MaxValue = maxV
            });
        }

        RenderChannels = list;
    }

    partial void OnCursorAPosMsChanged(double? value) => UpdateCursorCalculations();
    partial void OnCursorBPosMsChanged(double? value) => UpdateCursorCalculations();

    private void UpdateCursorCalculations()
    {
        if (CursorAPosMs.HasValue && CursorBPosMs.HasValue)
        {
            DeltaTimeMs = Math.Abs(CursorBPosMs.Value - CursorAPosMs.Value);
            DeltaFreqHz = DeltaTimeMs > 0.001 ? (1000.0 / DeltaTimeMs) : 0.0;
        }
    }

    [RelayCommand]
    public void ExportCsv()
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Xuất Lịch Sử Tín Hiệu PLC Oscilloscope ra CSV",
                Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"PlcOscilloscope_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dlg.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("#,Timestamp,TimeString,Channel,Address,OldState,NewState,Duration_ms,PulseWidth_ms,TransitionType");
                foreach (var evt in Events.OrderBy(e => e.Index))
                {
                    sb.AppendLine($"{evt.Index},{evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{evt.TimeString},CH{evt.ChannelId},{evt.Address},{evt.OldState},{evt.NewState},{evt.DurationMs:F2},{evt.PulseWidthMs:F2},{evt.TransitionType}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                StatusText = $"💾 Đã xuất thành công {Events.Count} sự kiện ra tệp: {Path.GetFileName(dlg.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Lỗi xuất CSV: {ex.Message}";
        }
    }

    private static double ConvertToDouble(object? val)
    {
        if (val == null) return 0.0;
        if (val is bool b) return b ? 1.0 : 0.0;
        if (val is IConvertible conv)
        {
            try { return conv.ToDouble(CultureInfo.InvariantCulture); } catch { }
        }
        string s = val.ToString() ?? "";
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
        return 0.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _uiRefreshTimer.Stop();
        _plcService.OnTagChanged -= HandlePlcTagChanged;

        try
        {
            _samplingCts?.Cancel();
            _samplingCts?.Dispose();
        }
        catch { }
    }
}

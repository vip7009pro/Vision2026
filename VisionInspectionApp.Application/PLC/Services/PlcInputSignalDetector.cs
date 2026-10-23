using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

/// <summary>
/// Bộ phát hiện tín hiệu đầu vào công nghiệp từ PLC:
///  - Bắt cạnh lên (Rising Edge 0 -> 1)
///  - Bắt cạnh xuống (Falling Edge 1 -> 0)
///  - Bắt nhấn giữ (Long Press: duy trì mức 1 liên tục trong ít nhất DurationMs)
/// </summary>
public sealed class PlcInputSignalDetector : IDisposable
{
    private readonly object _lock = new();
    private bool _lastState = false;
    private bool _hasReceivedFirstSample = false;
    private DateTime _pressStartTime = DateTime.MinValue;
    private DateTime _lastTriggerTime = DateTime.MinValue;
    private bool _longPressTriggered = false;
    private CancellationTokenSource? _longPressCts;

    public PlcInputSignalType SignalType { get; set; } = PlcInputSignalType.RisingEdge;
    public int DurationMs { get; set; } = 500;

    /// <summary>
    /// Sự kiện kích hoạt khi điều kiện tín hiệu (Cạnh lên / Cạnh xuống / Long Press) được thỏa mãn.
    /// </summary>
    public event EventHandler? OnTriggered;

    /// <summary>
    /// Xử lý một mẫu tín hiệu logic (bool) tại thời điểm now.
    /// Trả về true nếu phát hiện tín hiệu kích hoạt.
    /// </summary>
    public bool ProcessSample(bool currentState, DateTime? timestamp = null)
    {
        DateTime now = timestamp ?? DateTime.UtcNow;
        bool triggered = false;

        lock (_lock)
        {
            if (!_hasReceivedFirstSample)
            {
                _hasReceivedFirstSample = true;
                _lastState = currentState;
                if (currentState && SignalType == PlcInputSignalType.LongPress)
                {
                    _pressStartTime = now;
                    ScheduleLongPressTimer();
                }
                return false;
            }

            bool wasState = _lastState;
            _lastState = currentState;

            switch (SignalType)
            {
                case PlcInputSignalType.RisingEdge:
                    // Cạnh lên: 0 -> 1
                    if (!wasState && currentState)
                    {
                        double lockoutMs = Math.Max(20.0, DurationMs);
                        if ((now - _lastTriggerTime).TotalMilliseconds >= lockoutMs)
                        {
                            _lastTriggerTime = now;
                            triggered = true;
                        }
                    }
                    break;

                case PlcInputSignalType.FallingEdge:
                    // Cạnh xuống: 1 -> 0
                    if (wasState && !currentState)
                    {
                        double lockoutMs = Math.Max(20.0, DurationMs);
                        if ((now - _lastTriggerTime).TotalMilliseconds >= lockoutMs)
                        {
                            _lastTriggerTime = now;
                            triggered = true;
                        }
                    }
                    break;

                case PlcInputSignalType.LongPress:
                    if (!wasState && currentState)
                    {
                        // Bắt đầu nhấn giữ
                        _pressStartTime = now;
                        _longPressTriggered = false;
                        if (DurationMs <= 0)
                        {
                            _longPressTriggered = true;
                            _lastTriggerTime = now;
                            triggered = true;
                        }
                        else
                        {
                            ScheduleLongPressTimer();
                        }
                    }
                    else if (currentState)
                    {
                        // Đang giữ mức 1
                        if (!_longPressTriggered && _pressStartTime != DateTime.MinValue)
                        {
                            if ((now - _pressStartTime).TotalMilliseconds >= Math.Max(1, DurationMs))
                            {
                                _longPressTriggered = true;
                                _lastTriggerTime = now;
                                CancelLongPressTimer();
                                triggered = true;
                            }
                        }
                    }
                    else
                    {
                        // Nhả nút (về 0) -> Hủy timer và chuẩn bị cho lần nhấn tiếp theo
                        _pressStartTime = DateTime.MinValue;
                        _longPressTriggered = false;
                        CancelLongPressTimer();
                    }
                    break;
            }
        }

        if (triggered)
        {
            OnTriggered?.Invoke(this, EventArgs.Empty);
        }

        return triggered;
    }

    /// <summary>
    /// Xử lý mẫu tín hiệu thô (object) từ PLC tag cache.
    /// </summary>
    public bool ProcessSample(object? rawValue, DateTime? timestamp = null)
    {
        bool boolVal = ConvertToBool(rawValue);
        return ProcessSample(boolVal, timestamp);
    }

    private void ScheduleLongPressTimer()
    {
        CancelLongPressTimer();
        if (DurationMs <= 0) return;

        int delayMs = Math.Max(10, DurationMs);
        _longPressCts = new CancellationTokenSource();
        var token = _longPressCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token);
                if (token.IsCancellationRequested) return;

                bool shouldTrigger = false;
                lock (_lock)
                {
                    if (_lastState && !_longPressTriggered && _pressStartTime != DateTime.MinValue)
                    {
                        _longPressTriggered = true;
                        _lastTriggerTime = DateTime.UtcNow;
                        shouldTrigger = true;
                    }
                }

                if (shouldTrigger)
                {
                    OnTriggered?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlcInputSignalDetector] LongPress timer error: {ex.Message}");
            }
        }, token);
    }

    private void CancelLongPressTimer()
    {
        if (_longPressCts != null)
        {
            try
            {
                _longPressCts.Cancel();
                _longPressCts.Dispose();
            }
            catch { }
            _longPressCts = null;
        }
    }

    public static bool ConvertToBool(object? value)
    {
        if (value is null) return false;
        if (value is bool b) return b;
        if (value is int i) return i != 0;
        if (value is short s) return s != 0;
        if (value is ushort us) return us != 0;
        if (value is uint ui) return ui != 0;
        if (value is byte bt) return bt != 0;
        if (value is sbyte sb) return sb != 0;
        if (value is long l) return l != 0;
        if (value is ulong ul) return ul != 0;
        if (value is double d) return Math.Abs(d) > 0.0001;
        if (value is float f) return Math.Abs(f) > 0.0001f;
        if (value is decimal m) return m != 0;

        string str = value.ToString()?.Trim() ?? "";
        if (string.Equals(str, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(str, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(str, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _hasReceivedFirstSample = false;
            _lastState = false;
            _pressStartTime = DateTime.MinValue;
            _longPressTriggered = false;
            CancelLongPressTimer();
        }
    }

    public void Dispose()
    {
        CancelLongPressTimer();
    }
}

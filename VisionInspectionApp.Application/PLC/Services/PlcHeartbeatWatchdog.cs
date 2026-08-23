using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.PLC.Services;

/// <summary>
/// Watchdog Heartbeat 2 chiều PLC <-> Vision PC bảo vệ liên động an toàn 24/7.
/// Tự động ngắt motor / kích hoạt dừng khẩn cấp liên động nếu cáp mạng PLC hoặc phần mềm gặp sự cố.
/// </summary>
public sealed class PlcHeartbeatWatchdog : IDisposable
{
    private readonly IPlcManagerService? _plcManager;
    private readonly CancellationTokenSource _cts = new();
    private Task? _watchdogLoopTask;

    private string _plcId = "PLC1";
    private string _visionHeartbeatTagName = "Y0_VisionHeartbeat";
    private string _plcHeartbeatTagName = "X0_PlcHeartbeat";
    private int _intervalMs = 100;
    private int _timeoutMs = 300;
    private bool _enableEmergencyInterlock = true;
    private string _emergencyStopTagName = "Y10_VisionFault";

    private bool _isPlcAlive = true;
    private bool _heartbeatBit;
    private object? _lastPlcValue;
    private long _lastPlcResponseTimestamp = Stopwatch.GetTimestamp();

    public bool IsPlcAlive => _isPlcAlive;
    public string PlcId { get => _plcId; set => _plcId = value ?? "PLC1"; }
    public string VisionHeartbeatTagName { get => _visionHeartbeatTagName; set => _visionHeartbeatTagName = value ?? "Y0_VisionHeartbeat"; }
    public string PlcHeartbeatTagName { get => _plcHeartbeatTagName; set => _plcHeartbeatTagName = value ?? "X0_PlcHeartbeat"; }
    public int IntervalMs { get => _intervalMs; set => _intervalMs = Math.Clamp(value, 20, 1000); }
    public int TimeoutMs { get => _timeoutMs; set => _timeoutMs = Math.Clamp(value, 50, 5000); }
    public bool EnableEmergencyInterlock { get => _enableEmergencyInterlock; set => _enableEmergencyInterlock = value; }
    public string EmergencyStopTagName { get => _emergencyStopTagName; set => _emergencyStopTagName = value ?? "Y10_VisionFault"; }

    public event EventHandler<bool>? OnPlcHealthChanged;
    public event EventHandler<string>? OnEmergencyInterlockTriggered;

    public PlcHeartbeatWatchdog(IPlcManagerService? plcManager = null, string plcId = "PLC1")
    {
        _plcManager = plcManager;
        _plcId = plcId;
    }

    /// <summary>
    /// Bắt đầu chu trình giám sát Heartbeat ngầm
    /// </summary>
    public void Start()
    {
        if (_watchdogLoopTask != null) return;
        _lastPlcResponseTimestamp = Stopwatch.GetTimestamp();
        _watchdogLoopTask = Task.Run(WatchdogLoopAsync);
    }

    /// <summary>
    /// Vòng lặp giám sát Heartbeat không cấp phát vùng nhớ (Zero-Allocation Periodic Loop)
    /// </summary>
    private async Task WatchdogLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_intervalMs));
        var token = _cts.Token;

        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            if (_plcManager == null) continue;

            try
            {
                // 1. Gửi tín hiệu Heartbeat từ Vision sang PLC (đảo bit 0 <-> 1)
                if (!string.IsNullOrEmpty(_visionHeartbeatTagName))
                {
                    _heartbeatBit = !_heartbeatBit;
                    await _plcManager.WriteTagValueAsync(_plcId, _visionHeartbeatTagName, _heartbeatBit, token);
                }

                // 2. Đọc tín hiệu Heartbeat từ PLC gửi về
                if (!string.IsNullOrEmpty(_plcHeartbeatTagName))
                {
                    var tagVal = _plcManager.GetTagValue(_plcId, _plcHeartbeatTagName);
                    var plcVal = tagVal?.CurrentValue;
                    if (plcVal != null)
                    {
                        if (_lastPlcValue == null || !plcVal.Equals(_lastPlcValue))
                        {
                            _lastPlcValue = plcVal;
                            _lastPlcResponseTimestamp = Stopwatch.GetTimestamp();

                            if (!_isPlcAlive)
                            {
                                _isPlcAlive = true;
                                OnPlcHealthChanged?.Invoke(this, true);
                            }
                        }
                    }
                }

                // 3. Kiểm tra Timeout
                long elapsedTicks = Stopwatch.GetTimestamp() - _lastPlcResponseTimestamp;
                double elapsedMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;

                if (elapsedMs > _timeoutMs)
                {
                    if (_isPlcAlive)
                    {
                        _isPlcAlive = false;
                        OnPlcHealthChanged?.Invoke(this, false);

                        if (_enableEmergencyInterlock && !string.IsNullOrEmpty(_emergencyStopTagName))
                        {
                            // Kích hoạt tín hiệu lỗi liên động dừng máy kéo cuộn
                            await _plcManager.WriteTagValueAsync(_plcId, _emergencyStopTagName, true, token);
                            OnEmergencyInterlockTriggered?.Invoke(this, $"Mất kết nối Heartbeat PLC quá {_timeoutMs}ms! Đã kích hoạt liên động an toàn {_emergencyStopTagName}.");
                        }
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Debug.WriteLine($"[PlcHeartbeatWatchdog] Lỗi heartbeat: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

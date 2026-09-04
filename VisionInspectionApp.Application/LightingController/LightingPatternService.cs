using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.LightingController;

/// <summary>
/// Dịch vụ quản lý và thực thi kịch bản nháy đèn (Lighting Blink Pattern Service).
/// Tích hợp trực tiếp với LightingControllerService và lưu cấu hình bền vững.
/// </summary>
public sealed class LightingPatternService : IDisposable
{
    private readonly LightingControllerService _lightingService;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private CancellationTokenSource? _currentCts;
    private bool _isRunning;
    private string? _currentPatternName;
    private int _currentCycle;
    private int _totalCycles;
    private bool _disposed;

    // Snapshot cấu hình đèn ban đầu trước khi nháy NG để phục hồi
    private readonly Dictionary<int, (bool IsEnabled, int Brightness)> _savedChannelStates = new();

    /// <summary>Kịch bản đang được thực thi hay không.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                IsRunningChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Tên kịch bản đang chạy.</summary>
    public string? CurrentPatternName
    {
        get => _currentPatternName;
        private set => _currentPatternName = value;
    }

    /// <summary>Chu kỳ hiện tại đang chạy (1-indexed).</summary>
    public int CurrentCycle => _currentCycle;

    /// <summary>Tổng số chu kỳ của kịch bản hiện tại.</summary>
    public int TotalCycles => _totalCycles;

    // Events
    public event EventHandler<bool>? IsRunningChanged;
    public event EventHandler<(int cycle, int total, string stepText)>? OnStepProgress;
    public event EventHandler<string>? OnLog;

    public LightingPatternService(LightingControllerService lightingService)
    {
        _lightingService = lightingService;
    }

    /// <summary>
    /// Dừng ngay lập tức kịch bản nháy đèn đang chạy (nếu có).
    /// </summary>
    public void StopCurrentPattern()
    {
        try
        {
            _currentCts?.Cancel();
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// Thực thi kịch bản chào mừng khi mở ứng dụng (Startup Welcome).
    /// </summary>
    public async Task PlayStartupPatternAsync(
        bool enableStartupPattern,
        string? patternId,
        IEnumerable<LightingPatternModel>? patternList,
        int channelCount = 8,
        CancellationToken externalToken = default)
    {
        if (!enableStartupPattern || !_lightingService.IsConnected) return;

        var pattern = ResolvePattern(patternId, patternList, "pattern_welcome");
        if (pattern == null) return;

        Log($"[Startup Pattern] Bắt đầu chạy kịch bản khởi động: '{pattern.Name}' (Cycles: {pattern.RepeatCycles})");
        await PlayPatternAsync(pattern, channelCount, externalToken).ConfigureAwait(false);
        Log($"[Startup Pattern] Kịch bản khởi động hoàn thành.");
    }

    /// <summary>
    /// Thực thi kịch bản tạm biệt khi tắt ứng dụng (Shutdown Wave).
    /// </summary>
    public async Task PlayShutdownPatternAsync(
        bool enableShutdownPattern,
        string? patternId,
        IEnumerable<LightingPatternModel>? patternList,
        int channelCount = 8,
        CancellationToken externalToken = default)
    {
        if (!enableShutdownPattern || !_lightingService.IsConnected) return;

        var pattern = ResolvePattern(patternId, patternList, "pattern_shutdown");
        if (pattern == null) return;

        Log($"[Shutdown Pattern] Bắt đầu chạy kịch bản tắt app: '{pattern.Name}'");
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, externalToken);

        try
        {
            await PlayPatternAsync(pattern, channelCount, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log($"[Shutdown Pattern] Đã dừng hoặc hết thời gian chờ kịch bản tắt app.");
        }
    }

    /// <summary>
    /// Thực thi kịch bản cảnh báo khi kiểm tra hàng NG.
    /// Tự động ghi nhớ trạng thái sáng ban đầu và phục hồi lại sau khi nháy xong.
    /// </summary>
    public async Task PlayNgPatternAsync(
        bool enableNgPattern,
        string? patternId,
        IEnumerable<LightingPatternModel>? patternList,
        int channelCount = 8,
        CancellationToken externalToken = default)
    {
        if (!enableNgPattern || !_lightingService.IsConnected) return;

        var pattern = ResolvePattern(patternId, patternList, "pattern_ng_alert");
        if (pattern == null) return;

        // Dừng bất kỳ pattern nào đang chạy dở
        StopCurrentPattern();

        await _executionLock.WaitAsync(externalToken).ConfigureAwait(false);
        try
        {
            // 1. Ghi nhớ trạng thái sáng hiện tại của các kênh
            CaptureCurrentLightingState(channelCount);

            // 2. Chạy kịch bản NG
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _currentCts = cts;
            IsRunning = true;
            CurrentPatternName = pattern.Name;
            _totalCycles = Math.Max(1, pattern.RepeatCycles);
            _currentCycle = 1;

            var steps = LightingPatternParser.Parse(pattern.Script, channelCount);
            if (steps.Count > 0)
            {
                Log($"[NG Alert Pattern] Kích hoạt chớp cảnh báo lỗi NG: '{pattern.Name}'");
                for (int cycle = 1; cycle <= _totalCycles; cycle++)
                {
                    _currentCycle = cycle;
                    cts.Token.ThrowIfCancellationRequested();
                    await ExecuteStepsAsync(steps, cycle, _totalCycles, cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log($"[NG Alert Pattern] Kịch bản NG đã bị hủy bởi lượt quét mới hoặc thao tác dừng.");
        }
        catch (Exception ex)
        {
            Log($"[NG Alert Pattern] Lỗi thực thi kịch bản: {ex.Message}");
        }
        finally
        {
            // 3. Tự động phục hồi lại trạng thái chiếu sáng ban đầu để quan sát/kiểm tra tiếp
            await RestorePreviousLightingStateAsync(channelCount).ConfigureAwait(false);
            IsRunning = false;
            CurrentPatternName = null;
            _currentCts = null;
            _executionLock.Release();
        }
    }

    /// <summary>
    /// Thực thi kịch bản tùy ý (dùng cho nút Chạy Thử trên giao diện).
    /// </summary>
    public async Task PlayPatternAsync(LightingPatternModel pattern, int channelCount = 8, CancellationToken externalToken = default)
    {
        if (pattern == null || string.IsNullOrWhiteSpace(pattern.Script) || !_lightingService.IsConnected) return;

        StopCurrentPattern();

        await _executionLock.WaitAsync(externalToken).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _currentCts = cts;
            IsRunning = true;
            CurrentPatternName = pattern.Name;
            _totalCycles = Math.Max(1, pattern.RepeatCycles);
            _currentCycle = 1;

            var steps = LightingPatternParser.Parse(pattern.Script, channelCount);
            if (steps.Count == 0) return;

            for (int cycle = 1; cycle <= _totalCycles; cycle++)
            {
                _currentCycle = cycle;
                cts.Token.ThrowIfCancellationRequested();
                await ExecuteStepsAsync(steps, cycle, _totalCycles, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Log($"Kịch bản '{pattern.Name}' đã dừng.");
        }
        finally
        {
            IsRunning = false;
            CurrentPatternName = null;
            _currentCts = null;
            _executionLock.Release();
        }
    }

    /// <summary>
    /// Thực thi tuần tự các bước trong 1 chu kỳ.
    /// </summary>
    private async Task ExecuteStepsAsync(List<LightingPatternStep> steps, int currentCycle, int totalCycles, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();
            OnStepProgress?.Invoke(this, (currentCycle, totalCycles, step.SummaryText));

            if (step.StepType == LightingPatternStepType.Delay)
            {
                if (step.DelayMs > 0)
                {
                    await Task.Delay(step.DelayMs, ct).ConfigureAwait(false);
                }
                continue;
            }

            if (step.StepType == LightingPatternStepType.Command)
            {
                // Thực thi trên từng kênh chỉ định
                foreach (var ch in step.Channels)
                {
                    ct.ThrowIfCancellationRequested();

                    // Đặt độ sáng trước (nếu có và nếu Bật)
                    if (step.Brightness.HasValue && step.PowerOn != false)
                    {
                        await _lightingService.SetBrightnessAsync(ch, step.Brightness.Value, ct).ConfigureAwait(false);
                    }

                    // Đặt trạng thái Bật / Tắt
                    if (step.PowerOn.HasValue)
                    {
                        await _lightingService.SetChannelPowerAsync(ch, step.PowerOn.Value, ct).ConfigureAwait(false);
                    }
                }

                // Chờ khoảng trễ sau lệnh
                if (step.DelayMs > 0)
                {
                    await Task.Delay(step.DelayMs, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private void CaptureCurrentLightingState(int channelCount)
    {
        _savedChannelStates.Clear();
        var lastState = _lightingService.LastKnownState;
        int total = channelCount > 0 ? channelCount : 8;

        for (int ch = 0; ch < total && ch < 8; ch++)
        {
            if (lastState != null && ch < lastState.Channels.Length)
            {
                var channelState = lastState.Channels[ch];
                _savedChannelStates[ch] = (channelState.IsEnabled, channelState.Brightness);
            }
            else
            {
                _savedChannelStates[ch] = (true, 120);
            }
        }
    }

    private async Task RestorePreviousLightingStateAsync(int channelCount)
    {
        if (_savedChannelStates.Count == 0 || !_lightingService.IsConnected) return;

        try
        {
            foreach (var kvp in _savedChannelStates)
            {
                int ch = kvp.Key;
                var (enabled, brightness) = kvp.Value;

                await _lightingService.SetChannelPowerAsync(ch, enabled).ConfigureAwait(false);
                if (enabled)
                {
                    await _lightingService.SetBrightnessAsync(ch, brightness).ConfigureAwait(false);
                }
            }
            Log("[Lighting Pattern] Đã phục hồi lại trạng thái đèn chiếu sáng ban đầu.");
        }
        catch (Exception ex)
        {
            Log($"[Lighting Pattern] Không thể phục hồi trạng thái đèn: {ex.Message}");
        }
    }

    private static LightingPatternModel? ResolvePattern(string? patternId, IEnumerable<LightingPatternModel>? patternList, string fallbackId)
    {
        var list = patternList?.ToList();
        if (list == null || list.Count == 0)
        {
            list = LightingPatternModel.CreateDefaultPatterns();
        }

        // Tìm theo patternId
        if (!string.IsNullOrWhiteSpace(patternId))
        {
            var found = list.FirstOrDefault(p => string.Equals(p.Id, patternId, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }

        // Fallback theo fallbackId
        var fallback = list.FirstOrDefault(p => string.Equals(p.Id, fallbackId, StringComparison.OrdinalIgnoreCase));
        return fallback ?? list.FirstOrDefault();
    }

    private void Log(string message)
    {
        OnLog?.Invoke(this, message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCurrentPattern();
        _executionLock.Dispose();
    }
}

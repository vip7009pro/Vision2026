using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Application;

namespace VisionInspectionApp.UI.Services.Plc;

public sealed class PlcOrchestratorService : IAsyncDisposable
{
    private readonly GlobalAppSettingsService _globalSettings;
    private readonly CameraService _cameraService;
    private readonly IConfigService _configService;
    private readonly IInspectionService _inspectionService;
    private readonly IPlcClient _plc;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private bool _lastTrigger;
    private DateTime _lastTriggerEdgeTime = DateTime.MinValue;

    public PlcOrchestratorService(
        GlobalAppSettingsService globalSettings,
        CameraService cameraService,
        IConfigService configService,
        IInspectionService inspectionService,
        IPlcClient plcClient)
    {
        _globalSettings = globalSettings;
        _cameraService = cameraService;
        _configService = configService;
        _inspectionService = inspectionService;
        _plc = plcClient;
    }

    public bool IsRunning => _loopTask is not null && !_loopTask.IsCompleted;

    public bool IsConnected => _plc.IsConnected;

    public PlcAppState CurrentState { get; private set; } = PlcAppState.Idle;

    public string? LastError { get; private set; }

    public event EventHandler<string>? Log;
    public event EventHandler? StateChanged;

    public async Task ConnectAndStartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        var s = _globalSettings.Settings.Plc;
        await _plc.ConnectAsync(s.LogicalStationNumber, cancellationToken).ConfigureAwait(false);
        WriteLog($"PLC connected. LogicalStation={s.LogicalStationNumber}");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAndDisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            try { _cts.Cancel(); } catch { /* ignore */ }
        }

        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); } catch { /* ignore */ }
        }

        _loopTask = null;

        await SafeWriteStateAsync(PlcAppState.Idle, cancellationToken).ConfigureAwait(false);
        await _plc.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        CurrentState = PlcAppState.Idle;
        OnStateChanged();
        WriteLog("PLC disconnected.");
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        var s = _globalSettings.Settings.Plc;
        _lastTrigger = false;
        LastError = null;

        await SafeWriteStateAsync(PlcAppState.Ready, cancellationToken).ConfigureAwait(false);
        CurrentState = PlcAppState.Ready;
        OnStateChanged();

        while (!cancellationToken.IsCancellationRequested)
        {
            s = _globalSettings.Settings.Plc; // allow live changes from UI

            try
            {
                var clear = await _plc.ReadBitAsync(s.ClearErrorBitDevice, cancellationToken).ConfigureAwait(false);
                if (clear && CurrentState == PlcAppState.Error)
                {
                    LastError = null;
                    await SafeWriteStateAsync(PlcAppState.Ready, cancellationToken).ConfigureAwait(false);
                    CurrentState = PlcAppState.Ready;
                    OnStateChanged();
                    WriteLog("ClearError received -> Ready.");
                }

                var trig = await _plc.ReadBitAsync(s.TriggerBitDevice, cancellationToken).ConfigureAwait(false);
                var now = DateTime.UtcNow;

                var rising = trig && !_lastTrigger;
                _lastTrigger = trig;

                if (rising)
                {
                    if ((now - _lastTriggerEdgeTime).TotalMilliseconds >= Math.Max(0, s.TriggerDebounceMs))
                    {
                        _lastTriggerEdgeTime = now;
                        _ = Task.Run(() => HandleTriggerOnceAsync(cancellationToken), cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                CurrentState = PlcAppState.Error;
                OnStateChanged();
                WriteLog($"PLC loop error: {ex.Message}");
                await SafeWriteStateAsync(PlcAppState.Error, cancellationToken).ConfigureAwait(false);

                // backoff a bit on errors
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(Math.Clamp(s.PollIntervalMs, 1, 2000), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleTriggerOnceAsync(CancellationToken cancellationToken)
    {
        var s = _globalSettings.Settings.Plc;

        try
        {
            await _plc.WriteBitAsync(s.BusyBitDevice, true, cancellationToken).ConfigureAwait(false);
            await SafeWriteStateAsync(PlcAppState.Busy, cancellationToken).ConfigureAwait(false);
            CurrentState = PlcAppState.Busy;
            OnStateChanged();

            var img = _cameraService.TryGetLatestFrameClone();
            if (img is null || img.Empty())
            {
                img?.Dispose();
                await _plc.WriteWordAsync(s.ResultCodeWordDevice, (short)PlcResultCode.NoImage, cancellationToken).ConfigureAwait(false);
                WriteLog("Trigger -> NoImage (camera not running / no frame).");
                await PulseDoneAsync(s, cancellationToken).ConfigureAwait(false);
                return;
            }

            var cfgCode = s.ProductCode;
            if (string.IsNullOrWhiteSpace(cfgCode))
            {
                img.Dispose();
                await _plc.WriteWordAsync(s.ResultCodeWordDevice, (short)PlcResultCode.ConfigMissing, cancellationToken).ConfigureAwait(false);
                WriteLog("Trigger -> ConfigMissing (ProductCode empty).");
                await PulseDoneAsync(s, cancellationToken).ConfigureAwait(false);
                return;
            }

            var config = _configService.LoadConfig(cfgCode);
            var result = await Task.Run(() => _inspectionService.Inspect(img, config), cancellationToken).ConfigureAwait(false);
            img.Dispose();

            var code = result.Pass ? PlcResultCode.Ok : PlcResultCode.Ng;
            await _plc.WriteWordAsync(s.ResultCodeWordDevice, (short)code, cancellationToken).ConfigureAwait(false);
            WriteLog($"Trigger -> Inspect done. Product={cfgCode}, Result={code}");

            await PulseDoneAsync(s, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            CurrentState = PlcAppState.Error;
            OnStateChanged();

            try
            {
                await _plc.WriteWordAsync(s.ResultCodeWordDevice, (short)PlcResultCode.Exception, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore secondary failures
            }

            await SafeWriteStateAsync(PlcAppState.Error, cancellationToken).ConfigureAwait(false);
            WriteLog($"Trigger handler error: {ex.Message}");
        }
        finally
        {
            try { await _plc.WriteBitAsync(s.BusyBitDevice, false, cancellationToken).ConfigureAwait(false); } catch { /* ignore */ }
            try
            {
                if (CurrentState != PlcAppState.Error)
                {
                    await SafeWriteStateAsync(PlcAppState.Ready, cancellationToken).ConfigureAwait(false);
                    CurrentState = PlcAppState.Ready;
                    OnStateChanged();
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task PulseDoneAsync(PlcSettings s, CancellationToken cancellationToken)
    {
        try
        {
            await _plc.WriteBitAsync(s.DoneBitDevice, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(Math.Clamp(s.DonePulseMs, 1, 2000), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { await _plc.WriteBitAsync(s.DoneBitDevice, false, cancellationToken).ConfigureAwait(false); } catch { /* ignore */ }
        }
    }

    private async Task SafeWriteStateAsync(PlcAppState state, CancellationToken cancellationToken)
    {
        var s = _globalSettings.Settings.Plc;
        try
        {
            await _plc.WriteWordAsync(s.AppStateWordDevice, (short)state, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // don't throw from state writes; upper loop handles errors on next cycle
        }
    }

    private void WriteLog(string msg) => Log?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {msg}");

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAndDisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        await _plc.DisposeAsync().ConfigureAwait(false);
        _cts?.Dispose();
        _cts = null;
    }
}


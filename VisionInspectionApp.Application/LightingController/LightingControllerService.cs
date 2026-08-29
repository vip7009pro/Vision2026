using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.LightingController;

/// <summary>
/// Log entry for Lighting Controller communication.
/// </summary>
public sealed class LightingLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// High-level service for communicating with the 8-channel Lighting Controller.
/// Coordinates protocol generation, transport, response parsing, and logging.
/// Thread-safe: all commands are serialized through the transport's SemaphoreSlim.
/// </summary>
public sealed class LightingControllerService : IDisposable
{
    private ILightingTransport? _transport;
    private volatile LightingConnectionState _connectionState = LightingConnectionState.Disconnected;
    private readonly ConcurrentQueue<LightingLogEntry> _logs = new();
    private const int MaxLogCount = 500;
    private bool _disposed;

    // Last known controller state
    private LightingControllerState? _lastKnownState;

    /// <summary>Current connection state.</summary>
    public LightingConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (_connectionState != value)
            {
                _connectionState = value;
                OnConnectionStateChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Whether the transport is connected.</summary>
    public bool IsConnected => _transport?.IsConnected == true && ConnectionState == LightingConnectionState.Connected;

    /// <summary>Last known state from the controller (from ReadAll).</summary>
    public LightingControllerState? LastKnownState => _lastKnownState;

    /// <summary>Last error message, if any.</summary>
    public string? LastError { get; private set; }

    /// <summary>Recent log entries.</summary>
    public IReadOnlyCollection<LightingLogEntry> Logs => _logs.ToArray();

    // Events
    public event EventHandler<LightingConnectionState>? OnConnectionStateChanged;
    public event EventHandler<LightingLogEntry>? OnLogAdded;
    public event EventHandler<LightingControllerState>? OnStateUpdated;
    public event EventHandler<string>? OnError;

    // =====================================================================
    // Connection Management
    // =====================================================================

    /// <summary>Connect to the Lighting Controller.</summary>
    public async Task ConnectAsync(string ip, int port, LightingNetworkMode mode,
        int connectTimeoutMs = 3000, int receiveTimeoutMs = 3000,
        string? lineEnding = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            LastError = null;
            ConnectionState = LightingConnectionState.Connecting;
            Log("INFO", $"Connecting to {ip}:{port} (Mode: {mode})...");

            await DisconnectInternalAsync().ConfigureAwait(false);

            if (mode == LightingNetworkMode.UdpBroadcast)
            {
                var udp = new UdpLightingTransport(receiveTimeoutMs, lineEnding);
                await udp.ConnectAsync(ip, port, cancellationToken).ConfigureAwait(false);
                _transport = udp;
            }
            else
            {
                var tcp = new TcpLightingTransport(connectTimeoutMs, receiveTimeoutMs, lineEnding);
                await tcp.ConnectAsync(ip, port, cancellationToken).ConfigureAwait(false);
                _transport = tcp;
            }

            // Probe / Read current state from controller
            var readResult = await ReadAllParametersAsync(cancellationToken).ConfigureAwait(false);
            if (!readResult.IsSuccess && readResult.ErrorCode == "TIMEOUT")
            {
                var errMsg = $"Không nhận được phản hồi từ bộ điều khiển đèn tại {ip}:{port} sau {receiveTimeoutMs}ms.";
                Log("WARN", errMsg);
                LastError = errMsg;
                ConnectionState = LightingConnectionState.Error;
                await DisconnectInternalAsync().ConfigureAwait(false);
                OnError?.Invoke(this, errMsg);
                return;
            }

            ConnectionState = LightingConnectionState.Connected;
            Log("INFO", $"Connected to {ip}:{port} successfully.");
        }
        catch (Exception ex)
        {
            ConnectionState = LightingConnectionState.Error;
            LastError = $"Không thể kết nối đến {ip}:{port}: {ex.Message}";
            Log("ERROR", LastError);
            await DisconnectInternalAsync().ConfigureAwait(false);
            OnError?.Invoke(this, LastError);
        }
    }

    /// <summary>Connect to the Lighting Controller via Serial RS-232 / COM Port.</summary>
    public async Task ConnectSerialAsync(
        string portName,
        int baudRate = 19200,
        System.IO.Ports.Parity parity = System.IO.Ports.Parity.None,
        int dataBits = 8,
        System.IO.Ports.StopBits stopBits = System.IO.Ports.StopBits.One,
        int readTimeoutMs = 3000,
        int writeTimeoutMs = 3000,
        string? lineEnding = null,
        bool dtrEnable = false,
        bool rtsEnable = false,
        bool autoReadState = true,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            LastError = null;
            ConnectionState = LightingConnectionState.Connecting;
            var leDisplay = lineEnding == null ? "None" : lineEnding.Replace("\r", "\\r").Replace("\n", "\\n");
            Log("INFO", $"Connecting to Serial {portName} ({baudRate}bps, {dataBits} bits, Parity: {parity}, StopBits: {stopBits}, LineEnding: '{leDisplay}')...");

            await DisconnectInternalAsync().ConfigureAwait(false);

            var serialTransport = new SerialLightingTransport(readTimeoutMs, writeTimeoutMs, lineEnding, dtrEnable, rtsEnable);
            await serialTransport.ConnectAsync(portName, baudRate, parity, dataBits, stopBits, cancellationToken).ConfigureAwait(false);
            _transport = serialTransport;

            // Probe / Read current state from controller if enabled
            if (autoReadState)
            {
                var probeResult = await ReadAllParametersAsync(cancellationToken).ConfigureAwait(false);
                if (!probeResult.IsSuccess && probeResult.ErrorCode == "TIMEOUT")
                {
                    // Cổng COM mở được nhưng thiết bị không phản hồi (chưa cắm cáp / tắt nguồn)
                    var errMsg = $"Không nhận được phản hồi từ cổng {portName} sau {readTimeoutMs}ms. Hãy kiểm tra kết nối cáp RS-232, nguồn bộ điều khiển và cài đặt cổng COM.";
                    Log("WARN", errMsg);
                    LastError = errMsg;
                    ConnectionState = LightingConnectionState.Error;
                    await DisconnectInternalAsync().ConfigureAwait(false);
                    OnError?.Invoke(this, errMsg);
                    return;
                }
            }

            ConnectionState = LightingConnectionState.Connected;
            Log("INFO", $"Connected to Serial {portName} successfully.");
        }
        catch (Exception ex)
        {
            ConnectionState = LightingConnectionState.Error;
            LastError = $"Không thể mở cổng {portName}: {ex.Message}";
            Log("ERROR", LastError);
            await DisconnectInternalAsync().ConfigureAwait(false);
            OnError?.Invoke(this, LastError);
        }
    }

    /// <summary>Disconnect from the Lighting Controller.</summary>
    public async Task DisconnectAsync()
    {
        Log("INFO", "Disconnecting...");
        await DisconnectInternalAsync().ConfigureAwait(false);
        ConnectionState = LightingConnectionState.Disconnected;
        Log("INFO", "Disconnected.");
    }

    private async Task DisconnectInternalAsync()
    {
        if (_transport != null)
        {
            try
            {
                await _transport.DisconnectAsync().ConfigureAwait(false);
            }
            catch { /* ignore */ }
            try
            {
                _transport.Dispose();
            }
            catch { /* ignore */ }
            _transport = null;
        }
    }

    // =====================================================================
    // Command Execution
    // =====================================================================

    /// <summary>Send a raw command and return parsed result.</summary>
    public async Task<LightingCommandResult> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _transport == null)
        {
            return LightingCommandResult.Error("ER", "Not connected");
        }

        try
        {
            Log("TX", command);
            var rawResponse = await _transport.SendAndReceiveAsync(command, cancellationToken).ConfigureAwait(false);
            Log("RX", rawResponse);

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                var errMsg = "Không nhận được phản hồi từ thiết bị.";
                Log("ERROR", errMsg);
                OnError?.Invoke(this, errMsg);
                return LightingCommandResult.Error("TIMEOUT", errMsg);
            }

            if (rawResponse.StartsWith("[TIMEOUT]", StringComparison.OrdinalIgnoreCase))
            {
                var errMsg = rawResponse.Length > 9 ? rawResponse.Substring(9).Trim() : "Quá thời gian chờ phản hồi từ thiết bị.";
                Log("ERROR", errMsg);
                LastError = errMsg;
                OnError?.Invoke(this, errMsg);
                return LightingCommandResult.Error("TIMEOUT", errMsg);
            }

            if (rawResponse.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                var errMsg = rawResponse.Length > 7 ? rawResponse.Substring(7).Trim() : "Lỗi giao tiếp thiết bị.";
                Log("ERROR", errMsg);
                LastError = errMsg;
                ConnectionState = LightingConnectionState.Error;
                OnError?.Invoke(this, errMsg);
                return LightingCommandResult.Error("COMM_ERROR", errMsg);
            }

            if (rawResponse.StartsWith("[CANCELLED]", StringComparison.OrdinalIgnoreCase))
            {
                var errMsg = "Thao tác gửi nhận lệnh đã bị hủy.";
                Log("WARN", errMsg);
                return LightingCommandResult.Error("CANCELLED", errMsg);
            }

            var result = LightingProtocol.ParseResponse(rawResponse);

            if (!result.IsSuccess)
            {
                var errMsg = $"Controller error {result.ErrorCode}: {result.ErrorMessage}";
                Log("ERROR", errMsg);
                OnError?.Invoke(this, errMsg);
            }

            if (result.Data != null)
            {
                _lastKnownState = result.Data;
                OnStateUpdated?.Invoke(this, result.Data);
            }

            return result;
        }
        catch (TimeoutException ex)
        {
            Log("ERROR", $"Timeout: {ex.Message}");
            OnError?.Invoke(this, $"Connection timed out: {ex.Message}");
            return LightingCommandResult.Error("TIMEOUT", ex.Message);
        }
        catch (Exception ex) when (ex is System.IO.IOException || ex is System.Net.Sockets.SocketException)
        {
            Log("ERROR", $"Communication error: {ex.Message}");
            ConnectionState = LightingConnectionState.Error;
            OnError?.Invoke(this, $"Communication error: {ex.Message}");
            return LightingCommandResult.Error("COMM_ERROR", ex.Message);
        }
    }

    // =====================================================================
    // Channel Control
    // =====================================================================

    public Task<LightingCommandResult> SetChannelPowerAsync(int channel, bool on, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildSetChannelPower(channel, on), ct);

    public Task<LightingCommandResult> SetBrightnessAsync(int channel, int brightness, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildSetBrightness(channel, brightness), ct);

    public Task<LightingCommandResult> SetLightingTimeAsync(int channel, int timeMs, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildSetLightingTime(channel, timeMs), ct);

    public Task<LightingCommandResult> SetTriggerModeAsync(LightingTriggerMode mode, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildSetTriggerMode(mode), ct);

    /// <summary>Apply multiple settings for a single channel in one command.</summary>
    public Task<LightingCommandResult> ApplyChannelSettingsAsync(int channel, bool? on = null, int? brightness = null, int? timeMs = null, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildChannelConfig(channel, on, brightness, timeMs), ct);

    /// <summary>Read parameters for a specific channel.</summary>
    public Task<LightingCommandResult> ReadChannelAsync(int channel, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildReadChannel(channel), ct);

    /// <summary>Read all parameters from the controller (tries $RD=9999#, fallbacks to $RD=0..N if needed).</summary>
    public Task<LightingCommandResult> ReadAllParametersAsync(CancellationToken ct = default)
        => ReadAllParametersAsync(channelCount: 4, ct);

    /// <summary>Read all parameters from the controller with specified channel count for fallback.</summary>
    public async Task<LightingCommandResult> ReadAllParametersAsync(int channelCount, CancellationToken ct = default)
    {
        var result = await SendCommandAsync(LightingProtocol.BuildReadAll(), ct).ConfigureAwait(false);
        if (result.IsSuccess && result.Data != null)
        {
            return result;
        }

        // Fallback: If RD=9999 didn't return channel data, read per-channel RD=0..channelCount-1
        var compositeState = _lastKnownState ?? new LightingControllerState();
        bool anySuccess = false;
        for (int ch = 0; ch < channelCount && ch < 8; ch++)
        {
            var chResult = await SendCommandAsync(LightingProtocol.BuildReadChannel(ch), ct).ConfigureAwait(false);
            if (chResult.IsSuccess && chResult.Data != null)
            {
                anySuccess = true;
                compositeState.Channels[ch] = chResult.Data.Channels[ch];
            }
        }

        if (anySuccess)
        {
            _lastKnownState = compositeState;
            OnStateUpdated?.Invoke(this, compositeState);
            return LightingCommandResult.WithData(compositeState, "+OK");
        }

        return result;
    }

    // =====================================================================
    // Configuration
    // =====================================================================

    public Task<LightingCommandResult> SaveConfigAsync(CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildSave(), ct);

    public Task<LightingCommandResult> RestoreFactoryDefaultsAsync(CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildFactoryReset(), ct);

    public Task<LightingCommandResult> SetLockAsync(bool locked, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildSetLock(locked), ct);

    public Task<LightingCommandResult> SetNetworkConfigAsync(
        LightingNetworkMode mode, string ip, string subnet, string gateway, int localPort,
        string? destIp = null, int? destPort = null, CancellationToken ct = default)
        => SendCommandAsync(LightingProtocol.BuildNetworkConfig(mode, ip, subnet, gateway, localPort, destIp, destPort), ct);

    // =====================================================================
    // Logging
    // =====================================================================

    private void Log(string level, string message)
    {
        var entry = new LightingLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };

        _logs.Enqueue(entry);
        while (_logs.Count > MaxLogCount)
            _logs.TryDequeue(out _);

        OnLogAdded?.Invoke(this, entry);
    }

    public void ClearLogs()
    {
        while (_logs.TryDequeue(out _)) { }
    }

    // =====================================================================
    // Dispose
    // =====================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            DisconnectInternalAsync().GetAwaiter().GetResult();
        }
        catch { /* ignore on shutdown */ }
    }
}

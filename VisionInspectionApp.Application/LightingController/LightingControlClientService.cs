using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.LightingController;

/// <summary>
/// Dịch vụ kết nối Lighting Control Client giao tiếp với Lighting Control Server qua mạng LAN.
/// </summary>
public sealed class LightingControlClientService : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentQueue<LightingLogEntry> _logs = new();
    private const int MaxLogCount = 500;

    private volatile LightingConnectionState _connectionState = LightingConnectionState.Disconnected;
    private LightingControllerState _state = new();
    private bool _disposed;

    public string ServerIp { get; private set; } = "127.0.0.1";
    public int ServerPort { get; private set; } = 5050;
    public long LastLatencyMs { get; private set; }

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

    public bool IsConnected => _connectionState == LightingConnectionState.Connected && _tcpClient?.Connected == true;
    public LightingControllerState State => _state;
    public IReadOnlyCollection<LightingLogEntry> Logs => _logs.ToArray();

    public event EventHandler<LightingConnectionState>? OnConnectionStateChanged;
    public event EventHandler<LightingControllerState>? OnStateUpdated;
    public event EventHandler<LightingLogEntry>? OnLogAdded;
    public event EventHandler<string>? OnError;

    // =====================================================================
    // Connection Management
    // =====================================================================

    public async Task ConnectAsync(string ip, int port = 5050, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LightingControlClientService));

        ServerIp = ip;
        ServerPort = port;
        ConnectionState = LightingConnectionState.Connecting;
        Log("INFO", $"Đang kết nối tới Lighting Server tại {ip}:{port}...");

        await DisconnectInternalAsync().ConfigureAwait(false);

        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.NoDelay = true;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            await _tcpClient.ConnectAsync(ip, port, cts.Token).ConfigureAwait(false);
            _stream = _tcpClient.GetStream();

            ConnectionState = LightingConnectionState.Connected;
            Log("INFO", $"Đã kết nối thành công tới Server {ip}:{port}.");

            // Đọc trạng thái ban đầu từ server
            _ = ReadAllAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            ConnectionState = LightingConnectionState.Error;
            var err = $"Không thể kết nối đến {ip}:{port}: {ex.Message}";
            Log("ERROR", err);
            OnError?.Invoke(this, err);
            await DisconnectInternalAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        Log("INFO", "Đang ngắt kết nối khỏi Server...");
        await DisconnectInternalAsync().ConfigureAwait(false);
        ConnectionState = LightingConnectionState.Disconnected;
        Log("INFO", "Đã ngắt kết nối.");
    }

    private async Task DisconnectInternalAsync()
    {
        if (_stream != null)
        {
            try
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
            catch { /* ignore */ }
            _stream = null;
        }

        if (_tcpClient != null)
        {
            try
            {
                _tcpClient.Close();
                _tcpClient.Dispose();
            }
            catch { /* ignore */ }
            _tcpClient = null;
        }
    }

    // =====================================================================
    // Command Execution
    // =====================================================================

    public async Task<LightingCommandResult> SendCommandAsync(string command, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null)
        {
            return LightingCommandResult.Error("ER", "Chưa kết nối đến Lighting Server.");
        }

        var sw = Stopwatch.StartNew();
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Log("TX", command);

            var payload = command.EndsWith("\n") ? command : command + "\r\n";
            var sendBytes = Encoding.ASCII.GetBytes(payload);
            await _stream.WriteAsync(sendBytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Đọc phản hồi
            var buffer = new byte[4096];
            var sb = new StringBuilder();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            while (true)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    ConnectionState = LightingConnectionState.Disconnected;
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                var extracted = LightingProtocol.TryExtractResponse(sb.ToString());
                if (extracted != null)
                {
                    sw.Stop();
                    LastLatencyMs = sw.ElapsedMilliseconds;
                    Log("RX", $"{extracted} ({LastLatencyMs}ms)");

                    var parsed = LightingProtocol.ParseResponse(extracted);
                    if (parsed.Data != null)
                    {
                        _state = parsed.Data;
                        OnStateUpdated?.Invoke(this, _state);
                    }
                    return parsed;
                }

                if (!_stream.DataAvailable)
                    break;
            }

            sw.Stop();
            LastLatencyMs = sw.ElapsedMilliseconds;
            var fallback = sb.ToString().Trim();
            Log("RX", $"{fallback} ({LastLatencyMs}ms)");
            return LightingProtocol.ParseResponse(fallback);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            var errMsg = $"Quá thời gian chờ phản hồi ({timeoutMs}ms) từ Server.";
            Log("TIMEOUT", errMsg);
            OnError?.Invoke(this, errMsg);
            return LightingCommandResult.Error("TIMEOUT", errMsg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ConnectionState = LightingConnectionState.Error;
            var errMsg = $"Lỗi truyền thông socket: {ex.Message}";
            Log("ERROR", errMsg);
            OnError?.Invoke(this, errMsg);
            return LightingCommandResult.Error("COMM_ERROR", errMsg);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // =====================================================================
    // Channel Controls
    // =====================================================================

    public async Task<LightingCommandResult> SetChannelPowerAsync(int channel, bool on, CancellationToken ct = default)
    {
        var cmd = LightingProtocol.BuildSetChannelPower(channel, on);
        var res = await SendCommandAsync(cmd, cancellationToken: ct).ConfigureAwait(false);
        if (res.IsSuccess && channel >= 0 && channel < LightingControllerState.MaxChannels)
        {
            _state.Channels[channel].IsEnabled = on;
            OnStateUpdated?.Invoke(this, _state);
        }
        return res;
    }

    public async Task<LightingCommandResult> SetBrightnessAsync(int channel, int brightness, CancellationToken ct = default)
    {
        var cmd = LightingProtocol.BuildSetBrightness(channel, brightness);
        var res = await SendCommandAsync(cmd, cancellationToken: ct).ConfigureAwait(false);
        if (res.IsSuccess && channel >= 0 && channel < LightingControllerState.MaxChannels)
        {
            _state.Channels[channel].Brightness = brightness;
            OnStateUpdated?.Invoke(this, _state);
        }
        return res;
    }

    public async Task<LightingCommandResult> SetLightingTimeAsync(int channel, int timeMs, CancellationToken ct = default)
    {
        var cmd = LightingProtocol.BuildSetLightingTime(channel, timeMs);
        var res = await SendCommandAsync(cmd, cancellationToken: ct).ConfigureAwait(false);
        if (res.IsSuccess && channel >= 0 && channel < LightingControllerState.MaxChannels)
        {
            _state.Channels[channel].LightingTimeMs = timeMs;
            OnStateUpdated?.Invoke(this, _state);
        }
        return res;
    }

    public Task<LightingCommandResult> ReadAllAsync(int channelCount = 8, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(LightingProtocol.BuildReadAll(), cancellationToken: cancellationToken);
    }

    public async Task<LightingCommandResult> TurnOffAllAsync(int channelCount = 8, CancellationToken ct = default)
    {
        for (int ch = 0; ch < channelCount && ch < LightingControllerState.MaxChannels; ch++)
        {
            await SetChannelPowerAsync(ch, false, ct).ConfigureAwait(false);
        }
        return LightingCommandResult.Ok("+OK");
    }

    public async Task<LightingCommandResult> TurnOnAllAsync(int channelCount = 8, int brightness = 120, CancellationToken ct = default)
    {
        for (int ch = 0; ch < channelCount && ch < LightingControllerState.MaxChannels; ch++)
        {
            await SetChannelPowerAsync(ch, true, ct).ConfigureAwait(false);
            await SetBrightnessAsync(ch, brightness, ct).ConfigureAwait(false);
        }
        return LightingCommandResult.Ok("+OK");
    }

    public Task<LightingCommandResult> SetTriggerModeAsync(LightingTriggerMode mode, CancellationToken ct = default)
    {
        return SendCommandAsync(LightingProtocol.BuildSetTriggerMode(mode), cancellationToken: ct);
    }

    public Task<LightingCommandResult> SaveConfigAsync(CancellationToken ct = default)
    {
        return SendCommandAsync(LightingProtocol.BuildSave(), cancellationToken: ct);
    }

    // =====================================================================
    // Logging & Dispose
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
        {
            _logs.TryDequeue(out _);
        }

        OnLogAdded?.Invoke(this, entry);
    }

    public void ClearLogs()
    {
        while (_logs.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisconnectAsync().GetAwaiter().GetResult();
        _sendLock.Dispose();
    }
}

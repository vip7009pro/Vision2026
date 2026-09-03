using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.LightingController;

/// <summary>
/// Thông tin một Client đang kết nối vào Lighting Control Server.
/// </summary>
public sealed class LightingConnectedClientInfo
{
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
    public string RemoteEndPoint { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; } = DateTime.Now;
    public DateTime LastActivityAt { get; set; } = DateTime.Now;
    public long CommandsProcessed { get; set; }
    public string LastCommand { get; set; } = string.Empty;
}

/// <summary>
/// Ghi nhận lịch sử giao tiếp (Traffic log) trên Server.
/// </summary>
public sealed class LightingTrafficLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ClientEndPoint { get; set; } = string.Empty;
    public string Direction { get; set; } = "RX"; // RX = Nhận từ Client, TX = Gửi trả Client, HW = Cổng COM
    public string Content { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public bool IsSuccess { get; set; } = true;
}

/// <summary>
/// Máy chủ Lighting Control Server cho phép nhiều máy tính trong mạng LAN kết nối
/// và điều khiển bộ điều khiển đèn 8 kênh thông qua kết nối TCP Socket.
/// </summary>
public sealed class LightingControlServer : IDisposable
{
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _serverCts;
    private Task? _listenerTask;
    private readonly SemaphoreSlim _execLock = new(1, 1);
    private readonly ConcurrentDictionary<string, (TcpClient Client, LightingConnectedClientInfo Info)> _clients = new();
    private readonly ConcurrentQueue<LightingTrafficLogEntry> _trafficLogs = new();
    private const int MaxLogCount = 500;

    private readonly LightingControllerService _hardwareService;
    private readonly bool _ownsHardwareService;
    private volatile bool _isRunning;
    private volatile bool _disposed;

    // Trạng thái cục bộ đệm của 8 kênh đèn
    private readonly LightingControllerState _cachedState = new();

    public int ListeningPort { get; private set; } = 5050;
    public bool IsRunning => _isRunning && !_disposed;
    public LightingControllerState CurrentState => _cachedState;
    public LightingControllerService HardwareService => _hardwareService;

    // Danh sách Client hiện đang kết nối
    public IReadOnlyList<LightingConnectedClientInfo> ConnectedClients =>
        _clients.Values.Select(x => x.Info).ToList();

    public IReadOnlyCollection<LightingTrafficLogEntry> TrafficLogs => _trafficLogs.ToArray();

    // Sự kiện
    public event EventHandler<bool>? OnServerRunningChanged;
    public event EventHandler<LightingConnectedClientInfo>? OnClientConnected;
    public event EventHandler<LightingConnectedClientInfo>? OnClientDisconnected;
    public event EventHandler<LightingTrafficLogEntry>? OnTrafficLogged;
    public event EventHandler<LightingControllerState>? OnStateChanged;

    public LightingControlServer(LightingControllerService? hardwareService = null)
    {
        if (hardwareService != null)
        {
            _hardwareService = hardwareService;
            _ownsHardwareService = false;
        }
        else
        {
            _hardwareService = new LightingControllerService();
            _ownsHardwareService = true;
        }

        // Lắng nghe cập nhật trạng thái từ hardware service nếu có
        _hardwareService.OnStateUpdated += (_, state) =>
        {
            SyncStateFromHardware(state);
        };
    }

    /// <summary>
    /// Lấy danh sách các địa chỉ IPv4 của các card mạng đang hoạt động trên máy tính.
    /// </summary>
    public static List<string> GetLocalIPv4Addresses()
    {
        var list = new List<string>();
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                    {
                        list.Add(addr.Address.ToString());
                    }
                }
            }
        }
        catch { /* ignore */ }

        if (list.Count == 0)
        {
            list.Add("127.0.0.1");
        }
        return list;
    }

    // =====================================================================
    // Server Lifecycle (Start / Stop)
    // =====================================================================

    /// <summary>
    /// Khởi động máy chủ TCP Server trên cổng chỉ định (mặc định 5050).
    /// Tự động đọc trạng thái thực tế từ đèn (nếu đã kết nối COM) để đồng bộ và phản hồi Client.
    /// </summary>
    public async Task StartServerAsync(int port = 5050, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LightingControlServer));
        if (_isRunning) return;

        ListeningPort = Math.Clamp(port, 1, 65535);
        _serverCts = new CancellationTokenSource();

        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, ListeningPort);
            _tcpListener.Start();
            _isRunning = true;

            LogTraffic("SERVER", "STATUS", $"Máy chủ Lighting Control Server đã khởi động trên cổng {ListeningPort}.", 0, true);
            OnServerRunningChanged?.Invoke(this, true);

            _listenerTask = Task.Run(() => AcceptClientsLoopAsync(_serverCts.Token), _serverCts.Token);

            // Đọc trạng thái thực tế từ thiết bị đèn nếu cổng COM đang kết nối
            if (_hardwareService.IsConnected)
            {
                await ReadStateFromHardwareAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _isRunning = false;
            LogTraffic("SERVER", "ERROR", $"Lỗi khi khởi động Server trên cổng {ListeningPort}: {ex.Message}", 0, false);
            OnServerRunningChanged?.Invoke(this, false);
            throw;
        }
    }

    /// <summary>
    /// Dừng máy chủ TCP Server và đóng toàn bộ kết nối Client.
    /// </summary>
    public async Task StopServerAsync()
    {
        if (!_isRunning) return;
        _isRunning = false;

        try
        {
            _serverCts?.Cancel();
        }
        catch { /* ignore */ }

        try
        {
            _tcpListener?.Stop();
        }
        catch { /* ignore */ }

        // Đóng toàn bộ socket client
        foreach (var kvp in _clients)
        {
            try
            {
                kvp.Value.Client.Close();
                kvp.Value.Client.Dispose();
            }
            catch { /* ignore */ }
        }
        _clients.Clear();

        if (_listenerTask != null)
        {
            try
            {
                await _listenerTask.ConfigureAwait(false);
            }
            catch { /* ignore */ }
            _listenerTask = null;
        }

        LogTraffic("SERVER", "STATUS", "Máy chủ Lighting Control Server đã dừng.", 0, true);
        OnServerRunningChanged?.Invoke(this, false);
    }

    private async Task AcceptClientsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                var client = await _tcpListener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var ep = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                var clientInfo = new LightingConnectedClientInfo
                {
                    RemoteEndPoint = ep,
                    ConnectedAt = DateTime.Now,
                    LastActivityAt = DateTime.Now
                };

                _clients[clientInfo.ClientId] = (client, clientInfo);
                LogTraffic(ep, "CONNECT", $"Client kết nối từ {ep}.", 0, true);
                OnClientConnected?.Invoke(this, clientInfo);

                // Chạy vòng lặp tiếp nhận lệnh từ Client trên task riêng
                _ = Task.Run(() => HandleClientAsync(client, clientInfo, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (!_isRunning) break;
                LogTraffic("SERVER", "ERROR", $"Lỗi chấp nhận kết nối: {ex.Message}", 0, false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // =====================================================================
    // Client Communication Loop
    // =====================================================================

    private async Task HandleClientAsync(TcpClient client, LightingConnectedClientInfo clientInfo, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        var stream = client.GetStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && client.Connected && _isRunning)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) break; // Client đã ngắt kết nối

                sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                clientInfo.LastActivityAt = DateTime.Now;

                // Xử lý các gói tin ASCII kết thúc bằng '#' hoặc dòng hoàn chỉnh
                while (true)
                {
                    var text = sb.ToString();
                    var hashIdx = text.IndexOf('#');
                    if (hashIdx >= 0)
                    {
                        var dollarIdx = text.IndexOf('$');
                        if (dollarIdx >= 0 && dollarIdx <= hashIdx)
                        {
                            var cmd = text.Substring(dollarIdx, hashIdx - dollarIdx + 1);
                            sb.Remove(0, hashIdx + 1);

                            await ProcessClientCommandAsync(stream, clientInfo, cmd, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            // Ký tự trước hash không có $, bỏ qua phần đầu
                            sb.Remove(0, hashIdx + 1);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException)
        {
            // Kết nối đóng bình thường hoặc timeout
        }
        finally
        {
            _clients.TryRemove(clientInfo.ClientId, out _);
            try
            {
                client.Close();
                client.Dispose();
            }
            catch { /* ignore */ }

            LogTraffic(clientInfo.RemoteEndPoint, "DISCONNECT", $"Client đã ngắt kết nối ({clientInfo.RemoteEndPoint}).", 0, true);
            OnClientDisconnected?.Invoke(this, clientInfo);
        }
    }

    private async Task ProcessClientCommandAsync(NetworkStream stream, LightingConnectedClientInfo clientInfo, string command, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        clientInfo.LastCommand = command;
        clientInfo.CommandsProcessed++;

        string response;
        bool isSuccess = true;

        await _execLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            response = await ExecuteCommandInternalAsync(command, ct).ConfigureAwait(false);
            if (response.StartsWith("E") || response.StartsWith("[ERROR]"))
            {
                isSuccess = false;
            }
        }
        catch (Exception ex)
        {
            response = $"[ERROR] Lỗi thực thi lệnh: {ex.Message}";
            isSuccess = false;
        }
        finally
        {
            _execLock.Release();
        }

        sw.Stop();
        LogTraffic(clientInfo.RemoteEndPoint, "RX/TX", $"{command} -> {response}", sw.ElapsedMilliseconds, isSuccess);

        // Gửi phản hồi lại Client qua socket TCP
        try
        {
            var payload = response.EndsWith("\n") ? response : response + "\r\n";
            var bytes = Encoding.ASCII.GetBytes(payload);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch { /* ignore client disconnect while writing */ }
    }

    // =====================================================================
    // Internal Command Execution & Cache Synchronization
    // =====================================================================

    private async Task<string> ExecuteCommandInternalAsync(string rawCommand, CancellationToken ct)
    {
        var trimmed = rawCommand.Trim();

        // 1. Lệnh Đọc Tất Cả ($RD=9999#) -> Phản hồi siêu tốc từ cache
        if (trimmed.Equals("$RD=9999#", StringComparison.OrdinalIgnoreCase))
        {
            // Nếu phần cứng đang kết nối, có thể đọc mới nếu cần, hoặc trả ngay chuỗi $ID=0,L0=...#
            return BuildDataResponseString(_cachedState);
        }

        // 2. Lệnh Đọc 1 Kênh ($RD=0..7#)
        if (trimmed.StartsWith("$RD=", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("#"))
        {
            var param = trimmed.Substring(4, trimmed.Length - 5);
            if (int.TryParse(param, out var ch) && ch >= 0 && ch < LightingControllerState.MaxChannels)
            {
                var chState = _cachedState.Channels[ch];
                return $"$ID=0,L{ch}={chState.Brightness},T{ch}={chState.LightingTimeMs},F{ch}={(chState.IsEnabled ? 1 : 0)}#";
            }
        }

        // 3. Nếu phần cứng đang kết nối qua COM port -> Chuyển tiếp lệnh xuống phần cứng
        string response;
        if (_hardwareService.IsConnected)
        {
            var result = await _hardwareService.SendCommandAsync(trimmed, ct).ConfigureAwait(false);
            response = string.IsNullOrWhiteSpace(result.RawResponse) ? (result.IsSuccess ? "+OK" : "ER") : result.RawResponse;
        }
        else
        {
            // Chế độ mô phỏng / ảo hóa (Virtual simulation mode khi chưa cắm phần cứng thật)
            response = "+OK";
        }

        // 4. Cập nhật bộ đệm trạng thái cục bộ (_cachedState) tương ứng với lệnh vừa chạy
        UpdateCacheFromCommand(trimmed);

        OnStateChanged?.Invoke(this, _cachedState);
        return response;
    }

    private void UpdateCacheFromCommand(string command)
    {
        var content = command.Trim().TrimStart('$').TrimEnd('#');
        var pairs = content.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair.Substring(0, eq).Trim().ToUpperInvariant();
            var val = pair.Substring(eq + 1).Trim();

            // F0-F7 (Power)
            if (key.Length == 2 && key[0] == 'F' && char.IsDigit(key[1]))
            {
                int ch = key[1] - '0';
                if (ch >= 0 && ch < LightingControllerState.MaxChannels)
                    _cachedState.Channels[ch].IsEnabled = val != "0";
            }
            // L0-L7 (Brightness)
            else if (key.Length == 2 && key[0] == 'L' && char.IsDigit(key[1]))
            {
                int ch = key[1] - '0';
                if (ch >= 0 && ch < LightingControllerState.MaxChannels && int.TryParse(val, out var br))
                    _cachedState.Channels[ch].Brightness = Math.Clamp(br, 0, 255);
            }
            // T0-T7 (Time)
            else if (key.Length == 2 && key[0] == 'T' && char.IsDigit(key[1]))
            {
                int ch = key[1] - '0';
                if (ch >= 0 && ch < LightingControllerState.MaxChannels && int.TryParse(val, out var t))
                    _cachedState.Channels[ch].LightingTimeMs = Math.Clamp(t, 1, 999);
            }
            // TR (Trigger Mode)
            else if (key == "TR" && int.TryParse(val, out var tr) && Enum.IsDefined(typeof(LightingTriggerMode), tr))
            {
                _cachedState.TriggerMode = (LightingTriggerMode)tr;
            }
        }
    }

    private void SyncStateFromHardware(LightingControllerState state)
    {
        for (int i = 0; i < LightingControllerState.MaxChannels && i < state.Channels.Length; i++)
        {
            _cachedState.Channels[i].IsEnabled = state.Channels[i].IsEnabled;
            _cachedState.Channels[i].Brightness = state.Channels[i].Brightness;
            _cachedState.Channels[i].LightingTimeMs = state.Channels[i].LightingTimeMs;
        }
        _cachedState.TriggerMode = state.TriggerMode;
        OnStateChanged?.Invoke(this, _cachedState);
    }

    /// <summary>
    /// Xây dựng chuỗi dữ liệu phản hồi chuẩn cho lệnh $RD=9999#.
    /// </summary>
    public static string BuildDataResponseString(LightingControllerState state)
    {
        var sb = new StringBuilder();
        sb.Append("$ID=0");
        for (int i = 0; i < LightingControllerState.MaxChannels; i++)
        {
            var ch = state.Channels[i];
            sb.Append($",L{i}={ch.Brightness},T{i}={ch.LightingTimeMs},F{i}={(ch.IsEnabled ? 1 : 0)}");
        }
        sb.Append($",TR={(int)state.TriggerMode},NE=0#");
        return sb.ToString();
    }

    // =====================================================================
    // Direct Server Operations (Thao tác trực tiếp từ giao diện Server)
    // =====================================================================

    public async Task<LightingCommandResult> SetChannelPowerDirectAsync(int channel, bool on, CancellationToken ct = default)
    {
        var cmd = LightingProtocol.BuildSetChannelPower(channel, on);
        await _execLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ExecuteCommandInternalAsync(cmd, ct).ConfigureAwait(false);
            return LightingCommandResult.Ok("+OK");
        }
        finally
        {
            _execLock.Release();
        }
    }

    public async Task<LightingCommandResult> SetBrightnessDirectAsync(int channel, int brightness, CancellationToken ct = default)
    {
        var cmd = LightingProtocol.BuildSetBrightness(channel, brightness);
        await _execLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ExecuteCommandInternalAsync(cmd, ct).ConfigureAwait(false);
            return LightingCommandResult.Ok("+OK");
        }
        finally
        {
            _execLock.Release();
        }
    }

    public async Task<LightingCommandResult> SetLightingTimeDirectAsync(int channel, int timeMs, CancellationToken ct = default)
    {
        var cmd = LightingProtocol.BuildSetLightingTime(channel, timeMs);
        await _execLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ExecuteCommandInternalAsync(cmd, ct).ConfigureAwait(false);
            return LightingCommandResult.Ok("+OK");
        }
        finally
        {
            _execLock.Release();
        }
    }

    public async Task<LightingCommandResult> TurnOffAllChannelsDirectAsync(int channelCount = 8, CancellationToken ct = default)
    {
        for (int i = 0; i < channelCount && i < LightingControllerState.MaxChannels; i++)
        {
            await SetChannelPowerDirectAsync(i, false, ct).ConfigureAwait(false);
        }
        return LightingCommandResult.Ok("+OK");
    }

    /// <summary>
    /// Đọc trạng thái thực tế hiện tại từ phần cứng đèn (nếu đang kết nối COM),
    /// cập nhật vào bộ đệm _cachedState và phát sự kiện OnStateChanged để đồng bộ lên UI.
    /// </summary>
    public async Task<LightingCommandResult> ReadStateFromHardwareAsync(CancellationToken ct = default)
    {
        if (!_hardwareService.IsConnected)
        {
            return LightingCommandResult.Ok("+OK");
        }

        await _execLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LogTraffic("HARDWARE", "POLL", "Đang đọc trạng thái thực tế từ bộ điều khiển đèn qua COM...", 0, true);

            // 1. Thử đọc toàn bộ 8 kênh bằng lệnh chuẩn $RD=9999#
            var res = await _hardwareService.ReadAllParametersAsync(ct).ConfigureAwait(false);
            if (res.IsSuccess && res.Data != null)
            {
                SyncStateFromHardware(res.Data);
                LogTraffic("HARDWARE", "RX_STATE", $"Đã đồng bộ trạng thái {res.Data.Channels.Length} kênh từ $RD=9999#.", 0, true);
                return res;
            }

            // 2. Nếu $RD=9999# không thành công, thử đọc từng kênh riêng biệt $RD=0#, $RD=1#...
            for (int ch = 0; ch < LightingControllerState.MaxChannels; ch++)
            {
                var chRes = await _hardwareService.SendCommandAsync(LightingProtocol.BuildReadChannel(ch), ct).ConfigureAwait(false);
                if (chRes.IsSuccess && chRes.Data != null)
                {
                    SyncStateFromHardware(chRes.Data);
                }
            }

            LogTraffic("HARDWARE", "RX_STATE", "Đã đồng bộ trạng thái từng kênh từ phần cứng.", 0, true);
            return LightingCommandResult.Ok("+OK");
        }
        catch (Exception ex)
        {
            LogTraffic("HARDWARE", "ERROR", $"Lỗi khi đọc trạng thái từ phần cứng: {ex.Message}", 0, false);
            return LightingCommandResult.Error("READ_ERROR", ex.Message);
        }
        finally
        {
            _execLock.Release();
        }
    }

    // =====================================================================
    // Logging & Cleanup
    // =====================================================================

    private void LogTraffic(string ep, string direction, string content, long elapsedMs, bool isSuccess)
    {
        var entry = new LightingTrafficLogEntry
        {
            Timestamp = DateTime.Now,
            ClientEndPoint = ep,
            Direction = direction,
            Content = content,
            ElapsedMs = elapsedMs,
            IsSuccess = isSuccess
        };

        _trafficLogs.Enqueue(entry);
        while (_trafficLogs.Count > MaxLogCount)
        {
            _trafficLogs.TryDequeue(out _);
        }

        OnTrafficLogged?.Invoke(this, entry);
    }

    public void ClearLogs()
    {
        while (_trafficLogs.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopServerAsync().GetAwaiter().GetResult();
        _execLock.Dispose();

        if (_ownsHardwareService)
        {
            _hardwareService.Dispose();
        }
    }
}

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.LightingController;

/// <summary>
/// Transport abstraction for Lighting Controller communication.
/// Separates network transport from protocol logic.
/// </summary>
public interface ILightingTransport : IDisposable
{
    bool IsConnected { get; }

    Task DisconnectAsync();

    /// <summary>
    /// Send a command string and receive the response.
    /// Thread-safe: internally serialized via SemaphoreSlim.
    /// </summary>
    Task<string> SendAndReceiveAsync(string command, CancellationToken cancellationToken = default);
}

/// <summary>TCP transport for Lighting Controller.</summary>
public sealed class TcpLightingTransport : ILightingTransport
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly int _connectTimeoutMs;
    private readonly int _receiveTimeoutMs;
    private readonly string? _lineEnding;
    private volatile bool _disposed;

    /// <summary>
    /// Create a TCP transport.
    /// </summary>
    /// <param name="connectTimeoutMs">Connection timeout in ms (default 3000).</param>
    /// <param name="receiveTimeoutMs">Receive timeout in ms (default 3000).</param>
    /// <param name="lineEnding">Optional line ending to append (null = none, "\r\n" for CR/LF).</param>
    public TcpLightingTransport(int connectTimeoutMs = 3000, int receiveTimeoutMs = 3000, string? lineEnding = null)
    {
        _connectTimeoutMs = connectTimeoutMs;
        _receiveTimeoutMs = receiveTimeoutMs;
        _lineEnding = lineEnding;
    }

    public bool IsConnected => _client?.Connected == true && _stream != null && !_disposed;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);

        _client = new TcpClient();
        _client.ReceiveTimeout = _receiveTimeoutMs;
        _client.SendTimeout = _connectTimeoutMs;
        _client.NoDelay = true;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_connectTimeoutMs);

        try
        {
            await _client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            _stream = _client.GetStream();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Connection to {host}:{port} timed out after {_connectTimeoutMs}ms.");
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_stream != null)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
                _stream = null;
            }
        }
        catch { /* ignore */ }

        try
        {
            _client?.Close();
            _client?.Dispose();
            _client = null;
        }
        catch { /* ignore */ }
    }

    public async Task<string> SendAndReceiveAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TcpLightingTransport));
        if (!IsConnected) throw new InvalidOperationException("Transport is not connected.");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = _lineEnding != null ? command + _lineEnding : command;
            var sendBytes = Encoding.ASCII.GetBytes(payload);
            await _stream!.WriteAsync(sendBytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Read response
            return await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<string> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_receiveTimeoutMs);

        try
        {
            // Read until we get a complete response
            // Response ends with # for data, or is a short string like +OK, E1, etc.
            while (true)
            {
                var bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
                if (bytesRead == 0) break; // Connection closed

                sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

                var current = sb.ToString().Trim();

                // Check if we have a complete response
                if (current.Equals("+OK", StringComparison.OrdinalIgnoreCase) ||
                    (current.StartsWith("E", StringComparison.OrdinalIgnoreCase) && current.Length <= 3) ||
                    (current.StartsWith("$") && current.EndsWith("#")))
                {
                    break;
                }

                // Also break if data doesn't start with known prefix (avoid infinite loop)
                if (bytesRead < buffer.Length && !_stream.DataAvailable)
                    break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (sb.Length > 0) return sb.ToString().Trim();
            throw new TimeoutException($"Receive timed out after {_receiveTimeoutMs}ms.");
        }

        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        _sendLock.Dispose();
    }
}

/// <summary>UDP transport for Lighting Controller.</summary>
public sealed class UdpLightingTransport : ILightingTransport
{
    private UdpClient? _client;
    private IPEndPoint? _remoteEndpoint;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly int _receiveTimeoutMs;
    private readonly string? _lineEnding;
    private volatile bool _connected;
    private volatile bool _disposed;

    public UdpLightingTransport(int receiveTimeoutMs = 3000, string? lineEnding = null)
    {
        _receiveTimeoutMs = receiveTimeoutMs;
        _lineEnding = lineEnding;
    }

    public bool IsConnected => _connected && _client != null && !_disposed;

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        DisconnectAsync().GetAwaiter().GetResult();

        _remoteEndpoint = new IPEndPoint(IPAddress.Parse(host), port);
        _client = new UdpClient();
        _client.Client.ReceiveTimeout = _receiveTimeoutMs;
        _client.Connect(_remoteEndpoint);
        _connected = true;

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        try
        {
            _client?.Close();
            _client?.Dispose();
            _client = null;
        }
        catch { /* ignore */ }
        return Task.CompletedTask;
    }

    public async Task<string> SendAndReceiveAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UdpLightingTransport));
        if (!IsConnected || _client == null) throw new InvalidOperationException("Transport is not connected.");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = _lineEnding != null ? command + _lineEnding : command;
            var sendBytes = Encoding.ASCII.GetBytes(payload);
            await _client.SendAsync(sendBytes, sendBytes.Length).ConfigureAwait(false);

            // Receive response with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_receiveTimeoutMs);

            try
            {
                var result = await _client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                return Encoding.ASCII.GetString(result.Buffer).Trim();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"UDP receive timed out after {_receiveTimeoutMs}ms.");
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        _sendLock.Dispose();
    }
}

/// <summary>Serial RS-232 / COM Port transport for Lighting Controller.</summary>
public sealed class SerialLightingTransport : ILightingTransport
{
    private System.IO.Ports.SerialPort? _serialPort;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly int _readTimeoutMs;
    private readonly int _writeTimeoutMs;
    private readonly string? _lineEnding;
    private readonly bool _dtrEnable;
    private readonly bool _rtsEnable;
    private volatile bool _disposed;

    /// <summary>
    /// Create a Serial RS-232 transport.
    /// Default config: 19200bps, 8 DataBits, 1 StopBit, No Parity (Half-duplex), DTR/RTS false.
    /// </summary>
    public SerialLightingTransport(
        int readTimeoutMs = 3000,
        int writeTimeoutMs = 3000,
        string? lineEnding = null,
        bool dtrEnable = false,
        bool rtsEnable = false)
    {
        _readTimeoutMs = readTimeoutMs;
        _writeTimeoutMs = writeTimeoutMs;
        _lineEnding = lineEnding;
        _dtrEnable = dtrEnable;
        _rtsEnable = rtsEnable;
    }

    public bool IsConnected => _serialPort?.IsOpen == true && !_disposed;

    public Task ConnectAsync(
        string portName,
        int baudRate = 19200,
        System.IO.Ports.Parity parity = System.IO.Ports.Parity.None,
        int dataBits = 8,
        System.IO.Ports.StopBits stopBits = System.IO.Ports.StopBits.One,
        CancellationToken cancellationToken = default)
    {
        DisconnectAsync().GetAwaiter().GetResult();

        _serialPort = new System.IO.Ports.SerialPort
        {
            PortName = portName,
            BaudRate = baudRate,
            Parity = parity,
            DataBits = dataBits,
            StopBits = stopBits,
            Handshake = System.IO.Ports.Handshake.None,
            ReadTimeout = _readTimeoutMs,
            WriteTimeout = _writeTimeoutMs,
            Encoding = Encoding.ASCII,
            DtrEnable = _dtrEnable,
            RtsEnable = _rtsEnable
        };

        _serialPort.Open();
        Thread.Sleep(150); // Stabilization delay for USB-Serial adapters
        _serialPort.DiscardInBuffer();
        _serialPort.DiscardOutBuffer();

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        try
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    _serialPort.Close();
                }
                _serialPort.Dispose();
                _serialPort = null;
            }
        }
        catch { /* ignore */ }
        return Task.CompletedTask;
    }

    public async Task<string> SendAndReceiveAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SerialLightingTransport));
        if (!IsConnected || _serialPort == null) throw new InvalidOperationException("Serial transport is not connected.");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                _serialPort.DiscardInBuffer();
                var payload = _lineEnding != null ? command + _lineEnding : command;
                var bytes = Encoding.ASCII.GetBytes(payload);
                _serialPort.Write(bytes, 0, bytes.Length);
                try { _serialPort.BaseStream.Flush(); } catch { }

                var sb = new StringBuilder();
                var buffer = new byte[1024];
                var startTime = DateTime.UtcNow;

                while ((DateTime.UtcNow - startTime).TotalMilliseconds < _readTimeoutMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_serialPort.BytesToRead > 0)
                    {
                        int toRead = Math.Min(buffer.Length, _serialPort.BytesToRead);
                        int bytesRead = _serialPort.Read(buffer, 0, toRead);
                        if (bytesRead > 0)
                        {
                            sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

                            var extracted = LightingProtocol.TryExtractResponse(sb.ToString());
                            if (extracted != null)
                            {
                                return extracted;
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(2);
                    }
                }

                if (sb.Length > 0)
                {
                    var fallback = LightingProtocol.TryExtractResponse(sb.ToString()) ?? sb.ToString().Trim();
                    return fallback;
                }
                throw new TimeoutException($"Không nhận được phản hồi từ cổng {_serialPort.PortName} sau {_readTimeoutMs}ms. Hãy kiểm tra kết nối cáp RS-232, nguồn bộ điều khiển và cài đặt cổng COM.");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        _sendLock.Dispose();
    }
}

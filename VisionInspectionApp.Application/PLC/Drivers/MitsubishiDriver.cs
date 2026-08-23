using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Drivers;

/// <summary>
/// Driver for Mitsubishi PLCs using MC Protocol 3E Binary / Socket TCP communication.
/// Supports FX5U, Q, L, iQ-R series and FX3U Ethernet.
/// Includes an offline fallback simulator for seamless execution without hardware.
/// </summary>
public sealed class MitsubishiDriver : IPlcDriver
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private bool _disposed;
    private readonly ConcurrentDictionary<string, object> _simulatedMemory = new(StringComparer.OrdinalIgnoreCase);

    public PlcModel Config { get; }

    public bool IsConnected => ForceSimulationMode || (_tcpClient != null && _tcpClient.Connected);

    public bool ForceSimulationMode { get; set; } = false;

    public MitsubishiDriver(PlcModel config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return true;

            if (ForceSimulationMode)
            {
                Config.State = PlcConnectionState.Connected;
                return true;
            }

            try
            {
                _tcpClient = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMilliseconds(2000)); // 2s timeout attempt

                await _tcpClient.ConnectAsync(Config.IPAddress, Config.Port, cts.Token);
                _stream = _tcpClient.GetStream();
                Config.State = PlcConnectionState.Connected;
                return true;
            }
            catch
            {
                CleanupSocket();
                if (ForceSimulationMode)
                {
                    Config.CpuName = "Simulated MC Protocol";
                    Config.State = PlcConnectionState.Connected;
                    return true;
                }

                Config.State = PlcConnectionState.Error;
                return false;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            CleanupSocket();
            Config.State = PlcConnectionState.Disconnected;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();
        return await ConnectAsync(cancellationToken);
    }

    public async Task<object?> ReadAsync(PlcTag tag, CancellationToken cancellationToken = default)
    {
        if (tag == null) return null;
        var batchResult = await ReadBatchAsync(new[] { tag }, cancellationToken);
        return batchResult.TryGetValue(tag.Name, out var val) ? val : tag.DefaultValue;
    }

    public async Task<bool> WriteAsync(PlcTag tag, object value, CancellationToken cancellationToken = default)
    {
        if (tag == null) return false;
        var dict = new Dictionary<PlcTag, object> { { tag, value } };
        return await WriteBatchAsync(dict, cancellationToken);
    }

    public async Task<IDictionary<string, object?>> ReadBatchAsync(IEnumerable<PlcTag> tags, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object?>();
        var tagList = tags.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Address)).ToList();
        if (tagList.Count == 0) return result;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected && _stream != null)
            {
                try
                {
                    foreach (var tag in tagList)
                    {
                        var rawValue = await ReadMcProtocolTagAsync(tag, cancellationToken);
                        result[tag.Name] = ApplyScale(rawValue, tag.Scale);
                    }
                    return result;
                }
                catch
                {
                    CleanupSocket();
                    if (!ForceSimulationMode)
                    {
                        Config.State = PlcConnectionState.Error;
                        return result;
                    }
                }
            }

            if (!ForceSimulationMode)
            {
                Config.State = PlcConnectionState.Error;
                return result;
            }

            // Fallback / Simulated mode read only when ForceSimulationMode is explicitly enabled
            foreach (var tag in tagList)
            {
                if (_simulatedMemory.TryGetValue(tag.Address, out var existing))
                {
                    result[tag.Name] = ApplyScale(existing, tag.Scale);
                }
                else
                {
                    var def = tag.DefaultValue ?? GetDefaultValueForDataType(tag.DataType);
                    _simulatedMemory[tag.Address] = def;
                    result[tag.Name] = ApplyScale(def, tag.Scale);
                }
            }

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> WriteBatchAsync(IDictionary<PlcTag, object> values, CancellationToken cancellationToken = default)
    {
        if (values == null || values.Count == 0) return true;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected && _stream != null)
            {
                try
                {
                    foreach (var (tag, val) in values)
                    {
                        await WriteMcProtocolTagAsync(tag, val, cancellationToken);
                        _simulatedMemory[tag.Address] = val;
                    }
                    return true;
                }
                catch
                {
                    CleanupSocket();
                    if (!ForceSimulationMode)
                    {
                        Config.State = PlcConnectionState.Error;
                        return false;
                    }
                }
            }

            if (!ForceSimulationMode)
            {
                Config.State = PlcConnectionState.Error;
                return false;
            }

            // Simulated memory write only when ForceSimulationMode is explicitly enabled
            foreach (var (tag, val) in values)
            {
                _simulatedMemory[tag.Address] = val;
            }

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    #region MC Protocol Ethernet Socket Implementation

    private async Task<object?> ReadMcProtocolTagAsync(PlcTag tag, CancellationToken cancellationToken)
    {
        if (_stream == null) return tag.DefaultValue;

        var (deviceCode, headNumber) = ParseDeviceAddress(tag.Address);
        ushort wordCount = GetWordCount(tag.DataType);

        // Frame 3E Batch Read Command: 0x0401 (Subcommand: 0x0000)
        // Payload following command/subcommand is 6 bytes (3 bytes headNumber + 1 byte deviceCode + 2 bytes wordCount)
        byte[] requestPacket = Build3EHeader(command: 0x0401, subcommand: 0x0000, dataLength: 6);

        // Device Code (1 byte), Head Address (3 bytes little endian), Device Count (2 bytes)
        byte[] payload = new byte[6];
        payload[0] = (byte)(headNumber & 0xFF);
        payload[1] = (byte)((headNumber >> 8) & 0xFF);
        payload[2] = (byte)((headNumber >> 16) & 0xFF);
        payload[3] = deviceCode;
        payload[4] = (byte)(wordCount & 0xFF);
        payload[5] = (byte)((wordCount >> 8) & 0xFF);

        byte[] fullPacket = CombineArrays(requestPacket, payload);

        await _stream.WriteAsync(fullPacket, 0, fullPacket.Length, cancellationToken);

        byte[] headerBuffer = new byte[11];
        int readCount = await ReadExactAsync(_stream, headerBuffer, 0, 11, cancellationToken);
        if (readCount < 11) return tag.DefaultValue;

        ushort returnCode = (ushort)(headerBuffer[9] | (headerBuffer[10] << 8));
        if (returnCode != 0) return tag.DefaultValue; // MC Protocol Error

        ushort dataLen = (ushort)(headerBuffer[7] | (headerBuffer[8] << 8));
        int payloadLen = dataLen - 2;
        if (payloadLen <= 0) return tag.DefaultValue;

        byte[] dataBuffer = new byte[payloadLen];
        await ReadExactAsync(_stream, dataBuffer, 0, payloadLen, cancellationToken);

        return DecodeDataBuffer(dataBuffer, tag.DataType);
    }

    private async Task WriteMcProtocolTagAsync(PlcTag tag, object val, CancellationToken cancellationToken)
    {
        if (_stream == null) return;

        var (deviceCode, headNumber) = ParseDeviceAddress(tag.Address);
        byte[] valBytes = EncodeDataBuffer(val, tag.DataType);
        ushort wordCount = (ushort)Math.Max(1, valBytes.Length / 2);

        // Frame 3E Batch Write Command: 0x1401
        // Payload following command/subcommand is 6 bytes header + valBytes.Length
        ushort payloadTotalLen = (ushort)(6 + valBytes.Length);
        byte[] requestPacket = Build3EHeader(command: 0x1401, subcommand: 0x0000, dataLength: payloadTotalLen);

        byte[] payloadHeader = new byte[6];
        payloadHeader[0] = (byte)(headNumber & 0xFF);
        payloadHeader[1] = (byte)((headNumber >> 8) & 0xFF);
        payloadHeader[2] = (byte)((headNumber >> 16) & 0xFF);
        payloadHeader[3] = deviceCode;
        payloadHeader[4] = (byte)(wordCount & 0xFF);
        payloadHeader[5] = (byte)((wordCount >> 8) & 0xFF);

        byte[] fullPacket = CombineArrays(requestPacket, payloadHeader, valBytes);
        await _stream.WriteAsync(fullPacket, 0, fullPacket.Length, cancellationToken);

        byte[] respBuffer = new byte[11];
        await ReadExactAsync(_stream, respBuffer, 0, 11, cancellationToken);
    }

    private static byte[] Build3EHeader(ushort command, ushort subcommand, ushort dataLength)
    {
        byte[] header = new byte[15];
        header[0] = 0x50; // Subheader 3E
        header[1] = 0x00;
        header[2] = 0x00; // Network No
        header[3] = 0xFF; // PLC No
        header[4] = 0xFF; // Target IO Low
        header[5] = 0x03; // Target IO High
        header[6] = 0x00; // Target Station
        // Request Data Length (2 bytes little endian: 2 bytes timer + 2 bytes cmd + 2 bytes subcmd + dataLen)
        ushort totalLen = (ushort)(6 + dataLength);
        header[7] = (byte)(totalLen & 0xFF);
        header[8] = (byte)((totalLen >> 8) & 0xFF);
        header[9] = 0x10; // CPU Timer (4s)
        header[10] = 0x00;
        header[11] = (byte)(command & 0xFF);
        header[12] = (byte)((command >> 8) & 0xFF);
        header[13] = (byte)(subcommand & 0xFF);
        header[14] = (byte)((subcommand >> 8) & 0xFF);
        return header;
    }

    private static (byte code, int headNumber) ParseDeviceAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return (0xA8, 0);

        string clean = address.Trim().ToUpperInvariant();
        string prefix = new string(clean.TakeWhile(char.IsLetter).ToArray());
        string numStr = new string(clean.SkipWhile(char.IsLetter).ToArray());
        int.TryParse(numStr, out int num);

        byte code = prefix switch
        {
            "D" => 0xA8,
            "W" => 0xB4,
            "R" => 0xAF,
            "ZR" => 0xB0,
            "M" => 0x90,
            "X" => 0x9C,
            "Y" => 0x9D,
            "L" => 0x92,
            "B" => 0xA0,
            _ => 0xA8
        };

        return (code, num);
    }

    private static ushort GetWordCount(PlcDataType type) => type switch
    {
        PlcDataType.Bool => 1,
        PlcDataType.Int16 or PlcDataType.UInt16 => 1,
        PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float => 2,
        PlcDataType.Double => 4,
        PlcDataType.String => 8,
        _ => 1
    };

    private static object? DecodeDataBuffer(byte[] buffer, PlcDataType type)
    {
        if (buffer.Length < 2 && type != PlcDataType.Bool) return 0;
        return type switch
        {
            PlcDataType.Bool => buffer[0] != 0,
            PlcDataType.Int16 => BitConverter.ToInt16(buffer, 0),
            PlcDataType.UInt16 => BitConverter.ToUInt16(buffer, 0),
            PlcDataType.Int32 => buffer.Length >= 4 ? BitConverter.ToInt32(buffer, 0) : BitConverter.ToInt16(buffer, 0),
            PlcDataType.UInt32 => buffer.Length >= 4 ? BitConverter.ToUInt32(buffer, 0) : BitConverter.ToUInt16(buffer, 0),
            PlcDataType.Float => buffer.Length >= 4 ? BitConverter.ToSingle(buffer, 0) : 0.0f,
            PlcDataType.Double => buffer.Length >= 8 ? BitConverter.ToDouble(buffer, 0) : 0.0,
            PlcDataType.String => Encoding.ASCII.GetString(buffer).TrimEnd('\0'),
            _ => BitConverter.ToInt16(buffer, 0)
        };
    }

    private static byte[] EncodeDataBuffer(object val, PlcDataType type)
    {
        double dVal = 0;
        if (val != null)
        {
            if (val is bool b) dVal = b ? 1 : 0;
            else if (val is int iVal) dVal = iVal;
            else if (val is double dbl) dVal = dbl;
            else if (val is float flt) dVal = flt;
            else double.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dVal);
        }

        string valStr = val?.ToString() ?? "0";
        return type switch
        {
            PlcDataType.Bool => new byte[] { (byte)(dVal != 0 ? 1 : 0), 0 },
            PlcDataType.Int16 => BitConverter.GetBytes((short)Math.Round(dVal)),
            PlcDataType.UInt16 => BitConverter.GetBytes((ushort)Math.Max(0, Math.Round(dVal))),
            PlcDataType.Int32 => BitConverter.GetBytes((int)Math.Round(dVal)),
            PlcDataType.UInt32 => BitConverter.GetBytes((uint)Math.Max(0, Math.Round(dVal))),
            PlcDataType.Float => BitConverter.GetBytes((float)dVal),
            PlcDataType.Double => BitConverter.GetBytes(dVal),
            PlcDataType.String => Encoding.ASCII.GetBytes(valStr.PadRight(16, '\0')),
            _ => BitConverter.GetBytes((short)Math.Round(dVal))
        };
    }

    private static object? ApplyScale(object? raw, double scale)
    {
        if (raw == null || Math.Abs(scale - 1.0) < 1e-6) return raw;
        if (double.TryParse(raw.ToString(), out double dVal))
        {
            return dVal * scale;
        }
        return raw;
    }

    private static object GetDefaultValueForDataType(PlcDataType type) => type switch
    {
        PlcDataType.Bool => false,
        PlcDataType.Int16 or PlcDataType.UInt16 or PlcDataType.Int32 or PlcDataType.UInt32 => 0,
        PlcDataType.Float or PlcDataType.Double => 0.0,
        PlcDataType.String => string.Empty,
        _ => 0
    };

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    private static byte[] CombineArrays(params byte[][] arrays)
    {
        int totalLen = arrays.Sum(a => a.Length);
        byte[] res = new byte[totalLen];
        int offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, res, offset, arr.Length);
            offset += arr.Length;
        }
        return res;
    }

    private void CleanupSocket()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        _stream = null;
        _tcpClient = null;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupSocket();
        _lock.Dispose();
    }
}

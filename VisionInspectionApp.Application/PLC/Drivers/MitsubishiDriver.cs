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

    private ushort _targetIo = 0x03FF;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return true;

            if (ForceSimulationMode)
            {
                Config.CpuName = "Simulated MC Protocol";
                Config.State = PlcConnectionState.Connected;
                return true;
            }

            try
            {
                _tcpClient = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMilliseconds(3000)); // 3s timeout attempt

                await _tcpClient.ConnectAsync(Config.IPAddress, Config.Port, cts.Token);
                _stream = _tcpClient.GetStream();
                _stream.ReadTimeout = 2000;
                _stream.WriteTimeout = 2000;

                // Thăm dò xác thực gói tin MC Protocol 3E từ PLC thật (Target IO 0x03FF / 0x0000)
                string cpuModel = await ProbeAndIdentifyCpuAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(cpuModel))
                {
                    // Socket TCP mở được nhưng PLC không phản hồi gói tin MC Protocol (Port 5000 đang là MELSOFT port)
                    Config.CpuName = $"Port {Config.Port} không phản hồi MC Protocol 3E";
                    Config.State = PlcConnectionState.Error;
                    CleanupSocket();
                    return false;
                }

                Config.CpuName = cpuModel;
                Config.State = PlcConnectionState.Connected;
                return true;
            }
            catch (Exception ex)
            {
                CleanupSocket();
                if (ForceSimulationMode)
                {
                    Config.CpuName = "Simulated MC Protocol";
                    Config.State = PlcConnectionState.Connected;
                    return true;
                }

                Config.CpuName = $"Lỗi kết nối: {ex.Message}";
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

    /// <summary>
    /// Thăm dò và nhận diện chính xác CPU PLC thật qua Target IO (0x03FF hoặc 0x0000)
    /// </summary>
    private async Task<string> ProbeAndIdentifyCpuAsync(CancellationToken cancellationToken)
    {
        if (_stream == null) return string.Empty;

        // Thử 1: Target IO = 0x03FF (Mặc định MC Protocol 3E)
        _targetIo = 0x03FF;
        string name = await TryReadCpuNameInternalAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        // Thử 2: Target IO = 0x0000 (Self-station CPU FX5U)
        _targetIo = 0x0000;
        name = await TryReadCpuNameInternalAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        // Thử 3: Đọc thử 1 Word Device D0 để kiểm tra kết nối có nhận lệnh không
        _targetIo = 0x03FF;
        if (await TryProbeReadDeviceAsync(0xA8, 0, cancellationToken)) return "FX5U CPU";

        _targetIo = 0x0000;
        if (await TryProbeReadDeviceAsync(0xA8, 0, cancellationToken)) return "FX5U CPU";

        return string.Empty;
    }

    private async Task<bool> TryProbeReadDeviceAsync(byte deviceCode, int headNumber, CancellationToken cancellationToken)
    {
        try
        {
            byte[] req = Build3EHeader(command: 0x0401, subcommand: 0x0000, dataLength: 6);
            byte[] payload = new byte[6];
            payload[0] = (byte)(headNumber & 0xFF);
            payload[1] = (byte)((headNumber >> 8) & 0xFF);
            payload[2] = (byte)((headNumber >> 16) & 0xFF);
            payload[3] = deviceCode;
            payload[4] = 0x01; // 1 word
            payload[5] = 0x00;

            byte[] full = CombineArrays(req, payload);
            await _stream!.WriteAsync(full, 0, full.Length, cancellationToken);

            byte[] header = new byte[11];
            int rCount = await ReadExactAsync(_stream, header, 0, 11, cancellationToken);
            if (rCount >= 11)
            {
                ushort retCode = (ushort)(header[9] | (header[10] << 8));
                if (retCode == 0) return true;
            }
        }
        catch { }
        return false;
    }

    private async Task<string> TryReadCpuNameInternalAsync(CancellationToken cancellationToken)
    {
        if (_stream == null) return string.Empty;

        // TẦNG 1: Thử lệnh MC Protocol Frame 3E chuẩn 0x0101 (Read CPU type)
        try
        {
            byte[] requestPacket = Build3EHeader(command: 0x0101, subcommand: 0x0000, dataLength: 0);
            await _stream.WriteAsync(requestPacket, 0, requestPacket.Length, cancellationToken);

            byte[] headerBuffer = new byte[11];
            int readCount = await ReadExactAsync(_stream, headerBuffer, 0, 11, cancellationToken);
            if (readCount >= 11)
            {
                ushort returnCode = (ushort)(headerBuffer[9] | (headerBuffer[10] << 8));
                if (returnCode == 0)
                {
                    ushort dataLen = (ushort)(headerBuffer[7] | (headerBuffer[8] << 8));
                    int payloadLen = dataLen - 2;
                    if (payloadLen > 0)
                    {
                        byte[] dataBuffer = new byte[payloadLen];
                        await ReadExactAsync(_stream, dataBuffer, 0, payloadLen, cancellationToken);
                        if (dataBuffer.Length >= 16)
                        {
                            string cpuName = Encoding.ASCII.GetString(dataBuffer, 0, 16).Trim('\0', ' ');
                            if (!string.IsNullOrWhiteSpace(cpuName) && cpuName.All(c => c >= 32 && c <= 126))
                            {
                                return cpuName;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // TẦNG 2: Đọc thanh ghi đặc biệt SD200..SD207 (chứa chuỗi Model Name ASCII của FX5U / Q Series)
        try
        {
            byte[] reqSD200 = Build3EHeader(command: 0x0401, subcommand: 0x0000, dataLength: 6);
            byte[] payloadSD200 = new byte[6];
            payloadSD200[0] = 200 & 0xFF;
            payloadSD200[1] = (200 >> 8) & 0xFF;
            payloadSD200[2] = 0x00;
            payloadSD200[3] = 0xA9; // Device SD
            payloadSD200[4] = 0x08; // 8 words
            payloadSD200[5] = 0x00;

            byte[] fullSD200 = CombineArrays(reqSD200, payloadSD200);
            await _stream.WriteAsync(fullSD200, 0, fullSD200.Length, cancellationToken);

            byte[] respHeader = new byte[11];
            int rCount = await ReadExactAsync(_stream, respHeader, 0, 11, cancellationToken);
            if (rCount >= 11)
            {
                ushort retCode = (ushort)(respHeader[9] | (respHeader[10] << 8));
                if (retCode == 0)
                {
                    ushort dataLen = (ushort)(respHeader[7] | (respHeader[8] << 8));
                    int pLen = dataLen - 2;
                    if (pLen >= 16)
                    {
                        byte[] sdData = new byte[pLen];
                        await ReadExactAsync(_stream, sdData, 0, pLen, cancellationToken);
                        string sdModel = Encoding.ASCII.GetString(sdData, 0, 16).Trim('\0', ' ');
                        if (!string.IsNullOrWhiteSpace(sdModel) && (sdModel.StartsWith("FX5", StringComparison.OrdinalIgnoreCase) || sdModel.StartsWith("Q", StringComparison.OrdinalIgnoreCase) || sdModel.StartsWith("R", StringComparison.OrdinalIgnoreCase) || sdModel.StartsWith("L", StringComparison.OrdinalIgnoreCase)))
                        {
                            return sdModel;
                        }
                    }
                }
            }
        }
        catch { }

        // TẦNG 3: Đọc SD0 (CPU Model Code - 1 word)
        try
        {
            byte[] reqSD0 = Build3EHeader(command: 0x0401, subcommand: 0x0000, dataLength: 6);
            byte[] payloadSD0 = new byte[6];
            payloadSD0[0] = 0x00;
            payloadSD0[1] = 0x00;
            payloadSD0[2] = 0x00;
            payloadSD0[3] = 0xA9; // Device SD
            payloadSD0[4] = 0x01; // 1 word
            payloadSD0[5] = 0x00;

            byte[] fullSD0 = CombineArrays(reqSD0, payloadSD0);
            await _stream.WriteAsync(fullSD0, 0, fullSD0.Length, cancellationToken);

            byte[] respHeader = new byte[11];
            int rCount = await ReadExactAsync(_stream, respHeader, 0, 11, cancellationToken);
            if (rCount >= 11)
            {
                ushort retCode = (ushort)(respHeader[9] | (respHeader[10] << 8));
                if (retCode == 0)
                {
                    ushort dataLen = (ushort)(respHeader[7] | (respHeader[8] << 8));
                    int pLen = dataLen - 2;
                    if (pLen >= 2)
                    {
                        byte[] sdData = new byte[pLen];
                        await ReadExactAsync(_stream, sdData, 0, pLen, cancellationToken);
                        ushort modelCode = BitConverter.ToUInt16(sdData, 0);
                        if ((modelCode >= 0x0020 && modelCode <= 0x002F) || (modelCode >= 0x1000 && modelCode <= 0x10FF) || modelCode == 0x0210)
                        {
                            return "FX5U CPU";
                        }
                        if (modelCode >= 0x0030 && modelCode <= 0x003F)
                        {
                            return "MELSEC-Q Series";
                        }
                        if (modelCode >= 0x0050 && modelCode <= 0x005F)
                        {
                            return "MELSEC iQ-R Series";
                        }
                    }
                }
            }
        }
        catch { }

        return string.Empty;
    }

    private static bool IsBitDevice(string address, PlcDataType dataType)
    {
        if (dataType == PlcDataType.Bool) return true;
        if (string.IsNullOrWhiteSpace(address)) return false;

        string clean = address.Trim().ToUpperInvariant();
        if (clean.StartsWith("SM") || clean.StartsWith("TS") || clean.StartsWith("TC") ||
            clean.StartsWith("CS") || clean.StartsWith("CC") || clean.StartsWith("DX") ||
            clean.StartsWith("DY"))
        {
            return true;
        }

        if (clean.StartsWith("X") || clean.StartsWith("Y") || clean.StartsWith("M") ||
            clean.StartsWith("L") || clean.StartsWith("F") || clean.StartsWith("B") ||
            clean.StartsWith("S"))
        {
            return true;
        }

        return false;
    }

    private async Task<object?> ReadMcProtocolTagAsync(PlcTag tag, CancellationToken cancellationToken)
    {
        if (_stream == null) return tag.DefaultValue;

        var (deviceCode, headNumber) = ParseDeviceAddress(tag.Address);
        bool isBit = IsBitDevice(tag.Address, tag.DataType);

        if (isBit)
        {
            // === ĐỌC BIT DEVICE ===
            // Bước 1: Thử đọc Bit đơn lẻ (Command 0x0401, Subcommand 0x0001 - Bit units)
            try
            {
                byte[] requestPacket = Build3EHeader(command: 0x0401, subcommand: 0x0001, dataLength: 6);
                byte[] payload = new byte[6];
                payload[0] = (byte)(headNumber & 0xFF);
                payload[1] = (byte)((headNumber >> 8) & 0xFF);
                payload[2] = (byte)((headNumber >> 16) & 0xFF);
                payload[3] = deviceCode;
                payload[4] = 0x01; // 1 point (bit)
                payload[5] = 0x00;

                byte[] fullPacket = CombineArrays(requestPacket, payload);
                await _stream.WriteAsync(fullPacket, 0, fullPacket.Length, cancellationToken);

                byte[] headerBuffer = new byte[11];
                int readCount = await ReadExactAsync(_stream, headerBuffer, 0, 11, cancellationToken);
                if (readCount >= 11)
                {
                    ushort returnCode = (ushort)(headerBuffer[9] | (headerBuffer[10] << 8));
                    if (returnCode == 0)
                    {
                        ushort dataLen = (ushort)(headerBuffer[7] | (headerBuffer[8] << 8));
                        int payloadLen = dataLen - 2;
                        if (payloadLen > 0)
                        {
                            byte[] dataBuffer = new byte[payloadLen];
                            await ReadExactAsync(_stream, dataBuffer, 0, payloadLen, cancellationToken);
                            bool bitValue = dataBuffer.Length > 0 && (dataBuffer[0] != 0);
                            return bitValue;
                        }
                    }
                }
            }
            catch { }

            // Bước 2: Fallback đọc khối Word bao quanh bit (Command 0x0401, Subcommand 0x0000 - Word units)
            try
            {
                int wordHeadNumber = (headNumber / 16) * 16;
                int bitOffset = headNumber - wordHeadNumber;

                byte[] requestPacket = Build3EHeader(command: 0x0401, subcommand: 0x0000, dataLength: 6);
                byte[] payload = new byte[6];
                payload[0] = (byte)(wordHeadNumber & 0xFF);
                payload[1] = (byte)((wordHeadNumber >> 8) & 0xFF);
                payload[2] = (byte)((wordHeadNumber >> 16) & 0xFF);
                payload[3] = deviceCode;
                payload[4] = 0x01; // 1 word
                payload[5] = 0x00;

                byte[] fullPacket = CombineArrays(requestPacket, payload);
                await _stream.WriteAsync(fullPacket, 0, fullPacket.Length, cancellationToken);

                byte[] headerBuffer = new byte[11];
                int readCount = await ReadExactAsync(_stream, headerBuffer, 0, 11, cancellationToken);
                if (readCount >= 11)
                {
                    ushort returnCode = (ushort)(headerBuffer[9] | (headerBuffer[10] << 8));
                    if (returnCode == 0)
                    {
                        ushort dataLen = (ushort)(headerBuffer[7] | (headerBuffer[8] << 8));
                        int payloadLen = dataLen - 2;
                        if (payloadLen >= 2)
                        {
                            byte[] dataBuffer = new byte[payloadLen];
                            await ReadExactAsync(_stream, dataBuffer, 0, payloadLen, cancellationToken);
                            ushort wordVal = BitConverter.ToUInt16(dataBuffer, 0);
                            bool bitValue = ((wordVal >> bitOffset) & 1) != 0;
                            return bitValue;
                        }
                    }
                }
            }
            catch { }

            return false;
        }
        else
        {
            // === ĐỌC WORD DEVICE: Command 0x0401, Subcommand 0x0000 (Word units) ===
            ushort wordCount = GetWordCount(tag.DataType);
            byte[] requestPacket = Build3EHeader(command: 0x0401, subcommand: 0x0000, dataLength: 6);

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
    }

    private async Task WriteMcProtocolTagAsync(PlcTag tag, object val, CancellationToken cancellationToken)
    {
        if (_stream == null) return;

        var (deviceCode, headNumber) = ParseDeviceAddress(tag.Address);
        bool isBit = IsBitDevice(tag.Address, tag.DataType);

        if (isBit)
        {
            // === GHI BIT DEVICE: Command 0x1401, Subcommand 0x0001 (Bit units) ===
            bool bitState = false;
            if (val is bool b) bitState = b;
            else if (val != null && double.TryParse(val.ToString(), out double dVal)) bitState = dVal != 0;

            // 1 point bit write payload length = 6 bytes header + 1 byte data = 7 bytes
            byte[] requestPacket = Build3EHeader(command: 0x1401, subcommand: 0x0001, dataLength: 7);

            byte[] payloadHeader = new byte[6];
            payloadHeader[0] = (byte)(headNumber & 0xFF);
            payloadHeader[1] = (byte)((headNumber >> 8) & 0xFF);
            payloadHeader[2] = (byte)((headNumber >> 16) & 0xFF);
            payloadHeader[3] = deviceCode;
            payloadHeader[4] = 0x01; // 1 point
            payloadHeader[5] = 0x00;

            // Bit value: 0x10 (ON) hoặc 0x00 (OFF)
            byte[] valBytes = new byte[] { (byte)(bitState ? 0x10 : 0x00) };

            byte[] fullPacket = CombineArrays(requestPacket, payloadHeader, valBytes);
            await _stream.WriteAsync(fullPacket, 0, fullPacket.Length, cancellationToken);

            byte[] respBuffer = new byte[11];
            await ReadExactAsync(_stream, respBuffer, 0, 11, cancellationToken);
        }
        else
        {
            // === GHI WORD DEVICE: Command 0x1401, Subcommand 0x0000 (Word units) ===
            byte[] valBytes = EncodeDataBuffer(val, tag.DataType);
            ushort wordCount = (ushort)Math.Max(1, valBytes.Length / 2);

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
    }

    private byte[] Build3EHeader(ushort command, ushort subcommand, ushort dataLength)
    {
        byte[] header = new byte[15];
        header[0] = 0x50; // Subheader 3E
        header[1] = 0x00;
        header[2] = 0x00; // Network No
        header[3] = 0xFF; // PLC No
        header[4] = (byte)(_targetIo & 0xFF); // Target IO Low
        header[5] = (byte)((_targetIo >> 8) & 0xFF); // Target IO High (0x03FF hoặc 0x0000)
        header[6] = 0x00; // Target Station
        // Request Data Length (2 bytes little endian: 2 bytes timer + 2 bytes cmd + 2 bytes subcmd + dataLen)
        ushort totalLen = (ushort)(6 + dataLength);
        header[7] = (byte)(totalLen & 0xFF);
        header[8] = (byte)((totalLen >> 8) & 0xFF);
        header[9] = 0x10; // CPU Timer (4s = 16 * 250ms)
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
        
        string prefix = "";
        string numStr = "";

        // Kiểm tra tiền tố 2 ký tự trước
        if (clean.StartsWith("SM") || clean.StartsWith("SD") || clean.StartsWith("ZR") ||
            clean.StartsWith("TS") || clean.StartsWith("TC") || clean.StartsWith("CS") ||
            clean.StartsWith("CC") || clean.StartsWith("DX") || clean.StartsWith("DY") ||
            clean.StartsWith("TN") || clean.StartsWith("CN"))
        {
            prefix = clean.Substring(0, 2);
            numStr = clean.Substring(2);
        }
        else
        {
            prefix = new string(clean.TakeWhile(char.IsLetter).ToArray());
            numStr = new string(clean.SkipWhile(char.IsLetter).ToArray());
        }

        int num = 0;
        if (prefix == "X" || prefix == "Y")
        {
            // X và Y trong Mitsubishi mặc định là Octal (0..7, 10..17, 20..27...)
            // Nếu tất cả các chữ số là 0-7, parse dạng Octal sang số nguyên cho MC Protocol
            bool isOctal = numStr.Length > 0 && numStr.All(c => c >= '0' && c <= '7');
            if (isOctal)
            {
                try { num = Convert.ToInt32(numStr, 8); }
                catch { int.TryParse(numStr, out num); }
            }
            else
            {
                int.TryParse(numStr, out num);
            }
        }
        else if (prefix == "B" || prefix == "W")
        {
            // B và W là hệ Hex trong Q/L/FX series
            try { num = Convert.ToInt32(numStr, 16); }
            catch { int.TryParse(numStr, out num); }
        }
        else
        {
            int.TryParse(numStr, out num);
        }

        byte code = prefix switch
        {
            "D" => 0xA8,
            "SD" => 0xA9,
            "W" => 0xB4,
            "R" => 0xAF,
            "ZR" => 0xB0,
            "TN" => 0xC2,
            "CN" => 0xC5,
            "M" => 0x90,
            "SM" => 0x91,
            "X" => 0x9C,
            "DX" => 0xA2,
            "Y" => 0x9D,
            "DY" => 0xA3,
            "L" => 0x92,
            "F" => 0x93,
            "B" => 0xA0,
            "S" => 0x98,
            "TS" => 0xC1,
            "TC" => 0xC0,
            "CS" => 0xC4,
            "CC" => 0xC3,
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

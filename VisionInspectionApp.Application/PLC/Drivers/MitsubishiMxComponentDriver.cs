using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Drivers;

public sealed class MitsubishiMxComponentDriver : IPlcDriver
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, object> _simulatedMemory = new(StringComparer.OrdinalIgnoreCase);

    // In-process COM fields (for x86 execution)
    private object? _comObject;
    private Type? _comType;

    // Out-of-process Socket Bridge fields (for x64 execution)
    private MxBridgeClient? _bridgeClient;

    private bool _disposed;

    public PlcModel Config { get; }

    public bool ForceSimulationMode { get; set; } = false;

    public bool IsConnected => ForceSimulationMode || (Config.State == PlcConnectionState.Connected && (_comObject != null || (_bridgeClient != null && _bridgeClient.IsConnected)));

    public MitsubishiMxComponentDriver(PlcModel config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(8000);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _lock.WaitAsync(linkedCts.Token);
        }
        catch
        {
            Config.State = PlcConnectionState.Error;
            return false;
        }

        try
        {
            if (IsConnected && (_comObject != null || (_bridgeClient != null && _bridgeClient.IsConnected))) return true;

            if (ForceSimulationMode)
            {
                Config.CpuName = $"Simulated (St.{Config.LogicalStationNumber})";
                Config.State = PlcConnectionState.Connected;
                return true;
            }

            // 1. If running as 64-bit process, use out-of-process 32-bit PLC Bridge via Localhost Socket
            if (Environment.Is64BitProcess)
            {
                return await ConnectViaBridgeAsync(linkedCts.Token);
            }

            // 2. If running as 32-bit process, try direct in-process COM first, fallback to bridge
            try
            {
                _comType = Type.GetTypeFromProgID("ActUtlType.ActUtlType")
                          ?? Type.GetTypeFromProgID("ActProgType.ActProgType")
                          ?? Type.GetTypeFromProgID("ActFXUtlType.ActFXUtlType");

                if (_comType != null)
                {
                    _comObject = Activator.CreateInstance(_comType);
                    if (_comObject != null)
                    {
                        SetComProperty("ActLogicalStationNumber", Config.LogicalStationNumber);

                        var openResult = InvokeComMethod("Open");
                        if (openResult is int resCode && resCode == 0)
                        {
                            Config.State = PlcConnectionState.Connected;

                            try
                            {
                                object[] cpuArgs = new object[] { "", 0 };
                                ParameterModifier p = new ParameterModifier(2);
                                p[0] = true;
                                p[1] = true;
                                ParameterModifier[] mods = new ParameterModifier[] { p };

                                var cpuRes = _comType.InvokeMember("GetCpuType",
                                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                                    null, _comObject, cpuArgs, mods, null, null);

                                int resCpuCode = cpuRes is int cCode ? cCode : -1;
                                string cpuName = cpuArgs[0]?.ToString()?.Trim() ?? "";
                                string cpuType = cpuArgs[1]?.ToString()?.Trim() ?? "";

                                if (resCpuCode == 0 && !string.IsNullOrEmpty(cpuName))
                                {
                                    Config.CpuName = string.IsNullOrEmpty(cpuType) ? cpuName : $"{cpuName} (Type {cpuType})";
                                }
                                else
                                {
                                    Config.CpuName = $"Mitsubishi PLC (St.{Config.LogicalStationNumber})";
                                }
                            }
                            catch
                            {
                                Config.CpuName = $"Mitsubishi PLC (St.{Config.LogicalStationNumber})";
                            }

                            return true;
                        }
                        else
                        {
                            int errCode = openResult is int rc ? rc : -1;
                            Config.CpuName = $"Open Failed (0x{errCode:X8})";
                            Config.State = PlcConnectionState.Error;
                            CleanupCom();
                            return false;
                        }
                    }
                }
            }
            catch
            {
                CleanupCom();
            }

            return await ConnectViaBridgeAsync(linkedCts.Token);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<bool> ConnectViaBridgeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _bridgeClient ??= new MxBridgeClient();
            var (success, cpuName, errMsg) = await _bridgeClient.ConnectAsync(Config.LogicalStationNumber, cancellationToken);
            if (success)
            {
                Config.State = PlcConnectionState.Connected;
                Config.CpuName = string.IsNullOrEmpty(cpuName) ? $"Mitsubishi PLC (St.{Config.LogicalStationNumber})" : cpuName;
                return true;
            }
            else
            {
                Config.State = PlcConnectionState.Error;
                Config.CpuName = errMsg ?? "Bridge Connection Failed";
                return false;
            }
        }
        catch (Exception ex)
        {
            Config.State = PlcConnectionState.Error;
            Config.CpuName = $"Bridge Error: {ex.Message}";
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _lock.WaitAsync(TimeSpan.FromMilliseconds(1000));
        }
        catch
        {
            return;
        }

        try
        {
            if (_bridgeClient != null)
            {
                try { await _bridgeClient.DisconnectAsync(); } catch { }
                _bridgeClient.Dispose();
                _bridgeClient = null;
            }

            if (_comObject != null)
            {
                try { InvokeComMethod("Close"); } catch { }
                CleanupCom();
            }

            Config.State = PlcConnectionState.Disconnected;
            Config.CpuName = string.Empty;
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
        var batch = new[] { tag };
        var dict = await ReadBatchAsync(batch, cancellationToken);
        return dict.TryGetValue(tag.Name, out var val) ? val : tag.DefaultValue;
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

        using var timeoutCts = new CancellationTokenSource(2000);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _lock.WaitAsync(linkedCts.Token);
        }
        catch
        {
            return FallbackReadSimulation(tagList);
        }

        try
        {
            if (!ForceSimulationMode && Config.State == PlcConnectionState.Connected)
            {
                if (_bridgeClient != null && _bridgeClient.IsConnected)
                {
                    try
                    {
                        // 1. Thử đọc toàn bộ batch trong 1 lệnh duy nhất (ReadDeviceRandom2)
                        var batchResult = await TryReadBridgeBatchRandom2Async(_bridgeClient, tagList, linkedCts.Token);
                        if (batchResult != null)
                        {
                            return batchResult;
                        }

                        // 2. Fallback đọc tuần tự từng tag nếu thiết bị không hỗ trợ Random2
                        foreach (var tag in tagList)
                        {
                            object? val = await ReadBridgeTagValueAsync(_bridgeClient, tag, linkedCts.Token);
                            result[tag.Name] = ApplyScale(val, tag.Scale);
                        }
                        return result;
                    }
                    catch
                    {
                        Config.State = PlcConnectionState.Error;
                    }
                }
                else if (_comObject != null)
                {
                    try
                    {
                        // 1. Thử đọc toàn bộ batch qua in-process COM ReadDeviceRandom2
                        var batchResult = TryReadComBatchRandom2(tagList);
                        if (batchResult != null)
                        {
                            return batchResult;
                        }

                        // 2. Fallback đọc tuần tự từng tag
                        foreach (var tag in tagList)
                        {
                            object? val = ReadComTagValue(tag);
                            result[tag.Name] = ApplyScale(val, tag.Scale);
                        }
                        return result;
                    }
                    catch
                    {
                        CleanupCom();
                        Config.State = PlcConnectionState.Error;
                    }
                }
            }

            return FallbackReadSimulation(tagList);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Dictionary<string, object?> FallbackReadSimulation(List<PlcTag> tagList)
    {
        var result = new Dictionary<string, object?>();
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

    public async Task<bool> WriteBatchAsync(IDictionary<PlcTag, object> values, CancellationToken cancellationToken = default)
    {
        if (values == null || values.Count == 0) return true;

        using var timeoutCts = new CancellationTokenSource(2000);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _lock.WaitAsync(linkedCts.Token);
        }
        catch
        {
            return false;
        }

        try
        {
            if (!ForceSimulationMode && Config.State == PlcConnectionState.Connected)
            {
                if (_bridgeClient != null && _bridgeClient.IsConnected)
                {
                    try
                    {
                        foreach (var (tag, val) in values)
                        {
                            await WriteBridgeTagValueAsync(_bridgeClient, tag, val, linkedCts.Token);
                            _simulatedMemory[tag.Address] = val;
                        }
                        return true;
                    }
                    catch
                    {
                        Config.State = PlcConnectionState.Error;
                    }
                }
                else if (_comObject != null)
                {
                    try
                    {
                        foreach (var (tag, val) in values)
                        {
                            WriteComTagValue(tag, val);
                            _simulatedMemory[tag.Address] = val;
                        }
                        return true;
                    }
                    catch
                    {
                        CleanupCom();
                        Config.State = PlcConnectionState.Error;
                    }
                }
            }

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

    #region Bridge Read/Write Helpers

    private static async Task<Dictionary<string, object?>?> TryReadBridgeBatchRandom2Async(MxBridgeClient bridge, List<PlcTag> tagList, CancellationToken cancellationToken)
    {
        try
        {
            var deviceQueries = new List<string>();
            var tagIndexMap = new List<(PlcTag Tag, int Index1, int Index2)>();

            foreach (var tag in tagList)
            {
                string dev = tag.Address.Trim();
                if (tag.DataType == PlcDataType.Float || tag.DataType == PlcDataType.Int32 || tag.DataType == PlcDataType.UInt32)
                {
                    int idx1 = deviceQueries.Count;
                    deviceQueries.Add(dev);
                    int idx2 = deviceQueries.Count;
                    deviceQueries.Add(IncrementDeviceAddress(dev, 1));
                    tagIndexMap.Add((tag, idx1, idx2));
                }
                else
                {
                    int idx = deviceQueries.Count;
                    deviceQueries.Add(dev);
                    tagIndexMap.Add((tag, idx, -1));
                }
            }

            var (rc, data) = await bridge.ReadDeviceRandom2Async(deviceQueries.ToArray(), cancellationToken);
            if (rc != 0 || data == null || data.Length != deviceQueries.Count)
            {
                return null; // Fallback to sequential
            }

            var result = new Dictionary<string, object?>();
            foreach (var (tag, idx1, idx2) in tagIndexMap)
            {
                if (idx2 >= 0)
                {
                    short w1 = data[idx1];
                    short w2 = data[idx2];
                    if (tag.DataType == PlcDataType.Float)
                    {
                        float fVal = WordsToFloat(new int[] { w1, w2 });
                        result[tag.Name] = ApplyScale(fVal, tag.Scale);
                    }
                    else if (tag.DataType == PlcDataType.Int32)
                    {
                        int i32 = (int)((uint)(ushort)w1 | ((uint)(ushort)w2 << 16));
                        result[tag.Name] = ApplyScale(i32, tag.Scale);
                    }
                    else if (tag.DataType == PlcDataType.UInt32)
                    {
                        uint u32 = (uint)(ushort)w1 | ((uint)(ushort)w2 << 16);
                        result[tag.Name] = ApplyScale(u32, tag.Scale);
                    }
                }
                else
                {
                    short val = data[idx1];
                    if (tag.DataType == PlcDataType.Bool)
                    {
                        result[tag.Name] = val != 0;
                    }
                    else
                    {
                        object decoded = ConvertFromShort(val, tag.DataType);
                        result[tag.Name] = ApplyScale(decoded, tag.Scale);
                    }
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<string, object?>? TryReadComBatchRandom2(List<PlcTag> tagList)
    {
        if (_comObject == null || _comType == null) return null;
        try
        {
            var deviceQueries = new List<string>();
            var tagIndexMap = new List<(PlcTag Tag, int Index1, int Index2)>();

            foreach (var tag in tagList)
            {
                string dev = tag.Address.Trim();
                if (tag.DataType == PlcDataType.Float || tag.DataType == PlcDataType.Int32 || tag.DataType == PlcDataType.UInt32)
                {
                    int idx1 = deviceQueries.Count;
                    deviceQueries.Add(dev);
                    int idx2 = deviceQueries.Count;
                    deviceQueries.Add(IncrementDeviceAddress(dev, 1));
                    tagIndexMap.Add((tag, idx1, idx2));
                }
                else
                {
                    int idx = deviceQueries.Count;
                    deviceQueries.Add(dev);
                    tagIndexMap.Add((tag, idx, -1));
                }
            }

            string deviceList = string.Join("\n", deviceQueries);
            int size = deviceQueries.Count;
            short[] buffer = new short[size];
            object[] args = new object[] { deviceList, size, buffer };
            ParameterModifier[] modifiers = new ParameterModifier[1];
            modifiers[0] = new ParameterModifier(3);
            modifiers[0][2] = true;

            var res = _comType.InvokeMember("ReadDeviceRandom2",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _comObject, args, modifiers, null, null);

            int rc = res is int c ? c : -1;
            if (rc != 0) return null;

            short[] data = (args[2] is short[] resArr) ? resArr : buffer;
            if (data.Length != size) return null;

            var result = new Dictionary<string, object?>();
            foreach (var (tag, idx1, idx2) in tagIndexMap)
            {
                if (idx2 >= 0)
                {
                    short w1 = data[idx1];
                    short w2 = data[idx2];
                    if (tag.DataType == PlcDataType.Float)
                    {
                        float fVal = WordsToFloat(new int[] { w1, w2 });
                        result[tag.Name] = ApplyScale(fVal, tag.Scale);
                    }
                    else if (tag.DataType == PlcDataType.Int32)
                    {
                        int i32 = (int)((uint)(ushort)w1 | ((uint)(ushort)w2 << 16));
                        result[tag.Name] = ApplyScale(i32, tag.Scale);
                    }
                    else if (tag.DataType == PlcDataType.UInt32)
                    {
                        uint u32 = (uint)(ushort)w1 | ((uint)(ushort)w2 << 16);
                        result[tag.Name] = ApplyScale(u32, tag.Scale);
                    }
                }
                else
                {
                    short val = data[idx1];
                    if (tag.DataType == PlcDataType.Bool)
                    {
                        result[tag.Name] = val != 0;
                    }
                    else
                    {
                        object decoded = ConvertFromShort(val, tag.DataType);
                        result[tag.Name] = ApplyScale(decoded, tag.Scale);
                    }
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<object?> ReadBridgeTagValueAsync(MxBridgeClient bridge, PlcTag tag, CancellationToken cancellationToken)
    {
        string device = tag.Address.Trim();

        if (tag.DataType == PlcDataType.Float)
        {
            string nextDevice = IncrementDeviceAddress(device, 1);
            var (rc1, w1) = await bridge.GetDevice2Async(device, cancellationToken);
            var (rc2, w2) = await bridge.GetDevice2Async(nextDevice, cancellationToken);
            if (rc1 == 0 && rc2 == 0)
            {
                float fVal = WordsToFloat(new int[] { w1, w2 });
                return ApplyScale(fVal, tag.Scale);
            }
            return tag.DefaultValue;
        }

        var (rc, val) = await bridge.GetDeviceAsync(device, cancellationToken);
        if (rc == 0)
        {
            if (tag.DataType == PlcDataType.Bool)
            {
                return val != 0;
            }
            return ConvertFromInt(val, tag.DataType);
        }

        return tag.DefaultValue;
    }

    private static async Task WriteBridgeTagValueAsync(MxBridgeClient bridge, PlcTag tag, object val, CancellationToken cancellationToken)
    {
        string device = tag.Address.Trim();

        if (tag.DataType == PlcDataType.Float)
        {
            float fVal = 0f;
            if (val != null)
            {
                if (val is float f) fVal = f;
                else if (val is double d) fVal = (float)d;
                else float.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fVal);
            }

            int[] words = FloatToWords(fVal);
            string nextDevice = IncrementDeviceAddress(device, 1);
            await bridge.SetDevice2Async(device, (short)words[0], cancellationToken);
            await bridge.SetDevice2Async(nextDevice, (short)words[1], cancellationToken);
            return;
        }

        int iVal = ConvertToInt(val, tag.DataType);
        await bridge.SetDeviceAsync(device, iVal, cancellationToken);
    }

    #endregion

    #region In-Process COM Reflection Helpers

    private object? ReadComTagValue(PlcTag tag)
    {
        if (_comObject == null || _comType == null) return null;

        string device = tag.Address.Trim();

        if (tag.DataType == PlcDataType.Float)
        {
            try
            {
                string nextDevice = IncrementDeviceAddress(device, 1);
                object[] args1 = new object[] { device, (short)0 };
                object[] args2 = new object[] { nextDevice, (short)0 };
                ParameterModifier[] modifiers1 = new ParameterModifier[1];
                modifiers1[0] = new ParameterModifier(2);
                modifiers1[0][1] = true;
                ParameterModifier[] modifiers2 = new ParameterModifier[1];
                modifiers2[0] = new ParameterModifier(2);
                modifiers2[0][1] = true;

                var res1 = _comType.InvokeMember("GetDevice2", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, _comObject, args1, modifiers1, null, null);
                var res2 = _comType.InvokeMember("GetDevice2", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, _comObject, args2, modifiers2, null, null);

                if (res1 is int rc1 && rc1 == 0 && res2 is int rc2 && rc2 == 0)
                {
                    short w1 = Convert.ToInt16(args1[1]);
                    short w2 = Convert.ToInt16(args2[1]);
                    float fVal = WordsToFloat(new int[] { w1, w2 });
                    return ApplyScale(fVal, tag.Scale);
                }
            }
            catch { }
        }

        try
        {
            object[] args = new object[] { device, 0 };
            ParameterModifier[] modifiers = new ParameterModifier[1];
            modifiers[0] = new ParameterModifier(2);
            modifiers[0][1] = true;

            var res = _comType.InvokeMember("GetDevice",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _comObject, args, modifiers, null, null);

            int retCode = res is int rc ? rc : -1;
            if (retCode == 0)
            {
                int iVal = Convert.ToInt32(args[1]);
                if (tag.DataType == PlcDataType.Bool)
                {
                    return iVal != 0;
                }
                return ConvertFromInt(iVal, tag.DataType);
            }
        }
        catch { }

        try
        {
            object[] args = new object[] { device, (short)0 };
            ParameterModifier[] modifiers = new ParameterModifier[1];
            modifiers[0] = new ParameterModifier(2);
            modifiers[0][1] = true;

            var res = _comType.InvokeMember("GetDevice2",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _comObject, args, modifiers, null, null);

            int retCode = res is int rc ? rc : -1;
            if (retCode == 0)
            {
                short sVal = Convert.ToInt16(args[1]);
                if (tag.DataType == PlcDataType.Bool)
                {
                    return sVal != 0;
                }
                return ConvertFromShort(sVal, tag.DataType);
            }
        }
        catch { }

        return tag.DefaultValue;
    }

    private void WriteComTagValue(PlcTag tag, object val)
    {
        if (_comObject == null || _comType == null) return;

        string device = tag.Address.Trim();

        if (tag.DataType == PlcDataType.Float)
        {
            float fVal = 0f;
            if (val != null)
            {
                if (val is float f) fVal = f;
                else if (val is double d) fVal = (float)d;
                else float.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fVal);
            }

            int[] words = FloatToWords(fVal);
            string nextDevice = IncrementDeviceAddress(device, 1);

            try
            {
                object[] args1 = new object[] { device, (short)words[0] };
                object[] args2 = new object[] { nextDevice, (short)words[1] };
                _comType.InvokeMember("SetDevice2", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, _comObject, args1);
                _comType.InvokeMember("SetDevice2", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, _comObject, args2);
            }
            catch { }

            return;
        }

        int iVal = ConvertToInt(val, tag.DataType);

        try
        {
            object[] args = new object[] { device, iVal };
            _comType.InvokeMember("SetDevice",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _comObject, args);
        }
        catch
        {
            short sVal = (short)iVal;
            object[] args = new object[] { device, sVal };
            _comType.InvokeMember("SetDevice2",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _comObject, args);
        }
    }

    private void SetComProperty(string propName, object val)
    {
        if (_comObject == null || _comType == null) return;
        _comType.InvokeMember(propName,
            BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
            null, _comObject, new[] { val });
    }

    private object? InvokeComMethod(string methodName, params object[] args)
    {
        if (_comObject == null || _comType == null) return null;
        return _comType.InvokeMember(methodName,
            BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
            null, _comObject, args);
    }

    private void CleanupCom()
    {
        if (_comObject != null)
        {
            try { Marshal.FinalReleaseComObject(_comObject); } catch { }
            _comObject = null;
        }
        _comType = null;
    }

    #endregion

    #region Common Utility Helpers

    private static string IncrementDeviceAddress(string address, int offset)
    {
        if (string.IsNullOrWhiteSpace(address)) return address;
        string clean = address.Trim().ToUpperInvariant();
        string prefix = new string(clean.TakeWhile(char.IsLetter).ToArray());
        string numStr = new string(clean.SkipWhile(char.IsLetter).ToArray());
        if (int.TryParse(numStr, out int num))
        {
            return $"{prefix}{num + offset}";
        }
        return address;
    }

    public static int[] FloatToWords(float value)
    {
        byte[] arr = BitConverter.GetBytes(value);
        byte[] highWord = { arr[2], arr[3] };
        byte[] lowWord = { arr[0], arr[1] };
        int valueD1 = BitConverter.ToInt16(lowWord, 0);
        int valueD3 = BitConverter.ToInt16(highWord, 0);
        return new int[] { valueD1, valueD3 };
    }

    public static float WordsToFloat(int[] doubles)
    {
        byte[] highWordByte = BitConverter.GetBytes((short)doubles[1]);
        byte[] lowWordByte = BitConverter.GetBytes((short)doubles[0]);
        byte[] combineWordByte = { lowWordByte[0], lowWordByte[1], highWordByte[0], highWordByte[1] };
        return BitConverter.ToSingle(combineWordByte, 0);
    }

    private static int ConvertToInt(object val, PlcDataType type)
    {
        if (val == null) return 0;
        if (type == PlcDataType.Bool)
        {
            string s = val.ToString() ?? "";
            return bool.TryParse(s, out bool b) && b ? 1 : 0;
        }

        if (double.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dVal))
        {
            return (int)Math.Round(dVal);
        }
        return 0;
    }

    private static object ConvertFromInt(int iVal, PlcDataType type) => type switch
    {
        PlcDataType.Bool => iVal != 0,
        PlcDataType.Int16 => (short)iVal,
        PlcDataType.UInt16 => (ushort)iVal,
        PlcDataType.Int32 => iVal,
        PlcDataType.UInt32 => (uint)iVal,
        _ => iVal
    };

    private static short ConvertToShort(object val, PlcDataType type)
    {
        string str = val?.ToString() ?? "0";
        if (type == PlcDataType.Bool)
        {
            return bool.TryParse(str, out bool b) && b ? (short)1 : (short)0;
        }
        return short.TryParse(str, out short s) ? s : (short)0;
    }

    private static object ConvertFromShort(short sVal, PlcDataType type) => type switch
    {
        PlcDataType.Bool => sVal != 0,
        PlcDataType.Int16 => sVal,
        PlcDataType.UInt16 => (ushort)sVal,
        _ => sVal
    };

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
        PlcDataType.Int16 => (short)0,
        PlcDataType.UInt16 => (ushort)0,
        PlcDataType.Int32 => 0,
        PlcDataType.UInt32 => 0U,
        PlcDataType.Float => 0.0f,
        PlcDataType.String => string.Empty,
        _ => 0
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_bridgeClient != null)
        {
            try { _bridgeClient.Dispose(); } catch { }
            _bridgeClient = null;
        }

        CleanupCom();
        _lock.Dispose();
    }

    #endregion

    #region Out-of-Process Localhost TCP Socket Bridge Client

    private sealed class MxBridgeClient : IDisposable
    {
        private const int BridgePort = 39871;
        private readonly SemaphoreSlim _clientLock = new(1, 1);
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private Process? _bridgeProcess;
        private bool _isConnected;
        private bool _disposed;
        private DateTime _lastLaunchAttempt = DateTime.MinValue;

        public bool IsConnected => _isConnected && _tcpClient != null && _tcpClient.Connected;

        public async Task<(bool Success, string CpuName, string? ErrorMessage)> ConnectAsync(int stationNumber, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(8000);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _clientLock.WaitAsync(linkedCts.Token);
            }
            catch
            {
                return (false, "", "Connect request timed out.");
            }

            try
            {
                await EnsureBridgeProcessAndSocketConnectedAsync(linkedCts.Token);

                string response = await SendCommandInternalAsync($"CONNECT|{stationNumber}", linkedCts.Token);
                string[] parts = response.Split('|');
                if (parts.Length > 0 && string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase))
                {
                    _isConnected = true;
                    string cpuName = parts.Length > 2 ? parts[2] : $"Mitsubishi PLC (St.{stationNumber})";
                    string cpuType = parts.Length > 3 ? parts[3] : "";
                    if (!string.IsNullOrEmpty(cpuType) && !cpuName.Contains(cpuType))
                    {
                        cpuName = $"{cpuName} (Type {cpuType})";
                    }
                    return (true, cpuName, null);
                }

                string err = parts.Length > 2 ? parts[2] : "Connect rejected by bridge.";
                return (false, "", err);
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                await _clientLock.WaitAsync(TimeSpan.FromMilliseconds(500));
            }
            catch
            {
                return;
            }

            try
            {
                if (IsConnected)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(500);
                        await SendCommandInternalAsync("DISCONNECT", cts.Token);
                    }
                    catch { }
                }
                _isConnected = false;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async Task<(int ResCode, int Value)> GetDeviceAsync(string device, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(1500);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _clientLock.WaitAsync(linkedCts.Token);
            }
            catch
            {
                return (-99, 0);
            }

            try
            {
                if (!IsConnected) return (-1, 0);
                string res = await SendCommandInternalAsync($"GET_DEVICE|{device}", linkedCts.Token);
                string[] parts = res.Split('|');
                if (parts.Length > 1 && string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(parts[1], out int val);
                    return (0, val);
                }
                int.TryParse(parts.Length > 1 ? parts[1] : "-1", out int errCode);
                return (errCode, 0);
            }
            catch
            {
                return (-99, 0);
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async Task<int> SetDeviceAsync(string device, int value, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(1500);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _clientLock.WaitAsync(linkedCts.Token);
            }
            catch
            {
                return -99;
            }

            try
            {
                if (!IsConnected) return -1;
                string res = await SendCommandInternalAsync($"SET_DEVICE|{device}|{value}", linkedCts.Token);
                string[] parts = res.Split('|');
                if (parts.Length > 0 && string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }
                int.TryParse(parts.Length > 1 ? parts[1] : "-1", out int errCode);
                return errCode;
            }
            catch
            {
                return -99;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async Task<(int ResCode, short Value)> GetDevice2Async(string device, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(1500);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _clientLock.WaitAsync(linkedCts.Token);
            }
            catch
            {
                return (-99, (short)0);
            }

            try
            {
                if (!IsConnected) return (-1, (short)0);
                string res = await SendCommandInternalAsync($"GET_DEVICE2|{device}", linkedCts.Token);
                string[] parts = res.Split('|');
                if (parts.Length > 1 && string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase))
                {
                    short.TryParse(parts[1], out short val);
                    return (0, val);
                }
                int.TryParse(parts.Length > 1 ? parts[1] : "-1", out int errCode);
                return (errCode, (short)0);
            }
            catch
            {
                return (-99, (short)0);
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async Task<int> SetDevice2Async(string device, short value, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(1500);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _clientLock.WaitAsync(linkedCts.Token);
            }
            catch
            {
                return -99;
            }

            try
            {
                if (!IsConnected) return -1;
                string res = await SendCommandInternalAsync($"SET_DEVICE2|{device}|{value}", linkedCts.Token);
                string[] parts = res.Split('|');
                if (parts.Length > 0 && string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }
                int.TryParse(parts.Length > 1 ? parts[1] : "-1", out int errCode);
                return errCode;
            }
            catch
            {
                return -99;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async Task<(int ResCode, short[] Data)> ReadDeviceRandom2Async(string[] devices, CancellationToken cancellationToken)
        {
            if (devices == null || devices.Length == 0) return (0, Array.Empty<short>());

            using var timeoutCts = new CancellationTokenSource(2000);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _clientLock.WaitAsync(linkedCts.Token);
            }
            catch
            {
                return (-99, Array.Empty<short>());
            }

            try
            {
                if (!IsConnected) return (-1, Array.Empty<short>());
                string res = await SendCommandInternalAsync($"READ_RANDOM2|{string.Join(",", devices)}", linkedCts.Token);
                string[] parts = res.Split('|');
                if (parts.Length > 1 && string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase))
                {
                    short[] data = Array.ConvertAll(parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries), s => short.TryParse(s, out short v) ? v : (short)0);
                    return (0, data);
                }
                int.TryParse(parts.Length > 1 ? parts[1] : "-1", out int errCode);
                return (errCode, Array.Empty<short>());
            }
            catch
            {
                return (-99, Array.Empty<short>());
            }
            finally
            {
                _clientLock.Release();
            }
        }

        private async Task EnsureBridgeProcessAndSocketConnectedAsync(CancellationToken cancellationToken)
        {
            if (_tcpClient != null && _tcpClient.Connected)
            {
                return;
            }

            CloseSocketConnection();

            // 1. First, attempt quick connection to existing running bridge instance
            bool connected = false;
            try
            {
                _tcpClient = new TcpClient { NoDelay = true };
                using var quickCts = new CancellationTokenSource(400);
                await _tcpClient.ConnectAsync(IPAddress.Loopback, BridgePort, quickCts.Token);
                connected = true;
            }
            catch
            {
                CloseSocketConnection();
            }

            // 2. If not already running, launch bridge process
            if (!connected)
            {
                if ((DateTime.UtcNow - _lastLaunchAttempt).TotalSeconds > 2)
                {
                    _lastLaunchAttempt = DateTime.UtcNow;
                    LaunchBridgeProcess();
                }

                // 3. Retry connection loop up to 5000ms while bridge starts up
                int maxAttempts = 25;
                while (maxAttempts-- > 0 && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(200, cancellationToken);
                        _tcpClient = new TcpClient { NoDelay = true };
                        using var retryCts = new CancellationTokenSource(500);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retryCts.Token);
                        await _tcpClient.ConnectAsync(IPAddress.Loopback, BridgePort, linked.Token);
                        connected = true;
                        break;
                    }
                    catch
                    {
                        CloseSocketConnection();
                    }
                }
            }

            if (!connected || _tcpClient == null || !_tcpClient.Connected)
            {
                throw new IOException($"Could not connect to 32-bit PLC Bridge on 127.0.0.1:{BridgePort}.");
            }

            _networkStream = _tcpClient.GetStream();
            _reader = new StreamReader(_networkStream, Encoding.UTF8, false, 4096, leaveOpen: true);
            _writer = new StreamWriter(_networkStream, Encoding.UTF8, 4096, leaveOpen: true) { AutoFlush = true };
        }

        private static void KillExistingZombieBridges()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("VisionInspectionApp.PlcBridge"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }

        private static string? ResolveBridgePath(string baseDir)
        {
            var candidatePaths = new List<string>();

            // 1. In current base directory
            candidatePaths.Add(Path.Combine(baseDir, "VisionInspectionApp.PlcBridge.dll"));

            // 2. Search upwards to find solution/workspace root
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string bridgeProjDir = Path.Combine(dir.FullName, "VisionInspectionApp.PlcBridge", "bin");
                if (Directory.Exists(bridgeProjDir))
                {
                    candidatePaths.Add(Path.Combine(dir.FullName, "VisionInspectionApp.PlcBridge", "bin", "x86", "Debug", "net8.0-windows", "VisionInspectionApp.PlcBridge.dll"));
                    candidatePaths.Add(Path.Combine(dir.FullName, "VisionInspectionApp.PlcBridge", "bin", "x86", "Release", "net8.0-windows", "VisionInspectionApp.PlcBridge.dll"));
                    candidatePaths.Add(Path.Combine(dir.FullName, "VisionInspectionApp.PlcBridge", "bin", "Debug", "net8.0-windows", "VisionInspectionApp.PlcBridge.dll"));
                    candidatePaths.Add(Path.Combine(dir.FullName, "VisionInspectionApp.PlcBridge", "bin", "Release", "net8.0-windows", "VisionInspectionApp.PlcBridge.dll"));
                    break;
                }
                dir = dir.Parent;
            }

            string? bestDll = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var p in candidatePaths)
            {
                try
                {
                    if (File.Exists(p))
                    {
                        var writeTime = File.GetLastWriteTimeUtc(p);
                        if (writeTime > bestTime)
                        {
                            bestTime = writeTime;
                            bestDll = p;
                        }
                    }
                }
                catch { }
            }

            // If a newer PlcBridge binary was found in source tree, automatically copy to baseDir
            if (bestDll != null)
            {
                try
                {
                    string bestDir = Path.GetDirectoryName(bestDll)!;
                    if (!string.Equals(bestDir, baseDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        string baseDll = Path.Combine(baseDir, "VisionInspectionApp.PlcBridge.dll");
                        if (!File.Exists(baseDll) || File.GetLastWriteTimeUtc(bestDll) > File.GetLastWriteTimeUtc(baseDll))
                        {
                            foreach (var ext in new[] { ".dll", ".exe", ".pdb", ".deps.json", ".runtimeconfig.json" })
                            {
                                string src = Path.Combine(bestDir, "VisionInspectionApp.PlcBridge" + ext);
                                string dst = Path.Combine(baseDir, "VisionInspectionApp.PlcBridge" + ext);
                                if (File.Exists(src))
                                {
                                    File.Copy(src, dst, overwrite: true);
                                }
                            }
                            bestDll = baseDll;
                        }
                    }
                }
                catch { }
            }

            return bestDll;
        }

        private void LaunchBridgeProcess()
        {
            KillExistingZombieBridges();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? bridgeDll = ResolveBridgePath(baseDir);
            string bridgeExe = Path.Combine(baseDir, "VisionInspectionApp.PlcBridge.exe");

            if (bridgeDll != null)
            {
                bridgeExe = Path.ChangeExtension(bridgeDll, ".exe");
            }

            if ((bridgeDll == null || !File.Exists(bridgeDll)) && !File.Exists(bridgeExe))
            {
                throw new FileNotFoundException($"Cannot find 32-bit PLC Bridge worker executable or assembly in: {baseDir}");
            }

            int currentPid = Process.GetCurrentProcess().Id;
            string x86Dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe");

            ProcessStartInfo psi;
            if (File.Exists(x86Dotnet) && bridgeDll != null && File.Exists(bridgeDll))
            {
                psi = new ProcessStartInfo
                {
                    FileName = x86Dotnet,
                    Arguments = $"\"{bridgeDll}\" --parent-pid {currentPid} --port {BridgePort}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(bridgeDll) ?? baseDir
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = bridgeExe,
                    Arguments = $"--parent-pid {currentPid} --port {BridgePort}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(bridgeExe) ?? baseDir
                };
            }

            string x86DotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet");
            if (Directory.Exists(x86DotnetRoot))
            {
                psi.Environment["DOTNET_ROOT"] = x86DotnetRoot;
            }

            _bridgeProcess = Process.Start(psi);
        }

        private async Task<string> SendCommandInternalAsync(string command, CancellationToken cancellationToken)
        {
            if (_writer == null || _reader == null)
            {
                throw new InvalidOperationException("Bridge socket is not connected.");
            }

            using var cmdTimeoutCts = new CancellationTokenSource(2500);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cmdTimeoutCts.Token);

            await _writer.WriteLineAsync(command.AsMemory(), linkedCts.Token);
            await _writer.FlushAsync(linkedCts.Token);
            string? response = await _reader.ReadLineAsync(linkedCts.Token);
            if (response == null)
            {
                _isConnected = false;
                throw new IOException("Bridge socket connection closed unexpectedly.");
            }

            return response;
        }

        private void CloseSocketConnection()
        {
            _isConnected = false;
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _networkStream?.Dispose(); } catch { }
            try { _tcpClient?.Dispose(); } catch { }
            _reader = null;
            _writer = null;
            _networkStream = null;
            _tcpClient = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (IsConnected && _writer != null)
                {
                    _writer.WriteLine("EXIT");
                    _writer.Flush();
                }
            }
            catch { }

            CloseSocketConnection();

            if (_bridgeProcess != null)
            {
                try
                {
                    if (!_bridgeProcess.HasExited)
                    {
                        _bridgeProcess.WaitForExit(500);
                    }
                }
                catch { }
                try { _bridgeProcess.Dispose(); } catch { }
                _bridgeProcess = null;
            }

            _clientLock.Dispose();
        }
    }

    #endregion
}

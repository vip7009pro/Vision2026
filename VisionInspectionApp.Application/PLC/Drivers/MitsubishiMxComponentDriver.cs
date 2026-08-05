using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Drivers;

public sealed class MitsubishiMxComponentDriver : IPlcDriver
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, object> _simulatedMemory = new(StringComparer.OrdinalIgnoreCase);

    private object? _comObject;
    private Type? _comType;
    private bool _disposed;

    public PlcModel Config { get; }

    public bool ForceSimulationMode { get; set; } = false;

    public bool IsConnected => ForceSimulationMode || (Config.State == PlcConnectionState.Connected && _comObject != null);

    public MitsubishiMxComponentDriver(PlcModel config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected && _comObject != null) return true;

            if (ForceSimulationMode)
            {
                Config.CpuName = $"Simulated (St.{Config.LogicalStationNumber})";
                Config.State = PlcConnectionState.Connected;
                return true;
            }

            try
            {
                _comType = Type.GetTypeFromProgID("ActUtlType.ActUtlType")
                          ?? Type.GetTypeFromProgID("ActUtlType64.ActUtlType")
                          ?? Type.GetTypeFromProgID("ActFXUtlType.ActFXUtlType");

                if (_comType != null)
                {
                    _comObject = Activator.CreateInstance(_comType);
                    if (_comObject != null)
                    {
                        // Set ActLogicalStationNumber
                        SetComProperty("ActLogicalStationNumber", Config.LogicalStationNumber);

                        // Call Open()
                        var openResult = InvokeComMethod("Open");
                        if (openResult is int resCode && resCode == 0)
                        {
                            Config.State = PlcConnectionState.Connected;

                            // Call GetCpuType(out szCpuName, out iCpuType) like reference Form1.cs
                            try
                            {
                                object[] cpuArgs = new object[] { "", 0 };
                                ParameterModifier p = new ParameterModifier(2);
                                p[0] = true; // out szCpuName
                                p[1] = true; // out iCpuType
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
            catch (Exception ex)
            {
                Config.CpuName = $"COM Error: {ex.Message}";
                Config.State = PlcConnectionState.Error;
                CleanupCom();
                return false;
            }

            // COM type not registered on Windows
            Config.CpuName = "MX Component Not Installed";
            Config.State = PlcConnectionState.Error;
            return false;
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

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!ForceSimulationMode && _comObject != null && Config.State == PlcConnectionState.Connected)
            {
                try
                {
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

            // Fallback / Simulation mode read
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
            if (!ForceSimulationMode && _comObject != null && Config.State == PlcConnectionState.Connected)
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

            // Simulated memory write
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

    #region COM Reflection Helpers

    private object? ReadComTagValue(PlcTag tag)
    {
        if (_comObject == null || _comType == null) return null;

        string device = tag.Address.Trim();

        // Special handling for Float data type (requires 2 words = 32-bit Float)
        if (tag.DataType == PlcDataType.Float)
        {
            try
            {
                short[] sWords = new short[2];
                object[] args = new object[] { device, 2, sWords };
                ParameterModifier[] modifiers = new ParameterModifier[1];
                modifiers[0] = new ParameterModifier(3);
                modifiers[0][2] = true;

                var res = _comType.InvokeMember("ReadDeviceBlock2",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args, modifiers, null, null);

                int retCode = res is int rc ? rc : -1;
                if (retCode == 0)
                {
                    short[] retArr = (short[])args[2];
                    int[] words = new int[] { retArr[0], retArr[1] };
                    float fVal = WordsToFloat(words);
                    return ApplyScale(fVal, tag.Scale);
                }
            }
            catch { }

            try
            {
                int[] iWords = new int[2];
                object[] args = new object[] { device, 2, iWords };
                ParameterModifier[] modifiers = new ParameterModifier[1];
                modifiers[0] = new ParameterModifier(3);
                modifiers[0][2] = true;

                var res = _comType.InvokeMember("ReadDeviceBlock",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args, modifiers, null, null);

                int retCode = res is int rc ? rc : -1;
                if (retCode == 0)
                {
                    int[] retArr = (int[])args[2];
                    float fVal = WordsToFloat(retArr);
                    return ApplyScale(fVal, tag.Scale);
                }
            }
            catch { }

            // Fallback: Read 2 individual words using GetDevice2
            try
            {
                string nextDevice = IncrementDeviceAddress(device, 1);
                object[] args1 = new object[] { device, (short)0 };
                object[] args2 = new object[] { nextDevice, (short)0 };
                ParameterModifier[] modifiers = new ParameterModifier[1];
                modifiers[0] = new ParameterModifier(2);
                modifiers[0][1] = true;

                var res1 = _comType.InvokeMember("GetDevice2", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, _comObject, args1, modifiers, null, null);
                var res2 = _comType.InvokeMember("GetDevice2", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, _comObject, args2, modifiers, null, null);

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

        // 1. Try GetDevice(szDevice, out int iData) - standard Mitsubishi MX Component API
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

        // 2. Fallback to GetDevice2(szDevice, out short sData)
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
            short[] sWords = new short[] { (short)words[0], (short)words[1] };

            try
            {
                object[] args = new object[] { device, 2, sWords };
                ParameterModifier[] modifiers = new ParameterModifier[1];
                modifiers[0] = new ParameterModifier(3);
                modifiers[0][2] = true;

                _comType.InvokeMember("WriteDeviceBlock2",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args, modifiers, null, null);
                return;
            }
            catch { }

            try
            {
                object[] args = new object[] { device, 2, words };
                ParameterModifier[] modifiers = new ParameterModifier[1];
                modifiers[0] = new ParameterModifier(3);
                modifiers[0][2] = true;

                _comType.InvokeMember("WriteDeviceBlock",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args, modifiers, null, null);
                return;
            }
            catch { }

            // Reliable Fallback: Write 2 individual words using SetDevice2 / SetDevice
            try
            {
                string nextDevice = IncrementDeviceAddress(device, 1);
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
            try { System.Runtime.InteropServices.Marshal.ReleaseComObject(_comObject); } catch { }
            _comObject = null;
        }
        _comType = null;
    }

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

        try { DisconnectAsync().Wait(); } catch { }
        _lock.Dispose();
    }
    #endregion
}

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.PlcBridge;

public sealed class MxComWorker : IDisposable
{
    private sealed record WorkItem(Func<object?> Run, TaskCompletionSource<object?> Tcs);

    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _staThread;

    private object? _comObject;
    private Type? _comType;
    private bool _isConnected;
    private int _currentStationNumber = -1;
    private bool _disposed;

    public bool IsConnected => _isConnected && _comObject != null;

    public MxComWorker()
    {
        _staThread = new Thread(StaWorkerLoop)
        {
            IsBackground = true,
            Name = "MxComWorker STA Thread"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    private string _cachedCpuName = "";
    private string _cachedCpuType = "";

    public Task<(int ResCode, string CpuName, string CpuType, string? ErrorMessage)> ConnectAsync(int stationNumber)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_isConnected && _currentStationNumber == stationNumber && _comObject != null)
            {
                if (!string.IsNullOrEmpty(_cachedCpuName))
                {
                    return (0, _cachedCpuName, _cachedCpuType, (string?)null);
                }
            }

            DisconnectInternal();

            _currentStationNumber = stationNumber;
            try
            {
                _comType = Type.GetTypeFromProgID("ActUtlType.ActUtlType")
                          ?? Type.GetTypeFromProgID("ActProgType.ActProgType")
                          ?? Type.GetTypeFromProgID("ActFXUtlType.ActFXUtlType");

                if (_comType == null)
                {
                    return (-1, "", "", (string?)"MX Component (ActUtlType) is not registered in Windows 32-bit registry.");
                }

                _comObject = Activator.CreateInstance(_comType);
                if (_comObject == null)
                {
                    return (-2, "", "", (string?)"Failed to create ActUtlType COM instance.");
                }

                // Set ActLogicalStationNumber
                SetComProperty("ActLogicalStationNumber", stationNumber);

                // Call Open()
                var openRes = InvokeComMethod("Open");
                int resCode = openRes is int rc ? rc : -1;
                if (resCode != 0)
                {
                    DisconnectInternal();
                    return (resCode, "", "", (string?)$"Open() failed with error code 0x{resCode:X8}");
                }

                _isConnected = true;

                // Query CPU Type
                string cpuName = $"Mitsubishi PLC (St.{stationNumber})";
                string cpuType = "";
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
                    string cName = cpuArgs[0]?.ToString()?.Trim() ?? "";
                    string cType = cpuArgs[1]?.ToString()?.Trim() ?? "";

                    if (resCpuCode == 0 && !string.IsNullOrEmpty(cName))
                    {
                        cpuName = string.IsNullOrEmpty(cType) ? cName : $"{cName} (Type {cType})";
                        cpuType = cType;
                    }
                }
                catch { }

                _cachedCpuName = cpuName;
                _cachedCpuType = cpuType;

                return (0, cpuName, cpuType, (string?)null);
            }
            catch (Exception ex)
            {
                DisconnectInternal();
                return (-99, "", "", (string?)ex.Message);
            }
        }, 3000, (-100, "", "", "Connect operation timed out."));
    }

    public Task<int> DisconnectAsync()
    {
        return InvokeWithTimeoutAsync(() =>
        {
            DisconnectInternal();
            return 0;
        }, 1500, 0);
    }

    public Task<(int ResCode, int Value)> GetDeviceAsync(string device)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null) return (-1, 0);

            try
            {
                object[] args = new object[] { device, 0 };
                ParameterModifier[] modifiers = new ParameterModifier[1];
                modifiers[0] = new ParameterModifier(2);
                modifiers[0][1] = true;

                var res = _comType.InvokeMember("GetDevice",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args, modifiers, null, null);

                int rc = res is int c ? c : -1;
                int val = Convert.ToInt32(args[1]);
                return (rc, val);
            }
            catch
            {
                return (-99, 0);
            }
        }, 1500, (-100, 0));
    }

    public Task<int> SetDeviceAsync(string device, int value)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null) return -1;

            try
            {
                object[] args = new object[] { device, value };
                var res = _comType.InvokeMember("SetDevice",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args);

                return res is int c ? c : -1;
            }
            catch
            {
                return -99;
            }
        }, 1500, -100);
    }

    public Task<(int ResCode, short Value)> GetDevice2Async(string device)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null) return (-1, (short)0);

            try
            {
                object[] args = new object[] { device, (short)0 };
                ParameterModifier[] modifiers = new ParameterModifier[1];
                modifiers[0] = new ParameterModifier(2);
                modifiers[0][1] = true;

                var res = _comType.InvokeMember("GetDevice2",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args, modifiers, null, null);

                int rc = res is int c ? c : -1;
                short val = Convert.ToInt16(args[1]);
                return (rc, val);
            }
            catch
            {
                return (-99, (short)0);
            }
        }, 1500, (-100, (short)0));
    }

    public Task<int> SetDevice2Async(string device, short value)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null) return -1;

            try
            {
                object[] args = new object[] { device, value };
                var res = _comType.InvokeMember("SetDevice2",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null, _comObject, args);

                return res is int c ? c : -1;
            }
            catch
            {
                return -99;
            }
        }, 1500, -100);
    }

    private static string IncrementDeviceAddress(string address, int offset)
    {
        if (string.IsNullOrWhiteSpace(address) || offset == 0) return address;
        string clean = address.Trim().ToUpperInvariant();
        string prefix = new string(clean.TakeWhile(char.IsLetter).ToArray());
        string numStr = new string(clean.SkipWhile(char.IsLetter).ToArray());
        if (int.TryParse(numStr, out int num))
        {
            return $"{prefix}{num + offset}";
        }
        return address;
    }

    public Task<(int ResCode, int[] Data)> ReadDeviceBlockAsync(string device, int size)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null || size <= 0) return (-1, Array.Empty<int>());

            try
            {
                int[] list = new int[size];
                for (int i = 0; i < size; i++)
                {
                    string dev = IncrementDeviceAddress(device, i);
                    object[] args = new object[] { dev, 0 };
                    ParameterModifier[] modifiers = new ParameterModifier[1];
                    modifiers[0] = new ParameterModifier(2);
                    modifiers[0][1] = true;

                    var res = _comType.InvokeMember("GetDevice",
                        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                        null, _comObject, args, modifiers, null, null);

                    int rc = res is int c ? c : -1;
                    if (rc != 0) return (rc, Array.Empty<int>());
                    list[i] = Convert.ToInt32(args[1]);
                }
                return (0, list);
            }
            catch (Exception ex)
            {
                Program.Log($"ReadDeviceBlockAsync exception: {ex}");
                return (-99, Array.Empty<int>());
            }
        }, 2000, (-100, Array.Empty<int>()));
    }

    public Task<int> WriteDeviceBlockAsync(string device, int[] data)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null || data == null || data.Length == 0) return -1;

            try
            {
                for (int i = 0; i < data.Length; i++)
                {
                    string dev = IncrementDeviceAddress(device, i);
                    object[] args = new object[] { dev, data[i] };
                    var res = _comType.InvokeMember("SetDevice",
                        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                        null, _comObject, args);

                    int rc = res is int c ? c : -1;
                    if (rc != 0) return rc;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Program.Log($"WriteDeviceBlockAsync exception: {ex}");
                return -99;
            }
        }, 2000, -100);
    }

    public Task<(int ResCode, short[] Data)> ReadDeviceBlock2Async(string device, int size)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null || size <= 0) return (-1, Array.Empty<short>());

            try
            {
                short[] list = new short[size];
                for (int i = 0; i < size; i++)
                {
                    string dev = IncrementDeviceAddress(device, i);
                    object[] args = new object[] { dev, (short)0 };
                    ParameterModifier[] modifiers = new ParameterModifier[1];
                    modifiers[0] = new ParameterModifier(2);
                    modifiers[0][1] = true;

                    var res = _comType.InvokeMember("GetDevice2",
                        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                        null, _comObject, args, modifiers, null, null);

                    int rc = res is int c ? c : -1;
                    if (rc != 0) return (rc, Array.Empty<short>());
                    list[i] = Convert.ToInt16(args[1]);
                }
                return (0, list);
            }
            catch (Exception ex)
            {
                Program.Log($"ReadDeviceBlock2Async exception: {ex}");
                return (-99, Array.Empty<short>());
            }
        }, 2000, (-100, Array.Empty<short>()));
    }

    public Task<(int ResCode, short[] Data)> ReadDeviceRandom2Async(string[] devices)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null || devices == null || devices.Length == 0)
                return (-1, Array.Empty<short>());

            try
            {
                var list = new short[devices.Length];
                for (int i = 0; i < devices.Length; i++)
                {
                    object[] args = new object[] { devices[i].Trim(), (short)0 };
                    ParameterModifier[] modifiers = new ParameterModifier[1];
                    modifiers[0] = new ParameterModifier(2);
                    modifiers[0][1] = true;

                    var res = _comType.InvokeMember("GetDevice2",
                        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                        null, _comObject, args, modifiers, null, null);

                    int rc = res is int c ? c : -1;
                    if (rc != 0) return (rc, Array.Empty<short>());
                    list[i] = Convert.ToInt16(args[1]);
                }
                return (0, list);
            }
            catch (Exception ex)
            {
                Program.Log($"ReadDeviceRandom2Async exception: {ex}");
                return (-99, Array.Empty<short>());
            }
        }, 2000, (-100, Array.Empty<short>()));
    }

    public Task<int> WriteDeviceBlock2Async(string device, short[] data)
    {
        return InvokeWithTimeoutAsync(() =>
        {
            if (_comObject == null || _comType == null || data == null || data.Length == 0) return -1;

            try
            {
                for (int i = 0; i < data.Length; i++)
                {
                    string dev = IncrementDeviceAddress(device, i);
                    object[] args = new object[] { dev, data[i] };
                    var res = _comType.InvokeMember("SetDevice2",
                        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                        null, _comObject, args);

                    int rc = res is int c ? c : -1;
                    if (rc != 0) return rc;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Program.Log($"WriteDeviceBlock2Async exception: {ex}");
                return -99;
            }
        }, 2000, -100);
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

    private void DisconnectInternal()
    {
        _isConnected = false;
        _cachedCpuName = "";
        _cachedCpuType = "";
        if (_comObject != null)
        {
            try { InvokeComMethod("Close"); } catch { }
            try { Marshal.FinalReleaseComObject(_comObject); } catch { }
            _comObject = null;
        }
        _comType = null;
        _currentStationNumber = -1;
    }

    private void StaWorkerLoop()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            try
            {
                var res = item.Run();
                item.Tcs.TrySetResult(res);
            }
            catch (Exception ex)
            {
                item.Tcs.TrySetException(ex);
            }
        }

        DisconnectInternal();
    }

    private async Task<T> InvokeWithTimeoutAsync<T>(Func<T> action, int timeoutMs, T fallbackValue)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(new WorkItem(() => action(), tcs));
        }
        catch
        {
            return fallbackValue;
        }

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        if (completedTask == tcs.Task)
        {
            try
            {
                var result = await tcs.Task;
                return (T)result!;
            }
            catch
            {
                return fallbackValue;
            }
        }

        return fallbackValue;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            DisconnectInternal();
        }
        catch { }

        try
        {
            _queue.CompleteAdding();
        }
        catch { }
    }
}

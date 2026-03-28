using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Plc;

/// <summary>
/// MX Component COM wrapper (ActUtlType) executed on a dedicated STA thread.
/// This avoids COM apartment issues when called from background tasks.
/// </summary>
public sealed class MxComponentPlcClient : IPlcClient
{
    private sealed record WorkItem(Func<object?> Run, TaskCompletionSource<object?> Tcs, CancellationToken CancellationToken);

    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _staThread;

    private object? _actUtlType;
    private bool _isConnected;
    private int _logicalStationNumber;

    public MxComponentPlcClient()
    {
        _staThread = new Thread(StaWorkerLoop)
        {
            IsBackground = true,
            Name = "MXComponent STA Worker"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    public bool IsConnected => _isConnected;

    public Task ConnectAsync(int logicalStationNumber, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(() =>
        {
            if (_isConnected && _logicalStationNumber == logicalStationNumber && _actUtlType is not null)
            {
                return null;
            }

            DisconnectInternal();

            _logicalStationNumber = logicalStationNumber;
            _actUtlType = CreateActUtlTypeInstance();

            // ActLogicalStationNumber property
            SetComProperty(_actUtlType, "ActLogicalStationNumber", logicalStationNumber);

            // Open() returns int error code (0 = success)
            var rc = (int)InvokeComMethod(_actUtlType, "Open");
            if (rc != 0)
            {
                DisconnectInternal();
                throw new PlcException($"MX Component Open() failed. RC={rc}");
            }

            _isConnected = true;
            return null;
        }, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync(() =>
        {
            DisconnectInternal();
            return null;
        }, cancellationToken);
    }

    public Task<bool> ReadBitAsync(string device, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("Device is required.", nameof(device));

        return InvokeAsync(() =>
        {
            EnsureConnected();
            var value = 0;
            // GetDevice(string deviceName, out int data)
            var args = new object?[] { device, value };
            var rc = (int)InvokeComMethod(_actUtlType!, "GetDevice", args);
            if (rc != 0) throw new PlcException($"GetDevice({device}) failed. RC={rc}");
            value = Convert.ToInt32(args[1] ?? 0);
            return value != 0;
        }, cancellationToken);
    }

    public Task WriteBitAsync(string device, bool value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("Device is required.", nameof(device));

        return InvokeAsync(() =>
        {
            EnsureConnected();
            // SetDevice(string deviceName, int data)
            var rc = (int)InvokeComMethod(_actUtlType!, "SetDevice", device, value ? 1 : 0);
            if (rc != 0) throw new PlcException($"SetDevice({device}) failed. RC={rc}");
            return null;
        }, cancellationToken);
    }

    public Task<short> ReadWordAsync(string device, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("Device is required.", nameof(device));

        return InvokeAsync(() =>
        {
            EnsureConnected();
            var value = 0;
            var args = new object?[] { device, value };
            var rc = (int)InvokeComMethod(_actUtlType!, "GetDevice", args);
            if (rc != 0) throw new PlcException($"GetDevice({device}) failed. RC={rc}");
            value = Convert.ToInt32(args[1] ?? 0);
            return unchecked((short)value);
        }, cancellationToken);
    }

    public Task WriteWordAsync(string device, short value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("Device is required.", nameof(device));

        return InvokeAsync(() =>
        {
            EnsureConnected();
            var rc = (int)InvokeComMethod(_actUtlType!, "SetDevice", device, (int)value);
            if (rc != 0) throw new PlcException($"SetDevice({device}) failed. RC={rc}");
            return null;
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore during dispose
        }

        _queue.CompleteAdding();
    }

    private void StaWorkerLoop()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Tcs.TrySetCanceled(item.CancellationToken);
                continue;
            }

            try
            {
                var result = item.Run();
                item.Tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                item.Tcs.TrySetException(ex);
            }
        }

        DisconnectInternal();
    }

    private Task InvokeAsync(Func<object?> action, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(new WorkItem(action, tcs, cancellationToken), cancellationToken);
        return tcs.Task;
    }

    private Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(new WorkItem(() => func()!, tcs, cancellationToken), cancellationToken);
        return tcs.Task.ContinueWith(
            t => (T)t.Result!,
            cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void EnsureConnected()
    {
        if (!_isConnected || _actUtlType is null) throw new PlcException("PLC is not connected.");
    }

    private void DisconnectInternal()
    {
        _isConnected = false;
        if (_actUtlType is not null)
        {
            try
            {
                // Close() returns int RC
                InvokeComMethod(_actUtlType, "Close");
            }
            catch
            {
                // ignore
            }

            try
            {
                Marshal.FinalReleaseComObject(_actUtlType);
            }
            catch
            {
                // ignore
            }
        }

        _actUtlType = null;
    }

    private static object CreateActUtlTypeInstance()
    {
        // ProgID for ActUtlType: "ActUtlType.ActUtlType"
        var t = Type.GetTypeFromProgID("ActUtlType.ActUtlType");
        if (t is null)
        {
            throw new PlcException("MX Component is not installed or ActUtlType ProgID not registered.");
        }

        try
        {
            return Activator.CreateInstance(t) ?? throw new PlcException("Failed to create ActUtlType instance.");
        }
        catch (Exception ex)
        {
            throw new PlcException("Failed to create ActUtlType instance.", ex);
        }
    }

    private static object InvokeComMethod(object target, string methodName, params object?[]? args)
    {
        try
        {
            return target.GetType().InvokeMember(
                methodName,
                BindingFlags.InvokeMethod,
                binder: null,
                target: target,
                args: args) ?? throw new PlcException($"COM call returned null: {methodName}");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void SetComProperty(object target, string propertyName, object value)
    {
        try
        {
            target.GetType().InvokeMember(
                propertyName,
                BindingFlags.SetProperty,
                binder: null,
                target: target,
                args: new[] { value });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}


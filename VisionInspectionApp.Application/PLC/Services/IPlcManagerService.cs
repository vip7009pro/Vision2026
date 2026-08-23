using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

public interface IPlcManagerService : IDisposable
{
    ObservableCollection<PlcModel> Plcs { get; }

    ObservableCollection<PlcTag> Tags { get; }

    PlcTagCache Cache { get; }

    IPlcLogger Logger { get; }

    PlcPollingEngine PollingEngine { get; }

    PlcIndustrialConfig IndustrialConfig { get; set; }

    void LoadConfig(IEnumerable<PlcModel> plcs, IEnumerable<PlcTag> tags);

    void SaveGlobalConfig();

    void LoadGlobalConfig();

    IPlcDriver? GetDriver(string plcId);

    IPlcDriver? GetDriverByName(string plcName);

    PlcTagValue? GetTagValue(string plcId, string tagName);

    Task<object?> ReadTagValueAsync(string plcId, string tagOrAddress, CancellationToken cancellationToken = default);

    Task<bool> WriteTagValueAsync(string plcId, string tagName, object value, CancellationToken cancellationToken = default);

    IReadOnlyList<PlcTag> GetAllTagsToPoll();

    bool IsPollingActive { get; }

    void AcquirePollingLock(string sourceId);

    void ReleasePollingLock(string sourceId);

    Task StartPollingAsync();

    Task StopPollingAsync();

    Task ConnectAllAsync(CancellationToken cancellationToken = default);

    Task DisconnectAllAsync();

    event EventHandler<TagChangedEventArgs>? OnTagChanged;

    event EventHandler<string>? OnConnected;

    event EventHandler<string>? OnDisconnected;

    event EventHandler<(string PlcId, string Message)>? OnError;

    event EventHandler<PlcIndustrialConfig>? OnIndustrialConfigChanged;
}

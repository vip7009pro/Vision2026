using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Drivers;

public interface IPlcDriver : IDisposable
{
    PlcModel Config { get; }

    bool IsConnected { get; }

    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    Task<object?> ReadAsync(PlcTag tag, CancellationToken cancellationToken = default);

    Task<bool> WriteAsync(PlcTag tag, object value, CancellationToken cancellationToken = default);

    Task<IDictionary<string, object?>> ReadBatchAsync(IEnumerable<PlcTag> tags, CancellationToken cancellationToken = default);

    Task<bool> WriteBatchAsync(IDictionary<PlcTag, object> values, CancellationToken cancellationToken = default);

    Task<bool> ReconnectAsync(CancellationToken cancellationToken = default);
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Plc;

public interface IPlcClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(int logicalStationNumber, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<bool> ReadBitAsync(string device, CancellationToken cancellationToken = default);

    Task WriteBitAsync(string device, bool value, CancellationToken cancellationToken = default);

    Task<short> ReadWordAsync(string device, CancellationToken cancellationToken = default);

    Task WriteWordAsync(string device, short value, CancellationToken cancellationToken = default);
}


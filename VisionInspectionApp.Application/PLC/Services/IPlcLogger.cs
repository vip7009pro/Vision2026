using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Application.PLC.Services;

public sealed class PlcLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string Level { get; set; } = "INFO";

    public string PlcId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public interface IPlcLogger
{
    IReadOnlyList<PlcLogEntry> Logs { get; }

    event EventHandler<PlcLogEntry>? OnLogAdded;

    void LogConnect(string plcId, string name);

    void LogDisconnect(string plcId, string name);

    void LogReadError(string plcId, string tagName, string message);

    void LogWriteError(string plcId, string tagName, string message);

    void LogReconnect(string plcId, string message);

    void LogTimeout(string plcId, string operation);

    void Clear();
}

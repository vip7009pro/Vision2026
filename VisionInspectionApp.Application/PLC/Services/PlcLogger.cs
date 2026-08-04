using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VisionInspectionApp.Application.PLC.Services;

public sealed class PlcLogger : IPlcLogger
{
    private readonly ConcurrentQueue<PlcLogEntry> _logs = new();
    private const int MaxLogCount = 1000;

    public IReadOnlyList<PlcLogEntry> Logs => _logs.ToList();

    public event EventHandler<PlcLogEntry>? OnLogAdded;

    public void LogConnect(string plcId, string name) => AddLog("INFO", plcId, $"PLC [{name}] connected.");

    public void LogDisconnect(string plcId, string name) => AddLog("WARN", plcId, $"PLC [{name}] disconnected.");

    public void LogReadError(string plcId, string tagName, string message) => AddLog("ERROR", plcId, $"Read Tag [{tagName}] Error: {message}");

    public void LogWriteError(string plcId, string tagName, string message) => AddLog("ERROR", plcId, $"Write Tag [{tagName}] Error: {message}");

    public void LogReconnect(string plcId, string message) => AddLog("INFO", plcId, $"Reconnecting PLC: {message}");

    public void LogTimeout(string plcId, string operation) => AddLog("WARN", plcId, $"PLC Timeout during [{operation}]");

    public void Clear()
    {
        while (_logs.TryDequeue(out _)) { }
    }

    private void AddLog(string level, string plcId, string message)
    {
        var entry = new PlcLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            PlcId = plcId,
            Message = message
        };

        _logs.Enqueue(entry);
        while (_logs.Count > MaxLogCount)
        {
            _logs.TryDequeue(out _);
        }

        OnLogAdded?.Invoke(this, entry);
    }
}

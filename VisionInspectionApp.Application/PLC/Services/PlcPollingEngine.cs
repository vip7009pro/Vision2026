using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

public sealed class TagChangedEventArgs : EventArgs
{
    public string PlcId { get; }
    public string TagName { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }
    public DateTime Timestamp { get; }

    public TagChangedEventArgs(string plcId, string tagName, object? oldValue, object? newValue, DateTime timestamp)
    {
        PlcId = plcId;
        TagName = tagName;
        OldValue = oldValue;
        NewValue = newValue;
        Timestamp = timestamp;
    }
}

public sealed class PlcPollingMetrics
{
    public double LatencyMs { get; set; }
    public double PollingTimeMs { get; set; }
    public long PacketCount { get; set; }
    public int ReconnectCount { get; set; }
    public string LastError { get; set; } = string.Empty;
}

public sealed class PlcPollingEngine
{
    private readonly PlcTagCache _cache;
    private readonly IPlcLogger _logger;
    private Task? _pollingTask;
    private CancellationTokenSource? _cts;

    public event EventHandler<TagChangedEventArgs>? OnTagChanged;

    public ConcurrentMetricsStore Metrics { get; } = new();

    public PlcPollingEngine(PlcTagCache cache, IPlcLogger logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start(IReadOnlyList<PlcModel> plcs, IReadOnlyList<PlcTag> tags, Func<string, IPlcDriver?> driverLookup)
    {
        Stop();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _pollingTask = Task.Run(() => PollingLoopAsync(plcs, tags, driverLookup, token), token);
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try { _pollingTask?.Wait(1000); } catch { }
            _cts.Dispose();
            _cts = null;
            _pollingTask = null;
        }
    }

    private async Task PollingLoopAsync(
        IReadOnlyList<PlcModel> plcs,
        IReadOnlyList<PlcTag> tags,
        Func<string, IPlcDriver?> driverLookup,
        CancellationToken cancellationToken)
    {
        var enabledPlcs = plcs.Where(p => p.Enabled).ToList();
        if (enabledPlcs.Count == 0) return;

        while (!cancellationToken.IsCancellationRequested)
        {
            var swTotal = Stopwatch.StartNew();

            foreach (var plc in enabledPlcs)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var driver = driverLookup(plc.Id);
                if (driver == null || !driver.IsConnected) continue;

                var plcTags = tags.Where(t => string.Equals(t.PlcId, plc.Id, StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(t.PlcId, plc.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (plcTags.Count == 0) continue;

                var swPlc = Stopwatch.StartNew();
                try
                {
                    var readResults = await driver.ReadBatchAsync(plcTags, cancellationToken);
                    swPlc.Stop();

                    double elapsedMs = swPlc.Elapsed.TotalMilliseconds;
                    var metric = Metrics.GetOrAdd(plc.Id);
                    metric.PollingTimeMs = elapsedMs;
                    metric.LatencyMs = elapsedMs;
                    metric.PacketCount += 1;

                    foreach (var tag in plcTags)
                    {
                        if (readResults.TryGetValue(tag.Name, out var newVal))
                        {
                            var existingVal = _cache.Get(plc.Id, tag.Name) ?? _cache.Get(plc.Name, tag.Name);
                            var oldVal = existingVal?.CurrentValue;

                            _cache.Set(plc.Id, tag.Name, newVal, TagQuality.Good);
                            _cache.Set(plc.Name, tag.Name, newVal, TagQuality.Good);
                            if (!string.Equals(tag.PlcId, plc.Id, StringComparison.OrdinalIgnoreCase) && !string.Equals(tag.PlcId, plc.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                _cache.Set(tag.PlcId, tag.Name, newVal, TagQuality.Good);
                            }

                            if (!ValuesEqual(oldVal, newVal))
                            {
                                OnTagChanged?.Invoke(this, new TagChangedEventArgs(plc.Id, tag.Name, oldVal, newVal, DateTime.Now));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogReadError(plc.Id, "BatchRead", ex.Message);
                    var metric = Metrics.GetOrAdd(plc.Id);
                    metric.LastError = ex.Message;
                }
            }

            swTotal.Stop();

            int minScanMs = enabledPlcs.Min(p => Math.Max(50, p.ScanIntervalMs <= 0 ? 100 : p.ScanIntervalMs));
            int remainingDelay = Math.Max(50, minScanMs - (int)swTotal.ElapsedMilliseconds);

            try
            {
                await Task.Delay(remainingDelay, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static bool ValuesEqual(object? v1, object? v2)
    {
        if (v1 == null && v2 == null) return true;
        if (v1 == null || v2 == null) return false;
        return string.Equals(v1.ToString(), v2.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ConcurrentMetricsStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PlcPollingMetrics> _store = new(StringComparer.OrdinalIgnoreCase);

    public PlcPollingMetrics GetOrAdd(string plcId) => _store.GetOrAdd(plcId, _ => new PlcPollingMetrics());
}

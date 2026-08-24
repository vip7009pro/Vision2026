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
    private readonly object _startLock = new();

    public event EventHandler<TagChangedEventArgs>? OnTagChanged;

    public ConcurrentMetricsStore Metrics { get; } = new();

    public PlcPollingEngine(PlcTagCache cache, IPlcLogger logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start(Func<IReadOnlyList<PlcModel>> plcsLookup, Func<IReadOnlyList<PlcTag>> tagsLookup, Func<string, IPlcDriver?> driverLookup)
    {
        lock (_startLock)
        {
            if (_cts != null && !_cts.IsCancellationRequested && _pollingTask != null && !_pollingTask.IsCompleted)
            {
                // Already running
                return;
            }

            Stop();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _pollingTask = Task.Run(() => PollingLoopAsync(plcsLookup, tagsLookup, driverLookup, token), token);
        }
    }

    public void Start(IReadOnlyList<PlcModel> plcs, IReadOnlyList<PlcTag> tags, Func<string, IPlcDriver?> driverLookup)
    {
        Start(() => plcs, () => tags, driverLookup);
    }

    public void Stop()
    {
        lock (_startLock)
        {
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
                catch { }
                _cts = null;
                _pollingTask = null;
            }
        }
    }

    private async Task PollingLoopAsync(
        Func<IReadOnlyList<PlcModel>> plcsLookup,
        Func<IReadOnlyList<PlcTag>> tagsLookup,
        Func<string, IPlcDriver?> driverLookup,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var plcs = plcsLookup();
            var tags = tagsLookup();
            var enabledPlcs = plcs.Where(p => p.Enabled).ToList();

            if (enabledPlcs.Count == 0)
            {
                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                continue;
            }

            var swTotal = Stopwatch.StartNew();

            foreach (var plc in enabledPlcs)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var driver = driverLookup(plc.Id);
                if (driver == null) continue;

                if (!driver.IsConnected)
                {
                    try
                    {
                        using var connectCts = new CancellationTokenSource(1500);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectCts.Token);
                        await driver.ConnectAsync(linkedCts.Token);
                    }
                    catch { }
                }

                if (!driver.IsConnected) continue;

                var plcTags = tags.Where(t => string.IsNullOrWhiteSpace(t.PlcId)
                                              || string.Equals(t.PlcId, plc.Id, StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(t.PlcId, plc.Name, StringComparison.OrdinalIgnoreCase)
                                              || enabledPlcs.Count == 1).ToList();
                if (plcTags.Count == 0) continue;

                var swPlc = Stopwatch.StartNew();
                try
                {
                    using var readCts = new CancellationTokenSource(2000);
                    using var linkedReadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readCts.Token);
                    var readResults = await driver.ReadBatchAsync(plcTags, linkedReadCts.Token);
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

                            if (!string.IsNullOrWhiteSpace(tag.Address))
                            {
                                _cache.Set(plc.Id, tag.Address, newVal, TagQuality.Good);
                                _cache.Set(plc.Name, tag.Address, newVal, TagQuality.Good);
                            }

                            if (!string.Equals(tag.PlcId, plc.Id, StringComparison.OrdinalIgnoreCase) && !string.Equals(tag.PlcId, plc.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                _cache.Set(tag.PlcId, tag.Name, newVal, TagQuality.Good);
                                if (!string.IsNullOrWhiteSpace(tag.Address))
                                {
                                    _cache.Set(tag.PlcId, tag.Address, newVal, TagQuality.Good);
                                }
                            }

                            if (!ValuesEqual(oldVal, newVal))
                            {
                                OnTagChanged?.Invoke(this, new TagChangedEventArgs(plc.Id, tag.Name, oldVal, newVal, DateTime.Now));
                                if (!string.Equals(plc.Id, plc.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    OnTagChanged?.Invoke(this, new TagChangedEventArgs(plc.Name, tag.Name, oldVal, newVal, DateTime.Now));
                                }

                                if (!string.Equals(tag.Name, tag.Address, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tag.Address))
                                {
                                    OnTagChanged?.Invoke(this, new TagChangedEventArgs(plc.Id, tag.Address, oldVal, newVal, DateTime.Now));
                                    if (!string.Equals(plc.Id, plc.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        OnTagChanged?.Invoke(this, new TagChangedEventArgs(plc.Name, tag.Address, oldVal, newVal, DateTime.Now));
                                    }
                                }
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

            int minScanMs = enabledPlcs.Min(p => Math.Max(1, p.ScanIntervalMs <= 0 ? 50 : p.ScanIntervalMs));
            int remainingDelay = minScanMs - (int)swTotal.ElapsedMilliseconds;

            if (remainingDelay > 0)
            {
                try
                {
                    await Task.Delay(remainingDelay, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            else
            {
                await Task.Yield();
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Triển khai dịch vụ quản lý Lịch sử kiểm tra với Background Channel Worker (Zero-latency logging)
/// </summary>
public sealed class InspectionLogService : IInspectionLogService
{
    private static readonly string HistoryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisionInspectionApp", "History");

    private static readonly string SessionsIndexFile = Path.Combine(HistoryDir, "sessions_index.json");

    private readonly ConcurrentDictionary<string, InspectionSessionRecord> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<InspectionPartRecord>> _partsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private InspectionSessionRecord? _currentSession;
    private Channel<InspectionPartEnvelope>? _logChannel;
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;

    public InspectionSessionRecord? CurrentSession => _currentSession;

    public event EventHandler<InspectionSessionRecord>? SessionUpdated;
    public event EventHandler<InspectionPartRecord>? PartLogged;

    public InspectionLogService()
    {
        try
        {
            if (!Directory.Exists(HistoryDir))
            {
                Directory.CreateDirectory(HistoryDir);
            }
            LoadSessionsIndex();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InspectionLogService] Init error: {ex.Message}");
        }

        StartBackgroundWorker();
    }

    private void StartBackgroundWorker()
    {
        _workerCts = new CancellationTokenSource();
        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        };
        _logChannel = Channel.CreateBounded<InspectionPartEnvelope>(options);

        var token = _workerCts.Token;
        _workerTask = Task.Run(async () =>
        {
            if (_logChannel == null) return;
            var reader = _logChannel.Reader;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    while (await reader.WaitToReadAsync(token))
                    {
                        while (reader.TryRead(out var env))
                        {
                            try
                            {
                                ProcessPartEnvelope(env);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[InspectionLogWorker] Process error: {ex.Message}");
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InspectionLogWorker] Loop error: {ex.Message}");
                }
            }
        }, token);
    }

    private void ProcessPartEnvelope(InspectionPartEnvelope env)
    {
        var session = _sessions.TryGetValue(env.SessionId, out var s) ? s : null;
        if (session == null) return;

        var part = ExtractPartRecord(env.SessionId, env.PartIndex, env.Result, env.Config);

        lock (_lock)
        {
            var list = _partsCache.GetOrAdd(env.SessionId, _ => new List<InspectionPartRecord>());
            list.Add(part);

            session.TotalParts++;
            if (part.Pass) session.PassParts++;
            else session.FailParts++;
        }

        PartLogged?.Invoke(this, part);
        SessionUpdated?.Invoke(this, session);
    }

    public Task<InspectionSessionRecord> StartSessionAsync(string productName, string jobFilePath, string material = "-")
    {
        var session = new InspectionSessionRecord
        {
            Id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"),
            SessionCode = $"SES-{DateTime.Now:yyyyMMdd-HHmmss}",
            ProductName = string.IsNullOrWhiteSpace(productName) ? "Chưa gán" : productName,
            JobFilePath = jobFilePath ?? "",
            Material = string.IsNullOrWhiteSpace(material) ? "-" : material,
            StartTime = DateTime.Now,
            IsRunning = true
        };

        _sessions[session.Id] = session;
        _partsCache[session.Id] = new List<InspectionPartRecord>();
        _currentSession = session;

        SaveSessionsIndex();
        SessionUpdated?.Invoke(this, session);

        return Task.FromResult(session);
    }

    public Task<InspectionSessionRecord?> EndSessionAsync()
    {
        if (_currentSession == null) return Task.FromResult<InspectionSessionRecord?>(null);

        var session = _currentSession;
        session.EndTime = DateTime.Now;
        session.IsRunning = false;

        _currentSession = null;

        // Lưu dữ liệu parts của session ra đĩa
        SaveSessionPartsToDisk(session.Id);
        SaveSessionsIndex();

        SessionUpdated?.Invoke(this, session);

        return Task.FromResult<InspectionSessionRecord?>(session);
    }

    public void EnqueueInspectionResult(InspectionResult result, VisionConfig? config, int partIndex)
    {
        if (_currentSession == null || _logChannel == null || result == null) return;

        var env = new InspectionPartEnvelope
        {
            SessionId = _currentSession.Id,
            PartIndex = partIndex,
            Result = result,
            Config = config
        };

        // Fire-and-forget: Đẩy vào channel không đợi, zero-latency cho vision loop
        _logChannel.Writer.TryWrite(env);
    }

    public Task<IReadOnlyList<InspectionSessionRecord>> GetAllSessionsAsync()
    {
        var list = _sessions.Values
            .OrderByDescending(s => s.StartTime)
            .ToList();
        return Task.FromResult<IReadOnlyList<InspectionSessionRecord>>(list);
    }

    public Task<IReadOnlyList<InspectionPartRecord>> GetPartsForSessionAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return Task.FromResult<IReadOnlyList<InspectionPartRecord>>(Array.Empty<InspectionPartRecord>());

        if (_partsCache.TryGetValue(sessionId, out var cachedList))
        {
            lock (_lock)
            {
                return Task.FromResult<IReadOnlyList<InspectionPartRecord>>(cachedList.ToList());
            }
        }

        // Đọc từ file đĩa nếu chưa nạp vào cache
        var diskList = LoadSessionPartsFromDisk(sessionId);
        _partsCache[sessionId] = diskList;

        return Task.FromResult<IReadOnlyList<InspectionPartRecord>>(diskList);
    }

    public Task<bool> DeleteSessionAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return Task.FromResult(false);

        _sessions.TryRemove(sessionId, out _);
        _partsCache.TryRemove(sessionId, out _);

        try
        {
            string sessionFile = Path.Combine(HistoryDir, $"parts_{sessionId}.json");
            if (File.Exists(sessionFile))
            {
                File.Delete(sessionFile);
            }
            SaveSessionsIndex();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task ClearAllHistoryAsync()
    {
        _sessions.Clear();
        _partsCache.Clear();

        try
        {
            if (Directory.Exists(HistoryDir))
            {
                Directory.Delete(HistoryDir, true);
                Directory.CreateDirectory(HistoryDir);
            }
            SaveSessionsIndex();
        }
        catch { }

        return Task.CompletedTask;
    }

    private static InspectionPartRecord ExtractPartRecord(string sessionId, int partIndex, InspectionResult res, VisionConfig? cfg)
    {
        var isCalibrated = cfg != null && cfg.PixelsPerMm > 0 && Math.Abs(cfg.PixelsPerMm - 1.0) > 1e-6;
        var distUnit = isCalibrated ? "mm" : "px";

        var part = new InspectionPartRecord
        {
            SessionId = sessionId,
            PartIndex = partIndex,
            Timestamp = DateTime.Now,
            Pass = res.Pass
        };

        var measurements = new List<InspectionItemMeasurement>();

        if (res.Distances != null)
        {
            foreach (var d in res.Distances)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = d.Name,
                    ToolType = "Distance",
                    Nominal = d.Nominal,
                    TolPlus = d.TolPlus,
                    TolMinus = d.TolMinus,
                    MeasuredValue = d.Value,
                    Unit = distUnit,
                    Pass = d.Pass
                });
            }
        }

        if (res.LineToLineDistances != null)
        {
            foreach (var d in res.LineToLineDistances)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = d.Name,
                    ToolType = "LineLineDist",
                    Nominal = d.Nominal,
                    TolPlus = d.TolPlus,
                    TolMinus = d.TolMinus,
                    MeasuredValue = d.Value,
                    Unit = distUnit,
                    Pass = d.Pass
                });
            }
        }

        if (res.PointToLineDistances != null)
        {
            foreach (var d in res.PointToLineDistances)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = d.Name,
                    ToolType = "PointLineDist",
                    Nominal = d.Nominal,
                    TolPlus = d.TolPlus,
                    TolMinus = d.TolMinus,
                    MeasuredValue = d.Value,
                    Unit = distUnit,
                    Pass = d.Pass
                });
            }
        }

        if (res.SegmentLineDistances != null)
        {
            foreach (var d in res.SegmentLineDistances)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = d.Name,
                    ToolType = "SegmentLineDist",
                    Nominal = d.Nominal,
                    TolPlus = d.TolPlus,
                    TolMinus = d.TolMinus,
                    MeasuredValue = d.Value,
                    Unit = distUnit,
                    Pass = d.Pass
                });
            }
        }

        if (res.Angles != null)
        {
            foreach (var a in res.Angles)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = a.Name,
                    ToolType = "Angle",
                    Nominal = a.Nominal,
                    TolPlus = a.TolPlus,
                    TolMinus = a.TolMinus,
                    MeasuredValue = a.ValueDeg,
                    Unit = "°",
                    Pass = a.Pass
                });
            }
        }

        if (res.Diameters != null)
        {
            foreach (var dia in res.Diameters)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = dia.Name,
                    ToolType = "Diameter",
                    Nominal = dia.Nominal,
                    TolPlus = dia.TolPlus,
                    TolMinus = dia.TolMinus,
                    MeasuredValue = dia.Value,
                    Unit = distUnit,
                    Pass = dia.Pass
                });
            }
        }

        if (res.EdgePairs != null)
        {
            foreach (var ep in res.EdgePairs)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = ep.Name,
                    ToolType = "EdgePair",
                    Nominal = ep.Nominal,
                    TolPlus = ep.TolPlus,
                    TolMinus = ep.TolMinus,
                    MeasuredValue = ep.Value,
                    Unit = distUnit,
                    Pass = ep.Pass
                });
            }
        }

        if (res.EdgePairDetections != null)
        {
            foreach (var epd in res.EdgePairDetections)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = epd.Name,
                    ToolType = "EdgePairDetect",
                    Nominal = epd.Nominal,
                    TolPlus = epd.TolPlus,
                    TolMinus = epd.TolMinus,
                    MeasuredValue = epd.Value,
                    Unit = distUnit,
                    Pass = epd.Pass
                });
            }
        }

        if (res.LinePairDetections != null)
        {
            foreach (var lpd in res.LinePairDetections)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = lpd.Name,
                    ToolType = "LinePairDetect",
                    Nominal = lpd.Nominal,
                    TolPlus = lpd.TolPlus,
                    TolMinus = lpd.TolMinus,
                    MeasuredValue = lpd.Value,
                    Unit = distUnit,
                    Pass = lpd.Pass
                });
            }
        }

        if (res.ColorDiffs != null)
        {
            foreach (var cd in res.ColorDiffs)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = cd.Name,
                    ToolType = "ColorDiff",
                    Nominal = 0.0,
                    TolPlus = cd.MaxDeltaE,
                    TolMinus = 0.0,
                    MeasuredValue = cd.DeltaE,
                    Unit = "ΔE",
                    Pass = cd.Pass
                });
            }
        }

        if (res.Calipers != null)
        {
            foreach (var cal in res.Calipers)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = cal.Name,
                    ToolType = "Caliper",
                    Nominal = 0.0,
                    TolPlus = 0.0,
                    TolMinus = 0.0,
                    MeasuredValue = cal.Found ? 1.0 : 0.0,
                    Unit = "found",
                    Pass = cal.Found
                });
            }
        }

        if (res.CircleFinders != null)
        {
            foreach (var cf in res.CircleFinders)
            {
                measurements.Add(new InspectionItemMeasurement
                {
                    ItemName = cf.Name,
                    ToolType = "CircleFinder",
                    Nominal = 0.0,
                    TolPlus = 0.0,
                    TolMinus = 0.0,
                    MeasuredValue = cf.Found ? cf.RadiusPx * 2 : 0.0,
                    Unit = distUnit,
                    Pass = cf.Found
                });
            }
        }

        part.Measurements = measurements;

        // Trích xuất lý do lỗi
        if (!part.Pass)
        {
            var failedItems = measurements.Where(m => !m.Pass).Select(m => $"{m.ItemName} ({m.MeasuredValue:F2} ∉ [{m.Lsl:F2}..{m.Usl:F2}])");
            part.DetailedReason = string.Join(", ", failedItems);
            if (string.IsNullOrEmpty(part.DetailedReason))
            {
                part.DetailedReason = "NG (Defect or Detection Fail)";
            }
        }

        return part;
    }

    private void LoadSessionsIndex()
    {
        if (!File.Exists(SessionsIndexFile)) return;
        try
        {
            string json = File.ReadAllText(SessionsIndexFile);
            var list = JsonSerializer.Deserialize<List<InspectionSessionRecord>>(json);
            if (list != null)
            {
                foreach (var s in list)
                {
                    _sessions[s.Id] = s;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InspectionLogService] Load index error: {ex.Message}");
        }
    }

    private void SaveSessionsIndex()
    {
        try
        {
            var list = _sessions.Values.OrderByDescending(s => s.StartTime).ToList();
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SessionsIndexFile, json);
        }
        catch { }
    }

    private void SaveSessionPartsToDisk(string sessionId)
    {
        if (!_partsCache.TryGetValue(sessionId, out var parts) || parts == null) return;
        try
        {
            string sessionFile = Path.Combine(HistoryDir, $"parts_{sessionId}.json");
            lock (_lock)
            {
                string json = JsonSerializer.Serialize(parts, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(sessionFile, json);
            }
        }
        catch { }
    }

    private List<InspectionPartRecord> LoadSessionPartsFromDisk(string sessionId)
    {
        string sessionFile = Path.Combine(HistoryDir, $"parts_{sessionId}.json");
        if (!File.Exists(sessionFile)) return new List<InspectionPartRecord>();
        try
        {
            string json = File.ReadAllText(sessionFile);
            return JsonSerializer.Deserialize<List<InspectionPartRecord>>(json) ?? new List<InspectionPartRecord>();
        }
        catch
        {
            return new List<InspectionPartRecord>();
        }
    }

    public void Dispose()
    {
        try
        {
            _workerCts?.Cancel();
            _workerCts?.Dispose();
        }
        catch { }
    }

    private sealed class InspectionPartEnvelope
    {
        public string SessionId { get; set; } = "";
        public int PartIndex { get; set; }
        public InspectionResult Result { get; set; } = null!;
        public VisionConfig? Config { get; set; }
    }
}

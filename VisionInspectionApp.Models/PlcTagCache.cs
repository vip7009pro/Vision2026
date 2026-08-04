using System;
using System.Collections.Concurrent;

namespace VisionInspectionApp.Models;

public sealed class PlcTagValue
{
    public object? CurrentValue { get; set; }

    public object? PreviousValue { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public TagQuality Quality { get; set; } = TagQuality.Good;
}

public sealed class PlcTagCache
{
    private readonly ConcurrentDictionary<string, PlcTagValue> _cache = new(StringComparer.OrdinalIgnoreCase);

    public PlcTagValue? Get(string plcId, string tagName)
    {
        var key = BuildKey(plcId, tagName);
        if (_cache.TryGetValue(key, out var val))
        {
            return val;
        }
        return null;
    }

    public void Set(string plcId, string tagName, object? newValue, TagQuality quality = TagQuality.Good)
    {
        var key = BuildKey(plcId, tagName);
        _cache.AddOrUpdate(
            key,
            _ => new PlcTagValue
            {
                CurrentValue = newValue,
                PreviousValue = null,
                Timestamp = DateTime.Now,
                Quality = quality
            },
            (_, existing) =>
            {
                var prev = existing.CurrentValue;
                existing.PreviousValue = prev;
                existing.CurrentValue = newValue;
                existing.Timestamp = DateTime.Now;
                existing.Quality = quality;
                return existing;
            });
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public static string BuildKey(string plcId, string tagName) => $"{plcId}:{tagName}";
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application;

/// <summary>
/// Explicit development benchmark; it is never invoked by production flows.
/// Supply a real 5120x3840 image and the existing inspection service/configuration.
/// </summary>
public static class VisionBenchmarkRunner
{
    public static VisionBenchmarkReport RunStandard(IInspectionService inspectionService, VisionConfig config, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(inspectionService);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        using var source = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (source.Empty()) throw new InvalidOperationException($"Unable to load benchmark image: {imagePath}");
        if (source.Width != 5120 || source.Height != 3840)
            throw new ArgumentException($"Benchmark image must be 5120x3840; received {source.Width}x{source.Height}.", nameof(imagePath));

        var report = new VisionBenchmarkReport(imagePath, source.Width, source.Height, Snapshot());
        foreach (var iterations in new[] { 1, 5, 10, 20 })
        {
            report.Runs.Add(Run(inspectionService, config, source, iterations));
        }
        return report;
    }

    private static VisionBenchmarkRun Run(IInspectionService inspectionService, VisionConfig config, Mat source, int iterations)
    {
        var before = Snapshot();
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            // The runner owns this clone; Inspect treats input as read-only and does not own it.
            using var frame = source.Clone();
            inspectionService.Inspect(frame, config);
        }
        stopwatch.Stop();
        var after = Snapshot();
        return new VisionBenchmarkRun(iterations, stopwatch.Elapsed, before, after);
    }

    private static VisionMemorySample Snapshot()
    {
        using var process = Process.GetCurrentProcess();
        return new VisionMemorySample(
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.PeakWorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }
}

public sealed record VisionBenchmarkReport(string ImagePath, int Width, int Height, VisionMemorySample LoadMemory)
{
    public List<VisionBenchmarkRun> Runs { get; } = new();
}

public sealed record VisionBenchmarkRun(int Iterations, TimeSpan Elapsed, VisionMemorySample Before, VisionMemorySample After);

public sealed record VisionMemorySample(long WorkingSetBytes, long PrivateBytes, long PeakWorkingSetBytes, long ManagedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);

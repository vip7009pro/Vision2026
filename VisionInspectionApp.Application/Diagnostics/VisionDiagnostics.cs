using System;
using System.Diagnostics;

namespace VisionInspectionApp.Application;

/// <summary>
/// Development-only process memory telemetry. It intentionally measures process-private
/// and working-set memory because OpenCV Mats are predominantly unmanaged allocations.
/// </summary>
internal static class VisionDiagnostics
{
    internal static Scope Begin(string operationName) => new(operationName);

    internal readonly struct Scope
    {
#if DEBUG
        private readonly string _operationName;
        private readonly Snapshot _before;
        private readonly Stopwatch _stopwatch;
#endif

        internal Scope(string operationName)
        {
#if DEBUG
            _operationName = operationName;
            _before = Snapshot.Capture();
            _stopwatch = Stopwatch.StartNew();
#endif
        }

        /// <summary>Call after deterministic Mat cleanup, never after a forced GC.</summary>
        internal void CompleteAfterCleanup()
        {
#if DEBUG
            _stopwatch.Stop();
            var after = Snapshot.Capture();
            Debug.WriteLine(
                $"[VisionMemory] {_operationName} | elapsed={_stopwatch.ElapsedMilliseconds}ms | " +
                $"before WS={_before.WorkingSetMb:F1}MB Private={_before.PrivateMb:F1}MB Managed={_before.ManagedMb:F1}MB " +
                $"GC=({_before.Gen0},{_before.Gen1},{_before.Gen2}) | " +
                $"after-cleanup WS={after.WorkingSetMb:F1}MB Private={after.PrivateMb:F1}MB Managed={after.ManagedMb:F1}MB " +
                $"GC=({after.Gen0},{after.Gen1},{after.Gen2}) | " +
                $"delta WS={after.WorkingSetMb - _before.WorkingSetMb:+0.0;-0.0;0.0}MB " +
                $"Private={after.PrivateMb - _before.PrivateMb:+0.0;-0.0;0.0}MB");
#endif
        }
    }

#if DEBUG
    private readonly record struct Snapshot(long WorkingSetBytes, long PrivateBytes, long ManagedBytes, int Gen0, int Gen1, int Gen2)
    {
        internal double WorkingSetMb => WorkingSetBytes / 1024d / 1024d;
        internal double PrivateMb => PrivateBytes / 1024d / 1024d;
        internal double ManagedMb => ManagedBytes / 1024d / 1024d;

        internal static Snapshot Capture()
        {
            using var process = Process.GetCurrentProcess();
            return new Snapshot(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                GC.GetTotalMemory(forceFullCollection: false),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
    }
#endif
}

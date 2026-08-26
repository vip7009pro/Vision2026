using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspectionApp.UI.ViewModels;

public partial class ToolEditorViewModel
{
    // ═══════════════════════════════════════════════════════════════════
    // SYSTEM MONITOR: RAM + CPU Per-Core + CPU App Process
    // ═══════════════════════════════════════════════════════════════════

    private DispatcherTimer? _systemMonitorTimer;
    private int _cpuCoreCount;
    private TimeSpan _lastAppCpuTime;
    private DateTime _lastAppCpuSample;

    // Kernel32 API để lấy CPU per-core chính xác (không cần PerformanceCounter chậm)
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    private long[] _prevCoreIdle = Array.Empty<long>();
    private long[] _prevCoreKernel = Array.Empty<long>();
    private long[] _prevCoreUser = Array.Empty<long>();

    // ─── Observable Properties ───────────────────────────────────────

    [ObservableProperty]
    private string _systemRamText = "RAM: -- MB";

    [ObservableProperty]
    private string _cpuAppText = "App: --%";

    [ObservableProperty]
    private string _cpuSystemText = "CPU: --%";

    [ObservableProperty]
    private double _systemRamPercent;

    [ObservableProperty]
    private Brush _ramBarFillBrush = Brushes.LimeGreen;

    [ObservableProperty]
    private Brush _cpuAppBarFillBrush = Brushes.DodgerBlue;

    // CPU per-core heights (0.0 to 1.0 ratio) - tối đa 16 core slots
    [ObservableProperty] private double _cpuCore0Height;
    [ObservableProperty] private double _cpuCore1Height;
    [ObservableProperty] private double _cpuCore2Height;
    [ObservableProperty] private double _cpuCore3Height;
    [ObservableProperty] private double _cpuCore4Height;
    [ObservableProperty] private double _cpuCore5Height;
    [ObservableProperty] private double _cpuCore6Height;
    [ObservableProperty] private double _cpuCore7Height;
    [ObservableProperty] private double _cpuCore8Height;
    [ObservableProperty] private double _cpuCore9Height;
    [ObservableProperty] private double _cpuCore10Height;
    [ObservableProperty] private double _cpuCore11Height;
    [ObservableProperty] private double _cpuCore12Height;
    [ObservableProperty] private double _cpuCore13Height;
    [ObservableProperty] private double _cpuCore14Height;
    [ObservableProperty] private double _cpuCore15Height;

    // CPU per-core fill brushes (màu sắc theo tải)
    [ObservableProperty] private Brush _cpuCore0Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore1Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore2Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore3Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore4Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore5Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore6Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore7Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore8Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore9Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore10Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore11Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore12Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore13Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore14Fill = Brushes.LimeGreen;
    [ObservableProperty] private Brush _cpuCore15Fill = Brushes.LimeGreen;

    // Visibility cho từng core slot
    public int CpuCoreCount => _cpuCoreCount;
    public bool CpuCore0Visible => _cpuCoreCount > 0;
    public bool CpuCore1Visible => _cpuCoreCount > 1;
    public bool CpuCore2Visible => _cpuCoreCount > 2;
    public bool CpuCore3Visible => _cpuCoreCount > 3;
    public bool CpuCore4Visible => _cpuCoreCount > 4;
    public bool CpuCore5Visible => _cpuCoreCount > 5;
    public bool CpuCore6Visible => _cpuCoreCount > 6;
    public bool CpuCore7Visible => _cpuCoreCount > 7;
    public bool CpuCore8Visible => _cpuCoreCount > 8;
    public bool CpuCore9Visible => _cpuCoreCount > 9;
    public bool CpuCore10Visible => _cpuCoreCount > 10;
    public bool CpuCore11Visible => _cpuCoreCount > 11;
    public bool CpuCore12Visible => _cpuCoreCount > 12;
    public bool CpuCore13Visible => _cpuCoreCount > 13;
    public bool CpuCore14Visible => _cpuCoreCount > 14;
    public bool CpuCore15Visible => _cpuCoreCount > 15;

    [ObservableProperty]
    private double _cpuAppPercent;

    [ObservableProperty]
    private string _systemMonitorToolTip = "";

    // ─── Initialization ──────────────────────────────────────────────

    private void InitializeSystemMonitor()
    {
        _cpuCoreCount = Math.Min(16, Environment.ProcessorCount);

        try
        {
            var proc = Process.GetCurrentProcess();
            _lastAppCpuTime = proc.TotalProcessorTime;
            _lastAppCpuSample = DateTime.UtcNow;
        }
        catch { }

        // Khởi tạo dữ liệu per-core bằng WMI hoặc fallback
        InitializeCpuCoreTracking();

        _systemMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _systemMonitorTimer.Tick += (_, _) => UpdateSystemMonitor();
        _systemMonitorTimer.Start();

        // Cập nhật lần đầu
        UpdateSystemMonitor();
    }

    private PerformanceCounter[]? _cpuCoreCounters;

    private void InitializeCpuCoreTracking()
    {
        try
        {
            _cpuCoreCounters = new PerformanceCounter[_cpuCoreCount];
            for (int i = 0; i < _cpuCoreCount; i++)
            {
                _cpuCoreCounters[i] = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), true);
                _cpuCoreCounters[i].NextValue(); // Warm-up bắt buộc cho PerformanceCounter
            }
        }
        catch
        {
            _cpuCoreCounters = null;
        }
    }

    // ─── Update Tick ─────────────────────────────────────────────────

    private void UpdateSystemMonitor()
    {
        try
        {
            // ═══ RAM ═══
            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            long ramBytes = proc.WorkingSet64;
            double ramMb = ramBytes / (1024.0 * 1024.0);

            // Tổng RAM hệ thống (Win32 API)
            double totalRamGb = 0;
            double ramUsedPercent = 0;
            try
            {
                var memInfo = new MEMORYSTATUSEX();
                memInfo.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memInfo))
                {
                    totalRamGb = memInfo.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    ramUsedPercent = (ramMb / 1024.0) / totalRamGb * 100.0;
                }
            }
            catch { }

            if (ramMb < 1024)
                SystemRamText = $"RAM: {ramMb:F0} MB";
            else
                SystemRamText = $"RAM: {ramMb / 1024.0:F1} GB";
            SystemRamPercent = Math.Min(100, ramUsedPercent);
            RamBarFillBrush = ramUsedPercent < 50 ? Brushes.LimeGreen : ramUsedPercent < 75 ? Brushes.Orange : Brushes.OrangeRed;

            // ═══ CPU App (Process) ═══
            try
            {
                var now = DateTime.UtcNow;
                var currentCpuTime = proc.TotalProcessorTime;
                double elapsedSec = (now - _lastAppCpuSample).TotalSeconds;
                if (elapsedSec > 0.1)
                {
                    double cpuUsed = (currentCpuTime - _lastAppCpuTime).TotalSeconds;
                    double appCpuPercent = (cpuUsed / elapsedSec / Environment.ProcessorCount) * 100.0;
                    appCpuPercent = Math.Min(100, Math.Max(0, appCpuPercent));
                    CpuAppPercent = appCpuPercent;
                    CpuAppText = $"App: {appCpuPercent:F0}%";
                    CpuAppBarFillBrush = appCpuPercent < 50 ? Brushes.DodgerBlue : appCpuPercent < 80 ? Brushes.Orange : Brushes.OrangeRed;
                    _lastAppCpuTime = currentCpuTime;
                    _lastAppCpuSample = now;
                }
            }
            catch { }

            // ═══ CPU Per-Core (System-wide) ═══
            double totalCpuPercent = 0;
            int activeCount = 0;
            if (_cpuCoreCounters != null)
            {
                for (int i = 0; i < _cpuCoreCount && i < _cpuCoreCounters.Length; i++)
                {
                    try
                    {
                        double corePercent = Math.Min(100, Math.Max(0, _cpuCoreCounters[i].NextValue()));
                        totalCpuPercent += corePercent;
                        activeCount++;
                        double height = Math.Max(1, corePercent / 100.0 * 16.0); // 16px max height
                        var fill = GetCoreColorBrush(corePercent);
                        SetCoreVisuals(i, height, fill);
                    }
                    catch
                    {
                        SetCoreVisuals(i, 1, Brushes.DimGray);
                    }
                }
            }
            double avgCpu = activeCount > 0 ? totalCpuPercent / activeCount : 0;
            CpuSystemText = $"CPU: {avgCpu:F0}%";

            // ToolTip chi tiết
            SystemMonitorToolTip = $"💾 App RAM: {ramMb:F0} MB / System: {totalRamGb:F1} GB ({ramUsedPercent:F1}%)\n" +
                                   $"⚙️ App CPU: {CpuAppPercent:F1}%  |  System CPU: {avgCpu:F1}%\n" +
                                   $"🔧 Logical Cores: {_cpuCoreCount}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemMonitor] Error: {ex.Message}");
        }
    }

    private static Brush GetCoreColorBrush(double percent)
    {
        if (percent < 30) return Brushes.LimeGreen;
        if (percent < 60) return Brushes.YellowGreen;
        if (percent < 80) return Brushes.Orange;
        return Brushes.OrangeRed;
    }

    private void SetCoreVisuals(int index, double height, Brush fill)
    {
        switch (index)
        {
            case 0: CpuCore0Height = height; CpuCore0Fill = fill; break;
            case 1: CpuCore1Height = height; CpuCore1Fill = fill; break;
            case 2: CpuCore2Height = height; CpuCore2Fill = fill; break;
            case 3: CpuCore3Height = height; CpuCore3Fill = fill; break;
            case 4: CpuCore4Height = height; CpuCore4Fill = fill; break;
            case 5: CpuCore5Height = height; CpuCore5Fill = fill; break;
            case 6: CpuCore6Height = height; CpuCore6Fill = fill; break;
            case 7: CpuCore7Height = height; CpuCore7Fill = fill; break;
            case 8: CpuCore8Height = height; CpuCore8Fill = fill; break;
            case 9: CpuCore9Height = height; CpuCore9Fill = fill; break;
            case 10: CpuCore10Height = height; CpuCore10Fill = fill; break;
            case 11: CpuCore11Height = height; CpuCore11Fill = fill; break;
            case 12: CpuCore12Height = height; CpuCore12Fill = fill; break;
            case 13: CpuCore13Height = height; CpuCore13Fill = fill; break;
            case 14: CpuCore14Height = height; CpuCore14Fill = fill; break;
            case 15: CpuCore15Height = height; CpuCore15Fill = fill; break;
        }
    }

    private void DisposeSystemMonitor()
    {
        _systemMonitorTimer?.Stop();
        _systemMonitorTimer = null;

        if (_cpuCoreCounters != null)
        {
            foreach (var counter in _cpuCoreCounters)
            {
                try { counter?.Dispose(); } catch { }
            }
            _cpuCoreCounters = null;
        }
    }

    // ─── Win32 API for Total Physical Memory ─────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}

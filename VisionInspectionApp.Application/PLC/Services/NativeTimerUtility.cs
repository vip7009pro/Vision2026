using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VisionInspectionApp.Application.PLC.Services;

/// <summary>
/// Trình điều khiển độ phân giải Timer thời gian thực chuẩn công nghiệp trên Windows (High-Resolution Multimedia Timer).
/// Chuyển đổi Timer Resolution của hệ điều hành từ 15.625ms xuống 1.0ms, cho phép Task.Delay(1) và Thread.Sleep(1)
/// đạt độ chính xác mili-giây tuyệt đối phục vụ quét PLC tốc độ cao (100Hz - 1000Hz) và máy hiện sóng Oscilloscope.
/// </summary>
public static class NativeTimerUtility
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    private static extern uint TimeBeginPeriodNative(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    private static extern uint TimeEndPeriodNative(uint uMilliseconds);

    private static int _activePeriodRef;

    /// <summary>
    /// Kích hoạt chế độ High-Resolution Timer 1ms.
    /// Có cơ chế đếm tham chiếu (Reference Counting) thread-safe để bảo đảm an toàn khi nhiều module cùng yêu cầu.
    /// </summary>
    public static void TimeBeginPeriod(uint ms = 1)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (Interlocked.Increment(ref _activePeriodRef) == 1)
            {
                try
                {
                    TimeBeginPeriodNative(ms);
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Giải phóng chế độ High-Resolution Timer khi module kết thúc hoạt động.
    /// </summary>
    public static void TimeEndPeriod(uint ms = 1)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (Interlocked.Decrement(ref _activePeriodRef) <= 0)
            {
                Interlocked.Exchange(ref _activePeriodRef, 0);
                try
                {
                    TimeEndPeriodNative(ms);
                }
                catch { }
            }
        }
    }
}

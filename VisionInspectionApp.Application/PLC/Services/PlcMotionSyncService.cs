using System;
using System.Threading;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

/// <summary>
/// Dịch vụ đồng bộ hóa Chuyển động (Motion & Encoder) giữa PLC và Hệ thống Thị giác Vision PC
/// cho ứng dụng kiểm tra cuộn liên tục (Continuous Roll-to-Roll Web Inspection).
/// </summary>
public sealed class PlcMotionSyncService : IDisposable
{
    private readonly IPlcManagerService? _plcManager;
    private long _currentEncoderPulses;
    private double _currentLineSpeedMpm;
    private double _pulsesPerMm = 100.0; // Mặc định 100 xung / mm
    private double _mmPerPixel = 0.05;   // Mặc định 0.05 mm / pixel (50 micron)
    private double _nominalSpeedMpm = 30.0; // Vận tốc danh định 30 m/phút
    private double _baseExposureTimeUs = 500.0; // Thời gian phơi sáng chuẩn 500us
    private string _plcId = "PLC1";
    private string _encoderTagName = "D1000";
    private string _speedTagName = "D1002";
    private bool _isDisposed;

    public string PlcId
    {
        get => _plcId;
        set => _plcId = value ?? "PLC1";
    }

    public string EncoderTagName
    {
        get => _encoderTagName;
        set => _encoderTagName = value ?? "D1000";
    }

    public string SpeedTagName
    {
        get => _speedTagName;
        set => _speedTagName = value ?? "D1002";
    }

    public double PulsesPerMm
    {
        get => _pulsesPerMm;
        set => _pulsesPerMm = value <= 0 ? 1.0 : value;
    }

    public double MmPerPixel
    {
        get => _mmPerPixel;
        set => _mmPerPixel = value <= 0 ? 0.01 : value;
    }

    public double NominalSpeedMpm
    {
        get => _nominalSpeedMpm;
        set => _nominalSpeedMpm = value <= 0 ? 1.0 : value;
    }

    public double BaseExposureTimeUs
    {
        get => _baseExposureTimeUs;
        set => _baseExposureTimeUs = value <= 0 ? 100.0 : value;
    }

    public long CurrentEncoderPulses => Interlocked.Read(ref _currentEncoderPulses);

    public double CurrentWebPositionMm
    {
        get
        {
            long pulses = CurrentEncoderPulses;
            return pulses / PulsesPerMm;
        }
    }

    public double CurrentLineSpeedMpm
    {
        get
        {
            lock (this)
            {
                return _currentLineSpeedMpm;
            }
        }
    }

    public PlcMotionSyncService(IPlcManagerService? plcManager = null)
    {
        _plcManager = plcManager;
        if (_plcManager != null)
        {
            _plcManager.OnTagChanged += HandlePlcTagChanged;
        }
    }

    /// <summary>
    /// Xử lý cập nhật giá trị tag từ PLC Polling Engine
    /// </summary>
    private void HandlePlcTagChanged(object? sender, TagChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PlcId) || !string.Equals(e.PlcId, _plcId, StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(e.TagName, _encoderTagName, StringComparison.OrdinalIgnoreCase))
        {
            if (TryConvertToLong(e.NewValue, out long pulses))
            {
                Interlocked.Exchange(ref _currentEncoderPulses, pulses);
            }
        }
        else if (string.Equals(e.TagName, _speedTagName, StringComparison.OrdinalIgnoreCase))
        {
            if (TryConvertToDouble(e.NewValue, out double speed))
            {
                lock (this)
                {
                    _currentLineSpeedMpm = speed;
                }
            }
        }
    }

    /// <summary>
    /// Cập nhật thủ công xung Encoder và tốc độ (Dành cho bài kiểm thử hoặc luồng trực tiếp)
    /// </summary>
    public void UpdateMotionState(long encoderPulses, double lineSpeedMpm)
    {
        Interlocked.Exchange(ref _currentEncoderPulses, encoderPulses);
        lock (this)
        {
            _currentLineSpeedMpm = lineSpeedMpm;
        }
    }

    /// <summary>
    /// Đóng gói Siêu dữ liệu Chuyển động (FrameMetadata) cho một Frame ảnh vừa chụp
    /// </summary>
    public FrameMetadata CreateFrameMetadata(long frameIndex, ulong hardwareTimestampNs = 0)
    {
        double speed = CurrentLineSpeedMpm;
        double webPosMm = CurrentWebPositionMm;
        long pulses = CurrentEncoderPulses;

        // Tính hệ số bù trừ phơi sáng tự động theo vận tốc
        double exposureRatio = 1.0;
        if (_nominalSpeedMpm > 0 && speed > 0)
        {
            exposureRatio = Math.Clamp(_nominalSpeedMpm / speed, 0.2, 5.0);
        }

        return new FrameMetadata
        {
            FrameIndex = frameIndex,
            HardwareTimestampNs = hardwareTimestampNs,
            HostTimestamp = DateTime.UtcNow,
            EncoderPulses = pulses,
            WebPositionMm = webPosMm,
            LineSpeedMpm = speed,
            MmPerPixel = _mmPerPixel,
            ExposureCompensationRatio = exposureRatio
        };
    }

    /// <summary>
    /// Tính toán độ mờ chuyển động dự kiến (Motion Blur) theo pixel dựa trên thời gian phơi sáng và tốc độ cuộn
    /// </summary>
    /// <param name="exposureTimeUs">Thời gian phơi sáng microsecond (us)</param>
    /// <returns>Độ mờ vệt chuyển động tính theo pixel</returns>
    public double CalculateMotionBlurPixels(double exposureTimeUs)
    {
        double speedMpm = CurrentLineSpeedMpm;
        if (speedMpm <= 0 || exposureTimeUs <= 0 || _mmPerPixel <= 0) return 0.0;

        // Vận tốc mm/microsecond = (Speed * 1000 mm) / (60 * 1,000,000 us)
        double speedMmPerUs = (speedMpm * 1000.0) / (60.0 * 1_000_000.0);
        double blurMm = speedMmPerUs * exposureTimeUs;
        return blurMm / _mmPerPixel;
    }

    /// <summary>
    /// Tính thời gian phơi sáng tối đa cho phép để đảm bảo độ mờ chuyển động nhỏ hơn 1 pixel
    /// </summary>
    /// <param name="maxAllowedBlurPixels">Ngưỡng mờ tối đa (thông thường 0.5 - 1.0 pixel)</param>
    /// <returns>Thời gian phơi sáng tối đa (us)</returns>
    public double CalculateMaxExposureForSharpImage(double maxAllowedBlurPixels = 1.0)
    {
        double speedMpm = CurrentLineSpeedMpm;
        if (speedMpm <= 0) return _baseExposureTimeUs;

        double speedMmPerUs = (speedMpm * 1000.0) / (60.0 * 1_000_000.0);
        double maxBlurMm = maxAllowedBlurPixels * _mmPerPixel;
        return maxBlurMm / speedMmPerUs;
    }

    private static bool TryConvertToLong(object? val, out long result)
    {
        if (val is long l) { result = l; return true; }
        if (val is int i) { result = i; return true; }
        if (val is uint u) { result = u; return true; }
        if (val is ulong ul) { result = (long)ul; return true; }
        if (val != null && long.TryParse(val.ToString(), out long parsed))
        {
            result = parsed;
            return true;
        }
        result = 0;
        return false;
    }

    private static bool TryConvertToDouble(object? val, out double result)
    {
        if (val is double d) { result = d; return true; }
        if (val is float f) { result = f; return true; }
        if (val is int i) { result = i; return true; }
        if (val is long l) { result = l; return true; }
        if (val != null && double.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
        {
            result = parsed;
            return true;
        }
        result = 0;
        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_plcManager != null)
        {
            _plcManager.OnTagChanged -= HandlePlcTagChanged;
        }
    }
}

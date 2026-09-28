using System.Globalization;

namespace VisionInspectionApp.VisionEngine;

/// <summary>
/// Toán học phục vụ hiệu chuẩn tỉ lệ Calib (Pixels/mm) trực tiếp từ kết quả đo của các tool đo.
/// Được tách riêng khỏi ViewModel để có thể kiểm thử tự động (unit test) độc lập.
/// </summary>
public static class CalibFactorMath
{
    /// <summary>
    /// Ngưỡng tối thiểu để coi một kích thước (mm) hoặc một tỉ lệ (px/mm) là hợp lệ.
    /// </summary>
    public const double MinValidValue = 0.0001;

    /// <summary>
    /// Phân tích chuỗi số đo THỰC TẾ (mm) do kỹ sư nhập ở ô cạnh nút "Đặt làm Hệ Số Calib".
    /// Chấp nhận cả dấu chấm (50.02) lẫn dấu phẩy (50,02) và tự bỏ khoảng trắng.
    /// </summary>
    /// <returns>Số đo hợp lệ (mm); trả về 0.0 nếu ô để trống hoặc dữ liệu không hợp lệ.</returns>
    public static double ParseMeasuredMm(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.0;

        var raw = text.Trim().Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value > MinValidValue)
        {
            return value;
        }

        return 0.0;
    }

    /// <summary>
    /// Xác định kích thước chuẩn (mm) sẽ dùng để tính hệ số Calib.
    /// Ưu tiên số đo THỰC TẾ nhập ở ô riêng; chỉ khi ô đó để trống (= 0) mới dùng kích thước
    /// danh định (Nominal) trong phần Spec để bảo đảm tương thích ngược.
    /// </summary>
    /// <param name="calibActualMm">Số đo thực tế (mm) nhập ở ô riêng; bằng 0 khi để trống.</param>
    /// <param name="toolNominalMm">Kích thước danh định (Nominal) trong Spec của tool đo.</param>
    public static double ResolveNominalMm(double calibActualMm, double toolNominalMm)
        => calibActualMm > MinValidValue ? calibActualMm : toolNominalMm;

    /// <summary>
    /// Tính tỉ lệ Pixels/mm = kích thước đo được trên ảnh (px) chia cho kích thước thực tế (mm).
    /// Trả về 0.0 khi dữ liệu vào không hợp lệ (không trả về NaN/Infinity cho tầng giao diện).
    /// </summary>
    public static double ComputePixelsPerMm(double measuredPx, double effectiveNominalMm)
    {
        if (measuredPx <= MinValidValue || effectiveNominalMm <= MinValidValue)
            return 0.0;

        var ppm = measuredPx / effectiveNominalMm;
        if (double.IsNaN(ppm) || double.IsInfinity(ppm) || ppm <= MinValidValue)
            return 0.0;

        return ppm;
    }
}

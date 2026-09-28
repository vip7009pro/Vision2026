using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Views;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel
{
    private ICommand? _setAsCalibFactorCommand;

    /// <summary>
    /// Lệnh lấy kết quả đo pixel từ công cụ hiện tại chia cho kích thước thực tế (mm) để tính ra
    /// tỉ lệ PixelsPerMm (Trục X hoặc Trục Y) và áp dụng vào cấu hình Job.
    /// Kích thước thực tế được ưu tiên lấy từ ô "Đo thực tế (mm)" nằm ngay bên trái nút này;
    /// nếu ô đó để trống mới dùng kích thước danh định Nominal trong Spec (Spec luôn được giữ nguyên).
    /// </summary>
    public ICommand SetAsCalibFactorCommand => _setAsCalibFactorCommand ??= new RelayCommand(ExecuteSetAsCalibFactor);

    /// <summary>
    /// Cho biết công cụ đo hiện tại trong nhóm Distance có hỗ trợ hiệu chuẩn khoảng cách hay không (loại trừ Angle).
    /// </summary>
    public bool IsDistanceCalibratableNode =>
        IsDistanceNode || IsLineLineDistanceNode || IsPointLineDistanceNode ||
        IsSegmentLineDistanceNode || IsEdgePairNode || IsDiameterNode;

    private string _calibActualMmText = string.Empty;

    /// <summary>
    /// Ô nhập SỐ ĐO THỰC TẾ (mm) của cữ mẫu, nằm ngay bên trái nút "Đặt làm Hệ Số Calib".
    /// Ô này TÁCH RIÊNG hoàn toàn khỏi ô 'Nominal' trong phần Spec: giá trị nhập ở đây chỉ dùng để
    /// tính tỉ lệ Calib (px/mm), không ghi đè kích thước danh định nên không làm sai lệch Spec/Tolerance.
    /// </summary>
    public string CalibActualMmText
    {
        get => _calibActualMmText;
        set
        {
            var v = value ?? string.Empty;
            if (string.Equals(_calibActualMmText, v, StringComparison.Ordinal))
                return;

            _calibActualMmText = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CalibActualMm));
            OnPropertyChanged(nameof(HasCalibActualMm));
            OnPropertyChanged(nameof(CalibActualMmHint));
        }
    }

    /// <summary>Số đo thực tế (mm) đã phân tích từ <see cref="CalibActualMmText"/>; bằng 0 khi ô để trống.</summary>
    public double CalibActualMm => CalibFactorMath.ParseMeasuredMm(_calibActualMmText);

    /// <summary>Cho biết kỹ sư đã nhập số đo thực tế hay chưa (dùng để ẩn/hiện dòng ghi chú nhắc nhở).</summary>
    public bool HasCalibActualMm => CalibActualMm > CalibFactorMath.MinValidValue;

    /// <summary>Dòng ghi chú khẳng định ô Nominal (Spec) vẫn được giữ nguyên khi calib theo số đo thực tế.</summary>
    public string CalibActualMmHint => HasCalibActualMm
        ? $"✅ Calib theo số đo thực tế {CalibActualMm:F4} mm — ô Nominal (Spec) được giữ nguyên."
        : string.Empty;

    /// <summary>Xóa ô "Đo thực tế (mm)" khi chuyển sang công cụ đo khác để tránh calib nhầm số đo cũ.</summary>
    public void ResetCalibActualMm() => CalibActualMmText = string.Empty;

    public double PixelsPerMmX
    {
        get => _config?.GetEffectivePpmX() ?? 1.0;
        set
        {
            if (_config != null && Math.Abs(_config.PixelsPerMmX - value) > 0.00001)
            {
                _config.PixelsPerMmX = value;
                OnPropertyChanged();
                IsDirty = true;
            }
        }
    }

    public double PixelsPerMmY
    {
        get => _config?.GetEffectivePpmY() ?? 1.0;
        set
        {
            if (_config != null && Math.Abs(_config.PixelsPerMmY - value) > 0.00001)
            {
                _config.PixelsPerMmY = value;
                OnPropertyChanged();
                IsDirty = true;
            }
        }
    }

    /// <summary>
    /// Trích xuất khoảng cách đo đạc bằng pixel (measuredPx), kích thước danh định (nominalMm),
    /// và góc định hướng đo (angleDeg) từ công cụ đang chọn.
    /// </summary>
    public (bool Success, string Message, double MeasuredPx, double NominalMm, string ToolName, string ToolType, double AngleDeg) TryGetSelectedToolMeasurementForCalib()
    {
        if (SelectedNode is null || string.IsNullOrWhiteSpace(SelectedNode.RefName))
        {
            return (false, "Vui lòng chọn một công cụ đo lường trên đồ thị trước khi hiệu chuẩn!", 0, 0, "", "", double.NaN);
        }

        if (_lastRun is null)
        {
            return (false, "Chưa có kết quả kiểm tra gần nhất. Vui lòng bấm 'Chạy 1 lần' (Run Once) để lấy kết quả đo của công cụ!", 0, 0, SelectedNode.RefName, SelectedNode.Type, double.NaN);
        }

        var nodeType = SelectedNode.Type;
        var refName = SelectedNode.RefName;

        // 1. Distance (Đo khoảng cách 2 điểm neo)
        if (string.Equals(nodeType, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình của công cụ Distance.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.Distances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ Distance '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = 0.0;
            double angleDeg = 0.0;
            Point2d? pa = ResolveAnchorPoint(_lastRun, def.PointA);
            Point2d? pb = ResolveAnchorPoint(_lastRun, def.PointB);
            if (pa.HasValue && pb.HasValue)
            {
                var dx = pa.Value.X - pb.Value.X;
                var dy = pa.Value.Y - pb.Value.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
                angleDeg = CalculateAngleDeg(pa.Value, pb.Value);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "Distance", angleDeg);
        }

        // 2. SegmentLineDistance (Khoảng cách đoạn thẳng tới đường thẳng)
        if (string.Equals(nodeType, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedSegmentLineDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình SegmentLineDistance.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.SegmentLineDistances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ SegmentLineDistance '{refName}' chưa phát hiện được đường hoặc khoảng cách!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = 0.0;
            double angleDeg = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
                angleDeg = CalculateAngleDeg(r.ClosestA, r.ClosestB);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "SegmentLineDistance", angleDeg);
        }

        // 3. LineLineDistance (Khoảng cách giữa 2 đường thẳng)
        if (string.Equals(nodeType, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedLineLineDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình LineLineDistance.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ LineLineDistance '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = 0.0;
            double angleDeg = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
                angleDeg = CalculateAngleDeg(r.ClosestA, r.ClosestB);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "LineLineDistance", angleDeg);
        }

        // 4. PointLineDistance (Khoảng cách điểm tới đường thẳng)
        if (string.Equals(nodeType, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedPointLineDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình PointLineDistance.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ PointLineDistance '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = 0.0;
            double angleDeg = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
                angleDeg = CalculateAngleDeg(r.ClosestA, r.ClosestB);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "PointLineDistance", angleDeg);
        }

        // 5. EdgePairDetect (Dò tìm cặp biên song song Caliper)
        if (string.Equals(nodeType, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedEdgePairDetectDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình EdgePairDetect.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || double.IsNaN(r.Value))
                return (false, $"Công cụ EdgePairDetect '{refName}' chưa phát hiện được cặp biên song song!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = 0.0;
            double angleDeg = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
                angleDeg = CalculateAngleDeg(r.ClosestA, r.ClosestB);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "EdgePairDetect", angleDeg);
        }

        // 6. EdgePair (Khoảng cách giữa 2 đường biên)
        if (string.Equals(nodeType, "EdgePair", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedEdgePairDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình EdgePair.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || double.IsNaN(r.Value))
                return (false, $"Công cụ EdgePair '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = 0.0;
            double angleDeg = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
                angleDeg = CalculateAngleDeg(r.ClosestA, r.ClosestB);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "EdgePair", angleDeg);
        }

        // 7. Diameter (Đo đường kính từ Circle Finder)
        if (string.Equals(nodeType, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedDiameterDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình Diameter.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.Diameters.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || double.IsNaN(r.Value))
                return (false, $"Công cụ Diameter '{refName}' chưa tìm thấy đường tròn hợp lệ!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = r.RadiusPx > 0.0001 ? 2.0 * r.RadiusPx : ((_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value);
            return (true, "", measuredPx, def.Nominal, def.Name, "Diameter", double.NaN);
        }

        // 8. CircleFinder (Tìm đường tròn)
        if (string.Equals(nodeType, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedCircleFinderDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình CircleFinder.", 0, 0, refName, nodeType, double.NaN);

            var r = _lastRun.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || r.RadiusPx <= 0.0001)
                return (false, $"Công cụ CircleFinder '{refName}' chưa tìm thấy đường tròn trên ảnh!", 0, 0, refName, nodeType, double.NaN);

            double measuredPx = 2.0 * r.RadiusPx; // Mặc định tính theo đường kính (diameter)
            double nominalMm = def.NominalDiameter;

            if (nominalMm <= 0.0001 && _config?.Diameters != null)
            {
                var linkedDia = _config.Diameters.FirstOrDefault(x => string.Equals(x.CircleRef, def.Name, StringComparison.OrdinalIgnoreCase));
                if (linkedDia != null && linkedDia.Nominal > 0.0001)
                {
                    nominalMm = linkedDia.Nominal;
                }
            }

            return (true, "", measuredPx, nominalMm, def.Name, "CircleFinder", double.NaN);
        }

        return (false, $"Công cụ loại '{nodeType}' không hỗ trợ tính toán hiệu chuẩn tỉ lệ Pixels/mm.", 0, 0, refName, nodeType, double.NaN);
    }

    /// <summary>
    /// Mở hộp thoại chọn trục và thực hiện cập nhật hệ số Calib cho Job (hỗ trợ Trục X, Trục Y hoặc Đồng bộ).
    /// </summary>
    public void ExecuteSetAsCalibFactor()
    {
        var (success, message, measuredPx, nominalMm, toolName, toolType, angleDeg) = TryGetSelectedToolMeasurementForCalib();
        if (!success)
        {
            MessageBox.Show(message, "Hiệu Chuẩn Calib", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (measuredPx <= CalibFactorMath.MinValidValue)
        {
            MessageBox.Show($"Kích thước đo được không hợp lệ ({measuredPx:F2} px).\nVui lòng kiểm tra lại kết quả chạy của tool!", 
                "Hiệu Chuẩn Calib", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Ưu tiên SỐ ĐO THỰC TẾ nhập ở ô riêng cạnh nút; chỉ khi ô đó để trống mới dùng Nominal (Spec).
        double actualMm = CalibActualMm;
        bool usingActualMeasured = actualMm > CalibFactorMath.MinValidValue;
        double effectiveNominalMm = CalibFactorMath.ResolveNominalMm(actualMm, nominalMm);

        if (effectiveNominalMm <= CalibFactorMath.MinValidValue)
        {
            MessageBox.Show(
                "Chưa có kích thước chuẩn (mm) để hiệu chuẩn!\n\n" +
                "Vui lòng nhập số đo THỰC TẾ của cữ mẫu vào ô 'Đo thực tế (mm)' ngay bên trái nút này " +
                "(khuyến nghị), hoặc nhập kích thước danh định vào ô 'Nominal' (đối với đo khoảng cách) " +
                "/ 'Nom Dia' (đối với Circle Finder) rồi bấm lại nút 'Đặt làm Hệ Số Calib'.\n\n" +
                "Lưu ý: ô 'Đo thực tế (mm)' là ô nhập riêng, KHÔNG ghi đè kích thước Spec của công cụ.",
                "Chưa Nhập Kích Thước Chuẩn", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double newPixelsPerMm = CalibFactorMath.ComputePixelsPerMm(measuredPx, effectiveNominalMm);
        if (newPixelsPerMm <= CalibFactorMath.MinValidValue)
        {
            MessageBox.Show("Tỉ lệ tính toán không hợp lệ hoặc quá nhỏ. Vui lòng kiểm tra lại kích thước.", 
                "Lỗi Tính Toán", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        double curPpmX = _config?.GetEffectivePpmX() ?? 1.0;
        double curPpmY = _config?.GetEffectivePpmY() ?? 1.0;

        // Mở cửa sổ CalibAxisSelectionDialog cho phép kỹ sư chọn trục X, Y hoặc Cả hai
        var dialog = new CalibAxisSelectionDialog(
            toolName,
            toolType,
            measuredPx,
            effectiveNominalMm,
            newPixelsPerMm,
            angleDeg,
            curPpmX,
            curPpmY,
            usingActualMeasured)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        if (_config != null)
        {
            double finalPpm = dialog.NewPixelsPerMm;

            // Ghi rõ nguồn kích thước chuẩn đã dùng (giữ nguyên Spec hay dùng số đo thực tế) để kỹ sư đối chiếu.
            string sizeSourceText = usingActualMeasured
                ? $" [dùng số đo thực tế {effectiveNominalMm:F4} mm — Nominal Spec giữ nguyên]"
                : $" [dùng Nominal Spec {effectiveNominalMm:F4} mm]";

            switch (dialog.SelectedTarget)
            {
                case CalibAxisTarget.AxisX:
                    _config.PixelsPerMmX = finalPpm;
                    if (_config.PixelsPerMmY <= 0.0001)
                        _config.PixelsPerMmY = curPpmY;
                    _config.PixelsPerMm = finalPpm;
                    StatusBarText = $"🎯 Đã áp dụng Calib TRỤC X (Ngang): {finalPpm:F4} px/mm từ [{toolName}]{sizeSourceText}! (Hiện tại: X={_config.PixelsPerMmX:F2}, Y={_config.PixelsPerMmY:F2})";
                    break;

                case CalibAxisTarget.AxisY:
                    _config.PixelsPerMmY = finalPpm;
                    if (_config.PixelsPerMmX <= 0.0001)
                        _config.PixelsPerMmX = curPpmX;
                    _config.PixelsPerMm = finalPpm;
                    StatusBarText = $"🎯 Đã áp dụng Calib TRỤC Y (Dọc): {finalPpm:F4} px/mm từ [{toolName}]{sizeSourceText}! (Hiện tại: X={_config.PixelsPerMmX:F2}, Y={_config.PixelsPerMmY:F2})";
                    break;

                case CalibAxisTarget.BothAxes:
                default:
                    _config.PixelsPerMmX = finalPpm;
                    _config.PixelsPerMmY = finalPpm;
                    _config.PixelsPerMm = finalPpm;
                    StatusBarText = $"🎯 Đã áp dụng Calib ĐỒNG BỘ 2 TRỤC: {finalPpm:F4} px/mm từ [{toolName}]{sizeSourceText}!";
                    break;
            }

            OnPropertyChanged(nameof(PixelsPerMm));
            OnPropertyChanged(nameof(PixelsPerMmX));
            OnPropertyChanged(nameof(PixelsPerMmY));
            OnPropertyChanged(nameof(SpecResultsValueHeader));
            IsDirty = true;
            RequestAutoSave();

            // Tự động tính toán lại toàn bộ đồ thị theo tỉ lệ Calib 2 trục mới
            RunFlow();
        }
    }

    private static double CalculateAngleDeg(Point2d p1, Point2d p2)
    {
        var dx = Math.Abs(p2.X - p1.X);
        var dy = Math.Abs(p2.Y - p1.Y);
        if (dx < 1e-9 && dy < 1e-9)
            return 0.0;
        return Math.Atan2(dy, dx) * 180.0 / Math.PI;
    }

    private static Point2d? ResolveAnchorPoint(InspectionResult run, string anchorName)
    {
        if (string.IsNullOrWhiteSpace(anchorName))
            return null;

        var p = run.Points.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
        if (p is not null)
            return p.Position;

        var cf = run.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
        if (cf is not null && cf.Found)
            return cf.Center;

        var dia = run.Diameters.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
        if (dia is not null && dia.Found)
            return dia.Center;

        return null;
    }
}

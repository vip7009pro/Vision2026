using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel
{
    private ICommand? _setAsCalibFactorCommand;

    /// <summary>
    /// Lệnh lấy kết quả đo pixel từ công cụ hiện tại chia cho kích thước danh định (Nominal mm)
    /// để tính ra tỉ lệ PixelsPerMm và áp dụng vào cấu hình Job.
    /// </summary>
    public ICommand SetAsCalibFactorCommand => _setAsCalibFactorCommand ??= new RelayCommand(ExecuteSetAsCalibFactor);

    /// <summary>
    /// Cho biết công cụ đo hiện tại trong nhóm Distance có hỗ trợ hiệu chuẩn khoảng cách hay không (loại trừ Angle).
    /// </summary>
    public bool IsDistanceCalibratableNode =>
        IsDistanceNode || IsLineLineDistanceNode || IsPointLineDistanceNode ||
        IsSegmentLineDistanceNode || IsEdgePairNode || IsDiameterNode;

    /// <summary>
    /// Trích xuất khoảng cách đo đạc bằng pixel (measuredPx) và kích thước danh định (nominalMm)
    /// từ công cụ đo đang được chọn và kết quả chạy gần nhất.
    /// </summary>
    public (bool Success, string Message, double MeasuredPx, double NominalMm, string ToolName, string ToolType) TryGetSelectedToolMeasurementForCalib()
    {
        if (SelectedNode is null || string.IsNullOrWhiteSpace(SelectedNode.RefName))
        {
            return (false, "Vui lòng chọn một công cụ đo lường trên đồ thị trước khi hiệu chuẩn!", 0, 0, "", "");
        }

        if (_lastRun is null)
        {
            return (false, "Chưa có kết quả kiểm tra gần nhất. Vui lòng bấm 'Chạy 1 lần' (Run Once) để lấy kết quả đo của công cụ!", 0, 0, SelectedNode.RefName, SelectedNode.Type);
        }

        var nodeType = SelectedNode.Type;
        var refName = SelectedNode.RefName;
        double currentPpm = _config?.PixelsPerMm is > 0.0001 ? _config.PixelsPerMm : 1.0;

        // 1. Distance (Đo khoảng cách 2 điểm neo)
        if (string.Equals(nodeType, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình của công cụ Distance.", 0, 0, refName, nodeType);

            var r = _lastRun.Distances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ Distance '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType);

            double measuredPx = 0.0;
            Point2d? pa = ResolveAnchorPoint(_lastRun, def.PointA);
            Point2d? pb = ResolveAnchorPoint(_lastRun, def.PointB);
            if (pa.HasValue && pb.HasValue)
            {
                var dx = pa.Value.X - pb.Value.X;
                var dy = pa.Value.Y - pb.Value.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "Distance");
        }

        // 2. SegmentLineDistance (Khoảng cách đoạn thẳng tới đường thẳng)
        if (string.Equals(nodeType, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedSegmentLineDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình SegmentLineDistance.", 0, 0, refName, nodeType);

            var r = _lastRun.SegmentLineDistances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ SegmentLineDistance '{refName}' chưa phát hiện được đường hoặc khoảng cách!", 0, 0, refName, nodeType);

            double measuredPx = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "SegmentLineDistance");
        }

        // 3. LineLineDistance (Khoảng cách giữa 2 đường thẳng)
        if (string.Equals(nodeType, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedLineLineDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình LineLineDistance.", 0, 0, refName, nodeType);

            var r = _lastRun.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ LineLineDistance '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType);

            double measuredPx = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "LineLineDistance");
        }

        // 4. PointLineDistance (Khoảng cách điểm tới đường thẳng)
        if (string.Equals(nodeType, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedPointLineDistanceDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình PointLineDistance.", 0, 0, refName, nodeType);

            var r = _lastRun.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || double.IsNaN(r.Value))
                return (false, $"Công cụ PointLineDistance '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType);

            double measuredPx = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "PointLineDistance");
        }

        // 5. EdgePairDetect (Dò tìm cặp biên song song Caliper)
        if (string.Equals(nodeType, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedEdgePairDetectDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình EdgePairDetect.", 0, 0, refName, nodeType);

            var r = _lastRun.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || double.IsNaN(r.Value))
                return (false, $"Công cụ EdgePairDetect '{refName}' chưa phát hiện được cặp biên song song!", 0, 0, refName, nodeType);

            double measuredPx = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "EdgePairDetect");
        }

        // 6. EdgePair (Khoảng cách giữa 2 đường biên)
        if (string.Equals(nodeType, "EdgePair", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedEdgePairDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình EdgePair.", 0, 0, refName, nodeType);

            var r = _lastRun.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || double.IsNaN(r.Value))
                return (false, $"Công cụ EdgePair '{refName}' chưa có kết quả đo hợp lệ!", 0, 0, refName, nodeType);

            double measuredPx = 0.0;
            if (r.ClosestA != default || r.ClosestB != default)
            {
                var dx = r.ClosestA.X - r.ClosestB.X;
                var dy = r.ClosestA.Y - r.ClosestB.Y;
                measuredPx = Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                measuredPx = (_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value;
            }

            return (true, "", measuredPx, def.Nominal, def.Name, "EdgePair");
        }

        // 7. Diameter (Đo đường kính từ Circle Finder)
        if (string.Equals(nodeType, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedDiameterDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình Diameter.", 0, 0, refName, nodeType);

            var r = _lastRun.Diameters.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || double.IsNaN(r.Value))
                return (false, $"Công cụ Diameter '{refName}' chưa tìm thấy đường tròn hợp lệ!", 0, 0, refName, nodeType);

            double measuredPx = r.RadiusPx > 0.0001 ? 2.0 * r.RadiusPx : ((_config?.PixelsPerMm is > 0.0001) ? r.Value * _config.PixelsPerMm : r.Value);
            return (true, "", measuredPx, def.Nominal, def.Name, "Diameter");
        }

        // 8. CircleFinder (Tìm đường tròn)
        if (string.Equals(nodeType, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            var def = SelectedCircleFinderDef();
            if (def is null)
                return (false, "Không tìm thấy cấu hình CircleFinder.", 0, 0, refName, nodeType);

            var r = _lastRun.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || r.RadiusPx <= 0.0001)
                return (false, $"Công cụ CircleFinder '{refName}' chưa tìm thấy đường tròn trên ảnh!", 0, 0, refName, nodeType);

            double measuredPx = 2.0 * r.RadiusPx; // Mặc định tính theo đường kính (diameter)
            double nominalMm = def.NominalDiameter;

            // Nếu CircleFinder chưa điền NominalDiameter, kiểm tra xem có tool Diameter nào liên kết đến không
            if (nominalMm <= 0.0001 && _config?.Diameters != null)
            {
                var linkedDia = _config.Diameters.FirstOrDefault(x => string.Equals(x.CircleRef, def.Name, StringComparison.OrdinalIgnoreCase));
                if (linkedDia != null && linkedDia.Nominal > 0.0001)
                {
                    nominalMm = linkedDia.Nominal;
                }
            }

            return (true, "", measuredPx, nominalMm, def.Name, "CircleFinder");
        }

        return (false, $"Công cụ loại '{nodeType}' không hỗ trợ tính toán hiệu chuẩn tỉ lệ Pixels/mm.", 0, 0, refName, nodeType);
    }

    /// <summary>
    /// Thực hiện tính toán và gán hệ số Calib cho Job từ kết quả đo của công cụ đang chọn.
    /// </summary>
    public void ExecuteSetAsCalibFactor()
    {
        var (success, message, measuredPx, nominalMm, toolName, toolType) = TryGetSelectedToolMeasurementForCalib();
        if (!success)
        {
            MessageBox.Show(message, "Hiệu Chuẩn Calib", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (measuredPx <= 0.0001)
        {
            MessageBox.Show($"Kích thước đo được không hợp lệ ({measuredPx:F2} px).\nVui lòng kiểm tra lại kết quả chạy của tool!", 
                "Hiệu Chuẩn Calib", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (nominalMm <= 0.0001)
        {
            MessageBox.Show(
                $"Kích thước chuẩn danh định (Nominal) đang bằng 0 hoặc chưa được nhập!\n\n" +
                $"Vui lòng nhập kích thước thực tế của cữ mẫu vào ô 'Nominal' (đối với đo khoảng cách) " +
                $"hoặc 'Nom Dia' (đối với Circle Finder) trước khi bấm 'Đặt làm Hệ Số Calib'.",
                "Chưa Nhập Kích Thước Chuẩn", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double newPixelsPerMm = measuredPx / nominalMm;
        if (double.IsNaN(newPixelsPerMm) || double.IsInfinity(newPixelsPerMm) || newPixelsPerMm <= 0.0001)
        {
            MessageBox.Show("Tỉ lệ tính toán không hợp lệ hoặc quá nhỏ. Vui lòng kiểm tra lại kích thước.", 
                "Lỗi Tính Toán", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        newPixelsPerMm = Math.Round(newPixelsPerMm, 4);
        double currentPpm = _config?.PixelsPerMm is > 0.0001 ? _config.PixelsPerMm : 1.0;
        double diffPercent = currentPpm > 0.0001 ? ((newPixelsPerMm - currentPpm) / currentPpm) * 100.0 : 0.0;

        string warning = "";
        if (Math.Abs(diffPercent) > 10.0 && currentPpm > 0.0001)
        {
            warning = "⚠️ CẢNH BÁO: Tỉ lệ mới chênh lệch trên 10% so với tỉ lệ cũ!\nHãy chắc chắn bạn đang đặt phôi mẫu chuẩn (Golden Sample) dưới camera.\n\n";
        }

        string confirmMsg =
            $"🎯 XÁC NHẬN HIỆU CHUẨN TỈ LỆ CALIB CHO JOB\n\n" +
            $"• Công cụ: {toolName} ({toolType})\n" +
            $"• Kích thước đo thực tế: {measuredPx:F2} px\n" +
            $"• Kích thước danh định:  {nominalMm:F3} mm\n" +
            $"────────────────────────────────────────\n" +
            $"• Hệ số Calib hiện tại: {currentPpm:F4} px/mm\n" +
            $"• Hệ số Calib MỚI:     {newPixelsPerMm:F4} px/mm  ({(diffPercent >= 0 ? "+" : "")}{diffPercent:F2}%)\n" +
            $"────────────────────────────────────────\n\n" +
            warning +
            $"Bạn có muốn áp dụng hệ số {newPixelsPerMm:F4} px/mm này vào cấu hình Job hiện tại không?\n" +
            $"(Toàn bộ các công cụ đo lường trong Job sẽ tự động tính toán lại theo tỉ lệ mm mới)";

        var icon = Math.Abs(diffPercent) > 10.0 && currentPpm > 0.0001 ? MessageBoxImage.Warning : MessageBoxImage.Question;
        var res = MessageBox.Show(confirmMsg, "Xác Nhận Đặt Hệ Số Calib", MessageBoxButton.YesNo, icon);
        if (res != MessageBoxResult.Yes)
            return;

        if (_config != null)
        {
            _config.PixelsPerMm = newPixelsPerMm;
            OnPropertyChanged(nameof(PixelsPerMm));
            OnPropertyChanged(nameof(SpecResultsValueHeader));
            IsDirty = true;
            RequestAutoSave();

            StatusBarText = $"🎯 Đã áp dụng hệ số Calib Job: {newPixelsPerMm:F4} px/mm từ công cụ [{toolName}]!";
            
            // Tự động tính toán lại toàn bộ đồ thị theo tỉ lệ Calib mới
            RunFlow();
        }
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

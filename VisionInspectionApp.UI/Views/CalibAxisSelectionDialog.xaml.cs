using System;
using System.Windows;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Views;

public partial class CalibAxisSelectionDialog : Window
{
    public CalibAxisTarget SelectedTarget { get; private set; } = CalibAxisTarget.AxisX;
    public double NewPixelsPerMm { get; }

    public CalibAxisSelectionDialog(
        string toolName,
        string toolType,
        double measuredPx,
        double nominalMm,
        double calculatedPpm,
        double angleDeg,
        double currentPpmX,
        double currentPpmY)
    {
        InitializeComponent();

        NewPixelsPerMm = Math.Round(calculatedPpm, 4);

        TxtToolName.Text = $"{toolName} ({toolType})";
        TxtMeasuredPx.Text = $"{measuredPx:F2} px";
        TxtNominalMm.Text = $"{nominalMm:F3} mm";
        TxtCalculatedPpm.Text = $"{NewPixelsPerMm:F4} px/mm";

        double diffX = currentPpmX > 0.0001 ? ((NewPixelsPerMm - currentPpmX) / currentPpmX) * 100.0 : 0.0;
        double diffY = currentPpmY > 0.0001 ? ((NewPixelsPerMm - currentPpmY) / currentPpmY) * 100.0 : 0.0;

        TxtDiffX.Text = $"Tỉ lệ X: {currentPpmX:F4} ➔ {NewPixelsPerMm:F4} px/mm ({(diffX >= 0 ? "+" : "")}{diffX:F2}%)";
        TxtDiffY.Text = $"Tỉ lệ Y: {currentPpmY:F4} ➔ {NewPixelsPerMm:F4} px/mm ({(diffY >= 0 ? "+" : "")}{diffY:F2}%)";

        // Tự động nhận diện hướng đo để gợi ý trục tương ứng
        if (double.IsNaN(angleDeg) || string.Equals(toolType, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(toolType, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            TxtOrientation.Text = "Đường tròn 2D (Đồng hướng)";
            RadioBothAxes.IsChecked = true;
        }
        else if (angleDeg < 45.0)
        {
            TxtOrientation.Text = $"{angleDeg:F1}° (Phương Ngang ➔ Trục X)";
            RadioAxisX.IsChecked = true;
            TxtRecX.Visibility = Visibility.Visible;
        }
        else
        {
            TxtOrientation.Text = $"{angleDeg:F1}° (Phương Dọc ➔ Trục Y)";
            RadioAxisY.IsChecked = true;
            TxtRecY.Visibility = Visibility.Visible;
        }

        // Cảnh báo nếu độ lệch lớn
        if (Math.Abs(diffX) > 10.0 || Math.Abs(diffY) > 10.0)
        {
            BorderWarning.Visibility = Visibility.Visible;
            TxtWarning.Text = "⚠️ CẢNH BÁO: Tỉ lệ mới chênh lệch trên 10% so với cấu hình hiện tại! Hãy chắc chắn bạn đang đặt phôi mẫu chuẩn (Golden Sample).";
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (RadioAxisX.IsChecked == true)
        {
            SelectedTarget = CalibAxisTarget.AxisX;
        }
        else if (RadioAxisY.IsChecked == true)
        {
            SelectedTarget = CalibAxisTarget.AxisY;
        }
        else
        {
            SelectedTarget = CalibAxisTarget.BothAxes;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

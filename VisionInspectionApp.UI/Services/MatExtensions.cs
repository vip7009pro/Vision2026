using System;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace VisionInspectionApp.UI.Services;

public static class MatExtensions
{
    /// <summary>
    /// Chuyển đổi an toàn từ OpenCvSharp Mat sang WPF BitmapSource.
    /// Bọc khối try-catch bẫy tất cả ngoại lệ ObjectDisposedException hoặc Mat invalid.
    /// Tự động gọi Freeze() để đảm bảo BitmapSource an toàn truyền qua các thread UI.
    /// </summary>
    public static BitmapSource? ToBitmapSourceSafe(this Mat? mat)
    {
        if (mat is null) return null;
        try
        {
            if (mat.IsDisposed || mat.Empty() || mat.Width <= 0 || mat.Height <= 0)
                return null;

            var bmp = mat.ToBitmapSource();
            if (bmp != null && bmp.CanFreeze)
            {
                bmp.Freeze();
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

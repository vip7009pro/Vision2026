using System;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace VisionInspectionApp.UI.Services;

public static class MatExtensions
{
    private sealed class DisplaySourceMetadata
    {
        public DisplaySourceMetadata(int sourceWidth, int sourceHeight)
        {
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
        }

        public int SourceWidth { get; }
        public int SourceHeight { get; }
    }

    // Metadata follows a BitmapSource without retaining it. ImageViewerControl uses it to
    // keep overlays and ROI coordinates in original-image pixels when a display proxy is used.
    private static readonly ConditionalWeakTable<BitmapSource, DisplaySourceMetadata> DisplaySourceMetadataByBitmap = new();

    public static void RegisterDisplaySourcePixelSize(this BitmapSource bitmap, int sourceWidth, int sourceHeight)
    {
        DisplaySourceMetadataByBitmap.Remove(bitmap);
        DisplaySourceMetadataByBitmap.Add(bitmap, new DisplaySourceMetadata(sourceWidth, sourceHeight));
    }

    public static bool TryGetSourcePixelSize(this BitmapSource bitmap, out int sourceWidth, out int sourceHeight)
    {
        if (DisplaySourceMetadataByBitmap.TryGetValue(bitmap, out var metadata))
        {
            sourceWidth = metadata.SourceWidth;
            sourceHeight = metadata.SourceHeight;
            return true;
        }

        sourceWidth = bitmap.PixelWidth;
        sourceHeight = bitmap.PixelHeight;
        return false;
    }

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

    /// <summary>
    /// Chuyển đổi an toàn từ OpenCvSharp Mat sang WPF BitmapSource phục vụ hiển thị Live Stream Preview.
    /// Tự động scale down tối ưu nếu ảnh quá lớn (> maxDisplayWidth x maxDisplayHeight),
    /// giúp triệt tiêu áp lực LOH allocation (từ 60MB xuống 2-3MB) và tăng FPS hiển thị mượt mà 60 FPS.
    /// </summary>
    public static BitmapSource? ToBitmapSourceForDisplay(this Mat? mat, int maxDisplayWidth = 1280, int maxDisplayHeight = 720)
    {
        if (mat is null) return null;
        try
        {
            if (mat.IsDisposed || mat.Empty() || mat.Width <= 0 || mat.Height <= 0)
                return null;

            if (maxDisplayWidth > 0 && maxDisplayHeight > 0 && (mat.Width > maxDisplayWidth || mat.Height > maxDisplayHeight))
            {
                double scale = Math.Min((double)maxDisplayWidth / mat.Width, (double)maxDisplayHeight / mat.Height);
                int targetW = Math.Max(1, (int)Math.Round(mat.Width * scale));
                int targetH = Math.Max(1, (int)Math.Round(mat.Height * scale));

                using var resized = new Mat();
                Cv2.Resize(mat, resized, new OpenCvSharp.Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);
                var bitmap = resized.ToBitmapSourceSafe();
                if (bitmap is not null)
                {
                    DisplaySourceMetadataByBitmap.Add(bitmap, new DisplaySourceMetadata(mat.Width, mat.Height));
                }
                return bitmap;
            }

            return mat.ToBitmapSourceSafe();
        }
        catch
        {
            return null;
        }
    }
}

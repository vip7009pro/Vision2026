using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace VisionInspectionApp.UI.Services;

/// <summary>
/// Trình kết xuất hình ảnh thời gian thực hiệu năng cao cho WPF.
/// Sử dụng một đối tượng WriteableBitmap duy nhất được tái sử dụng qua các frame (Zero-Allocation UI Rendering).
/// Triệt tiêu hoàn toàn việc cấp phát mảng byte và BitmapSource mới mỗi giây trên GC Heap/LOH.
/// </summary>
public sealed class WriteableBitmapRenderer : IDisposable
{
    private WriteableBitmap? _bitmap;
    private readonly object _lock = new();
    private Mat? _resizeBuffer;
    private int _currentWidth;
    private int _currentHeight;
    private bool _isDisposed;

    public WriteableBitmap? Bitmap
    {
        get
        {
            lock (_lock)
            {
                return _bitmap;
            }
        }
    }

    public int Width => _currentWidth;
    public int Height => _currentHeight;

    /// <summary>
    /// Cập nhật dữ liệu từ OpenCvSharp Mat vào WriteableBitmap BackBuffer một cách trực tiếp.
    /// Phương thức này thread-safe và tự động điều chỉnh độ phân giải proxy nếu cần (ví dụ tối đa 1280x720 hoặc 1920x1080).
    /// </summary>
    /// <param name="frame">Khung hình nguồn từ Camera hoặc Vision Pipeline</param>
    /// <param name="maxDisplayWidth">Chiều rộng tối đa cho màn hình hiển thị</param>
    /// <param name="maxDisplayHeight">Chiều cao tối đa cho màn hình hiển thị</param>
    /// <param name="forceOriginalQuality">Bắt buộc giữ độ phân giải gốc nếu true, hoặc tuân theo MatExtensions.UseOriginalQualityPreview</param>
    /// <returns>Đối tượng WriteableBitmap hiện hành (hoặc vừa được cập nhật/khởi tạo)</returns>
    public WriteableBitmap? UpdateFromMat(Mat? frame, int maxDisplayWidth = 1280, int maxDisplayHeight = 720, bool? forceOriginalQuality = null)
    {
        if (_isDisposed || frame == null || frame.IsDisposed || frame.Empty() || frame.Width <= 0 || frame.Height <= 0)
        {
            return _bitmap;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            try
            {
                return dispatcher.Invoke(() => UpdateFromMat(frame, maxDisplayWidth, maxDisplayHeight, forceOriginalQuality));
            }
            catch
            {
                return _bitmap;
            }
        }

        try
        {
            int srcW = frame.Width;
            int srcH = frame.Height;

            int targetW = srcW;
            int targetH = srcH;

            bool isOriginal = forceOriginalQuality ?? MatExtensions.UseOriginalQualityPreview;
            bool needDownscale = !isOriginal && (maxDisplayWidth > 0 && maxDisplayHeight > 0 && (srcW > maxDisplayWidth || srcH > maxDisplayHeight));
            if (needDownscale)
            {
                double scale = Math.Min((double)maxDisplayWidth / srcW, (double)maxDisplayHeight / srcH);
                targetW = Math.Max(1, (int)Math.Round(srcW * scale));
                targetH = Math.Max(1, (int)Math.Round(srcH * scale));
            }

            // Đảm bảo targetW là bội số của 4 để căn lề stride 32-bit tối ưu cho GPU
            if ((targetW % 2) != 0) targetW--;

            Mat matToDraw = frame;
            Mat? convertedMat = null;

            if (needDownscale)
            {
                lock (_lock)
                {
                    if (_resizeBuffer == null || _resizeBuffer.IsDisposed || _resizeBuffer.Width != targetW || _resizeBuffer.Height != targetH || _resizeBuffer.Type() != frame.Type())
                    {
                        _resizeBuffer?.Dispose();
                        _resizeBuffer = new Mat(targetH, targetW, frame.Type());
                    }

                    Cv2.Resize(frame, _resizeBuffer, new OpenCvSharp.Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);
                    matToDraw = _resizeBuffer;
                }
            }

            // Chuẩn hóa định dạng màu (BGR24 hoặc Gray8)
            PixelFormat pixelFormat = PixelFormats.Bgr24;
            int channels = matToDraw.Channels();
            if (channels == 1)
            {
                pixelFormat = PixelFormats.Gray8;
            }
            else if (channels == 3)
            {
                pixelFormat = PixelFormats.Bgr24;
            }
            else if (channels == 4)
            {
                pixelFormat = PixelFormats.Bgra32;
            }
            else
            {
                // Fallback BGR
                convertedMat = new Mat();
                Cv2.CvtColor(matToDraw, convertedMat, ColorConversionCodes.GRAY2BGR);
                matToDraw = convertedMat;
                pixelFormat = PixelFormats.Bgr24;
            }

            lock (_lock)
            {
                if (_isDisposed) return null;

                // Kiểm tra xem WriteableBitmap hiện tại có cần tái tạo vì thay đổi kích thước/format không
                if (_bitmap == null || _bitmap.PixelWidth != targetW || _bitmap.PixelHeight != targetH || _bitmap.Format != pixelFormat)
                {
                    _bitmap = new WriteableBitmap(targetW, targetH, 96, 96, pixelFormat, null);
                    _bitmap.RegisterDisplaySourcePixelSize(srcW, srcH);
                    _currentWidth = targetW;
                    _currentHeight = targetH;
                }
                else
                {
                    _bitmap.RegisterDisplaySourcePixelSize(srcW, srcH);
                }

                _bitmap.Lock();
                try
                {
                    IntPtr pBackBuffer = _bitmap.BackBuffer;
                    int bmpStride = _bitmap.BackBufferStride;
                    int matStep = (int)matToDraw.Step();
                    int rowBytes = targetW * matToDraw.ElemSize();

                    IntPtr pSrc = matToDraw.Data;

                    if (bmpStride == matStep)
                    {
                        // Bộ nhớ liên tục, copy toàn bộ 1 lần
                        unsafe
                        {
                            Buffer.MemoryCopy((void*)pSrc, (void*)pBackBuffer, (long)bmpStride * targetH, (long)matStep * targetH);
                        }
                    }
                    else
                    {
                        // Copy từng dòng nếu stride khác nhau
                        unsafe
                        {
                            byte* pSrcRow = (byte*)pSrc;
                            byte* pDstRow = (byte*)pBackBuffer;

                            for (int y = 0; y < targetH; y++)
                            {
                                Buffer.MemoryCopy(pSrcRow, pDstRow, bmpStride, rowBytes);
                                pSrcRow += matStep;
                                pDstRow += bmpStride;
                            }
                        }
                    }

                    _bitmap.AddDirtyRect(new Int32Rect(0, 0, targetW, targetH));
                }
                finally
                {
                    _bitmap.Unlock();
                    convertedMat?.Dispose();
                }

                return _bitmap;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WriteableBitmapRenderer] Update error: {ex.Message}");
            return _bitmap;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _resizeBuffer?.Dispose();
            _resizeBuffer = null;
            _bitmap = null;
        }
    }
}

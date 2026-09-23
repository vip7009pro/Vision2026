using System;
using OpenCvSharp;

namespace VisionInspectionApp.UI.Services;

public sealed class SharedImageContext
{
    private Mat? _image;

    // ==================== DIAGNOSTICS / PERFORMANCE TESTS ====================
    private static long _snapshotCloneCount;

    /// <summary>
    /// Tổng số lần clone snapshot (mỗi lần clone tốn ~60MB cho ảnh 20MP).
    /// Dùng cho chẩn đoán hiệu năng và test tự động kiểm chứng "1 snapshot cho mỗi lượt refresh".
    /// </summary>
    public static long SnapshotCloneCount => System.Threading.Interlocked.Read(ref _snapshotCloneCount);

    public static void ResetSnapshotCloneCount() => System.Threading.Interlocked.Exchange(ref _snapshotCloneCount, 0);

    public event EventHandler? ImageChanged;

    /// <summary>
    /// Gán ảnh dùng chung cho toàn bộ Preview.
    /// </summary>
    /// <param name="image">Ảnh nguồn.</param>
    /// <param name="transferOwnership">
    /// true = context nhận trách nhiệm dispose <paramref name="image"/> (KHÔNG clone lại) — dùng khi
    /// caller vừa tạo một Mat mới chỉ để đưa vào đây (tiết kiệm 1 lần clone ảnh lớn mỗi frame).
    /// false = context tự clone.
    /// </param>
    public void SetImage(Mat? image, bool transferOwnership = false)
    {
        lock (this)
        {
            try
            {
                _image?.Dispose();
            }
            catch { }

            try
            {
                if (image is null || image.IsDisposed || image.Empty())
                {
                    _image = null;

                    // Nếu caller đã chuyển quyền sở hữu mà ảnh không dùng được thì phải tự dispose
                    // để không rò rỉ bộ nhớ unmanaged (trước đây nhánh này bỏ quên).
                    if (transferOwnership && image is not null && !image.IsDisposed)
                    {
                        image.Dispose();
                    }
                }
                else
                {
                    _image = transferOwnership ? image : image.Clone();
                }
            }
            catch
            {
                _image = null;
            }
        }

        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public Mat? GetSnapshot()
    {
        lock (this)
        {
            if (_image is null || _image.IsDisposed || _image.Empty())
                return null;

            try
            {
                System.Threading.Interlocked.Increment(ref _snapshotCloneCount);
                return _image.Clone();
            }
            catch
            {
                return null;
            }
        }
    }
}

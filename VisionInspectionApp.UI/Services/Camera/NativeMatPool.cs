using OpenCvSharp;
using System;
using System.Threading;

namespace VisionInspectionApp.UI.Services.Camera;

/// <summary>
/// Quản lý bộ nhớ đệm Ring Buffer cố định cho các Frame ảnh độ phân giải cao (12MP - 20MP),
/// loại bỏ hoàn toàn việc cấp phát 'new Mat()' và 'Clone()' lặp đi lặp lại trong chu kỳ chụp (Zero-Allocation Buffer).
/// </summary>
public sealed class NativeMatPool : IDisposable
{
    private readonly Mat[] _buffers;
    private readonly bool[] _inUse;
    private readonly object _lock = new();
    private readonly int _poolSize;
    private int _width;
    private int _height;
    private MatType _type;
    private bool _isInitialized;
    private bool _disposed;

    public int PoolSize => _poolSize;
    public int Width => _width;
    public int Height => _height;
    public bool IsInitialized => _isInitialized;

    public NativeMatPool(int poolSize = 8)
    {
        _poolSize = Math.Clamp(poolSize, 4, 32);
        _buffers = new Mat[_poolSize];
        _inUse = new bool[_poolSize];
    }

    /// <summary>
    /// Khởi tạo trước mảng Mat cố định khi biết kích thước ảnh từ Camera
    /// </summary>
    public void Initialize(int width, int height, MatType type)
    {
        lock (_lock)
        {
            if (_isInitialized && _width == width && _height == height && _type == type)
                return;

            CleanupBuffers();

            _width = width;
            _height = height;
            _type = type;

            for (int i = 0; i < _poolSize; i++)
            {
                _buffers[i] = new Mat(height, width, type, Scalar.All(0));
                _inUse[i] = false;
            }

            _isInitialized = true;
        }
    }

    /// <summary>
    /// Thuê một Buffer Mat từ Pool để sao chép dữ liệu ảnh mới trực tiếp vào bộ nhớ có sẵn
    /// </summary>
    public (int BufferIndex, Mat? Mat) Rent()
    {
        lock (_lock)
        {
            if (!_isInitialized || _disposed) return (-1, null);

            for (int i = 0; i < _poolSize; i++)
            {
                if (!_inUse[i])
                {
                    _inUse[i] = true;
                    return (i, _buffers[i]);
                }
            }

            // Nếu tất cả buffer đang bận, buộc lấy buffer cũ nhất (Index 0) để tái sử dụng
            _inUse[0] = true;
            return (0, _buffers[0]);
        }
    }

    /// <summary>
    /// Trả Buffer Mat về Pool sau khi Consumer đã hoàn thành xử lý
    /// </summary>
    public void Return(int bufferIndex)
    {
        if (bufferIndex < 0 || bufferIndex >= _poolSize) return;
        lock (_lock)
        {
            _inUse[bufferIndex] = false;
        }
    }

    private void CleanupBuffers()
    {
        for (int i = 0; i < _poolSize; i++)
        {
            if (_buffers[i] != null && !_buffers[i].IsDisposed)
            {
                try { _buffers[i].Dispose(); } catch { }
                _buffers[i] = null!;
            }
            _inUse[i] = false;
        }
        _isInitialized = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            CleanupBuffers();
        }
    }
}

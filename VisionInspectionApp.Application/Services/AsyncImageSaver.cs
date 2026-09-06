using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenCvSharp;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Quản lý hàng đợi ghi file ảnh ra đĩa bất đồng bộ (Non-blocking Background Image Saver).
/// Giải phóng luồng Inspection chính khỏi độ trễ nén ảnh (PNG/JPG encode) và I/O ổ đĩa (300-500ms).
/// </summary>
public sealed class AsyncImageSaver : IDisposable, IAsyncDisposable
{
    private static readonly Lazy<AsyncImageSaver> _instance = new(() => new AsyncImageSaver());
    public static AsyncImageSaver Instance => _instance.Value;

    public sealed class ImageSaveRequest : IDisposable
    {
        public Mat Image { get; set; }
        public required string FullPath { get; init; }
        public required string OutputName { get; init; }
        public DateTime EnqueuedTime { get; init; } = DateTime.UtcNow;
        public Func<Mat, Mat>? PreProcessBeforeSave { get; init; }

        public void Dispose()
        {
            try
            {
                if (Image is not null && !Image.IsDisposed)
                {
                    Image.Dispose();
                }
            }
            catch
            {
                // Ignored during cleanup
            }
        }
    }

    private readonly Channel<ImageSaveRequest> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workerTasks;
    private bool _disposed;
    private readonly int _capacity;
    private long _droppedCount;
    private int _activeWritingCount;

    // Giới hạn hàng đợi tối đa 30 ảnh để tránh quá tải bộ nhớ RAM nếu camera chụp nhanh hơn tốc độ ghi đĩa
    public const int DefaultCapacity = 30;

    public int PendingCount => _channel.Reader.Count;
    public int ActiveWritingCount => Volatile.Read(ref _activeWritingCount);
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public AsyncImageSaver(int capacity = DefaultCapacity, int workerCount = 2)
    {
        _capacity = Math.Clamp(capacity, 5, 50);
        var options = new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite, // Không tự động drop ngầm mà kiểm soát drop tường minh để Dispose() Native Mat
            SingleWriter = false,
            SingleReader = false
        };

        _channel = Channel.CreateBounded<ImageSaveRequest>(options);

        int workers = Math.Clamp(workerCount, 1, 4);
        _workerTasks = new Task[workers];
        for (int i = 0; i < workers; i++)
        {
            _workerTasks[i] = Task.Factory.StartNew(
                () => ProcessQueueLoopAsync(_cts.Token),
                TaskCreationOptions.LongRunning).Unwrap();
        }
    }

    /// <summary>
    /// Đẩy yêu cầu lưu ảnh vào hàng đợi bất đồng bộ (Non-blocking, mất < 0.01ms).
    /// Quyền sở hữu Mat được chuyển giao cho AsyncImageSaver, caller KHÔNG dispose Mat này.
    /// Cho phép truyền delegate preProcessBeforeSave (ví dụ: vẽ Overlay, đổi hệ màu) để chạy hoàn toàn trên background worker.
    /// Nếu hàng đợi đầy, request cũ nhất sẽ được giải phóng Native Mat an toàn (No Memory Leak).
    /// </summary>
    public bool Enqueue(Mat imageToSave, string fullPath, string outputName, Func<Mat, Mat>? preProcessBeforeSave = null)
    {
        if (_disposed || imageToSave is null || imageToSave.Empty() || string.IsNullOrWhiteSpace(fullPath))
        {
            imageToSave?.Dispose();
            return false;
        }

        var request = new ImageSaveRequest
        {
            Image = imageToSave,
            FullPath = fullPath,
            OutputName = outputName,
            PreProcessBeforeSave = preProcessBeforeSave
        };

        // Nếu hàng đợi đầy, chủ động lấy request cũ nhất ra và gọi Dispose() trước khi đẩy request mới vào
        while (_channel.Reader.Count >= _capacity)
        {
            if (_channel.Reader.TryRead(out var droppedReq))
            {
                droppedReq.Dispose();
                Interlocked.Increment(ref _droppedCount);
            }
            else
            {
                break;
            }
        }

        // Ghi vào Channel không khóa luồng
        if (!_channel.Writer.TryWrite(request))
        {
            // Nếu không ghi được (ví dụ channel đã đóng hoặc đầy)
            request.Dispose();
            Interlocked.Increment(ref _droppedCount);
            return false;
        }

        return true;
    }

    private async Task ProcessQueueLoopAsync(CancellationToken ct)
    {
        var reader = _channel.Reader;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var req))
                    {
                        Interlocked.Increment(ref _activeWritingCount);
                        try
                        {
                            using (req)
                            {
                                try
                                {
                                    if (req.PreProcessBeforeSave is not null)
                                    {
                                        var processed = req.PreProcessBeforeSave(req.Image);
                                        if (!ReferenceEquals(processed, req.Image))
                                        {
                                            req.Image.Dispose();
                                            req.Image = processed;
                                        }
                                    }

                                    var dir = Path.GetDirectoryName(req.FullPath);
                                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                    {
                                        Directory.CreateDirectory(dir);
                                    }

                                    Cv2.ImWrite(req.FullPath, req.Image);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[AsyncImageSaver] Failed to write image '{req.FullPath}': {ex.Message}");
                                }
                            }
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _activeWritingCount);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AsyncImageSaver] Worker exception: {ex.Message}");
            }
        }

        // Xử lý nốt các ảnh còn lại trong queue khi shutdown
        while (reader.TryRead(out var remainingReq))
        {
            Interlocked.Increment(ref _activeWritingCount);
            try
            {
                using (remainingReq)
                {
                    try
                    {
                        if (remainingReq.PreProcessBeforeSave is not null)
                        {
                            var processed = remainingReq.PreProcessBeforeSave(remainingReq.Image);
                            if (!ReferenceEquals(processed, remainingReq.Image))
                            {
                                remainingReq.Image.Dispose();
                                remainingReq.Image = processed;
                            }
                        }

                        var dir = Path.GetDirectoryName(remainingReq.FullPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        Cv2.ImWrite(remainingReq.FullPath, remainingReq.Image);
                    }
                    catch
                    {
                        // Ignored during shutdown
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeWritingCount);
            }
        }
    }

    public async Task FlushAsync(int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while ((PendingCount > 0 || Volatile.Read(ref _activeWritingCount) > 0) && !cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(20, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            Task.WaitAll(_workerTasks, TimeSpan.FromMilliseconds(2000));
        }
        catch
        {
            // Ignored
        }

        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            await Task.WhenAll(_workerTasks).ConfigureAwait(false);
        }
        catch
        {
            // Ignored
        }

        _cts.Dispose();
    }
}

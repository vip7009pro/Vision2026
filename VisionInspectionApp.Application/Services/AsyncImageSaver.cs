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
        public required Mat Image { get; init; }
        public required string FullPath { get; init; }
        public required string OutputName { get; init; }
        public DateTime EnqueuedTime { get; init; } = DateTime.UtcNow;

        public void Dispose()
        {
            try
            {
                if (!Image.IsDisposed)
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

    // Giới hạn hàng đợi tối đa 100 ảnh để tránh quá tải bộ nhớ RAM nếu camera chụp nhanh hơn tốc độ ghi đĩa
    public const int DefaultCapacity = 100;

    public int PendingCount => _channel.Reader.Count;

    public AsyncImageSaver(int capacity = DefaultCapacity, int workerCount = 2)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // Nếu ổ đĩa quá chậm, tự drop ảnh cũ nhất để bảo vệ bộ nhớ RAM
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
    /// </summary>
    public bool Enqueue(Mat imageToSave, string fullPath, string outputName)
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
            OutputName = outputName
        };

        // Ghi vào Channel không khóa luồng
        if (!_channel.Writer.TryWrite(request))
        {
            // Nếu không ghi được (ví dụ channel đã đóng)
            request.Dispose();
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
                        using (req)
                        {
                            try
                            {
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
            using (remainingReq)
            {
                try
                {
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
    }

    public async Task FlushAsync(int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (PendingCount > 0 && !cts.IsCancellationRequested)
        {
            await Task.Delay(50, cts.Token).ConfigureAwait(false);
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

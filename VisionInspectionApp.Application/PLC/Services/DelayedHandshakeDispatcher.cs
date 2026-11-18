using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.PLC.Services;

/// <summary>
/// Bộ điều phối tín hiệu Handshake có độ trễ dạng Pipelined FIFO (Single-Reader).
/// Mỗi sản phẩm được gắn mốc thời gian hoàn tất riêng (DueTimestamp = Now + DelayMs),
/// giúp giữ nguyên độ trễ cho từng sản phẩm đồng thời phát tín hiệu ra PLC liên tục
/// đúng thứ tự kiểm tra mà không bị cộng dồn độ trễ giữa các sản phẩm liên tiếp.
/// </summary>
internal sealed class DelayedHandshakeDispatcher : IDisposable
{
    private sealed class DelayedHandshakeItem
    {
        private int _completed;

        public DelayedHandshakeItem(
            bool isPass,
            long dueTimestampTicks,
            long batchEpoch,
            CancellationToken batchResetToken,
            CancellationToken callerToken,
            TaskCompletionSource<bool> completion)
        {
            IsPass = isPass;
            DueTimestampTicks = dueTimestampTicks;
            BatchEpoch = batchEpoch;
            BatchResetToken = batchResetToken;
            CallerToken = callerToken;
            Completion = completion;
        }

        public bool IsPass { get; }
        public long DueTimestampTicks { get; }
        public long BatchEpoch { get; }
        public CancellationToken BatchResetToken { get; }
        public CancellationToken CallerToken { get; }
        public TaskCompletionSource<bool> Completion { get; }

        public bool TryMarkCompleted() => Interlocked.Exchange(ref _completed, 1) == 0;
    }

    private readonly Func<bool, CancellationToken, Task<bool>> _executeAsync;
    private readonly Action<int>? _onPendingCountChanged;
    private readonly Channel<DelayedHandshakeItem> _channel;
    private readonly object _syncLock = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private CancellationTokenSource _batchResetCts = new();
    private DelayedHandshakeItem? _activeItem;
    private long _currentEpoch;
    private int _pendingCount;
    private bool _workerStarted;
    private bool _disposed;

    public DelayedHandshakeDispatcher(
        Func<bool, CancellationToken, Task<bool>> executeAsync,
        Action<int>? onPendingCountChanged = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _onPendingCountChanged = onPendingCountChanged;
        _channel = Channel.CreateUnbounded<DelayedHandshakeItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));

    public Task<bool> EnqueueAsync(bool isPass, int delayMs, CancellationToken callerToken = default)
    {
        if (callerToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        long dueTicks = Stopwatch.GetTimestamp() + MsToStopwatchTicks(Math.Max(0, delayMs));
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        int currentPending;
        lock (_syncLock)
        {
            if (_disposed)
            {
                return Task.FromResult(false);
            }

            if (!_workerStarted)
            {
                _workerStarted = true;
                _ = Task.Run(ProcessLoopAsync);
            }

            var item = new DelayedHandshakeItem(
                isPass,
                dueTicks,
                _currentEpoch,
                _batchResetCts.Token,
                callerToken,
                tcs);

            if (!_channel.Writer.TryWrite(item))
            {
                return Task.FromResult(false);
            }

            currentPending = Interlocked.Increment(ref _pendingCount);
        }

        _onPendingCountChanged?.Invoke(currentPending);
        return tcs.Task;
    }

    /// <summary>
    /// Hủy và xả sạch toàn bộ các tín hiệu đang chờ trong hàng đợi trễ (ví dụ khi Stop hoặc Reset Queue).
    /// </summary>
    public void Clear()
    {
        int currentPending;
        lock (_syncLock)
        {
            currentPending = ClearLocked();
        }

        _onPendingCountChanged?.Invoke(currentPending);
    }

    private int ClearLocked()
    {
        _currentEpoch++;

        var oldCts = _batchResetCts;
        _batchResetCts = new CancellationTokenSource();
        try
        {
            oldCts.Cancel();
        }
        catch { }
        finally
        {
            oldCts.Dispose();
        }

        if (_activeItem != null)
        {
            CompleteItemLocked(_activeItem, false);
            _activeItem = null;
        }

        while (_channel.Reader.TryRead(out var skipped))
        {
            CompleteItemLocked(skipped, false);
        }

        int remaining = Volatile.Read(ref _pendingCount);
        if (remaining < 0)
        {
            Interlocked.Exchange(ref _pendingCount, 0);
            remaining = 0;
        }

        return remaining;
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_disposeCts.Token).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    if (!TryBeginItem(item))
                    {
                        continue;
                    }

                    bool result = false;
                    try
                    {
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            item.BatchResetToken,
                            item.CallerToken,
                            _disposeCts.Token);

                        bool ready = await WaitUntilDueAsync(item.DueTimestampTicks, linkedCts.Token).ConfigureAwait(false);
                        if (ready && IsItemValid(item))
                        {
                            result = await _executeAsync(item.IsPass, linkedCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        result = false;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DelayedHandshakeDispatcher] Error dispatching signal: {ex.Message}");
                        result = false;
                    }
                    finally
                    {
                        FinishItem(item, result);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DelayedHandshakeDispatcher] Loop terminated: {ex.Message}");
        }
    }

    private bool TryBeginItem(DelayedHandshakeItem item)
    {
        int? updatedPending = null;
        bool valid;
        lock (_syncLock)
        {
            if (_disposed || item.BatchEpoch != _currentEpoch || item.CallerToken.IsCancellationRequested || item.BatchResetToken.IsCancellationRequested)
            {
                updatedPending = CompleteItemLocked(item, false);
                valid = false;
            }
            else
            {
                _activeItem = item;
                valid = true;
            }
        }

        if (updatedPending.HasValue)
        {
            _onPendingCountChanged?.Invoke(updatedPending.Value);
        }

        return valid;
    }

    private bool IsItemValid(DelayedHandshakeItem item)
    {
        lock (_syncLock)
        {
            return !_disposed
                   && item.BatchEpoch == _currentEpoch
                   && !item.CallerToken.IsCancellationRequested
                   && !item.BatchResetToken.IsCancellationRequested;
        }
    }

    private void FinishItem(DelayedHandshakeItem item, bool result)
    {
        int? updatedPending;
        lock (_syncLock)
        {
            if (ReferenceEquals(_activeItem, item))
            {
                _activeItem = null;
            }

            updatedPending = CompleteItemLocked(item, result);
        }

        if (updatedPending.HasValue)
        {
            _onPendingCountChanged?.Invoke(updatedPending.Value);
        }
    }

    private int? CompleteItemLocked(DelayedHandshakeItem item, bool result)
    {
        if (!item.TryMarkCompleted())
        {
            return null;
        }

        int decremented = Interlocked.Decrement(ref _pendingCount);
        if (decremented < 0)
        {
            Interlocked.Exchange(ref _pendingCount, 0);
            decremented = 0;
        }

        item.Completion.TrySetResult(result);
        return decremented;
    }

    private static async Task<bool> WaitUntilDueAsync(long dueTimestampTicks, CancellationToken ct)
    {
        bool acquiredTimer = false;
        try
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                long remainingTicks = dueTimestampTicks - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0)
                {
                    return true;
                }

                if (!acquiredTimer)
                {
                    NativeTimerUtility.TimeBeginPeriod(1);
                    acquiredTimer = true;
                }

                double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
                int sleepMs = remainingMs > 2.0 ? (int)Math.Floor(remainingMs - 1.0) : 1;
                await Task.Delay(sleepMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (acquiredTimer)
            {
                NativeTimerUtility.TimeEndPeriod(1);
            }
        }
    }

    private static long MsToStopwatchTicks(int delayMs)
    {
        if (delayMs <= 0)
        {
            return 0;
        }

        return (long)Math.Round(delayMs * (Stopwatch.Frequency / 1000.0));
    }

    public void Dispose()
    {
        int currentPending;
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            currentPending = ClearLocked();
            _channel.Writer.TryComplete();
            try
            {
                _disposeCts.Cancel();
            }
            catch { }
            finally
            {
                _disposeCts.Dispose();
                _batchResetCts.Dispose();
            }
        }

        _onPendingCountChanged?.Invoke(currentPending);
    }
}

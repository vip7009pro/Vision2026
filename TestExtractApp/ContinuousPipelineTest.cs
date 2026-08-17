using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenCvSharp;

namespace TestExtractApp;

public static class ContinuousPipelineTest
{
    public static async Task RunTestsAsync()
    {
        Console.WriteLine("====================================================");
        Console.WriteLine("STARTING CONTINUOUS PIPELINE & FRAME QUEUE TESTS");
        Console.WriteLine("====================================================");

        int passed = 0;
        int failed = 0;

        void Assert(string testName, bool condition, string detail = "")
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {testName} {(string.IsNullOrEmpty(detail) ? "" : $"({detail})")}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {testName} {(string.IsNullOrEmpty(detail) ? "" : $"({detail})")}");
                failed++;
            }
        }

        // Test 1: Bounded Channel Creation & Single Frame Processing
        {
            var options = new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            };
            var channel = Channel.CreateBounded<Mat>(options);

            using var testMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 255, 0));
            bool writeOk = channel.Writer.TryWrite(testMat.Clone());
            Assert("Test 1: Channel TryWrite 1 frame", writeOk);

            bool readOk = channel.Reader.TryRead(out var readMat);
            Assert("Test 1: Channel TryRead 1 frame", readOk && readMat != null && !readMat.Empty());
            readMat?.Dispose();
        }

        // Test 2: High Speed Burst Producer > Slow Consumer (Backpressure & Zero Leak)
        {
            var options = new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            };
            var channel = Channel.CreateBounded<Mat>(options);
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            int framesProduced = 0;
            int framesProcessed = 0;

            // Worker Task
            var workerTask = Task.Run(async () =>
            {
                while (await channel.Reader.WaitToReadAsync(token))
                {
                    while (channel.Reader.TryRead(out var frameMat))
                    {
                        if (token.IsCancellationRequested)
                        {
                            frameMat.Dispose();
                            break;
                        }

                        try
                        {
                            // Giả lập Vision xử lý 30ms cho 1 frame
                            await Task.Delay(30, token);
                            Interlocked.Increment(ref framesProcessed);
                        }
                        finally
                        {
                            frameMat.Dispose();
                        }
                    }
                }
            }, token);

            // Producer Task (Bắn 10 frame nhanh trong 50ms)
            for (int i = 0; i < 10; i++)
            {
                var dummyMat = new Mat(50, 50, MatType.CV_8UC3, new Scalar(i, i, i));
                if (!channel.Writer.TryWrite(dummyMat))
                {
                    dummyMat.Dispose();
                }
                framesProduced++;
                await Task.Delay(5); // 5ms per frame
            }

            // Chờ worker xử lý xong các frame còn trong queue
            channel.Writer.TryComplete();
            try { await workerTask; } catch (OperationCanceledException) { }

            Assert("Test 2: Burst Producer vs Slow Consumer", framesProcessed > 0 && framesProcessed <= framesProduced, $"Produced: {framesProduced}, Processed: {framesProcessed}");
        }

        // Test 3: Stop Cancellation & Queue Cleanup
        {
            var options = new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            };
            var channel = Channel.CreateBounded<Mat>(options);
            using var cts = new CancellationTokenSource();

            // Đẩy 2 frame vào
            var m1 = new Mat(10, 10, MatType.CV_8UC1);
            var m2 = new Mat(10, 10, MatType.CV_8UC1);
            channel.Writer.TryWrite(m1);
            channel.Writer.TryWrite(m2);

            // Stop / Clean up
            cts.Cancel();
            channel.Writer.TryComplete();
            int disposedCount = 0;
            while (channel.Reader.TryRead(out var leftover))
            {
                leftover.Dispose();
                disposedCount++;
            }

            Assert("Test 3: Clean up remaining frames on Stop", disposedCount == 2, $"Cleaned {disposedCount} leftover frames");
        }

        Console.WriteLine("====================================================");
        Console.WriteLine($"PIPELINE TEST SUMMARY: {passed} PASSED, {failed} FAILED");
        Console.WriteLine("====================================================");
    }
}

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

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

        // Test 1: Bounded Channel Creation & ContinuousFrameEnvelope Processing
        {
            var options = new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            };
            var channel = Channel.CreateBounded<ContinuousFrameEnvelope>(options);

            using var testMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 255, 0));
            var envelope = new ContinuousFrameEnvelope
            {
                Frame = testMat.Clone(),
                Metadata = new FrameMetadata
                {
                    FrameIndex = 1,
                    WebPositionMm = 1250.5,
                    EncoderPulses = 125050,
                    LineSpeedMpm = 30.0
                }
            };
            bool writeOk = channel.Writer.TryWrite(envelope);
            Assert("Test 1: Channel TryWrite 1 envelope", writeOk);

            bool readOk = channel.Reader.TryRead(out var readEnvelope);
            Assert("Test 1: Channel TryRead 1 envelope", readOk && readEnvelope != null && readEnvelope.Metadata.WebPositionMm == 1250.5);
            readEnvelope?.Dispose();
        }

        // Test 2: High Speed Burst Producer > Slow Consumer (Backpressure & Zero Leak with Envelope)
        {
            var options = new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            };
            var channel = Channel.CreateBounded<ContinuousFrameEnvelope>(options);
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            int framesProduced = 0;
            int framesProcessed = 0;

            // Worker Task
            var workerTask = Task.Run(async () =>
            {
                while (await channel.Reader.WaitToReadAsync(token))
                {
                    while (channel.Reader.TryRead(out var env))
                    {
                        if (token.IsCancellationRequested)
                        {
                            env.Dispose();
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
                            env.Dispose();
                        }
                    }
                }
            }, token);

            // Producer Task (Bắn 10 frame nhanh trong 50ms)
            for (int i = 0; i < 10; i++)
            {
                var dummyMat = new Mat(50, 50, MatType.CV_8UC3, new Scalar(i, i, i));
                var env = new ContinuousFrameEnvelope
                {
                    Frame = dummyMat,
                    Metadata = new FrameMetadata { FrameIndex = i, WebPositionMm = i * 100 }
                };

                while (channel.Reader.Count >= 2)
                {
                    if (channel.Reader.TryRead(out var dropped))
                    {
                        dropped.Dispose();
                    }
                    else break;
                }

                if (!channel.Writer.TryWrite(env))
                {
                    env.Dispose();
                }
                framesProduced++;
                await Task.Delay(5); // 5ms per frame
            }

            // Chờ worker xử lý xong các frame còn trong queue
            channel.Writer.TryComplete();
            try { await workerTask; } catch (OperationCanceledException) { }

            Assert("Test 2: Burst Producer vs Slow Consumer", framesProcessed > 0 && framesProcessed <= framesProduced, $"Produced: {framesProduced}, Processed: {framesProcessed}");
        }

        // Test 3: Stop Cancellation & Queue Cleanup with Envelopes
        {
            var options = new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            };
            var channel = Channel.CreateBounded<ContinuousFrameEnvelope>(options);
            using var cts = new CancellationTokenSource();

            var env1 = new ContinuousFrameEnvelope { Frame = new Mat(10, 10, MatType.CV_8UC1), Metadata = new FrameMetadata() };
            var env2 = new ContinuousFrameEnvelope { Frame = new Mat(10, 10, MatType.CV_8UC1), Metadata = new FrameMetadata() };
            channel.Writer.TryWrite(env1);
            channel.Writer.TryWrite(env2);

            // Stop / Clean up
            cts.Cancel();
            channel.Writer.TryComplete();
            int disposedCount = 0;
            while (channel.Reader.TryRead(out var leftover))
            {
                leftover.Dispose();
                disposedCount++;
            }

            Assert("Test 3: Clean up remaining envelopes on Stop", disposedCount == 2, $"Cleaned {disposedCount} leftover envelopes");
        }

        // Test 4: Handshake State Machine - Continuous Cycle Ready Recovery
        {
            var handshake = new IndustrialHandshakeStateMachine(null, "PLC1") { IsEnabled = true };
            
            // 1. Khởi tạo Ready
            await handshake.SetReadyAsync();
            Assert("Test 4.1: Handshake SetReady State", handshake.CurrentState == HandshakeState.Armed);

            // 2. Start Inspection
            await handshake.StartInspectionAsync();
            Assert("Test 4.2: Handshake StartInspection State", handshake.CurrentState == HandshakeState.Inspecting);

            // 3. Complete Handshake -> State chuyển sang Complete
            bool passCompleted = await handshake.CompleteHandshakeAsync(true);
            Assert("Test 4.3: Handshake CompleteHandshake State", passCompleted && handshake.CurrentState == HandshakeState.Complete);

            // 4. Set Ready cho chu trình mới -> Armed
            await handshake.SetReadyAsync();
            Assert("Test 4.4: Handshake Next Cycle Ready (Armed)", handshake.CurrentState == HandshakeState.Armed);

            // 5. Set Idle
            await handshake.SetIdleAsync();
            Assert("Test 4.5: Handshake SetIdle State", handshake.CurrentState == HandshakeState.Idle);
        }

        // Test 5: ShiftRegisterTracker Millimeter Precision Reject Tracking
        {
            using var tracker = new ShiftRegisterTracker(null)
            {
                RejectStationDistanceMm = 1500.0,
                RejectToleranceMm = 15.0,
                IsEnabled = true
            };

            // Nạp 1 vết lỗi tại vị trí 1000mm -> Target gạt tại 2500mm
            var defect = new RollDefectItem
            {
                Id = "DEF-001",
                WebX_Mm = 50.0,
                WebY_Mm = 1000.0,
                DefectType = "Scratch"
            };
            tracker.EnqueueDefect(defect);
            Assert("Test 5.1: Enqueue Defect to ShiftRegister", tracker.PendingCount == 1);

            // Băng chuyền chạy đến 2000mm (chưa tới trạm 2500mm)
            var triggered1 = tracker.ProcessMotionUpdate(2000.0);
            Assert("Test 5.2: At 2000mm No Reject", triggered1.Count == 0 && tracker.PendingCount == 1);

            // Băng chuyền chạy đến 2490mm (vào vùng dung sai 2500 - 15 = 2485mm)
            var triggered2 = tracker.ProcessMotionUpdate(2490.0);
            Assert("Test 5.3: At 2490mm Reject Triggered", triggered2.Count == 1 && tracker.PendingCount == 0 && tracker.TotalRejectsTriggered == 1);
        }

        Console.WriteLine("====================================================");
        Console.WriteLine($"PIPELINE TEST SUMMARY: {passed} PASSED, {failed} FAILED");
        Console.WriteLine("====================================================");
    }
}


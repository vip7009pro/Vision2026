using System;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Services.Camera;
using VisionInspectionApp.UI.Services.Camera.Drivers;

namespace TestExtractApp;

public static class OqcLiveViewOnJobLoadTests
{
    public static void RunTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🧪 RUNNING TESTS: OQC SCANNER LIVE VIEW & JOB LOAD TESTS");
        Console.WriteLine("=======================================================");

        TestCameraServiceLiveStreamRetentionOnApplyParameters().GetAwaiter().GetResult();
        TestCameraServiceNormalApplyParametersWhenNoLiveConsumer().GetAwaiter().GetResult();
        TestLiveStreamGrabbingAutoRestart().GetAwaiter().GetResult();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL OQC SCANNER LIVE VIEW TESTS PASSED!");
        Console.WriteLine("=======================================================\n");
    }

    private static async Task TestCameraServiceLiveStreamRetentionOnApplyParameters()
    {
        Console.WriteLine("--- Test 1: Đảm bảo CameraService duy trì Live Stream khi có consumer đang xem dù Job cấu hình TriggerMode=On ---");

        var cameraService = new CameraService();
        var simDevice = new CameraDeviceInfo
        {
            Vendor = CameraVendor.Simulator,
            InterfaceType = CameraInterfaceType.Virtual,
            Index = CameraService.SimulatorCameraIndex,
            ModelName = "Simulator Camera Test"
        };

        bool started = await cameraService.StartDriverCameraAsync(simDevice, new CameraParameters
        {
            Width = 640,
            Height = 480,
            TargetFps = 30
        });

        if (!started) throw new Exception("Không thể khởi động Simulator Camera Driver!");

        // 1. OQC Scanner đăng ký xem Live Stream
        bool reqLive = await cameraService.RequestLiveStreamAsync("OQCScanner", true);
        if (!reqLive) throw new Exception("RequestLiveStreamAsync thất bại!");

        if (cameraService.ActiveDriver == null || !cameraService.ActiveDriver.IsGrabbing)
        {
            throw new Exception("ActiveDriver phải đang ở trạng thái Grabbing khi có Live consumer!");
        }

        // 2. Mô phỏng nạp Job từ Quản lý Job: Job có cấu hình TriggerMode = On và IsLiveViewEnabled = false
        var jobCameraParams = new CameraParameters
        {
            ExposureTimeUs = 25000.0f,
            GainDb = 8.5f,
            Width = 1280,
            Height = 720,
            TriggerMode = CameraTriggerMode.On, // Job đặt TriggerMode = On
            IsLiveViewEnabled = false            // Job đặt IsLiveViewEnabled = false
        };

        await cameraService.ApplyParametersAsync(jobCameraParams);

        // 3. Kiểm tra:
        // - Các thông số quang học (Exposure, Gain, Size) từ Job phải được áp dụng chuẩn xác
        if (Math.Abs(cameraService.CurrentParameters.ExposureTimeUs - 25000.0f) > 0.01f)
        {
            throw new Exception($"ExposureTimeUs không khớp! Giá trị thực tế: {cameraService.CurrentParameters.ExposureTimeUs}");
        }
        if (Math.Abs(cameraService.CurrentParameters.GainDb - 8.5f) > 0.01f)
        {
            throw new Exception($"GainDb không khớp! Giá trị thực tế: {cameraService.CurrentParameters.GainDb}");
        }

        // - Nhưng TriggerMode và IsLiveViewEnabled PHẢI ĐƯỢC BẢO VỆ để Live Stream không bị đứt đoạn
        if (cameraService.CurrentParameters.TriggerMode != CameraTriggerMode.Off)
        {
            throw new Exception("TriggerMode phải được giữ ở trạng thái Off khi có active live consumer (OQCScanner)!");
        }
        if (!cameraService.CurrentParameters.IsLiveViewEnabled)
        {
            throw new Exception("IsLiveViewEnabled phải được giữ là true khi có active live consumer!");
        }
        if (cameraService.ActiveDriver == null || !cameraService.ActiveDriver.IsGrabbing)
        {
            throw new Exception("ActiveDriver phải tiếp tục Grabbing, không được dừng stream!");
        }

        Console.WriteLine("  -> PASSED: CameraService bảo vệ TriggerMode=Off và giữ Grabbing cho Live View khi nạp Job.");

        await cameraService.StopCameraAsync();
        cameraService.Dispose();
    }

    private static async Task TestCameraServiceNormalApplyParametersWhenNoLiveConsumer()
    {
        Console.WriteLine("--- Test 2: Đảm bảo CameraService áp dụng đúng TriggerMode=On khi KHÔNG có live consumer ---");

        var cameraService = new CameraService();
        var simDevice = new CameraDeviceInfo
        {
            Vendor = CameraVendor.Simulator,
            InterfaceType = CameraInterfaceType.Virtual,
            Index = CameraService.SimulatorCameraIndex,
            ModelName = "Simulator Camera Test 2"
        };

        await cameraService.StartDriverCameraAsync(simDevice);

        // Không có consumer nào (Count == 0)
        var triggerParams = new CameraParameters
        {
            ExposureTimeUs = 15000.0f,
            TriggerMode = CameraTriggerMode.On,
            IsLiveViewEnabled = false
        };

        await cameraService.ApplyParametersAsync(triggerParams);

        if (cameraService.CurrentParameters.TriggerMode != CameraTriggerMode.On)
        {
            throw new Exception("Khi không có Live consumer, TriggerMode phải được áp dụng đúng là On từ Job!");
        }

        Console.WriteLine("  -> PASSED: Áp dụng đầy đủ TriggerMode=On từ Job khi không có live consumer.");

        await cameraService.StopCameraAsync();
        cameraService.Dispose();
    }

    private static async Task TestLiveStreamGrabbingAutoRestart()
    {
        Console.WriteLine("--- Test 3: Đảm bảo StartGrabbingAsync tự động kích hoạt lại nếu driver bị dừng grabbing ---");

        var cameraService = new CameraService();
        var simDevice = new CameraDeviceInfo
        {
            Vendor = CameraVendor.Simulator,
            InterfaceType = CameraInterfaceType.Virtual,
            Index = CameraService.SimulatorCameraIndex,
            ModelName = "Simulator Camera Test 3"
        };

        await cameraService.StartDriverCameraAsync(simDevice);
        await cameraService.RequestLiveStreamAsync("OQCScanner", true);

        // Giả lập driver bị dừng grabbing (ví dụ sau một thao tác chụp snapshot độc lập)
        if (cameraService.ActiveDriver != null)
        {
            await cameraService.ActiveDriver.StopGrabbingAsync();
        }

        if (cameraService.ActiveDriver != null && cameraService.ActiveDriver.IsGrabbing)
        {
            throw new Exception("Lỗi thiết lập test: driver phải dừng grabbing!");
        }

        // Gọi ApplyParametersAsync khi mở Job mới
        await cameraService.ApplyParametersAsync(new CameraParameters
        {
            ExposureTimeUs = 30000.0f
        });

        // Driver phải tự động được bật lại Grabbing ngay lập tức
        if (cameraService.ActiveDriver == null || !cameraService.ActiveDriver.IsGrabbing)
        {
            throw new Exception("ActiveDriver phải tự động được StartGrabbingAsync lại khi có live consumer!");
        }

        Console.WriteLine("  -> PASSED: Tự động khởi động lại Grabbing mượt mà khi nạp Job.");

        await cameraService.StopCameraAsync();
        cameraService.Dispose();
    }
}

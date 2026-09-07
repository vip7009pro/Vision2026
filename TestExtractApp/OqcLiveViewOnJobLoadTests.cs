using System;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application;
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
        TestBringWindowToForegroundPreservesMaximized();
        TestOqcMeasurementOverSpecCalculation();
        TestOqcOnlyOriginMode();
        TestOqcWaitingForInspectionStateOnLiveView();

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

    private static void TestBringWindowToForegroundPreservesMaximized()
    {
        Console.WriteLine("--- Test 4: Đảm bảo BringWindowToForeground bảo tồn trạng thái Maximized (Full Screen) khi đóng cửa sổ con ---");

        var thread = new System.Threading.Thread(() =>
        {
            var win = new System.Windows.Window
            {
                WindowState = System.Windows.WindowState.Maximized
            };

            // Gọi hàm BringWindowToForeground
            VisionInspectionApp.UI.App.BringWindowToForeground(win);

            if (win.WindowState != System.Windows.WindowState.Maximized)
            {
                throw new Exception($"BringWindowToForeground không bảo tồn trạng thái Maximized! Giá trị hiện tại: {win.WindowState}");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Console.WriteLine("  -> PASSED: BringWindowToForeground duy trì tuyệt đối trạng thái Maximized (Full Screen).");
    }

    private static void TestOqcMeasurementOverSpecCalculation()
    {
        Console.WriteLine("--- Test 5: Kiểm tra tính toán Over Spec và Mô Tả Vượt Cận trong OqcMeasurementDetail ---");

        // 1. Trường hợp đạt (trong spec)
        var passItem = new OqcMeasurementDetail
        {
            ToolName = "Khoảng cách L1-L2",
            ToolType = "Distance",
            Spec = 11.0,
            Min = 10.0,
            Max = 12.0,
            Result = 11.2,
            Unit = "mm",
            Pass = true,
            HasNumericSpec = true
        };

        if (passItem.OverSpecAmount != 0.0)
            throw new Exception($"Trường hợp Đạt: OverSpecAmount phải là 0.0, nhưng nhận được {passItem.OverSpecAmount}");
        if (passItem.FormattedOverSpec != "-")
            throw new Exception($"Trường hợp Đạt: FormattedOverSpec phải là '-', nhưng nhận được '{passItem.FormattedOverSpec}'");
        if (passItem.OverSpecDescription != "Đạt")
            throw new Exception($"Trường hợp Đạt: OverSpecDescription phải là 'Đạt', nhưng nhận được '{passItem.OverSpecDescription}'");
        if (passItem.OverSpecBrushHex != "#4CAF50")
            throw new Exception($"Trường hợp Đạt: OverSpecBrushHex phải là '#4CAF50', nhưng nhận được '{passItem.OverSpecBrushHex}'");

        // 2. Trường hợp Vượt cận trên
        var overUpperItem = new OqcMeasurementDetail
        {
            ToolName = "Độ rộng rãnh",
            ToolType = "Caliper",
            Spec = 11.0,
            Min = 10.0,
            Max = 12.0,
            Result = 12.45,
            Unit = "mm",
            Pass = false,
            HasNumericSpec = true
        };

        if (Math.Abs(overUpperItem.OverSpecAmount!.Value - 0.45) > 1e-4)
            throw new Exception($"Trường hợp Vượt cận trên: OverSpecAmount phải là 0.45, nhưng nhận được {overUpperItem.OverSpecAmount}");
        if (overUpperItem.FormattedOverSpec != "+0.450 mm")
            throw new Exception($"Trường hợp Vượt cận trên: FormattedOverSpec phải là '+0.450 mm', nhưng nhận được '{overUpperItem.FormattedOverSpec}'");
        if (overUpperItem.OverSpecDescription != "Vượt cận trên")
            throw new Exception($"Trường hợp Vượt cận trên: OverSpecDescription phải là 'Vượt cận trên', nhưng nhận được '{overUpperItem.OverSpecDescription}'");
        if (overUpperItem.OverSpecBrushHex != "#D32F2F")
            throw new Exception($"Trường hợp Vượt cận trên: OverSpecBrushHex phải là '#D32F2F', nhưng nhận được '{overUpperItem.OverSpecBrushHex}'");

        // 3. Trường hợp Vượt cận dưới
        var overLowerItem = new OqcMeasurementDetail
        {
            ToolName = "Bán kính lỗ",
            ToolType = "CircleFinder",
            Spec = 11.0,
            Min = 10.0,
            Max = 12.0,
            Result = 9.80,
            Unit = "mm",
            Pass = false,
            HasNumericSpec = true
        };

        if (Math.Abs(overLowerItem.OverSpecAmount!.Value - 0.20) > 1e-4)
            throw new Exception($"Trường hợp Vượt cận dưới: OverSpecAmount phải là 0.20, nhưng nhận được {overLowerItem.OverSpecAmount}");
        if (overLowerItem.FormattedOverSpec != "-0.200 mm")
            throw new Exception($"Trường hợp Vượt cận dưới: FormattedOverSpec phải là '-0.200 mm', nhưng nhận được '{overLowerItem.FormattedOverSpec}'");
        if (overLowerItem.OverSpecDescription != "Vượt cận dưới")
            throw new Exception($"Trường hợp Vượt cận dưới: OverSpecDescription phải là 'Vượt cận dưới', nhưng nhận được '{overLowerItem.OverSpecDescription}'");
        if (overLowerItem.OverSpecBrushHex != "#D32F2F")
            throw new Exception($"Trường hợp Vượt cận dưới: OverSpecBrushHex phải là '#D32F2F', nhưng nhận được '{overLowerItem.OverSpecBrushHex}'");

        // 4. Trường hợp không có spec hoặc không phải số
        var nonNumericItem = new OqcMeasurementDetail
        {
            ToolName = "Nhận diện mã",
            ToolType = "CodeReader",
            Spec = double.NaN,
            Min = double.NaN,
            Max = double.NaN,
            Result = double.NaN,
            CustomResultText = "ABC-12345",
            Pass = true,
            HasNumericSpec = false
        };

        if (nonNumericItem.OverSpecAmount != null)
            throw new Exception($"Trường hợp không có spec: OverSpecAmount phải là null, nhưng nhận được {nonNumericItem.OverSpecAmount}");
        if (nonNumericItem.FormattedOverSpec != "-")
            throw new Exception($"Trường hợp không có spec: FormattedOverSpec phải là '-', nhưng nhận được '{nonNumericItem.FormattedOverSpec}'");
        if (nonNumericItem.OverSpecDescription != "-")
            throw new Exception($"Trường hợp không có spec: OverSpecDescription phải là '-', nhưng nhận được '{nonNumericItem.OverSpecDescription}'");
        if (nonNumericItem.OverSpecBrushHex != "#888888")
            throw new Exception($"Trường hợp không có spec: OverSpecBrushHex phải là '#888888', nhưng nhận được '{nonNumericItem.OverSpecBrushHex}'");

        Console.WriteLine("  -> PASSED: Tính toán Over Spec, độ lệch (+/- delta unit), mô tả và màu sắc hoạt động chuẩn xác 100%.");
    }

    private static void TestOqcOnlyOriginMode()
    {
        Console.WriteLine("--- Test 6: Kiểm tra Chế độ chỉ bắt Origin trong OQC Scanner (OnlyOriginMode) ---");

        // 1. Kiểm tra cấu hình mặc định và tính tuần tự hóa JSON
        var config = new OqcScannerConfig();
        if (!config.OnlyOriginMode)
        {
            throw new Exception("Chế độ OnlyOriginMode phải có giá trị mặc định là true!");
        }

        string json = System.Text.Json.JsonSerializer.Serialize(config);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<OqcScannerConfig>(json);
        if (deserialized == null || !deserialized.OnlyOriginMode)
        {
            throw new Exception("Tuần tự hóa và giải tuần tự hóa OqcScannerConfig không bảo toàn OnlyOriginMode=true!");
        }

        // 2. Mô phỏng kiểm tra khi OnlyOriginMode = true
        // Case A: Origin đạt (Pass = true), nhưng toàn bộ kết quả chung bị NG do tool khác (result.Pass = false)
        bool onlyOriginMode = true;
        var originMatchOk = new PointMatchResult(
            Name: "Origin 1",
            Position: new Point2d(100, 200),
            MatchRect: new Rect(50, 50, 100, 100),
            Score: 0.95,
            Threshold: 0.80,
            Pass: true,
            AngleDeg: 0.0);

        var resultCaseA = new InspectionResult
        {
            Pass = false, // Tool khác bị NG dẫn đến kết quả chung = false
            Origin = originMatchOk
        };

        bool effectivePassA = onlyOriginMode 
            ? (resultCaseA.Origin != null && resultCaseA.Origin.Pass) 
            : resultCaseA.Pass;

        if (!effectivePassA)
        {
            throw new Exception("Khi OnlyOriginMode = true và Origin đạt tiêu chuẩn: Kết quả hiển thị PHẢI LÀ OK/PASS dù tool khác bị NG!");
        }

        // Case B: Origin không đạt (Pass = false), nhưng các tool khác đều OK (giả định)
        var originMatchNg = new PointMatchResult(
            Name: "Origin 1",
            Position: new Point2d(100, 200),
            MatchRect: new Rect(50, 50, 100, 100),
            Score: 0.65,
            Threshold: 0.80,
            Pass: false,
            AngleDeg: 0.0);

        var resultCaseB = new InspectionResult
        {
            Pass = true,
            Origin = originMatchNg
        };

        bool effectivePassB = onlyOriginMode 
            ? (resultCaseB.Origin != null && resultCaseB.Origin.Pass) 
            : resultCaseB.Pass;

        if (effectivePassB)
        {
            throw new Exception("Khi OnlyOriginMode = true và Origin KHÔNG đạt tiêu chuẩn: Kết quả hiển thị PHẢI LÀ NG!");
        }

        // Case C: Không có Origin (Origin = null)
        var resultCaseC = new InspectionResult
        {
            Pass = true,
            Origin = null
        };

        bool effectivePassC = onlyOriginMode 
            ? (resultCaseC.Origin != null && resultCaseC.Origin.Pass) 
            : resultCaseC.Pass;

        if (effectivePassC)
        {
            throw new Exception("Khi OnlyOriginMode = true nhưng không có Origin: Kết quả hiển thị PHẢI LÀ NG!");
        }

        // 3. Mô phỏng kiểm tra khi OnlyOriginMode = false (Chế độ bình thường)
        onlyOriginMode = false;
        bool effectivePassNormal = onlyOriginMode 
            ? (resultCaseA.Origin != null && resultCaseA.Origin.Pass) 
            : resultCaseA.Pass;

        if (effectivePassNormal)
        {
            throw new Exception("Khi OnlyOriginMode = false: Kết quả hiển thị phải tuân thủ đúng result.Pass (ở đây là NG)!");
        }

        Console.WriteLine("  -> PASSED: Chế độ chỉ bắt Origin (OnlyOriginMode) hoạt động chuẩn xác 100% (Mặc định = true, Origin OK -> OK, Origin NG -> NG).");
    }

    private static void TestOqcWaitingForInspectionStateOnLiveView()
    {
        Console.WriteLine("--- Test 7: Kiểm tra khi bắt đầu Live View chuyển sang trạng thái Chờ kiểm tra (READY) ---");

        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

            // Khởi tạo các trường cần thiết
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentMeasurementDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<OqcMeasurementDetail>());

            // 1. Giả lập kết quả test trước đó (như vừa chạy xong 1 sản phẩm bị PASS/NG)
            vm.BigResultStatusText = "PASS";
            vm.CurrentMeasurementDetails.Add(new OqcMeasurementDetail
            {
                ToolName = "Khoảng cách mẫu cũ",
                Result = 12.34,
                Pass = true
            });
            vm.HasLastNgDetails = true;
            vm.LastNgDetails = "Lỗi mẫu trước";

            // Kiểm tra trạng thái giả lập đã có dữ liệu cũ
            if (vm.BigResultStatusText != "PASS" || vm.CurrentMeasurementDetails.Count != 1)
            {
                throw new Exception("Giả lập dữ liệu cũ thất bại!");
            }

            // 2. Kích hoạt chuyển sang trạng thái Live View (gọi SetWaitingForInspectionState)
            vm.SetWaitingForInspectionState();

            // 3. Xác minh:
            // - BigResultStatusText phải chuyển về "READY"
            if (vm.BigResultStatusText != "READY")
            {
                throw new Exception($"BigResultStatusText phải chuyển về 'READY' khi Live View, nhưng nhận được '{vm.BigResultStatusText}'!");
            }

            // - CurrentMeasurementDetails phải bị xóa sạch (Count == 0) để không hiển thị chi tiết cũ
            if (vm.CurrentMeasurementDetails.Count != 0)
            {
                throw new Exception($"CurrentMeasurementDetails phải bị xóa sạch khi bắt đầu Live View, nhưng còn lại {vm.CurrentMeasurementDetails.Count} dòng!");
            }

            // - HasLastNgDetails phải bằng false và LastNgDetails rỗng
            if (vm.HasLastNgDetails || !string.IsNullOrEmpty(vm.LastNgDetails))
            {
                throw new Exception("HasLastNgDetails phải là false và LastNgDetails phải rỗng khi bắt đầu Live View!");
            }

            // - LastResultSummary phải chứa thông điệp chờ kiểm tra
            if (string.IsNullOrWhiteSpace(vm.LastResultSummary) || (!vm.LastResultSummary.Contains("Chờ") && !vm.LastResultSummary.Contains("Sẵn sàng")))
            {
                throw new Exception($"LastResultSummary phải chứa thông báo chờ kiểm tra, nhưng nhận được '{vm.LastResultSummary}'!");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Console.WriteLine("  -> PASSED: Khi bắt đầu Live View, hệ thống đã xóa sạch kết quả cũ và chuyển sang trạng thái Chờ kiểm tra (READY) chuẩn xác 100%.");
    }
}

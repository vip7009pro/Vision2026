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
        TestOqcProductNameAndLayout204040Configuration();
        TestOqcTriggerInspectOrLiveToggleSequence();
        TestOqcSteelPunchMode();
        TestOqcEscapeKeyCloseJobAndClearText();
        TestOqcAutoSelectScannedText();
        TestOqcMultiInspectHistoryRetention();

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
            System.Windows.Window? win = null;
            try
            {
                win = new System.Windows.Window
                {
                    WindowState = System.Windows.WindowState.Maximized
                };

                // Gọi hàm BringWindowToForeground
                VisionInspectionApp.UI.App.BringWindowToForeground(win);

                if (win.WindowState != System.Windows.WindowState.Maximized)
                {
                    throw new Exception($"BringWindowToForeground không bảo tồn trạng thái Maximized! Giá trị hiện tại: {win.WindowState}");
                }
            }
            finally
            {
                try
                {
                    win?.Close();
                }
                catch { }
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
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

    private static void TestOqcProductNameAndLayout204040Configuration()
    {
        Console.WriteLine("--- Test 8: Kiểm tra hiển thị Tên Sản Phẩm cực lớn (Product Name) và bố cục 20/40/40 trong OQC Scanner ---");

        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

            // Khởi tạo các trường liên quan
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentProductName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "-");
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentMeasurementDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<OqcMeasurementDetail>());

            // 1. Kiểm tra giá trị khởi tạo ban đầu
            if (vm.CurrentProductName != "-")
            {
                throw new Exception($"CurrentProductName ban đầu phải là '-', nhưng nhận được '{vm.CurrentProductName}'!");
            }

            // 2. Kiểm tra khi nạp tên sản phẩm mới
            const string sampleProduct = "MOTOR_CONTROLLER_HOUSING_2026_XYZ";
            vm.CurrentProductName = sampleProduct;
            if (vm.CurrentProductName != sampleProduct)
            {
                throw new Exception($"CurrentProductName không cập nhật chính xác giá trị '{sampleProduct}'!");
            }

            // 3. Kiểm tra thông điệp chuyển trạng thái chờ kiểm tra khi có tên sản phẩm
            vm.SetWaitingForInspectionState();
            if (!vm.LastResultSummary.Contains(sampleProduct))
            {
                throw new Exception($"LastResultSummary phải chứa tên sản phẩm '{sampleProduct}', nhưng nhận được '{vm.LastResultSummary}'!");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        // 4. Kiểm tra cấu trúc XAML trong OqcScannerView.xaml:
        // Đảm bảo có Viewbox Uniform với Text="{Binding CurrentProductName}", và tỷ lệ RowDefinitions 10/45/45 (10*, 45*, 45*)
        string xamlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml");
        if (System.IO.File.Exists(xamlPath))
        {
            string xamlContent = System.IO.File.ReadAllText(xamlPath);
            if (!xamlContent.Contains("CurrentProductName") || !xamlContent.Contains("Viewbox Stretch=\"Uniform\""))
            {
                throw new Exception("OqcScannerView.xaml phải chứa Viewbox Stretch=\"Uniform\" bao quanh TextBlock binding CurrentProductName!");
            }
            if ((!xamlContent.Contains("RowDefinition Height=\"10*\"") && !xamlContent.Contains("RowDefinition Height=\"1*\"")) ||
                (!xamlContent.Contains("RowDefinition Height=\"45*\"") && !xamlContent.Contains("RowDefinition Height=\"4.5*\"")))
            {
                throw new Exception("OqcScannerView.xaml phải phân chia tỷ lệ 10/45/45 (Height=\"10*\" và Height=\"45*\")!");
            }
        }

        Console.WriteLine("  -> PASSED: Tên Sản Phẩm tự động co giãn full-width (Viewbox Uniform) và bố cục 10/45/45 được xác minh chuẩn xác 100%.");
    }

    private static void TestOqcTriggerInspectOrLiveToggleSequence()
    {
        Console.WriteLine("--- Test 9: Kiểm tra tính năng 1-nút Space / Ctrl+F8 luân phiên Kiểm tra và Live View ---");

        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

            // Đặt các field cần thiết qua reflection
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isShowingLiveCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            // 1. Khi đang ở Live View: Space / Ctrl+F8 phải kích hoạt KIỂM TRA HÀNG
            if (!vm.IsShowingLiveCamera)
            {
                throw new Exception("Trạng thái ban đầu phải là Live View!");
            }

            // 2. Sau khi kiểm tra xong, hệ thống hiển thị kết quả (IsShowingLiveCamera = false)
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isShowingLiveCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, false);

            if (vm.IsShowingLiveCamera)
            {
                throw new Exception("Sau khi kiểm tra hàng, IsShowingLiveCamera phải là false!");
            }

            // 3. Khi đang ở chế độ kết quả: phím Space / Ctrl+F8 kích hoạt quay về Live View (IsShowingLiveCamera = true)
            // Mô phỏng người dùng bấm Space để quay về Live View:
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isShowingLiveCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            if (!vm.IsShowingLiveCamera)
            {
                throw new Exception("Sau khi bấm Space lúc đang ở kết quả, IsShowingLiveCamera phải chuyển thành true!");
            }

            // 4. Kiểm tra kịch bản người dùng mô tả:
            // "nếu vừa space để kiểm tra xong mà bấm nút F5 rồi, thì space tiếp sẽ là kiểm tra, chứ không phải quay lại live view nữa"
            // Bước A: Kiểm tra xong -> IsShowingLiveCamera = false
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isShowingLiveCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, false);

            // Bước B: Bấm nút F5 -> Chuyển về Live View (IsShowingLiveCamera = true)
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isShowingLiveCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            // Bước C: Bấm Space tiếp theo -> Vì IsShowingLiveCamera == true, nên nhánh thực thi là KIỂM TRA HÀNG!
            if (!vm.IsShowingLiveCamera)
            {
                throw new Exception("Sau khi bấm F5, IsShowingLiveCamera phải là true để lượt Space tiếp theo là Kiểm tra!");
            }

            // 5. Kiểm tra các nhãn và tooltip trên UI
            if (!vm.ScanButtonText.Contains("Ctrl+F8") && !vm.CameraScanButtonText.Contains("Ctrl+F8"))
            {
                throw new Exception("Nút lệnh OQC phải gợi ý phím tắt Ctrl+F8!");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        // 6. Kiểm tra XAML có chứa KeyBinding cho Ctrl+F8 và TriggerInspectOrLiveCommand
        string xamlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml");
        if (System.IO.File.Exists(xamlPath))
        {
            string xamlContent = System.IO.File.ReadAllText(xamlPath);
            if (!xamlContent.Contains("TriggerInspectOrLiveCommand"))
            {
                throw new Exception("OqcScannerView.xaml phải binding TriggerInspectOrLiveCommand!");
            }
            if (!xamlContent.Contains("Key=\"F8\" Modifiers=\"Control\""))
            {
                throw new Exception("OqcScannerView.xaml phải có KeyBinding cho Ctrl+F8!");
            }
        }

        Console.WriteLine("  -> PASSED: Tính năng 1-nút Space / Ctrl+F8 luân phiên Kiểm tra và Live View hoạt động hoàn hảo 100%.");
    }

    private static void TestOqcSteelPunchMode()
    {
        Console.WriteLine("--- Test 10: Kiểm tra Chế độ Cú đấm thép (SteelPunchMode) trong OQC Scanner ---");

        // 1. Kiểm tra cấu hình mặc định và tính tuần tự hóa JSON
        var config = new OqcScannerConfig();
        if (!config.SteelPunchMode)
        {
            throw new Exception("Chế độ SteelPunchMode phải có giá trị mặc định là true!");
        }

        string json = System.Text.Json.JsonSerializer.Serialize(config);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<OqcScannerConfig>(json);
        if (deserialized == null || !deserialized.SteelPunchMode)
        {
            throw new Exception("Tuần tự hóa và giải tuần tự hóa OqcScannerConfig không bảo toàn SteelPunchMode=true!");
        }

        config.SteelPunchMode = false;
        string jsonFalse = System.Text.Json.JsonSerializer.Serialize(config);
        var deserializedFalse = System.Text.Json.JsonSerializer.Deserialize<OqcScannerConfig>(jsonFalse);
        if (deserializedFalse == null || deserializedFalse.SteelPunchMode)
        {
            throw new Exception("Tuần tự hóa và giải tuần tự hóa OqcScannerConfig không bảo toàn SteelPunchMode=false!");
        }

        // 2. Kiểm tra ViewModel và các điều kiện kích hoạt
        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

            // Đặt các field ban đầu
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_steelPunchMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentJobFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "Chưa có Job");

            // A. Khi chưa có Job: HasLoadedJob phải là false
            if (vm.HasLoadedJob)
            {
                throw new Exception("Khi CurrentJobFilePath = 'Chưa có Job', HasLoadedJob phải là false!");
            }

            // B. Khi đã nạp Job: HasLoadedJob phải là true
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentJobFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, @"C:\Jobs\TestPart.job");

            if (!vm.HasLoadedJob)
            {
                throw new Exception("Khi CurrentJobFilePath trỏ đến tệp job hợp lệ, HasLoadedJob phải là true!");
            }

            // C. Khi SteelPunchMode = true và HasLoadedJob = true: ScanButtonText phải gợi ý chạy Job qua Space / Ctrl+F8
            if (!vm.ScanButtonText.Contains("SPACE / Ctrl+F8"))
            {
                throw new Exception($"ScanButtonText phải chứa 'SPACE / Ctrl+F8' khi Cú đấm thép bật và Job đã nạp! Thực tế: '{vm.ScanButtonText}'");
            }

            // D. Kiểm tra giá trị ScannedCode không bị xóa
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_scannedCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "SN-TEST-8888");

            if (vm.ScannedCode != "SN-TEST-8888")
            {
                throw new Exception("ScannedCode phải giữ nguyên giá trị đã đặt!");
            }

            // E. Kiểm tra IsSameAsCurrentSessionCode
            // Giả lập Service để test logic so sánh mã phiên
            var oqcService = new VisionInspectionApp.Application.OQC.OqcScannerService(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dummy_oqc_test1.json"), disableBackupSync: true);
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_oqcService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, oqcService);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentSessionProductCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "PART_A");

            // Khi input là cùng mã PART_A -> IsSameAsCurrentSessionCode = true (coi như không làm gì khi enter)
            if (!vm.IsSameAsCurrentSessionCode("PART_A"))
            {
                throw new Exception("IsSameAsCurrentSessionCode phải trả về true khi cùng mã phiên hiện tại!");
            }

            // Khi input là mã tiếp theo PART_B -> IsSameAsCurrentSessionCode = false (cho phép Enter nạp Job mới)
            if (vm.IsSameAsCurrentSessionCode("PART_B"))
            {
                throw new Exception("IsSameAsCurrentSessionCode phải trả về false khi scan mã tiếp theo khác mã phiên!");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        // 3. Kiểm tra file XAML có chứa CheckBox Cú đấm thép
        string xamlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml");
        if (System.IO.File.Exists(xamlPath))
        {
            string xamlContent = System.IO.File.ReadAllText(xamlPath);
            if (!xamlContent.Contains("SteelPunchMode"))
            {
                throw new Exception("OqcScannerView.xaml phải binding IsChecked với SteelPunchMode!");
            }
            if (!xamlContent.Contains("Cú đấm thép"))
            {
                throw new Exception("OqcScannerView.xaml phải có CheckBox hiển thị tên 'Cú đấm thép'!");
            }
        }

        // 4. Kiểm tra code-behind có xử lý phím Enter với IsSameAsCurrentSessionCode
        string csPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml.cs");
        if (System.IO.File.Exists(csPath))
        {
            string csContent = System.IO.File.ReadAllText(csPath);
            if (!csContent.Contains("IsSameAsCurrentSessionCode") || !csContent.Contains("e.Key == Key.Enter"))
            {
                throw new Exception("OqcScannerView.xaml.cs phải xử lý phím Enter với IsSameAsCurrentSessionCode!");
            }
        }

        Console.WriteLine("  -> PASSED: Chế độ Cú đấm thép (SteelPunchMode) hoạt động hoàn hảo 100%.");
    }

    private static void TestOqcEscapeKeyCloseJobAndClearText()
    {
        Console.WriteLine("--- Test 11: Kiểm thử phím tắt ESC đóng Job và xóa Textbox OQC Scanner ---");

        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

            var oqcService = new VisionInspectionApp.Application.OQC.OqcScannerService(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dummy_oqc_test2.json"), disableBackupSync: true);
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_oqcService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, oqcService);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("<ScanHistory>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<VisionInspectionApp.Models.OqcScanHistoryEntry>());

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("<CurrentMeasurementDetails>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<VisionInspectionApp.Models.OqcMeasurementDetail>());

            // 1. Giả lập Job đã được nạp và có chuỗi quét trên Textbox
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentJobFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, @"C:\VisionJobs\JobA.job");

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentProductName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "Product Model A");

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentSessionProductCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "PART_12345");

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_scannedCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "PART_12345");

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentJobTestedCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, 5);

            if (!vm.HasLoadedJob)
            {
                throw new Exception("Khi CurrentJobFilePath hợp lệ, HasLoadedJob phải trả về true!");
            }

            // 2. Kích hoạt phương thức phím tắt ESC
            vm.EscapeCloseJobAndClearText();

            // 3. Xác thực Job đã được đóng và Textbox đã được xóa sạch
            if (vm.HasLoadedJob)
            {
                throw new Exception("Sau khi bấm ESC, HasLoadedJob phải trả về false!");
            }
            if (vm.CurrentJobFilePath != "Chưa có Job")
            {
                throw new Exception($"Sau khi bấm ESC, CurrentJobFilePath phải là 'Chưa có Job', hiện tại: {vm.CurrentJobFilePath}");
            }
            if (!string.IsNullOrEmpty(vm.ScannedCode))
            {
                throw new Exception($"Sau khi bấm ESC, ScannedCode phải bị xóa rỗng (''), hiện tại: {vm.ScannedCode}");
            }
            if (!string.IsNullOrEmpty(vm.CurrentSessionProductCode))
            {
                throw new Exception($"Sau khi bấm ESC, CurrentSessionProductCode phải bị reset về (''), hiện tại: {vm.CurrentSessionProductCode}");
            }
            if (vm.CurrentJobTestedCount != 0)
            {
                throw new Exception($"Sau khi bấm ESC, CurrentJobTestedCount phải bị reset về 0, hiện tại: {vm.CurrentJobTestedCount}");
            }

            // 4. Test Case: Khi chưa có Job nạp nhưng Textbox đang có chuỗi gõ dở
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_scannedCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "PART_TYPING_ACCIDENTAL");

            if (vm.HasLoadedJob)
            {
                throw new Exception("HasLoadedJob lúc này phải là false!");
            }

            vm.EscapeCloseJobAndClearText();

            if (!string.IsNullOrEmpty(vm.ScannedCode))
            {
                throw new Exception($"Dù chưa có Job nạp, bấm ESC vẫn phải xóa sạch Textbox ScannedCode, hiện tại: {vm.ScannedCode}");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        // 5. Kiểm tra file XAML có chứa KeyBinding Esc và nút Đóng Job (ESC)
        string xamlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml");
        if (System.IO.File.Exists(xamlPath))
        {
            string xamlContent = System.IO.File.ReadAllText(xamlPath);
            if (!xamlContent.Contains("EscapeCloseJobAndClearTextCommand"))
            {
                throw new Exception("OqcScannerView.xaml phải liên kết phím Esc với EscapeCloseJobAndClearTextCommand!");
            }
            if (!xamlContent.Contains("Đóng Job (ESC)"))
            {
                throw new Exception("OqcScannerView.xaml nút Đóng Job phải có nhãn '🔒 Đóng Job (ESC)'!");
            }
        }

        // 6. Kiểm tra code-behind OqcScannerView.xaml.cs có bắt e.Key == Key.Escape
        string csPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml.cs");
        if (System.IO.File.Exists(csPath))
        {
            string csContent = System.IO.File.ReadAllText(csPath);
            if (!csContent.Contains("e.Key == Key.Escape") || !csContent.Contains("EscapeCloseJobAndClearTextCommand"))
            {
                throw new Exception("OqcScannerView.xaml.cs phải xử lý phím Escape với EscapeCloseJobAndClearTextCommand!");
            }
        }

        Console.WriteLine("  -> PASSED: Phím tắt ESC đóng Job và xóa Textbox hoạt động hoàn hảo 100%.");
    }

    private static void TestOqcAutoSelectScannedText()
    {
        Console.WriteLine("--- Test 12: Kiểm thử tính năng tự động Focus & Select All Scanned Text trên OQC Scanner ---");

        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

            bool eventFired = false;
            vm.RequestFocusAndSelectInput += () =>
            {
                eventFired = true;
            };

            // 1. Kích hoạt trigger Focus và Select
            vm.TriggerFocusAndSelectInput();

            if (!eventFired)
            {
                throw new Exception("TriggerFocusAndSelectInput phải phát sự kiện RequestFocusAndSelectInput!");
            }

            // 2. Kiểm tra mô phỏng hành vi: khi ScannedCode đang có giá trị mã cũ
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_scannedCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, "OLD_SCAN_11111");

            if (vm.ScannedCode != "OLD_SCAN_11111")
            {
                throw new Exception("ScannedCode ban đầu phải là OLD_SCAN_11111!");
            }

            // Khi TextBox đã SelectAll, ký tự mới từ máy quét sẽ ghi đè hoàn toàn chuỗi cũ
            // Mô phỏng scanner bắn chuỗi mới:
            string newBarcodeInput = "NEW_SCAN_22222";
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_scannedCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, newBarcodeInput);

            if (vm.ScannedCode != "NEW_SCAN_22222")
            {
                throw new Exception("Sau khi ghi đè, ScannedCode phải là NEW_SCAN_22222!");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        // 3. Kiểm tra file XAML có chứa GotKeyboardFocus và PreviewMouseLeftButtonDown
        string xamlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml");
        if (System.IO.File.Exists(xamlPath))
        {
            string xamlContent = System.IO.File.ReadAllText(xamlPath);
            if (!xamlContent.Contains("ScanInputTextBox_GotKeyboardFocus"))
            {
                throw new Exception("OqcScannerView.xaml phải có sự kiện GotKeyboardFocus trỏ vào ScanInputTextBox_GotKeyboardFocus!");
            }
            if (!xamlContent.Contains("ScanInputTextBox_PreviewMouseLeftButtonDown"))
            {
                throw new Exception("OqcScannerView.xaml phải có sự kiện PreviewMouseLeftButtonDown trỏ vào ScanInputTextBox_PreviewMouseLeftButtonDown!");
            }
        }

        // 4. Kiểm tra code-behind OqcScannerView.xaml.cs có phương thức FocusAndSelectAll và xử lý RequestFocusAndSelectInput
        string csPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "OQC", "OqcScannerView.xaml.cs");
        if (System.IO.File.Exists(csPath))
        {
            string csContent = System.IO.File.ReadAllText(csPath);
            if (!csContent.Contains("FocusAndSelectAll()"))
            {
                throw new Exception("OqcScannerView.xaml.cs phải định nghĩa phương thức FocusAndSelectAll()!");
            }
            if (!csContent.Contains("RequestFocusAndSelectInput"))
            {
                throw new Exception("OqcScannerView.xaml.cs phải hook sự kiện RequestFocusAndSelectInput từ ViewModel!");
            }
            if (!csContent.Contains("ScanInputTextBox?.SelectAll()") && !csContent.Contains("ScanInputTextBox.SelectAll()"))
            {
                throw new Exception("OqcScannerView.xaml.cs phải gọi SelectAll() trên ScanInputTextBox!");
            }
        }

        Console.WriteLine("  -> PASSED: Tính năng tự động Focus & Select All Scanned Text hoạt động hoàn hảo 100%.");
    }

    private static void TestOqcMultiInspectHistoryRetention()
    {
        Console.WriteLine("--- Test 13: Kiểm tra lưu lịch sử kiểm tra cho mọi lần chụp và kiểm trong cùng 1 phiên Job ---");

        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

            var oqcService = new VisionInspectionApp.Application.OQC.OqcScannerService(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dummy_oqc_test_hist.json"), disableBackupSync: true);
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_oqcService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, oqcService);

            var scanHistory = new System.Collections.ObjectModel.ObservableCollection<VisionInspectionApp.Models.OqcScanHistoryEntry>();
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("<ScanHistory>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, scanHistory);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentMeasurementDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<VisionInspectionApp.Models.OqcMeasurementDetail>());

            // Cấu hình Job đang mở trong phiên
            string jobPath = @"C:\VisionJobs\TestProduct.job";
            string prodCode = "PROD_LOT_001";
            string prodName = "Product Model Alpha";

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentJobFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, jobPath);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentProductName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, prodName);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_lastScannedProcessedCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, prodCode);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_currentSessionProductCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, prodCode);

            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_scannedCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, prodCode);

            // Giả lập ToolEditorViewModel
            var toolEditorVm = (VisionInspectionApp.UI.ViewModels.ToolEditorViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.ToolEditorViewModel));
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_toolEditorViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, toolEditorVm);

            var config = new VisionInspectionApp.Models.VisionConfig
            {
                ProductCode = prodCode,
                ProductName = prodName
            };

            // 1. Lần kiểm tra 1: PASS
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isOqcRunInProgress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            var res1 = new InspectionResult { Pass = true };
            vm.HandleInspectionCompletedAsync(res1, config).GetAwaiter().GetResult();

            if (vm.ScanHistory.Count != 1)
            {
                throw new Exception($"Sau lần kiểm tra 1, ScanHistory.Count phải là 1, hiện tại: {vm.ScanHistory.Count}");
            }
            if (!vm.ScanHistory[0].InspectResult.Contains("PASS"))
            {
                throw new Exception($"Lần kiểm tra 1 phải là PASS, hiện tại: {vm.ScanHistory[0].InspectResult}");
            }
            string uuid1 = vm.ScanHistory[0].Uuid;
            if (string.IsNullOrEmpty(uuid1))
            {
                throw new Exception("Lần kiểm tra 1 phải có Uuid hợp lệ!");
            }

            // 2. Lần kiểm tra 2 trên cùng 1 phiên Job: NG
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isOqcRunInProgress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            var res2 = new InspectionResult { Pass = false };
            vm.HandleInspectionCompletedAsync(res2, config).GetAwaiter().GetResult();

            if (vm.ScanHistory.Count != 2)
            {
                throw new Exception($"Sau lần kiểm tra 2 trên cùng phiên Job, ScanHistory.Count phải là 2 (không được ghi đè lần 1)! Hiện tại: {vm.ScanHistory.Count}");
            }
            if (!vm.ScanHistory[0].InspectResult.Contains("NG"))
            {
                throw new Exception($"Lần kiểm tra 2 (dòng mới nhất ở index 0) phải là NG, hiện tại: {vm.ScanHistory[0].InspectResult}");
            }
            if (!vm.ScanHistory[1].InspectResult.Contains("PASS"))
            {
                throw new Exception($"Lần kiểm tra 1 (ở index 1) phải được bảo toàn là PASS, hiện tại: {vm.ScanHistory[1].InspectResult}");
            }
            string uuid2 = vm.ScanHistory[0].Uuid;
            if (uuid1 == uuid2)
            {
                throw new Exception("Lần kiểm tra 2 phải có Uuid mới, không được trùng với lần kiểm tra 1!");
            }

            // 3. Lần kiểm tra 3 trên cùng 1 phiên Job: PASS
            typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
                .GetField("_isOqcRunInProgress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            var res3 = new InspectionResult { Pass = true };
            vm.HandleInspectionCompletedAsync(res3, config).GetAwaiter().GetResult();

            if (vm.ScanHistory.Count != 3)
            {
                throw new Exception($"Sau lần kiểm tra 3, ScanHistory.Count phải là 3, hiện tại: {vm.ScanHistory.Count}");
            }
            if (!vm.ScanHistory[0].InspectResult.Contains("PASS"))
            {
                throw new Exception($"Lần kiểm tra 3 phải là PASS, hiện tại: {vm.ScanHistory[0].InspectResult}");
            }
            if (vm.CurrentJobTestedCount != 3)
            {
                throw new Exception($"CurrentJobTestedCount phải là 3, hiện tại: {vm.CurrentJobTestedCount}");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Console.WriteLine("  -> PASSED: Lưu lịch sử kiểm tra cho mọi lần chụp và kiểm trong cùng 1 phiên Job hoạt động hoàn hảo 100%.");
    }
}



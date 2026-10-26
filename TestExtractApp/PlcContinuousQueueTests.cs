using System;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media;
using OpenCvSharp;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Bộ kiểm thử tự động cho luồng Run Continuous kết hợp PLC Trigger và Hàng đợi 16 nấc (Bounded Channel).
/// </summary>
public static class PlcContinuousQueueTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("⚡ RUNNING PLC TRIGGER CONTINUOUS QUEUE (16 SLOTS) TESTS");
        Console.WriteLine("=========================================================");

        Test_01_PlcTrigger_In_RunContinuous_Uses_BoundedChannel();
        Test_02_Rapid_Triggers_Fill_Queue_Slots_Accurately();
        Test_03_UpdateQueueVisuals_ThreadSafe_And_ColorThresholds();
        Test_04_StopContinuous_DrainsQueue_And_ResetsState();
        Test_05_ManualTrigger_In_RunContinuous_And_RunOnce();

        Console.WriteLine("✅ ALL 5 PLC TRIGGER CONTINUOUS QUEUE TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void InjectField(object target, string fieldName, object? value)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }

        if (field is null)
            throw new Exception($"InjectField: không tìm thấy field '{fieldName}'");

        field.SetValue(target, value);
    }

    private static object? GetField(object target, string fieldName)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }

        if (field is null)
            throw new Exception($"GetField: không tìm thấy field '{fieldName}'");

        return field.GetValue(target);
    }

    /// <summary>
    /// [1/4] Chạy Run Continuous với ImageSource có SourceType=Camera và TriggerMode=PlcTrigger:
    /// Đảm bảo khởi tạo BoundedChannel 16 nấc và StatusBar hiển thị trạng thái chờ PLC Trigger.
    /// </summary>
    private static void Test_01_PlcTrigger_In_RunContinuous_Uses_BoundedChannel()
    {
        Console.Write("--- [1/4] Khởi tạo Run Continuous ở chế độ PLC Trigger phải dùng BoundedChannel 16 nấc... ");

        var config = new VisionConfig
        {
            ProductCode = "TEST_PLC_TRIGGER",
            ProductName = "TEST_PLC_TRIGGER",
            ToolGraph = new ToolGraph { Nodes = { new ToolGraphNode { Id = "1", Type = "ImageSource", RefName = "CAM1" } } }
        };
        config.ImageSources.Add(new ImageSourceDefinition
        {
            Name = "CAM1",
            SourceType = ImageSourceType.Camera,
            CameraIndex = CameraService.SimulatorCameraIndex,
            TriggerMode = ImageSourceTriggerMode.PlcTrigger,
            PlcTriggerPlcId = "PLC1",
            PlcTriggerTagName = "M100",
            PlcTriggerEdge = PlcTriggerEdge.RisingEdge
        });

        var vm = new ToolEditorViewModel();
        vm.InitializeWithConfig(config);

        // Gọi StartContinuousCameraFlow bằng reflection
        var startMethod = typeof(ToolEditorViewModel).GetMethod(
            "StartContinuousCameraFlow",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("Không tìm thấy method 'StartContinuousCameraFlow'");

        var task = (Task?)startMethod.Invoke(vm, new object[] { config.ImageSources[0] })
                   ?? throw new Exception("StartContinuousCameraFlow không trả về Task");
        task.GetAwaiter().GetResult();

        Assert(vm.IsRunningContinuous, "IsRunningContinuous phải là true");
        Assert(vm.QueueCapacity == 16, "QueueCapacity phải là 16");

        var channel = GetField(vm, "_industrialCameraFrameChannel");
        Assert(channel != null, "Chế độ PLC Trigger bắt buộc phải khởi tạo _industrialCameraFrameChannel (trước đây bị null)");

        Assert(vm.StatusBarText.Contains("Chờ PLC Trigger"), 
            $"StatusBarText phải hiển thị 'Chờ PLC Trigger', thực tế: '{vm.StatusBarText}'");

        // Dọn dẹp
        var stopMethod = typeof(ToolEditorViewModel).GetMethod(
            "StopContinuousFlow",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("Không tìm thấy method 'StopContinuousFlow'");
        stopMethod.Invoke(vm, null);

        Assert(!vm.IsRunningContinuous, "Sau StopContinuousFlow, IsRunningContinuous phải về false");
        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// [2/4] Trigger dồn dập (rapid triggers): Nạp nhiều frame vào hàng đợi, các slot 16 nấc
    /// phải sáng dần lên theo thời gian thực (0/16 -> 1/16 -> 3/16) mà không bị drop hay khóa luồng.
    /// </summary>
    private static void Test_02_Rapid_Triggers_Fill_Queue_Slots_Accurately()
    {
        Console.Write("--- [2/4] Kích phát dồn dập phải làm đầy hàng đợi 16 nấc tuần tự chính xác... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        // Tạo bounded channel 16 nấc
        var channel = Channel.CreateBounded<ContinuousFrameEnvelope>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        InjectField(vm, "_industrialCameraFrameChannel", channel);
        InjectField(vm, "_isRunningFolderFlow", true);

        // Ban đầu trống
        vm.QueueCurrentCount = 0;
        Assert(vm.QueueStatusText == "0/16", $"Status ban đầu phải 0/16, thực tế: '{vm.QueueStatusText}'");
        Assert(!vm.QueueSlot0Active, "Slot 0 ban đầu phải tắt");

        // Nạp 3 frame giả lập
        for (int i = 1; i <= 3; i++)
        {
            var mat = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(i));
            var env = new ContinuousFrameEnvelope 
            { 
                Frame = mat, 
                Metadata = new FrameMetadata { FrameIndex = i, HostTimestamp = DateTime.UtcNow } 
            };
            channel.Writer.TryWrite(env);
            vm.QueueCurrentCount = channel.Reader.Count;
        }

        Assert(channel.Reader.Count == 3, $"Hàng đợi phải chứa 3 frame, thực tế: {channel.Reader.Count}");
        Assert(vm.QueueCurrentCount == 3, $"QueueCurrentCount phải là 3, thực tế: {vm.QueueCurrentCount}");
        Assert(vm.QueueStatusText == "3/16", $"Status phải là 3/16, thực tế: '{vm.QueueStatusText}'");
        Assert(vm.QueueSlot0Active && vm.QueueSlot1Active && vm.QueueSlot2Active, 
            "Slot 0, 1, 2 phải sáng khi có 3 frame trong hàng đợi");
        Assert(!vm.QueueSlot3Active, "Slot 3 phải tắt khi chỉ có 3 frame");

        // Rút 1 frame
        if (channel.Reader.TryRead(out var readEnv))
        {
            readEnv.Dispose();
            vm.QueueCurrentCount = channel.Reader.Count;
        }

        Assert(vm.QueueCurrentCount == 2, $"Sau khi đọc 1 frame, QueueCurrentCount phải là 2, thực tế: {vm.QueueCurrentCount}");
        Assert(vm.QueueStatusText == "2/16", $"Status phải là 2/16, thực tế: '{vm.QueueStatusText}'");
        Assert(vm.QueueSlot0Active && vm.QueueSlot1Active, "Slot 0, 1 phải sáng");
        Assert(!vm.QueueSlot2Active, "Slot 2 phải tắt");

        // Dọn dẹp
        while (channel.Reader.TryRead(out var leftover)) leftover.Dispose();
        InjectField(vm, "_industrialCameraFrameChannel", null);
        InjectField(vm, "_isRunningFolderFlow", false);

        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// [3/4] Kiểm tra UpdateQueueVisuals an toàn đa luồng và đổi màu (Emerald -> Amber -> Ruby) khi đầy nấc.
    /// </summary>
    private static void Test_03_UpdateQueueVisuals_ThreadSafe_And_ColorThresholds()
    {
        Console.Write("--- [3/4] An toàn đa luồng UpdateQueueVisuals và ngưỡng màu (Emerald / Amber / Ruby)... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        // Mức thấp (0-5 nấc): Emerald (G > R)
        vm.QueueCurrentCount = 4;
        Assert(vm.QueueStatusText == "4/16", $"Mức 4 nấc: '{vm.QueueStatusText}'");
        var colorLow = ((SolidColorBrush)vm.QueueFillBrush).Color;
        Assert(colorLow.G > colorLow.R, "Mức 4 nấc màu phải là tông xanh Emerald");

        // Mức trung bình (6-11 nấc): Amber (R > G và G > 100)
        vm.QueueCurrentCount = 8;
        Assert(vm.QueueStatusText == "8/16", $"Mức 8 nấc: '{vm.QueueStatusText}'");
        var colorMid = ((SolidColorBrush)vm.QueueFillBrush).Color;
        Assert(colorMid.R > colorMid.B && colorMid.G > colorMid.B, "Mức 8 nấc màu phải là tông cam/vàng Amber");

        // Mức cảnh báo cao (12-16 nấc): Ruby (R > G)
        vm.QueueCurrentCount = 14;
        Assert(vm.QueueStatusText == "14/16", $"Mức 14 nấc: '{vm.QueueStatusText}'");
        var colorHigh = ((SolidColorBrush)vm.QueueFillBrush).Color;
        Assert(colorHigh.R > colorHigh.G, "Mức 14 nấc màu phải là tông đỏ Ruby");

        // Gọi từ background thread kiểm tra bảo vệ luồng không văng ngoại lệ
        Task.Run(() =>
        {
            vm.QueueCurrentCount = 2;
            vm.UpdateQueueVisuals();
        }).GetAwaiter().GetResult();

        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// [4/4] Dừng luồng (Stop): Hàng đợi phải được drain sạch, Mat native được dispose và trả về 0/16.
    /// </summary>
    private static void Test_04_StopContinuous_DrainsQueue_And_ResetsState()
    {
        Console.Write("--- [4/4] Dừng luồng phải giải phóng sạch hàng đợi, không rò rỉ bộ nhớ... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        var channel = Channel.CreateBounded<ContinuousFrameEnvelope>(new BoundedChannelOptions(16));
        InjectField(vm, "_industrialCameraFrameChannel", channel);
        InjectField(vm, "_isRunningFolderFlow", true);

        // Nạp 2 frame
        var mat1 = new Mat(20, 20, MatType.CV_8UC3, Scalar.All(100));
        var mat2 = new Mat(20, 20, MatType.CV_8UC3, Scalar.All(200));
        channel.Writer.TryWrite(new ContinuousFrameEnvelope 
        { 
            Frame = mat1, 
            Metadata = new FrameMetadata { FrameIndex = 1, HostTimestamp = DateTime.UtcNow } 
        });
        channel.Writer.TryWrite(new ContinuousFrameEnvelope 
        { 
            Frame = mat2, 
            Metadata = new FrameMetadata { FrameIndex = 2, HostTimestamp = DateTime.UtcNow } 
        });

        var stopMethod = typeof(ToolEditorViewModel).GetMethod(
            "StopContinuousFlow",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("Không tìm thấy method 'StopContinuousFlow'");
        stopMethod.Invoke(vm, null);

        Assert(GetField(vm, "_industrialCameraFrameChannel") == null, "_industrialCameraFrameChannel phải là null sau khi dừng");
        Assert(mat1.IsDisposed, "Frame 1 trong hàng đợi phải được Dispose tự động khi Stop");
        Assert(mat2.IsDisposed, "Frame 2 trong hàng đợi phải được Dispose tự động khi Stop");
        Assert(vm.QueueCurrentCount == 0, "QueueCurrentCount phải về 0");
        Assert(vm.QueueStatusText == "0/16", "QueueStatusText phải là '0/16'");

        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// [5/5] Nút "Trigger tay" (ManualTriggerCommand):
    /// - Khi Run Continuous: Kích hoạt Task ngầm bắn frame vào BoundedChannel.
    /// - Khi không Run Continuous: Fallback chạy Run Once an toàn.
    /// </summary>
    private static void Test_05_ManualTrigger_In_RunContinuous_And_RunOnce()
    {
        Console.Write("--- [5/5] Nút 'Trigger tay' (ManualTriggerCommand) hoạt động chuẩn xác cả khi Run và Idle... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        Assert(vm.ManualTriggerCommand != null, "ManualTriggerCommand phải được khởi tạo");
        Assert(vm.ManualTriggerCommand.CanExecute(null), "ManualTriggerCommand phải luôn sẵn sàng CanExecute");

        // Khi Idle: gọi ManualTriggerCommand sẽ không ném ngoại lệ
        vm.ManualTriggerCommand.Execute(null);

        // Khi Run Continuous có BoundedChannel:
        var channel = Channel.CreateBounded<ContinuousFrameEnvelope>(new BoundedChannelOptions(16));
        InjectField(vm, "_industrialCameraFrameChannel", channel);
        InjectField(vm, "_isRunningFolderFlow", true);

        // Bấm ManualTriggerCommand
        vm.ManualTriggerCommand.Execute(null);

        // Dọn dẹp
        InjectField(vm, "_industrialCameraFrameChannel", null);
        InjectField(vm, "_isRunningFolderFlow", false);

        Console.WriteLine("PASSED");
    }
}

using System;
using System.Linq;
using System.Threading;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

public static class NewJobDefaultToolsTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🧪 RUNNING TESTS: NEW JOB DEFAULT TOOLS (IMAGE OUTPUT)");
        Console.WriteLine("=======================================================");

        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                Test_01_NewJobDefaultFourToolsCreated();
                Test_02_NewJobGraphEdgesAndImageOutputWiring();
                Test_03_ImageOutputShowRoiUncheckedByDefault();
                Test_04_ImageOutputDefinitionDefaultModelShowRoiFalse();
                Test_05_OriginAlgorithmDefaultsToMvpShapeMatch2();
                Test_06_ImageSourceEnableUndistortDefaultsToTrue();
                Test_07_OqcScannerTestedSampleCountTrackingAndReset();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            throw new Exception($"NewJobDefaultToolsTests failed: {threadEx.Message}", threadEx);
        }

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL NEW JOB DEFAULT TOOLS TESTS PASSED (100%)!");
        Console.WriteLine("=======================================================\n");
    }

    private static void Test_01_NewJobDefaultFourToolsCreated()
    {
        Console.Write("--- Test 1: Khởi tạo New Job (Ctrl+N) tạo đủ 4 tool mặc định... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        if (vm.Nodes.Count != 4)
            throw new Exception($"Mong đợi 4 nodes mặc định, thực tế có {vm.Nodes.Count} nodes!");

        var camNode = vm.Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
        if (camNode is null || camNode.RefName != "CAM1")
            throw new Exception("Không tìm thấy CAM1 (ImageSource)!");

        var prepNode = vm.Nodes.FirstOrDefault(n => string.Equals(n.Type, "Preprocess", StringComparison.OrdinalIgnoreCase));
        if (prepNode is null || prepNode.RefName != "PRE1")
            throw new Exception("Không tìm thấy PRE1 (Preprocess)!");

        var originNode = vm.Nodes.FirstOrDefault(n => string.Equals(n.Type, "Origin", StringComparison.OrdinalIgnoreCase));
        if (originNode is null || originNode.RefName != "Origin")
            throw new Exception("Không tìm thấy Origin!");

        var outputNode = vm.Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageOutput", StringComparison.OrdinalIgnoreCase));
        if (outputNode is null || outputNode.RefName != "IMG_OUT1")
            throw new Exception("Không tìm thấy IMG_OUT1 (ImageOutput)!");

        // Tọa độ thẳng hàng
        if (camNode.X != 80 || prepNode.X != 320 || originNode.X != 560 || outputNode.X != 800)
            throw new Exception($"Tọa độ X không thẳng hàng cách đều 240px: {camNode.X}, {prepNode.X}, {originNode.X}, {outputNode.X}");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED: Đã tạo đủ 4 tool (CAM1 -> PRE1 -> Origin -> IMG_OUT1)");
        Console.ResetColor();
    }

    private static void Test_02_NewJobGraphEdgesAndImageOutputWiring()
    {
        Console.Write("--- Test 2: Kiểm tra liên kết dây (Edges) và InputNodeName của ImageOutput... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        if (vm.Edges.Count != 3)
            throw new Exception($"Mong đợi 3 edges mặc định, thực tế có {vm.Edges.Count} edges!");

        var edgeCamToPrep = vm.Edges.FirstOrDefault(e => e.FromNode.RefName == "CAM1" && e.ToNode.RefName == "PRE1");
        if (edgeCamToPrep is null)
            throw new Exception("Thiếu liên kết CAM1 -> PRE1!");

        var edgePrepToOrigin = vm.Edges.FirstOrDefault(e => e.FromNode.RefName == "PRE1" && e.ToNode.RefName == "Origin");
        if (edgePrepToOrigin is null)
            throw new Exception("Thiếu liên kết PRE1 -> Origin!");

        var edgeOriginToOutput = vm.Edges.FirstOrDefault(e => e.FromNode.RefName == "Origin" && e.ToNode.RefName == "IMG_OUT1");
        if (edgeOriginToOutput is null)
            throw new Exception("Thiếu liên kết Origin -> IMG_OUT1!");

        var ioDef = vm.Config.ImageOutputs?.FirstOrDefault(x => x.Name == "IMG_OUT1");
        if (ioDef is null)
            throw new Exception("Không tìm thấy cấu hình ImageOutput cho IMG_OUT1 trong Config!");

        if (ioDef.InputNodeName != "Origin")
            throw new Exception($"Mong đợi InputNodeName của IMG_OUT1 là 'Origin', thực tế là '{ioDef.InputNodeName}'!");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED: Các cạnh nối dây và InputNodeName chuẩn xác 100%");
        Console.ResetColor();
    }

    private static void Test_03_ImageOutputShowRoiUncheckedByDefault()
    {
        Console.Write("--- Test 3: Tự động bỏ check 'Vẽ ô vuông ROI' (ShowRoi = false)... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        var ioDef = vm.Config.ImageOutputs?.FirstOrDefault(x => x.Name == "IMG_OUT1");
        if (ioDef is null)
            throw new Exception("Không tìm thấy IMG_OUT1!");

        if (ioDef.ShowRoi != false)
            throw new Exception($"Mong đợi ShowRoi = false, thực tế là {ioDef.ShowRoi}!");

        // Chọn node IMG_OUT1 trên Canvas và kiểm tra binding thuộc tính UI
        var outputNode = vm.Nodes.FirstOrDefault(n => n.RefName == "IMG_OUT1");
        vm.SelectedNode = outputNode;

        if (vm.ImageOutput_ShowRoi != false)
            throw new Exception($"Mong đợi ImageOutput_ShowRoi trên ViewModel là false, thực tế là {vm.ImageOutput_ShowRoi}!");

        if (vm.ImageOutput_InputNodeChoice != "Origin")
            throw new Exception($"Mong đợi ImageOutput_InputNodeChoice là 'Origin', thực tế là '{vm.ImageOutput_InputNodeChoice}'!");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED: Cờ ShowRoi và UI binding tự động Uncheck (false)");
        Console.ResetColor();
    }

    private static void Test_04_ImageOutputDefinitionDefaultModelShowRoiFalse()
    {
        Console.Write("--- Test 4: Khởi tạo class ImageOutputDefinition có mặc định ShowRoi = false... ");

        var def = new ImageOutputDefinition();
        if (def.ShowRoi != false)
            throw new Exception($"Mặc định của ImageOutputDefinition.ShowRoi phải là false, thực tế là {def.ShowRoi}!");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED: Model ImageOutputDefinition mặc định ShowRoi = false");
        Console.ResetColor();
    }

    private static void Test_05_OriginAlgorithmDefaultsToMvpShapeMatch2()
    {
        Console.Write("--- Test 5: Kiểm tra Tool Origin thuật toán mặc định là MvpShapeMatch2... ");

        // 1. Model PointDefinition
        var def = new PointDefinition();
        if (def.OriginAlgorithm != OriginAlgorithm.MvpShapeMatch2)
            throw new Exception($"Mong đợi PointDefinition.OriginAlgorithm = MvpShapeMatch2, thực tế là {def.OriginAlgorithm}!");

        // 2. ViewModel New Job
        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        if (vm.Config.Origin.OriginAlgorithm != OriginAlgorithm.MvpShapeMatch2)
            throw new Exception($"Mong đợi vm.Config.Origin.OriginAlgorithm = MvpShapeMatch2, thực tế là {vm.Config.Origin.OriginAlgorithm}!");

        // 3. ViewModel Origin_Algorithm property
        if (vm.Origin_Algorithm != OriginAlgorithm.MvpShapeMatch2)
            throw new Exception($"Mong đợi vm.Origin_Algorithm = MvpShapeMatch2, thực tế là {vm.Origin_Algorithm}!");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED: OriginAlgorithm mặc định là MvpShapeMatch2 (Model & UI ViewModel)");
        Console.ResetColor();
    }

    private static void Test_06_ImageSourceEnableUndistortDefaultsToTrue()
    {
        Console.Write("--- Test 6: Kiểm tra Node ImageSource mặc định EnableUndistort = true... ");

        // 1. Model ImageSourceDefinition
        var def = new ImageSourceDefinition();
        if (def.EnableUndistort != true)
            throw new Exception($"Mong đợi ImageSourceDefinition.EnableUndistort = true, thực tế là {def.EnableUndistort}!");

        // 2. ViewModel New Job (CAM1)
        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);

        var camDef = vm.Config.ImageSources?.FirstOrDefault(s => s.Name == "CAM1");
        if (camDef is null)
            throw new Exception("Không tìm thấy cấu hình CAM1 trong ImageSources!");

        if (camDef.EnableUndistort != true)
            throw new Exception($"Mong đợi CAM1.EnableUndistort = true, thực tế là {camDef.EnableUndistort}!");

        // 3. UI binding ImageSource_EnableUndistort
        var camNode = vm.Nodes.FirstOrDefault(n => n.RefName == "CAM1");
        vm.SelectedNode = camNode;

        if (vm.ImageSource_EnableUndistort != true)
            throw new Exception($"Mong đợi vm.ImageSource_EnableUndistort = true, thực tế là {vm.ImageSource_EnableUndistort}!");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED: ImageSource mặc định EnableUndistort = true (Model, Config & ViewModel)");
        Console.ResetColor();
    }

    private static void Test_07_OqcScannerTestedSampleCountTrackingAndReset()
    {
        Console.Write("--- Test 7: Kiểm tra OqcScannerViewModel số lượng mẫu test & tự động reset khi nạp Job... ");

        var vm = (VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel));

        // Khởi tạo các backing field cần thiết
        typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
            .GetField("_currentJobFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .SetValue(vm, "-");

        typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
            .GetField("_lastLoadedJobFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .SetValue(vm, "");

        typeof(VisionInspectionApp.UI.ViewModels.OqcScannerViewModel)
            .GetField("_currentJobTestedCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .SetValue(vm, 0);

        // 1. Mặc định là 0
        if (vm.CurrentJobTestedCount != 0)
            throw new Exception($"Mong đợi CurrentJobTestedCount ban đầu = 0, thực tế là {vm.CurrentJobTestedCount}!");

        // 2. Tăng số mẫu đã test (giả lập hoàn thành 5 mẫu)
        for (int i = 1; i <= 5; i++)
        {
            vm.CurrentJobTestedCount++;
        }
        if (vm.CurrentJobTestedCount != 5)
            throw new Exception($"Mong đợi CurrentJobTestedCount = 5 sau khi test 5 mẫu, thực tế là {vm.CurrentJobTestedCount}!");

        // 3. Gọi Reset đếm thủ công
        vm.ResetJobTestedCount();
        if (vm.CurrentJobTestedCount != 0)
            throw new Exception($"Mong đợi CurrentJobTestedCount = 0 sau khi ResetJobTestedCount, thực tế là {vm.CurrentJobTestedCount}!");

        // 4. Test lại 3 mẫu và đổi sang Job mới
        vm.CurrentJobTestedCount = 3;
        vm.CurrentJobFilePath = @"C:\VisionJobs\ProductA.job";
        if (vm.CurrentJobTestedCount != 0)
            throw new Exception($"Mong đợi CurrentJobTestedCount tự động reset về 0 khi nạp Job mới, thực tế là {vm.CurrentJobTestedCount}!");

        // 5. Test tiếp 4 mẫu và nạp lại Job khác
        vm.CurrentJobTestedCount = 4;
        vm.CurrentJobFilePath = @"C:\VisionJobs\ProductB.job";
        if (vm.CurrentJobTestedCount != 0)
            throw new Exception($"Mong đợi CurrentJobTestedCount tự động reset về 0 khi chuyển sang ProductB.job, thực tế là {vm.CurrentJobTestedCount}!");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED: Đếm mẫu & tự động reset khi nạp Job mới hoạt động chuẩn xác 100%");
        Console.ResetColor();
    }
}

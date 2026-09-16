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
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử hồi quy cho việc chọn Line ở Properties Panel của tool SegmentLineDistance
/// (cùng các tool dùng AvailableLineNames như LineLineDistance, PointLineDistance, Angle, EdgePair).
/// </summary>
public static class SegmentLineDistanceSelectionTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("📏 RUNNING SEGMENT LINE DISTANCE SELECTION TESTS");
        Console.WriteLine("=========================================================");

        RunInSta(() =>
        {
            Test_01_AddCaliperAndLine_ThenSegmentLineDistance_WpfComboBoxesPopulateAndSelect();
            Test_02_InitializeWithConfig_DefaultSelectedImageSource_StillPopulatesAvailableLineNames();
            Test_03_SelectingLinesInPropertiesPanel_CreatesAndReplacesCanvasEdges();
            Test_04_RenameAndDeleteCaliperOrLine_UpdatesAvailableLineNamesAndSelection();
            Test_05_MissingSegmentLineDistanceDef_AutoHealsOnSelectionAndSet();
            Test_06_SelectedRunValueAndPass_ReflectSegmentLineDistanceResult();
            Test_07_AllLineSourceTypes_IncludedInAvailableLineNames();
        });

        Console.WriteLine("✅ ALL 7 SEGMENT LINE DISTANCE SELECTION TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex is not null)
            throw new Exception($"SegmentLineDistanceSelectionTests failed: {ex.Message}", ex);
    }

    private sealed class ComboBoxTestHarness : IDisposable
    {
        public Window Window { get; }
        public StackPanel Panel { get; }
        public ComboBox ComboA { get; }
        public ComboBox ComboB { get; }

        public ComboBoxTestHarness(ToolEditorViewModel vm)
        {
            Panel = new StackPanel();
            Panel.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(ToolEditorViewModel.IsSegmentLineDistanceNode))
            {
                Converter = new BooleanToVisibilityConverter()
            });

            ComboA = new ComboBox();
            ComboA.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ToolEditorViewModel.AvailableLineNames)));
            ComboA.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(ToolEditorViewModel.SegmentLineDistance_LineA)) { Mode = BindingMode.TwoWay });

            ComboB = new ComboBox();
            ComboB.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ToolEditorViewModel.AvailableLineNames)));
            ComboB.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(ToolEditorViewModel.SegmentLineDistance_LineB)) { Mode = BindingMode.TwoWay });

            Panel.Children.Add(ComboA);
            Panel.Children.Add(ComboB);

            Window = new Window
            {
                DataContext = vm,
                Content = Panel,
                Width = 300,
                Height = 200,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None
            };
            Window.Show();
            PumpDispatcher();
        }

        public static void PumpDispatcher()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(_ =>
                {
                    frame.Continue = false;
                    return null;
                }),
                null);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        public void Dispose()
        {
            Window.Close();
        }
    }

    private static void Test_01_AddCaliperAndLine_ThenSegmentLineDistance_WpfComboBoxesPopulateAndSelect()
    {
        Console.Write("--- [1/7] Thêm Caliper + Line rồi SegmentLineDistance -> WPF ComboBox hiện đủ và chọn được... ");

        var vm = new ToolEditorViewModel();
        using var harness = new ComboBoxTestHarness(vm);
        var cbA = harness.ComboA;
        var cbB = harness.ComboB;

        vm.NewGraphCommand.Execute(null);
        vm.AddNode("Caliper", new Point(200, 200));
        vm.AddNode("Line", new Point(200, 320));
        vm.AddNode("SegmentLineDistance", new Point(480, 260));

        var sldNode = vm.Nodes.First(n => n.Type == "SegmentLineDistance");
        vm.SelectedNode = sldNode;
        ComboBoxTestHarness.PumpDispatcher();

        Assert(vm.IsSegmentLineDistanceNode, "Phải nhận diện đúng IsSegmentLineDistanceNode");
        Assert(harness.Panel.Visibility == Visibility.Visible, "Panel SegmentLineDistance phải chuyển sang Visible");
        Assert(vm.AvailableLineNames.Contains("CAL1"), "AvailableLineNames phải có CAL1");
        Assert(vm.AvailableLineNames.Contains("L1"), "AvailableLineNames phải có L1");
        Assert(cbA.Items.Contains("CAL1") && cbA.Items.Contains("L1"),
            $"ComboBox Segment (A) phải có CAL1 và L1, thực tế count={cbA.Items.Count}");
        Assert(cbB.Items.Contains("CAL1") && cbB.Items.Contains("L1"),
            $"ComboBox Infinite Line (B) phải có CAL1 và L1, thực tế count={cbB.Items.Count}");

        cbA.SelectedItem = "CAL1";
        cbB.SelectedItem = "L1";

        Assert(vm.SegmentLineDistance_LineA == "CAL1", $"LineA phải là CAL1, thực tế: {vm.SegmentLineDistance_LineA}");
        Assert(vm.SegmentLineDistance_LineB == "L1", $"LineB phải là L1, thực tế: {vm.SegmentLineDistance_LineB}");

        var sldDef = vm.Config!.SegmentLineDistances.First(x => x.Name == sldNode.RefName);
        Assert(sldDef.LineA == "CAL1" && sldDef.LineB == "L1", "Config SegmentLineDistance phải lưu CAL1 và L1");

        Console.WriteLine("PASSED");
    }

    private static void Test_02_InitializeWithConfig_DefaultSelectedImageSource_StillPopulatesAvailableLineNames()
    {
        Console.Write("--- [2/7] Mở job có sẵn ImageSource làm node đầu -> chọn SLD vẫn thấy Caliper & Line... ");

        var vm = new ToolEditorViewModel();
        using var harness = new ComboBoxTestHarness(vm);
        var cbA = harness.ComboA;
        var cbB = harness.ComboB;

        var config = new VisionConfig
        {
            ProductCode = "TEST_SLD",
            ToolGraph = new ToolGraph
            {
                Nodes = new List<ToolGraphNode>
                {
                    new() { Id = "n1", Type = "ImageSource", RefName = "CAM1", X = 80, Y = 100 },
                    new() { Id = "n2", Type = "Caliper", RefName = "CAL1", X = 300, Y = 100 },
                    new() { Id = "n3", Type = "Line", RefName = "L1", X = 300, Y = 220 },
                    new() { Id = "n4", Type = "SegmentLineDistance", RefName = "SLD1", X = 560, Y = 160 }
                }
            },
            Calipers = new List<CaliperDefinition> { new() { Name = "CAL1" } },
            Lines = new List<LineToolDefinition> { new() { Name = "L1" } },
            SegmentLineDistances = new List<SegmentLineDistance> { new() { Name = "SLD1", LineA = "cal1" } }
        };

        vm.InitializeWithConfig(config);
        Assert(vm.SelectedNode?.Type == "ImageSource", "Node đầu tiên khi mở job là ImageSource");

        vm.SelectedNode = vm.Nodes.First(n => n.RefName == "SLD1");
        ComboBoxTestHarness.PumpDispatcher();
        Assert(cbA.Items.Contains("CAL1") && cbA.Items.Contains("L1"),
            $"ComboBox sau InitializeWithConfig phải có CAL1 & L1, thực tế count={cbA.Items.Count}");
        Assert(Equals(cbA.SelectedItem, "CAL1"),
            $"SelectedItem phải khớp canonical 'CAL1' dù config lưu 'cal1', thực tế: {cbA.SelectedItem}");

        cbB.SelectedItem = "L1";
        Assert(vm.SegmentLineDistance_LineB == "L1", "Chọn LineB từ Properties Panel sau khi mở job phải thành công");

        Console.WriteLine("PASSED");
    }

    private static void Test_03_SelectingLinesInPropertiesPanel_CreatesAndReplacesCanvasEdges()
    {
        Console.Write("--- [3/7] Chọn/đổi line ở Properties Panel tự động tạo và thay thế dây trên Canvas... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);
        vm.AddNode("Caliper", new Point(200, 120));
        vm.AddNode("Caliper", new Point(200, 240));
        vm.AddNode("Line", new Point(200, 360));
        vm.AddNode("SegmentLineDistance", new Point(500, 240));

        var sldNode = vm.Nodes.First(n => n.Type == "SegmentLineDistance");
        vm.SelectedNode = sldNode;

        vm.SegmentLineDistance_LineA = "CAL1";
        vm.SegmentLineDistance_LineB = "CAL2";

        Assert(vm.Edges.Count(e => e.ToNodeId == sldNode.Id && e.ToPort == "L1" && e.FromNode.RefName == "CAL1") == 1,
            "Phải có 1 dây từ CAL1 -> SLD1(L1)");
        Assert(vm.Edges.Count(e => e.ToNodeId == sldNode.Id && e.ToPort == "L2" && e.FromNode.RefName == "CAL2") == 1,
            "Phải có 1 dây từ CAL2 -> SLD1(L2)");

        // Đổi Segment (A) sang L1 -> dây cũ tới L1 phải được thay thế, không nhân đôi
        vm.SegmentLineDistance_LineA = "L1";
        Assert(vm.Edges.Count(e => e.ToNodeId == sldNode.Id && e.ToPort == "L1") == 1,
            "Chỉ được tồn tại đúng 1 dây vào port L1 sau khi đổi selection");
        Assert(vm.Edges.Any(e => e.ToNodeId == sldNode.Id && e.ToPort == "L1" && e.FromNode.RefName == "L1"),
            "Dây mới vào L1 phải xuất phát từ L1");

        Console.WriteLine("PASSED");
    }

    private static void Test_04_RenameAndDeleteCaliperOrLine_UpdatesAvailableLineNamesAndSelection()
    {
        Console.Write("--- [4/7] Đổi tên / xóa Caliper hoặc Line cập nhật ngay AvailableLineNames... ");

        var vm = new ToolEditorViewModel();
        using var harness = new ComboBoxTestHarness(vm);
        var cbA = harness.ComboA;

        vm.NewGraphCommand.Execute(null);
        vm.AddNode("Caliper", new Point(200, 120));
        vm.AddNode("Line", new Point(200, 260));
        vm.AddNode("SegmentLineDistance", new Point(500, 190));

        var calNode = vm.Nodes.First(n => n.RefName == "CAL1");
        var lineNode = vm.Nodes.First(n => n.RefName == "L1");
        var sldNode = vm.Nodes.First(n => n.Type == "SegmentLineDistance");

        vm.SelectedNode = sldNode;
        ComboBoxTestHarness.PumpDispatcher();
        cbA.SelectedItem = "CAL1";
        Assert(vm.SegmentLineDistance_LineA == "CAL1", "Trước khi đổi tên LineA phải là CAL1");

        // Đổi tên Caliper node (đang là SelectedNode, không phải IsLineNode!)
        vm.SelectedNode = calNode;
        calNode.RefName = "EdgeTop";
        ComboBoxTestHarness.PumpDispatcher();

        Assert(cbA.Items.Contains("EdgeTop") && !cbA.Items.Contains("CAL1"),
            "Đổi tên Caliper phải cập nhật AvailableLineNames ngay lập tức");

        vm.SelectedNode = sldNode;
        ComboBoxTestHarness.PumpDispatcher();
        Assert(vm.SegmentLineDistance_LineA == "EdgeTop" && Equals(cbA.SelectedItem, "EdgeTop"),
            $"SegmentLineDistance_LineA phải tự cập nhật sang EdgeTop, thực tế: {vm.SegmentLineDistance_LineA}");

        // Xóa Line node
        vm.SelectedNode = lineNode;
        vm.DeleteSelectedNode();
        ComboBoxTestHarness.PumpDispatcher();
        Assert(!cbA.Items.Contains("L1") && cbA.Items.Contains("EdgeTop"),
            "Xóa Line node phải loại L1 khỏi AvailableLineNames");

        Console.WriteLine("PASSED");
    }

    private static void Test_05_MissingSegmentLineDistanceDef_AutoHealsOnSelectionAndSet()
    {
        Console.Write("--- [5/7] Node SegmentLineDistance thiếu definition trong config vẫn tự phục hồi và chọn được Line... ");

        var vm = new ToolEditorViewModel();
        var config = new VisionConfig
        {
            ProductCode = "LEGACY",
            ToolGraph = new ToolGraph
            {
                Nodes = new List<ToolGraphNode>
                {
                    new() { Id = "c1", Type = "Caliper", RefName = "CAL1", X = 100, Y = 100 },
                    new() { Id = "s1", Type = "SegmentLineDistance", RefName = "SLD1", X = 350, Y = 100 }
                }
            },
            Calipers = new List<CaliperDefinition> { new() { Name = "CAL1" } },
            SegmentLineDistances = new List<SegmentLineDistance>() // Cố tình rỗng
        };

        vm.InitializeWithConfig(config);
        vm.SelectedNode = vm.Nodes.First(n => n.RefName == "SLD1");
        vm.SegmentLineDistance_LineA = "CAL1";

        Assert(vm.SegmentLineDistance_LineA == "CAL1", "Phải chọn được CAL1 dù definition ban đầu thiếu");
        Assert(config.SegmentLineDistances.Any(x => x.Name == "SLD1" && x.LineA == "CAL1"),
            "Phải tự tạo và lưu definition SLD1 với LineA = CAL1");

        Console.WriteLine("PASSED");
    }

    private static void Test_06_SelectedRunValueAndPass_ReflectSegmentLineDistanceResult()
    {
        Console.Write("--- [6/7] SelectedRunValue và SelectedRunPass hiển thị kết quả của SegmentLineDistance... ");

        var vm = new ToolEditorViewModel();
        vm.NewGraphCommand.Execute(null);
        vm.AddNode("SegmentLineDistance", new Point(400, 200));
        var sldNode = vm.Nodes.First(n => n.Type == "SegmentLineDistance");
        vm.SelectedNode = sldNode;

        var run = new InspectionResult();
        run.SegmentLineDistances.Add(new SegmentDistanceResult(
            sldNode.RefName, "CAL1", "L1", 12.345, 12.0, 0.5, 0.5, true,
            new OpenCvSharp.Point2d(10, 20), new OpenCvSharp.Point2d(10, 32.345)));

        var lastRunField = typeof(ToolEditorViewModel).GetField("_lastRun", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(lastRunField is not null, "Không tìm thấy field _lastRun");
        lastRunField!.SetValue(vm, run);

        Assert(vm.SelectedRunValue.HasValue && Math.Abs(vm.SelectedRunValue.Value - 12.345) < 1e-6,
            $"SelectedRunValue phải là 12.345, thực tế: {vm.SelectedRunValue}");
        Assert(vm.SelectedRunPass == true, $"SelectedRunPass phải là true, thực tế: {vm.SelectedRunPass}");

        Console.WriteLine("PASSED");
    }

    private static void Test_07_AllLineSourceTypes_IncludedInAvailableLineNames()
    {
        Console.Write("--- [7/7] Tất cả nguồn Line (Line, Caliper, CreateLine, LinePairDetection, EdgePairDetect) đều có mặt... ");

        var vm = new ToolEditorViewModel();
        using var harness = new ComboBoxTestHarness(vm);
        var cbA = harness.ComboA;

        vm.NewGraphCommand.Execute(null);
        vm.AddNode("Line", new Point(100, 100));
        vm.AddNode("Caliper", new Point(100, 200));
        vm.AddNode("CreateLine", new Point(100, 300));
        vm.AddNode("LinePairDetection", new Point(100, 400));
        vm.AddNode("EdgePairDetect", new Point(100, 500));
        vm.AddNode("SegmentLineDistance", new Point(400, 250));

        vm.SelectedNode = vm.Nodes.First(n => n.Type == "SegmentLineDistance");
        ComboBoxTestHarness.PumpDispatcher();

        foreach (var expected in new[] { "L1", "CAL1", "CP_L1", "LPD1", "EPD1" })
        {
            Assert(cbA.Items.Contains(expected), $"AvailableLineNames thiếu '{expected}'");
        }

        Console.WriteLine("PASSED");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}

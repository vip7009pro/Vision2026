using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Kiểm thử tự động tính năng hiển thị chi tiết lỗi NG của CodeDetection trong Tool Editor & Inspection View:
/// 1. Khi CodeDetection không đọc được mã (Found = false, Pass = false):
///    - Cột kết quả hiển thị NG
///    - Banner chi tiết lỗi (NgReasonsText) hiển thị rõ tên tool và lý do "Không đọc được mã"
///    - Bảng SpecResults có dòng CodeDetection màu đỏ (Pass = False)
///    - Thẻ Đọc mã vạch / QR Debug tự động hiển thị với Badge [NG]
/// 2. Khi CodeDetection đọc được mã nhưng sai tiêu chuẩn Spec (Found = true, Pass = false):
///    - Banner chi tiết lỗi hiển thị rõ mã đọc được và chuỗi tiêu chuẩn mong đợi
///    - Bảng SpecResults có dòng CodeDetection hiển thị chuỗi đọc được (A) và chuỗi Spec (B), Pass = False
///    - Thẻ Đọc mã vạch / QR Debug hiển thị Badge [NG] và lý do "Không khớp Spec"
/// 3. Khi CodeDetection đọc đúng mã (Found = true, Pass = true):
///    - Hiển thị OK / PASS
///    - Bảng SpecResults có dòng CodeDetection Pass = True
/// 4. Thuộc tính SelectedRunPass của ToolEditorViewModel phản ánh đúng trạng thái PASS/NG khi chọn node CodeDetection.
/// 5. ConditionEvaluator đăng ký đúng Pass/Status/Expected theo kết quả kiểm tra.
/// 6. InspectionViewModel tích hợp đồng bộ CodeDetection vào SpecResults và danh sách lý do lỗi.
/// </summary>
public static class CodeDetectionResultTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("🔍 RUNNING CODEDETECTION RESULT & NG REPORTING TESTS");
        Console.WriteLine("=========================================================");

        Test_01_CodeDetection_NotFound_ShowsInSpecResults_And_NgReasons();
        Test_02_CodeDetection_SpecMismatch_ShowsInSpecResults_And_NgReasons();
        Test_03_CodeDetection_MatchSpec_ShowsPass();
        Test_04_SelectedRunPass_ReflectsCodeDetection();
        Test_05_ConditionEvaluator_CodeDetection_Pass_Status_Expected();
        Test_06_InspectionViewModel_CodeDetection_SpecResults_And_NgReasons();

        Console.WriteLine("✅ ALL 6 CODEDETECTION RESULT & NG REPORTING TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[ASSERT FAILED] {message}");
        }
    }

    private static void InvokePrivate(object obj, string methodName, params object?[] parameters)
    {
        var method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null)
            throw new MissingMethodException(obj.GetType().FullName, methodName);
        method.Invoke(obj, parameters);
    }

    private static void Test_01_CodeDetection_NotFound_ShowsInSpecResults_And_NgReasons()
    {
        Console.Write("--- [1/6] Kiểm tra CodeDetection không đọc được mã (Found = false) -> Báo NG và chi tiết tool... ");

        var vm = new ToolEditorViewModel();
        var res = new InspectionResult();
        res.Pass = false;
        res.CodeDetections.Add(new CodeDetectionResult(
            Name: "QR_MAIN",
            Found: false,
            Text: string.Empty,
            BoundingBox: default,
            Angle: 0.0,
            Pass: false,
            ExpectedSpec: "GH63-12345A"));

        // Gọi làm mới dashboard kết quả
        InvokePrivate(vm, "RefreshInspectionDashboard", res);

        Assert(vm.IsNg, "IsNg phải là true khi kết quả NG");
        Assert(vm.ResultStatusText == "NG", $"ResultStatusText phải là 'NG', thực tế: '{vm.ResultStatusText}'");
        Assert(!string.IsNullOrWhiteSpace(vm.NgReasonsText), "NgReasonsText không được để trống khi NG");
        Assert(vm.NgReasonsText.Contains("QR_MAIN"), $"NgReasonsText phải chứa tên tool 'QR_MAIN', thực tế: '{vm.NgReasonsText}'");
        Assert(vm.NgReasonsText.Contains("Không đọc được mã"), $"NgReasonsText phải chứa 'Không đọc được mã', thực tế: '{vm.NgReasonsText}'");

        // Kiểm tra Bảng SpecResults
        var specRow = vm.SpecResults.FirstOrDefault(x => x.Tool == "CodeDetection" && x.Name == "QR_MAIN");
        Assert(specRow != null, "SpecResults phải chứa dòng công cụ 'CodeDetection' tên 'QR_MAIN'");
        Assert(!specRow!.Pass, "Dòng CodeDetection trong SpecResults phải có Pass = false");
        Assert(specRow.RefA == "(Not Found)", $"RefA phải là '(Not Found)', thực tế: '{specRow.RefA}'");
        Assert(specRow.RefB == "GH63-12345A", $"RefB phải là Spec 'GH63-12345A', thực tế: '{specRow.RefB}'");

        // Kiểm tra CodeDetectionResults
        var cdRow = vm.CodeDetectionResults.FirstOrDefault(x => x.Name == "QR_MAIN");
        Assert(cdRow != null, "CodeDetectionResults phải chứa phần tử 'QR_MAIN'");
        Assert(!cdRow!.Pass, "cdRow.Pass phải là false");
        Assert(!cdRow.Found, "cdRow.Found phải là false");
        Assert(cdRow.StatusText == "NG", $"cdRow.StatusText phải là 'NG', thực tế: '{cdRow.StatusText}'");
        Assert(cdRow.DetailText.Contains("Không đọc được mã"), $"cdRow.DetailText phải chứa 'Không đọc được mã', thực tế: '{cdRow.DetailText}'");
        Assert(vm.ShowCodeDetectionSection, "ShowCodeDetectionSection phải là true khi có kết quả CodeDetection");

        Console.WriteLine("PASSED!");
    }

    private static void Test_02_CodeDetection_SpecMismatch_ShowsInSpecResults_And_NgReasons()
    {
        Console.Write("--- [2/6] Kiểm tra CodeDetection đọc được mã nhưng sai Spec -> Báo NG và chi tiết chuỗi... ");

        var vm = new ToolEditorViewModel();
        var res = new InspectionResult();
        res.Pass = false;
        res.CodeDetections.Add(new CodeDetectionResult(
            Name: "Barcode_SN",
            Found: true,
            Text: "WRONG-SN-9999",
            BoundingBox: new Rect(100, 100, 200, 80),
            Angle: 0.0,
            Pass: false,
            ExpectedSpec: "VALID-SN-0001"));

        InvokePrivate(vm, "RefreshInspectionDashboard", res);

        Assert(vm.IsNg, "IsNg phải là true khi sai Spec");
        Assert(vm.ResultStatusText == "NG", "ResultStatusText phải là 'NG'");
        Assert(vm.NgReasonsText.Contains("Barcode_SN"), $"NgReasonsText phải chứa 'Barcode_SN', thực tế: '{vm.NgReasonsText}'");
        Assert(vm.NgReasonsText.Contains("WRONG-SN-9999"), $"NgReasonsText phải chứa mã thực tế 'WRONG-SN-9999', thực tế: '{vm.NgReasonsText}'");
        Assert(vm.NgReasonsText.Contains("VALID-SN-0001"), $"NgReasonsText phải chứa tiêu chuẩn 'VALID-SN-0001', thực tế: '{vm.NgReasonsText}'");

        // Kiểm tra Bảng SpecResults
        var specRow = vm.SpecResults.FirstOrDefault(x => x.Tool == "CodeDetection" && x.Name == "Barcode_SN");
        Assert(specRow != null, "SpecResults phải chứa dòng 'Barcode_SN'");
        Assert(!specRow!.Pass, "Dòng Barcode_SN trong SpecResults phải có Pass = false");
        Assert(specRow.RefA == "WRONG-SN-9999", $"RefA phải là 'WRONG-SN-9999', thực tế: '{specRow.RefA}'");
        Assert(specRow.RefB == "VALID-SN-0001", $"RefB phải là 'VALID-SN-0001', thực tế: '{specRow.RefB}'");

        // Kiểm tra CodeDetectionResults
        var cdRow = vm.CodeDetectionResults.FirstOrDefault(x => x.Name == "Barcode_SN");
        Assert(cdRow != null, "CodeDetectionResults phải chứa phần tử 'Barcode_SN'");
        Assert(!cdRow!.Pass, "cdRow.Pass phải là false");
        Assert(cdRow.Found, "cdRow.Found phải là true vì đã đọc được chuỗi");
        Assert(cdRow.StatusText == "NG", "cdRow.StatusText phải là 'NG'");
        Assert(cdRow.DetailText.Contains("VALID-SN-0001"), $"DetailText phải thông báo sai lệch Spec, thực tế: '{cdRow.DetailText}'");

        Console.WriteLine("PASSED!");
    }

    private static void Test_03_CodeDetection_MatchSpec_ShowsPass()
    {
        Console.Write("--- [3/6] Kiểm tra CodeDetection đọc đúng mã và khớp Spec -> PASS/OK... ");

        var vm = new ToolEditorViewModel();
        var res = new InspectionResult();
        res.Pass = true;
        res.CodeDetections.Add(new CodeDetectionResult(
            Name: "QR_OQC",
            Found: true,
            Text: "GH68-58216B",
            BoundingBox: new Rect(50, 50, 100, 100),
            Angle: 0.0,
            Pass: true,
            ExpectedSpec: "GH68-58216B"));

        InvokePrivate(vm, "RefreshInspectionDashboard", res);

        Assert(!vm.IsNg, "IsNg phải là false khi PASS");
        Assert(vm.ResultStatusText == "OK", $"ResultStatusText phải là 'OK', thực tế: '{vm.ResultStatusText}'");
        Assert(string.IsNullOrEmpty(vm.NgReasonsText), "NgReasonsText phải rỗng khi PASS");

        var specRow = vm.SpecResults.FirstOrDefault(x => x.Tool == "CodeDetection" && x.Name == "QR_OQC");
        Assert(specRow != null, "SpecResults phải chứa dòng 'QR_OQC'");
        Assert(specRow!.Pass, "specRow.Pass phải là true");
        Assert(specRow.RefA == "GH68-58216B", "RefA phải là chuỗi đọc được");
        Assert(specRow.RefB == "GH68-58216B", "RefB phải là chuỗi Spec");

        var cdRow = vm.CodeDetectionResults.FirstOrDefault(x => x.Name == "QR_OQC");
        Assert(cdRow != null, "CodeDetectionResults phải chứa phần tử 'QR_OQC'");
        Assert(cdRow!.Pass, "cdRow.Pass phải là true");
        Assert(cdRow.StatusText == "PASS", "cdRow.StatusText phải là 'PASS'");

        Console.WriteLine("PASSED!");
    }

    private static void Test_04_SelectedRunPass_ReflectsCodeDetection()
    {
        Console.Write("--- [4/6] Kiểm tra SelectedRunPass phản ánh chính xác trạng thái của node CodeDetection... ");

        var vm = new ToolEditorViewModel();
        var node = new ToolGraphNodeViewModel
        {
            Id = "node_cd_1",
            Type = "CodeDetection",
            RefName = "QR_TEST"
        };
        vm.Nodes.Add(node);
        vm.SelectedNode = node;

        var resFail = new InspectionResult();
        resFail.Pass = false;
        resFail.CodeDetections.Add(new CodeDetectionResult("QR_TEST", Found: false, Text: "", BoundingBox: default, Angle: 0, Pass: false, ExpectedSpec: "SPEC1"));

        var lastRunField = typeof(ToolEditorViewModel).GetField("_lastRun", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(lastRunField != null, "Không tìm thấy trường _lastRun");
        lastRunField!.SetValue(vm, resFail);

        Assert(vm.SelectedRunPass == false, $"SelectedRunPass phải là false khi QR_TEST fail, thực tế: {vm.SelectedRunPass}");

        var resPass = new InspectionResult();
        resPass.Pass = true;
        resPass.CodeDetections.Add(new CodeDetectionResult("QR_TEST", Found: true, Text: "SPEC1", BoundingBox: default, Angle: 0, Pass: true, ExpectedSpec: "SPEC1"));
        lastRunField.SetValue(vm, resPass);

        Assert(vm.SelectedRunPass == true, $"SelectedRunPass phải là true khi QR_TEST pass, thực tế: {vm.SelectedRunPass}");

        Console.WriteLine("PASSED!");
    }

    private static void Test_05_ConditionEvaluator_CodeDetection_Pass_Status_Expected()
    {
        Console.Write("--- [5/6] Kiểm tra ConditionEvaluator nhận diện biến Pass/Status/Expected của CodeDetection... ");

        var resNg = new InspectionResult();
        resNg.Pass = false;
        resNg.CodeDetections.Add(new CodeDetectionResult(
            Name: "QR1",
            Found: true,
            Text: "WRONG_DATA",
            BoundingBox: default,
            Angle: 0.0,
            Pass: false,
            ExpectedSpec: "EXPECTED_DATA"));

        var config = new VisionConfig
        {
            CodeDetections = new List<CodeDetectionDefinition>
            {
                new CodeDetectionDefinition { Name = "QR1", ExpectedText = "EXPECTED_DATA" }
            }
        };

        var vars = ConditionEvaluator.BuildVariableMap(resNg, config);

        var passCond = ConditionEvaluator.Evaluate("QR1.Pass == false", vars);
        Assert(passCond, "Biểu thức 'QR1.Pass == false' phải trả về true khi QR1 bị NG");

        var statusCond = ConditionEvaluator.Evaluate("QR1.Status == \"NG\"", vars);
        Assert(statusCond, "Biểu thức 'QR1.Status == \"NG\"' phải trả về true khi QR1 bị NG");

        var expectedCond = ConditionEvaluator.Evaluate("QR1.Expected == \"EXPECTED_DATA\"", vars);
        Assert(expectedCond, "Biểu thức 'QR1.Expected == \"EXPECTED_DATA\"' phải trả về true");

        Console.WriteLine("PASSED!");
    }

    private static void Test_06_InspectionViewModel_CodeDetection_SpecResults_And_NgReasons()
    {
        Console.Write("--- [6/6] Kiểm tra InspectionViewModel đồng bộ CodeDetection vào SpecResults & NgReasons... ");

        var vm = (InspectionViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(InspectionViewModel));
        var specField = typeof(InspectionViewModel).GetField("<SpecResults>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        specField?.SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<InspectionViewModel.SpecResultRow>());

        var cdField = typeof(InspectionViewModel).GetField("<CodeDetectionResults>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        cdField?.SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<InspectionViewModel.CodeDetectionRow>());

        var scField = typeof(InspectionViewModel).GetField("<SurfaceCompareDebugItems>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        scField?.SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<InspectionViewModel.SurfaceCompareDebugPick>());

        var res = new InspectionResult();
        res.Pass = false;
        res.CodeDetections.Add(new CodeDetectionResult(
            Name: "QR_PROD",
            Found: false,
            Text: "",
            BoundingBox: default,
            Angle: 0.0,
            Pass: false,
            ExpectedSpec: "BATCH_2026"));

        var lastResultProp = typeof(InspectionViewModel).GetProperty("LastResult");
        Assert(lastResultProp != null, "Không tìm thấy thuộc tính LastResult trên InspectionViewModel");
        lastResultProp!.SetValue(vm, res);

        Assert(vm.IsNg, "InspectionViewModel.IsNg phải là true");
        Assert(vm.NgReasonsText.Contains("QR_PROD"), $"NgReasonsText phải chứa 'QR_PROD', thực tế: '{vm.NgReasonsText}'");
        Assert(vm.NgReasonsText.Contains("Không đọc được mã"), $"NgReasonsText phải chứa 'Không đọc được mã', thực tế: '{vm.NgReasonsText}'");

        var specRow = vm.SpecResults.FirstOrDefault(x => x.Tool == "CodeDetection" && x.Name == "QR_PROD");
        Assert(specRow != null, "InspectionViewModel.SpecResults phải có dòng 'QR_PROD'");
        Assert(!specRow!.Pass, "specRow.Pass phải là false");

        Console.WriteLine("PASSED!");
    }
}

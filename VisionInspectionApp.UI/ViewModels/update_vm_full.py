import re

path = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Mangled text replacements
content = content.replace("S? l?i: {r.Count}, S.L?n nh?t: {r.MaxArea:0}", "Số lỗi: {r.Count}, S.Lớn nhất: {r.MaxArea:0}")
content = content.replace("S? l?i: {sc.Count}, S.L?n nh?t: {sc.MaxArea:0}", "Số lỗi: {sc.Count}, S.Lớn nhất: {sc.MaxArea:0}")
content = content.replace("Nh?n l?n sau d? quay l?i ch? d? Live", "Nhấn lần sau để quay lại chế độ Live")
content = content.replace('Khng th? ch?p ?nh t? camera. Vui lng ki?m tra l?i k?t n?i camera trong tab Live Camera.", "L?i camera"', 'Không thể chụp ảnh từ camera. Vui lòng kiểm tra lại kết nối camera trong tab Live Camera.", "Lỗi camera"')
content = content.replace("L?i ch?p ?nh:", "Lỗi chụp ảnh:")
content = content.replace('", "L?i",', '", "Lỗi",')
content = content.replace("ANG TINH -> B?m nt d? QUAY L?I LIVE PREVIEW", "ĐANG TĨNH -> Bấm nút để QUAY LẠI LIVE PREVIEW")
content = content.replace("L?i load config:", "Lỗi load config:")

# 2. Remove DefectRoi and LinePairDetection
content = content.replace('            "CodeDetection",\n            "DefectRoi"\n', '            "CodeDetection"\n')
content = content.replace('            "CodeDetection",\r\n            "DefectRoi"\r\n', '            "CodeDetection"\r\n')
content = content.replace('            "LinePairDetection",\n', '')
content = content.replace('            "LinePairDetection",\r\n', '')

# 3. Add SurfaceCompare to InPorts
blob_port_pattern = r'(?s)else if \(string\.Equals\(Type, "BlobDetection", StringComparison\.OrdinalIgnoreCase\)\)\s*\{\s*InPorts\.Add\(new ToolGraphNodePortViewModel \{ Name = "Image" \}\);\s*\}'
surface_port_repl = r'\g<0>\n            else if (string.Equals(Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))\n            {\n                InPorts.Add(new ToolGraphNodePortViewModel { Name = "Image" });\n            }'
content = re.sub(blob_port_pattern, surface_port_repl, content)

# 4. Add SurfaceCompare to preview
blob_block = """            else if (string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                var def = SelectedBlobNodeDef();
                if (def is not null)
                {
                    using var processed = _preprocessor.Run(loadedMat, _config.Preprocess);
                    SelectedNodePreviewImage = processed.ToBitmapSource();
                }
                else
                {
                    SelectedNodePreviewImage = loadedMat.ToBitmapSource();
                }
            }"""
surface_compare_block = """
            else if (string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
            {
                SelectedNodePreviewImage = loadedMat.ToBitmapSource();
            }"""
if blob_block in content:
    content = content.replace(blob_block, blob_block + surface_compare_block)

# 5. Add StatusBarText property
prop_pattern = r'public sealed partial class ToolEditorViewModel : ObservableObject\s*\{'
prop_repl = 'public sealed partial class ToolEditorViewModel : ObservableObject\n    {\n        [ObservableProperty]\n        private string _statusBarText = "Ready.";\n'
content = re.sub(prop_pattern, prop_repl, content, count=1)

# 6. Update RunFlow with stopwatch and status bar
run_block_pattern = r"""
        try
        \{
            _lastRunError = null;
            _lastRun = _inspectionService\.Inspect\(snap, _config\);
        \}
        catch \(Exception ex\)
        \{
            _lastRun = null;
            _lastRunError = "L\?i khi ch\?y Flow: " \+ ex\.Message;
        \}

        RefreshPreviews\(\);
        RaiseToolPropertyPanelsChanged\(\);
"""

run_block_repl = """
        System.Diagnostics.Stopwatch swOverlay = null;
        try
        {
            _lastRunError = null;
            _lastRun = _inspectionService.Inspect(snap, _config);
        }
        catch (Exception ex)
        {
            _lastRun = null;
            _lastRunError = "Lỗi khi chạy Flow: " + ex.Message;
        }

        swOverlay = System.Diagnostics.Stopwatch.StartNew();
        RefreshPreviews();
        swOverlay.Stop();
        if (_lastRun != null)
        {
            StatusBarText = $"Tổng thời gian chạy: {_lastRun.Timings.TotalMs} ms (Xử lý) + {swOverlay.ElapsedMilliseconds} ms (Vẽ overlay)";
        }
        else 
        {
            StatusBarText = "Lỗi khi chạy Flow.";
        }
        
        RaiseToolPropertyPanelsChanged();
"""
content = re.sub(run_block_pattern, run_block_repl, content)

# 7. Node execution time
node_pattern = r'(public sealed partial class ToolGraphNodeViewModel : ObservableObject\s*\{)'
node_repl = r'\1\n    [ObservableProperty]\n    private int? _executionTimeMs;\n'
content = re.sub(node_pattern, node_repl, content, count=1)

set_node_time_pattern = r'node\.HasError = hasError;'
set_node_time_repl = r'node.HasError = hasError;\n            if (_lastRun != null && _lastRun.Timings.NodeTimings.TryGetValue(node.RefName, out var ms))\n                node.ExecutionTimeMs = ms;\n            else\n                node.ExecutionTimeMs = null;'
content = re.sub(set_node_time_pattern, set_node_time_repl, content, count=1)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

print("Update completed.")

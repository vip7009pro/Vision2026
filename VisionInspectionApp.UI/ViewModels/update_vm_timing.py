import re

path = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add StatusBarText property
prop_pattern = r'public sealed partial class ToolEditorViewModel : ObservableObject\s*\{'
prop_repl = 'public sealed partial class ToolEditorViewModel : ObservableObject\n    {\n        [ObservableProperty]\n        private string _statusBarText = "Ready.";\n'
content = re.sub(prop_pattern, prop_repl, content, count=1)

# 2. Update StatusBarText on run
run_pattern = r'(_lastRun = _inspectionService\.Inspect\(snap, _config\);)'
run_repl = r'var swOverlay = System.Diagnostics.Stopwatch.StartNew();\n            \1'
content = re.sub(run_pattern, run_repl, content, count=1)

# we need to be careful with RefreshPreviews(); RequestAutoSave();
# It appears after try-catch
after_run_pattern = r'(RefreshPreviews\(\);\s*)(RequestAutoSave\(\);\s*\})'
after_run_repl = r'\1swOverlay.Stop();\n            if (_lastRun != null) StatusBarText = $"Tổng thời gian chạy: {_lastRun.Timings.TotalMs} ms (Xử lý) + {swOverlay.ElapsedMilliseconds} ms (Vẽ overlay)";\n            \2'
content = re.sub(after_run_pattern, after_run_repl, content, count=1)

# 3. Node execution time
node_pattern = r'(public sealed partial class ToolGraphNodeViewModel : ObservableObject\s*\{)'
node_repl = r'\1\n    [ObservableProperty]\n    private int? _executionTimeMs;\n'
content = re.sub(node_pattern, node_repl, content, count=1)

set_node_time_pattern = r'(node\.HasError = hasError;)'
set_node_time_repl = r'\1\n            if (_lastRun != null && _lastRun.Timings.NodeTimings.TryGetValue(node.RefName, out var ms))\n                node.ExecutionTimeMs = ms;\n            else\n                node.ExecutionTimeMs = null;'
content = re.sub(set_node_time_pattern, set_node_time_repl, content, count=1)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

print("ViewModel update done.")

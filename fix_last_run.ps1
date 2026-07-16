 = Get-Content -Path 'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs' -Raw

 =  -replace 'private InspectionResult\? _lastRun;', "private InspectionResult? _lastRun;
    private string? _lastRunError;"

 =  -replace 'SelectedNode = null;\s*_config = null;\s*_lastRun = null;', "SelectedNode = null;
        _config = null;
        _lastRun = null;
        _lastRunError = null;"

 =  -replace 'if \(snap is null \|\| _config is null\)\s*\{\s*_lastRun = null;', "if (snap is null || _config is null)
        {
            _lastRun = null;
            _lastRunError = "Không có ?nh ho?c config hi?n t?i.";"

 =  -replace 'if \(\(graphNeedsOrigin && !originOk\) \|\| \(graphNeedsPoint && anyPointNeedsTemplate\)\)\s*\{\s*_lastRun = null;', "if ((graphNeedsOrigin && !originOk) || (graphNeedsPoint && anyPointNeedsTemplate))
        {
            _lastRun = null;
            _lastRunError = "Thi?u ?nh Template cho Origin ho?c Point. Vui lòng thi?t l?p Template tru?c khi ch?y lu?ng.";"

 =  -replace 'try\s*\{\s*_lastRun = _inspectionService\.Inspect\(snap, _config\);\s*\}\s*catch\s*\{\s*_lastRun = null;\s*\}', "try
        {
            _lastRunError = null;
            _lastRun = _inspectionService.Inspect(snap, _config);
        }
        catch (Exception ex)
        {
            _lastRun = null;
            _lastRunError = "L?i ch?y lu?ng (Inspect): " + ex.Message;
        }"

 =  -replace 'SelectedNode = Nodes\.Count > 0 \? Nodes\[Math\.Clamp\(idx, 0, Nodes\.Count - 1\)\] : null;\s*_lastRun = null;', "SelectedNode = Nodes.Count > 0 ? Nodes[Math.Clamp(idx, 0, Nodes.Count - 1)] : null;

        _lastRun = null;
        _lastRunError = null;"

 =  -replace 'if \(_lastRun is null\)\s*\{\s*System\.Windows\.MessageBox\.Show\("Vui lòng ch?y lu?ng \(Run Flow\) tru?c khi xem giá tr? Output\.", "Không có d? li?u", System\.Windows\.MessageBoxButton\.OK, System\.Windows\.MessageBoxImage\.Information\);\s*return;\s*\}', "if (_lastRun is null)
        {
            var msg = _lastRunError ?? "Vui lòng ch?y lu?ng (Run Flow) tru?c khi xem giá tr? Output.";
            System.Windows.MessageBox.Show(msg, "Không có d? li?u (Ho?c l?i)", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }"

Set-Content -Path 'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs' -Value 

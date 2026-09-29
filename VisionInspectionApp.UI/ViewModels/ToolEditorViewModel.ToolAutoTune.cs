using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        /// <summary>True khi node đang chọn là công cụ nhận diện hỗ trợ Auto Tune preprocess.</summary>
        public bool IsAutoTuneCapable =>
            SelectedNode is not null &&
            (IsLineNode || IsCaliperNode || IsEdgePairDetectNode || IsCircleFinderNode || IsCodeDetectionNode);

        [ObservableProperty]
        private string _autoTuneStatusText = "Chọn công cụ nhận diện (Caliper, Line, EdgePairDetect, Circle Finder, CodeDetection) để Auto Tune thông số Preprocess.";

        [ObservableProperty]
        private string _autoTuneParamText = string.Empty;

        [ObservableProperty]
        private string _autoTuneBestScoreText = string.Empty;

        [ObservableProperty]
        private string _autoTuneStepText = string.Empty;

        [ObservableProperty]
        private string _autoTunePreprocessName = "(chưa nối)";

        [ObservableProperty]
        private bool _autoTuneIsRunning;

        /// <summary>Cho phép bấm Auto Tune khi chưa chạy.</summary>
        public bool AutoTuneCanRun => !AutoTuneIsRunning;

        partial void OnAutoTuneIsRunningChanged(bool value) => OnPropertyChanged(nameof(AutoTuneCanRun));

        private CancellationTokenSource? _autoTuneCts;

        /// <summary>Cập nhật lại trạng thái panel Auto Tune mỗi khi đổi node đang chọn.</summary>
        private void RefreshAutoTunePanelState()
        {
            OnPropertyChanged(nameof(IsAutoTuneCapable));

            // Không ghi đè thông tin tiến trình khi Auto Tune đang chạy.
            if (AutoTuneIsRunning)
            {
                return;
            }

            if (!IsAutoTuneCapable)
            {
                AutoTunePreprocessName = "(chưa nối)";
                AutoTuneStatusText = "Chọn công cụ nhận diện (Caliper, Line, EdgePairDetect, Circle Finder, CodeDetection) để Auto Tune thông số Preprocess.";
                return;
            }

            var preNode = FindParentPreprocessNode();
            AutoTunePreprocessName = preNode is not null ? preNode.RefName : "(chưa nối)";
            AutoTuneStatusText = preNode is not null
                ? $"Preprocess nguồn: {preNode.RefName}. Bấm Auto Tune để tìm thông số tối ưu."
                : $"Công cụ {SelectedNode!.RefName} chưa được nối với tool Preprocess. Hãy kéo Preprocess vào đầu vào Image của công cụ.";
        }

        /// <summary>Tìm node Preprocess đang nối vào đầu vào Image của node đang chọn.</summary>
        private ToolGraphNodeViewModel? FindParentPreprocessNode()
        {
            if (SelectedNode is null)
            {
                return null;
            }

            var edge = Edges.FirstOrDefault(e =>
                string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.ToPort, "Preprocess", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase)));

            if (edge is not null)
            {
                var from = Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.FromNodeId, StringComparison.OrdinalIgnoreCase));
                if (from is not null && string.Equals(from.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    return from;
                }
            }

            // Fallback: tìm theo ComboBox chọn Preprocess của tool.
            var choice = SelectedToolPreprocessChoice;
            if (!string.IsNullOrWhiteSpace(choice) && !string.Equals(choice, DefaultPreprocessChoice, StringComparison.OrdinalIgnoreCase))
            {
                return Nodes.FirstOrDefault(n => string.Equals(n.Type, "Preprocess", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, choice, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private async Task AutoTunePreprocessAsync()
        {
            if (AutoTuneIsRunning)
            {
                return;
            }

            if (_config is null || SelectedNode is null || !IsAutoTuneCapable)
            {
                AutoTuneStatusText = "⚠️ Hãy chọn một công cụ nhận diện (Caliper, Line, EdgePairDetect, Circle Finder, CodeDetection).";
                return;
            }

            var kind = PreprocessAutoTuner.ResolveKind(SelectedNode.Type);
            var preNode = FindParentPreprocessNode();
            if (preNode is null)
            {
                AutoTuneStatusText = $"⚠️ Công cụ {SelectedNode.RefName} chưa được nối với tool Preprocess. Hãy kéo Preprocess vào đầu vào Image của công cụ.";
                AutoTunePreprocessName = "(chưa nối)";
                return;
            }

            if (!TryResolveAutoTuneToolDefinitions(kind, out var cal, out var line, out var epd, out var circle, out var code))
            {
                AutoTuneStatusText = "⚠️ Không tìm thấy định nghĩa công cụ để Auto Tune (hoặc ROI chưa được vẽ).";
                return;
            }

            // Tìm/tạo định nghĩa Preprocess cha.
            var preDef = _config.PreprocessNodes.FirstOrDefault(x => string.Equals(x.Name, preNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (preDef is null)
            {
                preDef = new PreprocessNodeDefinition { Name = string.IsNullOrWhiteSpace(preNode.RefName) ? "PRE1" : preNode.RefName, Settings = new PreprocessSettings() };
                _config.PreprocessNodes.Add(preDef);
            }

            var baseline = PreprocessAutoTuner.CloneSettings(preDef.Settings);

            AutoTunePreprocessName = preNode.RefName;
            AutoTuneIsRunning = true;
            AutoTuneStepText = string.Empty;
            AutoTuneParamText = string.Empty;
            AutoTuneBestScoreText = string.Empty;
            AutoTuneStatusText = "⏳ Đang chuẩn bị Auto Tune...";

            _autoTuneCts?.Cancel();
            var cts = new CancellationTokenSource();
            _autoTuneCts = cts;

            var progress = new Progress<AutoTuneProgress>(p =>
            {
                AutoTuneStepText = $"{p.Step}/{p.Total}";
                AutoTuneParamText = p.ParameterText;
                AutoTuneBestScoreText = $"Điểm cao nhất: {p.BestScore:0.00}";
                AutoTuneStatusText = p.Message;
            });

            try
            {
                using var snap = _sharedImage.GetSnapshot();
                if (snap is null || snap.IsDisposed || snap.Empty())
                {
                    AutoTuneStatusText = "⚠️ Chưa có ảnh để Auto Tune. Hãy nạp/chụp ảnh trước.";
                    return;
                }

                using var baseImage = GetNodeInputImageForPreview(snap, preNode, "In");
                if (baseImage is null || baseImage.Empty())
                {
                    AutoTuneStatusText = "⚠️ Ảnh đầu vào của Preprocess rỗng.";
                    return;
                }

                GetOriginPose(out var originTeach, out var originFound, out var originAngleDeg);
                var rois = preDef.Rois;

                var token = cts.Token;
                var outcome = await Task.Run(() => PreprocessAutoTuner.Tune(
                    baseImage, kind, cal, line, epd, circle, code,
                    baseline, rois, originTeach, originFound, originAngleDeg,
                    _preprocessor, _lineDetector, progress, token), token);

                if (outcome.Success)
                {
                    PreprocessAutoTuner.ApplySettings(preDef.Settings, outcome.BestSettings);
                    AutoTuneBestScoreText = $"Điểm cao nhất: {outcome.BestScore:0.00}";
                    AutoTuneStatusText = "✅ " + outcome.Summary + " Đã áp dụng thông số tốt nhất cho Preprocess " + preNode.RefName + ".";
                    SyncSelectedToolPreprocessChoiceFromGraph();
                    RaiseToolPropertyPanelsChanged();
                    RefreshPreviewsNow();
                    RequestAutoSave();
                }
                else
                {
                    AutoTuneStatusText = "⚠️ " + outcome.Summary;
                }
            }
            catch (OperationCanceledException)
            {
                AutoTuneStatusText = "Đã hủy Auto Tune.";
            }
            catch (Exception ex)
            {
                AutoTuneStatusText = "❌ Lỗi Auto Tune: " + ex.Message;
            }
            finally
            {
                AutoTuneIsRunning = false;
                if (ReferenceEquals(_autoTuneCts, cts))
                {
                    _autoTuneCts = null;
                }
                cts.Dispose();
            }
        }

        private void CancelAutoTune() => _autoTuneCts?.Cancel();

        private bool TryResolveAutoTuneToolDefinitions(
            AutoTuneToolKind kind,
            out CaliperDefinition? cal,
            out LineToolDefinition? line,
            out EdgePairDetectDefinition? epd,
            out CircleFinderDefinition? circle,
            out CodeDetectionDefinition? code)
        {
            cal = null;
            line = null;
            epd = null;
            circle = null;
            code = null;

            if (_config is null || SelectedNode is null)
            {
                return false;
            }

            var name = SelectedNode.RefName ?? string.Empty;
            switch (kind)
            {
                case AutoTuneToolKind.Caliper:
                    cal = _config.Calipers?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    return cal is not null && cal.SearchRoi.Width > 0 && cal.SearchRoi.Height > 0;

                case AutoTuneToolKind.Line:
                    line = _config.Lines?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    return line is not null && line.SearchRoi.Width > 0 && line.SearchRoi.Height > 0;

                case AutoTuneToolKind.EdgePairDetect:
                    epd = _config.EdgePairDetections?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    return epd is not null && epd.SearchRoi.Width > 0 && epd.SearchRoi.Height > 0;

                case AutoTuneToolKind.CircleFinder:
                    circle = _config.CircleFinders?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    return circle is not null && circle.SearchRoi.Width > 0 && circle.SearchRoi.Height > 0;

                case AutoTuneToolKind.CodeDetection:
                    code = _config.CodeDetections?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    return code is not null && code.SearchRoi.Width > 0 && code.SearchRoi.Height > 0;

                default:
                    return false;
            }
        }
    }
}

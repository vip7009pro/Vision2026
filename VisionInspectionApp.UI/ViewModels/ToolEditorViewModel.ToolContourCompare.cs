using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        public IEnumerable<ContourMatchMethod> AvailableContourMatchMethods => Enum.GetValues<ContourMatchMethod>();

        public ICommand ContourCompare_SetSearchRoiCommand { get; }
        public ICommand ContourCompare_SetTemplateRoiCommand { get; }

        private void ContourCompare_SetSearchRoi()
        {
            if (SelectedNode is null || !string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
                return;
            ActiveRoiLabel = $"{SelectedNode.RefName} CC";
        }

        private void ContourCompare_SetTemplateRoi()
        {
            if (SelectedNode is null || !string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
                return;
            ActiveRoiLabel = $"{SelectedNode.RefName} CCT";
        }

        private ContourCompareDefinition? SelectedContourCompareDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }

        public ContourMatchMethod ContourCompare_MatchMethod
        {
            get => SelectedContourCompareDef()?.MatchMethod ?? ContourMatchMethod.HuMoments;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                if (def.MatchMethod == value) return;
                def.MatchMethod = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_CannyThreshold1
        {
            get => SelectedContourCompareDef()?.CannyThreshold1 ?? 50;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 1.0, 500.0);
                if (Math.Abs(def.CannyThreshold1 - v) < 1e-4) return;
                def.CannyThreshold1 = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_CannyThreshold2
        {
            get => SelectedContourCompareDef()?.CannyThreshold2 ?? 150;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 1.0, 500.0);
                if (Math.Abs(def.CannyThreshold2 - v) < 1e-4) return;
                def.CannyThreshold2 = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public int ContourCompare_MinContourArea
        {
            get => SelectedContourCompareDef()?.MinContourArea ?? 50;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Max(1, value);
                if (def.MinContourArea == v) return;
                def.MinContourArea = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_MaxShapeMatchScore
        {
            get => SelectedContourCompareDef()?.MaxShapeMatchScore ?? 0.10;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.001, 10.0);
                if (Math.Abs(def.MaxShapeMatchScore - v) < 1e-4) return;
                def.MaxShapeMatchScore = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_MaxHausdorffDistPx
        {
            get => SelectedContourCompareDef()?.MaxHausdorffDistPx ?? 5.0;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.1, 500.0);
                if (Math.Abs(def.MaxHausdorffDistPx - v) < 1e-4) return;
                def.MaxHausdorffDistPx = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_MaxAreaDiffPercent
        {
            get => SelectedContourCompareDef()?.MaxAreaDiffPercent ?? 5.0;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.1, 100.0);
                if (Math.Abs(def.MaxAreaDiffPercent - v) < 1e-4) return;
                def.MaxAreaDiffPercent = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double? ContourCompare_LastRunScore
        {
            get
            {
                if (_lastRun is null || SelectedNode is null) return null;
                if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase)) return null;
                var r = _lastRun.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.MatchScore;
            }
        }

        public double? ContourCompare_LastRunMaxDist
        {
            get
            {
                if (_lastRun is null || SelectedNode is null) return null;
                if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase)) return null;
                var r = _lastRun.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.MaxDistancePx;
            }
        }
    }
}

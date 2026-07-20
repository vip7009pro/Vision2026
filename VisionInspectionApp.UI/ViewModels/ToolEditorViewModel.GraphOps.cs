using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.VisionEngine;
namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        public ObservableCollection<ToolGraphNodeViewModel> SelectedNodes { get; } = new();
    
        partial void OnSelectedNodeChanged(ToolGraphNodeViewModel? value)
        {
            SelectedEdge = null;
            if (_selectedNodeHook is not null)
            {
                _selectedNodeHook.PropertyChanged -= SelectedNode_PropertyChanged;
            }
    
            _selectedNodeHook = value;
            if (_selectedNodeHook is not null)
            {
                _selectedNodeHook.PropertyChanged += SelectedNode_PropertyChanged;
            }
    
            _selectedNodePrevRefName = value?.RefName;
            if (value is null)
            {
                ClearNodeSelection();
            }
            else
            {
                // If selection is empty or this node isn't part of multi-selection, treat it as a single-select.
                if (SelectedNodes.Count == 0 || !value.IsSelected)
                {
                    SetSingleNodeSelection(value);
                }
                else if (!SelectedNodes.Contains(value))
                {
                    // Safety: keep SelectedNodes consistent.
                    SelectedNodes.Add(value);
                }
            }
    
            SyncPreprocessChoices();
            SyncSelectedToolPreprocessChoiceFromGraph();
            SyncTextNodeConditionRows();
            RaiseToolPropertyPanelsChanged();
            RefreshSelectedPreview();
            OnPropertyChanged(nameof(Blob_LastRunCount));
        }
    
        public void ClearNodeSelection()
        {
            foreach (var n in SelectedNodes)
            {
                n.IsSelected = false;
            }
    
            SelectedNodes.Clear();
        }
    
        public void SetSingleNodeSelection(ToolGraphNodeViewModel node)
        {
            if (node is null)
            {
                return;
            }
    
            ClearNodeSelection();
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }
    
        public void ToggleNodeSelection(ToolGraphNodeViewModel node)
        {
            if (node is null)
            {
                return;
            }
    
            if (node.IsSelected)
            {
                node.IsSelected = false;
                SelectedNodes.Remove(node);
                if (ReferenceEquals(SelectedNode, node))
                {
                    SelectedNode = SelectedNodes.Count > 0 ? SelectedNodes[0] : null;
                }
            }
            else
            {
                node.IsSelected = true;
                SelectedNodes.Add(node);
                // Keep the first-selected node as the primary for properties/preview.
                if (SelectedNode is null)
                {
                    SelectedNode = node;
                }
            }
        }
    
        [ObservableProperty]
        private List<OverlayItem> _selectedNodeOverlayItems = new();
        public ICommand DeleteSelectedNodeCommand { get; }
        public ICommand DeleteSelectedEdgeCommand { get; }
    
        public void SelectEdge(ToolGraphEdgeViewModel? edge)
        {
            SelectedNode = null;
            SelectedEdge = edge;
        }
    
        private void DeleteSelectedEdge()
        {
            if (SelectedEdge is null)
            {
                return;
            }
    
            ClearToolInputByEdge(SelectedEdge);
            Edges.Remove(SelectedEdge);
            SelectedEdge = null;
            SyncEdgesToConfig();
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    
        public void DeleteEdge(ToolGraphEdgeViewModel edge)
        {
            if (edge is null)
            {
                return;
            }
    
            ClearToolInputByEdge(edge);
            Edges.Remove(edge);
            SelectedEdge = null;
            SyncEdgesToConfig();
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    
        private PreprocessNodeDefinition? SelectedPreprocessNodeDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.PreprocessNodes.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        private void RemoveEdgesToSelectedNodePortsBeyondConditionCount(int inputCount)
        {
            if (SelectedNode is null)
                return;
            for (var i = Edges.Count - 1; i >= 0; i--)
            {
                var e = Edges[i];
                if (!string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
    
                if (e.ToPort.StartsWith("In", StringComparison.OrdinalIgnoreCase) && int.TryParse(e.ToPort.Substring(2), out var idx) && idx > inputCount)
                {
                    Edges.RemoveAt(i);
                }
            }
        }
    
        private void RemoveEdgesToSelectedNodePort(string toPort)
        {
            if (SelectedNode is null)
                return;
            for (int i = Edges.Count - 1; i >= 0; i--)
            {
                var e = Edges[i];
                if (string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(e.ToPort, toPort, StringComparison.OrdinalIgnoreCase))
                {
                    Edges.RemoveAt(i);
                }
            }
        }
    
        private void SelectedNode_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ToolGraphNodeViewModel.RefName) or nameof(ToolGraphNodeViewModel.Type))
            {
                if (e.PropertyName is nameof(ToolGraphNodeViewModel.RefName))
                {
                    RenameSelectedDefinitionIfNeeded();
                }
    
                RefreshPreviews();
                RaiseToolPropertyPanelsChanged();
            }
        }
    
        private void DeleteSelectedNode()
        {
            if (SelectedNode is null)
            {
                return;
            }
    
            var toRemove = SelectedNode;
            var idx = Nodes.IndexOf(toRemove);
            if (idx < 0)
            {
                return;
            }
    
            if (_config is not null && !string.IsNullOrWhiteSpace(toRemove.RefName))
            {
                if (string.Equals(toRemove.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    _config.PreprocessNodes.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Point", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Points.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Line", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Lines.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Calipers.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Distance", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Distances.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
                {
                    _config.LineToLineDistances.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
                {
                    _config.PointToLineDistances.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Angle", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Angles.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Condition", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Conditions.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                {
                    _config.ImageSources.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Text", StringComparison.OrdinalIgnoreCase))
                {
                    _config.TextNodes.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
                {
                    _config.BlobDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
                {
                    _config.SurfaceCompares.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
                {
                    _config.LinePairDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
                {
                    _config.EdgePairDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
                {
                    _config.CircleFinders.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Diameters.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
                {
                    _config.EdgePairs.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
    
                if (string.Equals(toRemove.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
                {
                    _config.CodeDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
                }
            }
    
            Nodes.RemoveAt(idx);
            toRemove.PropertyChanged -= Node_PropertyChanged;
            for (var i = Edges.Count - 1; i >= 0; i--)
            {
                var e = Edges[i];
                if (string.Equals(e.FromNodeId, toRemove.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToNodeId, toRemove.Id, StringComparison.OrdinalIgnoreCase))
                {
                    Edges.RemoveAt(i);
                }
            }
    
            SelectedNode = Nodes.Count > 0 ? Nodes[Math.Clamp(idx, 0, Nodes.Count - 1)] : null;
            _lastRun = null;
            _lastRunError = null;
            SyncToolGraphToConfig();
            RunFlow();
            RequestAutoSave();
        }
    
        public void AddNode(string type, System.Windows.Point canvasPosition)
        {
            _config ??= new VisionConfig
            {
                ProductCode = ProductCode
            };
            _config.ToolGraph ??= new ToolGraph();
            var node = new ToolGraphNodeViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = type,
                RefName = string.Empty,
                X = canvasPosition.X,
                Y = canvasPosition.Y
            };
            node.PropertyChanged += Node_PropertyChanged;
            Nodes.Add(node);
            // Ensure there is a backing definition in config so ROI overlays exist and can be taught immediately.
            EnsureDefinitionForNewNode(node);
            SelectedNode = node;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
        }
    
        public void CreateEdge(ToolGraphNodeViewModel fromNode, ToolGraphNodeViewModel toNode, string fromPort = "Out", string toPort = "In")
        {
            if (fromNode is null || toNode is null)
            {
                return;
            }
    
            if (ReferenceEquals(fromNode, toNode))
            {
                return;
            }
    
            // Image-processing nodes accept exactly one source image.  This also makes
            // Properties Panel selection and direct canvas wiring deterministically use
            // the same edge.
            if (string.Equals(toPort, "Image", StringComparison.OrdinalIgnoreCase) || string.Equals(toPort, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = Edges.Count - 1; i >= 0; i--)
                {
                    var e = Edges[i];
                    if (string.Equals(e.ToNodeId, toNode.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(e.ToPort, toPort, StringComparison.OrdinalIgnoreCase))
                    {
                        Edges.RemoveAt(i);
                    }
                }
            }
    
            var existed = Edges.Any(x => string.Equals(x.FromNodeId, fromNode.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ToNodeId, toNode.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(x.FromPort, fromPort, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ToPort, toPort, StringComparison.OrdinalIgnoreCase));
            if (existed)
            {
                return;
            }
    
            Edges.Add(new ToolGraphEdgeViewModel(fromNode, toNode, fromPort, toPort));
            SyncEdgesToConfig();
            // Auto-fill tool inputs based on graph wiring (VisionPro-like).
            if (_config is null)
            {
                return;
            }
    
            if (string.Equals(toNode.Type, "Distance", StringComparison.OrdinalIgnoreCase) && (string.Equals(fromNode.Type, "Point", StringComparison.OrdinalIgnoreCase) || string.Equals(fromNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(fromNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase)))
            {
                var def = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                {
                    if (string.Equals(toPort, "P1", StringComparison.OrdinalIgnoreCase))
                        def.PointA = fromNode.RefName;
                    else if (string.Equals(toPort, "P2", StringComparison.OrdinalIgnoreCase))
                        def.PointB = fromNode.RefName;
                }
            }
            else if (string.Equals(toNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase) && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
            {
                var def = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                {
                    if (string.Equals(toPort, "L1", StringComparison.OrdinalIgnoreCase))
                        def.LineA = fromNode.RefName;
                    else if (string.Equals(toPort, "L2", StringComparison.OrdinalIgnoreCase))
                        def.LineB = fromNode.RefName;
                }
            }
            else if (string.Equals(toNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                {
                    if (string.Equals(toPort, "P1", StringComparison.OrdinalIgnoreCase) && string.Equals(fromNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
                    {
                        def.Point = fromNode.RefName;
                    }
                    else if (string.Equals(toPort, "L1", StringComparison.OrdinalIgnoreCase) && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
                    {
                        def.Line = fromNode.RefName;
                    }
                }
            }
            else if (string.Equals(toNode.Type, "Angle", StringComparison.OrdinalIgnoreCase) && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
            {
                var def = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                {
                    if (string.Equals(toPort, "L1", StringComparison.OrdinalIgnoreCase))
                        def.LineA = fromNode.RefName;
                    else if (string.Equals(toPort, "L2", StringComparison.OrdinalIgnoreCase))
                        def.LineB = fromNode.RefName;
                }
            }
            else if (string.Equals(toNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase) && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
            {
                var def = _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                {
                    if (string.Equals(toPort, "E1", StringComparison.OrdinalIgnoreCase))
                        def.RefA = fromNode.RefName;
                    else if (string.Equals(toPort, "E2", StringComparison.OrdinalIgnoreCase))
                        def.RefB = fromNode.RefName;
                }
            }
            else if (string.Equals(toNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase) && string.Equals(fromNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                {
                    if (string.Equals(toPort, "C1", StringComparison.OrdinalIgnoreCase))
                        def.CircleRef = fromNode.RefName;
                }
            }
    
            if (!_syncingInputs)
            {
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
            }
        }
    
        private void AddConfigRoisForNode(ToolGraphNodeViewModel node, List<OverlayItem> dst)
        {
            if (_config is null)
            {
                return;
            }
    
            var config = _config;
            var showRois = ShowRoisInSelectedPreview;
            void AddPointRoi(string pointName)
            {
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
                if (p is null)
                {
                    return;
                }
    
                if (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(p.SearchRoi, Brushes.DeepSkyBlue, $"{p.Name} S"));
                }
    
                if (p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(p.TemplateRoi, Brushes.DeepSkyBlue, $"{p.Name} T"));
                }
            }
    
            void AddLineRoi(string lineName)
            {
                var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, lineName, StringComparison.OrdinalIgnoreCase));
                if (l is not null)
                {
                    if (l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                    {
                        dst.Add(CreateRotatedRoi(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} L"));
                    }
    
                    return;
                }
    
                var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, lineName, StringComparison.OrdinalIgnoreCase));
                if (c is null)
                {
                    return;
                }
    
                if (c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(c.SearchRoi, Brushes.Gold, $"{c.Name} Cal"));
                }
            }
    
            void AddCircleRoi(string circleName)
            {
                var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, circleName, StringComparison.OrdinalIgnoreCase));
                if (c is null)
                {
                    return;
                }
    
                if (c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(c.SearchRoi, Brushes.MediumPurple, $"{c.Name} CIR"));
                }
            }
    
            void AddDistanceAnchorRoi(string anchorName)
            {
                if (string.IsNullOrWhiteSpace(anchorName))
                {
                    return;
                }
    
                // Point
                if (_config.Points.Any(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase)))
                {
                    AddPointRoi(anchorName);
                    return;
                }
    
                // CircleFinder
                if (_config.CircleFinders.Any(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase)))
                {
                    AddCircleRoi(anchorName);
                    return;
                }
    
                // Diameter -> CircleFinder
                var dia = _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
                if (dia is not null && !string.IsNullOrWhiteSpace(dia.CircleRef))
                {
                    AddCircleRoi(dia.CircleRef);
                }
            }
    
            void AddBlobRoi(string blobName)
            {
                var b = config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, blobName, StringComparison.OrdinalIgnoreCase));
                if (b is null)
                {
                    return;
                }
    
                if (!showRois)
                {
                    return;
                }
    
                if (b.Rois is not null && b.Rois.Count > 0)
                {
                    var hasValidInclude = false;
                    for (var i = 0; i < b.Rois.Count; i++)
                    {
                        var rr = b.Rois[i];
                        if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                        {
                            continue;
                        }
    
                        if (rr.Mode == BlobRoiMode.Exclude)
                        {
                            dst.Add(CreateRotatedRoi(rr.Roi, Brushes.Red, $"{b.Name} BX{i + 1}"));
                        }
                        else
                        {
                            hasValidInclude = true;
                            dst.Add(CreateRotatedRoi(rr.Roi, Brushes.Gold, $"{b.Name} B{i + 1}"));
                        }
                    }
    
                    if (!hasValidInclude && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                    {
                        dst.Add(CreateRotatedRoi(b.InspectRoi, Brushes.Gold, $"{b.Name} B"));
                    }
    
                    return;
                }
    
                if (b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(b.InspectRoi, Brushes.Gold, $"{b.Name} B"));
                }
            }
    
            void AddSurfaceCompareRoi(string surfaceCompareName)
            {
                var sc = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
                if (sc is null)
                {
                    return;
                }
    
                if (!showRois)
                {
                    return;
                }
    
                if (sc.InspectRoi.Width > 0 && sc.InspectRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(sc.InspectRoi, Brushes.DeepSkyBlue, $"{sc.Name} SC"));
                }
    
                if (sc.TemplateRoi.Width > 0 && sc.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(sc.TemplateRoi, Brushes.DeepSkyBlue, $"{sc.Name} SCT"));
                }
    
                if (sc.Rois is not null && sc.Rois.Count > 0)
                {
                    for (var i = 0; i < sc.Rois.Count; i++)
                    {
                        var rr = sc.Rois[i];
                        if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                        {
                            continue;
                        }
    
                        if (rr.Mode == BlobRoiMode.Exclude)
                        {
                            dst.Add(CreateRotatedRoi(rr.Roi, Brushes.Red, $"{sc.Name} SCX{i + 1}"));
                        }
                        else
                        {
                            dst.Add(CreateRotatedRoi(rr.Roi, Brushes.DeepSkyBlue, $"{sc.Name} SC{i + 1}"));
                        }
                    }
                }
            }
    
            if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
            {
                if (showRois && _config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(_config.Origin.SearchRoi, Brushes.Lime, "Origin S"));
                }
    
                if (showRois && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(_config.Origin.TemplateRoi, Brushes.Lime, "Origin T"));
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Point", StringComparison.OrdinalIgnoreCase))
            {
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (p is null)
                {
                    return;
                }
    
                if (showRois)
                    AddPointRoi(p.Name);
                return;
            }
    
            if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (l is null)
                {
                    return;
                }
    
                if (showRois)
                    AddLineRoi(l.Name);
                return;
            }
    
            if (string.Equals(node.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (c is null)
                {
                    return;
                }
    
                if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(c.SearchRoi, Brushes.Lime, $"{c.Name} Cal"));
                    var stripCount = Math.Clamp(c.StripCount, 1, 100);
                    var stripLength = Math.Max(3, c.StripLength);
                    if (c.Orientation == CaliperOrientation.Vertical)
                    {
                        var y1 = c.SearchRoi.Y + (c.SearchRoi.Height - stripLength) / 2.0;
                        var y2 = y1 + stripLength;
                        for (var i = 0; i < stripCount; i++)
                        {
                            var x = c.SearchRoi.X + (i + 0.5) * c.SearchRoi.Width / stripCount;
                            dst.Add(new OverlayLineItem { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                        }
                    }
                    else
                    {
                        var x1 = c.SearchRoi.X + (c.SearchRoi.Width - stripLength) / 2.0;
                        var x2 = x1 + stripLength;
                        for (var i = 0; i < stripCount; i++)
                        {
                            var y = c.SearchRoi.Y + (i + 0.5) * c.SearchRoi.Height / stripCount;
                            dst.Add(new OverlayLineItem { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                        }
                    }
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (l is null)
                {
                    return;
                }
    
                if (showRois && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} LP"));
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (c is null)
                {
                    return;
                }
    
                if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(c.SearchRoi, Brushes.Lime, $"{c.Name} C"));
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                if (showRois)
                    AddBlobRoi(node.RefName);
                return;
            }
    
            if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
            {
                if (showRois)
                    AddSurfaceCompareRoi(node.RefName);
                return;
            }
    
            if (string.Equals(node.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
            {
                var e = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (e is null)
                {
                    return;
                }
    
                if (showRois && e.SearchRoi.Width > 0 && e.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(e.SearchRoi, Brushes.MediumPurple, $"{e.Name} EPD"));
                    var stripCount = Math.Clamp(e.StripCount, 1, 100);
                    var stripLength = Math.Max(3, e.StripLength);
                    if (e.Orientation == CaliperOrientation.Vertical)
                    {
                        var y1 = e.SearchRoi.Y + (e.SearchRoi.Height - stripLength) / 2.0;
                        var y2 = y1 + stripLength;
                        for (var i = 0; i < stripCount; i++)
                        {
                            var x = e.SearchRoi.X + (i + 0.5) * e.SearchRoi.Width / stripCount;
                            dst.Add(new OverlayLineItem { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = Brushes.MediumPurple, StrokeThickness = 1.0 });
                        }
                    }
                    else
                    {
                        var x1 = e.SearchRoi.X + (e.SearchRoi.Width - stripLength) / 2.0;
                        var x2 = x1 + stripLength;
                        for (var i = 0; i < stripCount; i++)
                        {
                            var y = e.SearchRoi.Y + (i + 0.5) * e.SearchRoi.Height / stripCount;
                            dst.Add(new OverlayLineItem { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = Brushes.MediumPurple, StrokeThickness = 1.0 });
                        }
                    }
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (c is null)
                {
                    return;
                }
    
                if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(c.SearchRoi, Brushes.MediumPurple, $"{c.Name} CIR"));
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                var d = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (d is null)
                {
                    return;
                }
    
                if (showRois)
                {
                    AddDistanceAnchorRoi(d.PointA);
                    AddDistanceAnchorRoi(d.PointB);
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var dd = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (dd is null)
                {
                    return;
                }
    
                if (showRois)
                {
                    AddLineRoi(dd.LineA);
                    AddLineRoi(dd.LineB);
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var ad = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (ad is null)
                {
                    return;
                }
    
                if (showRois)
                {
                    AddLineRoi(ad.LineA);
                    AddLineRoi(ad.LineB);
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var dd = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (dd is null)
                {
                    return;
                }
    
                if (showRois)
                {
                    AddPointRoi(dd.Point);
                    AddLineRoi(dd.Line);
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
            {
                var ep = _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (ep is null)
                {
                    return;
                }
    
                if (showRois)
                {
                    AddLineRoi(ep.RefA);
                    AddLineRoi(ep.RefB);
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
            {
                if (showRois && _config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(_config.DefectConfig.InspectRoi, Brushes.Orange, "DefectROI"));
                }
    
                return;
            }
        }
    }
}

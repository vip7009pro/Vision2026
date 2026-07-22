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
        [ObservableProperty]
        private string? _activeRoiLabel;
        private bool _finalPreviewDirty = true;
        private BitmapSource? _cachedFinalPreviewImage;
        private readonly System.Collections.Generic.Dictionary<string, (string SourcePath, Mat Image)> _imageSourcePreviewCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _specEditPreviewTimer;
        private readonly DispatcherTimer _blobThresholdPreviewTimer;
        private int _lastPreviewImageWidth;
        private int _lastPreviewImageHeight;
        private const int MaxBlobOverlayCount = 1000;
        private void OnRoiDeleted(string? labelRaw)
        {
            if (string.IsNullOrWhiteSpace(labelRaw) || _config is null)
            {
                return;
            }
    
            var label = labelRaw.Trim();
            // Defect / Origin / Point / Line deletes are not supported (for safety).
            // For BlobDetection we allow deleting: B (legacy inspect roi) and B#/BX# (multi rois).
            var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return;
            }
    
            var name = parts[0];
            var kind = parts[1];
            if (string.Equals(kind, "CIR", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is null)
                {
                    return;
                }
    
                c.SearchRoi = new Roi();
                RunFlow();
                RequestAutoSave();
                return;
            }
    
            if (kind.StartsWith("SC", StringComparison.OrdinalIgnoreCase))
            {
                var sc = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (sc is null)
                {
                    return;
                }
    
                if (string.Equals(kind, "SC", StringComparison.OrdinalIgnoreCase))
                {
                    sc.InspectRoi = new Roi();
                    sc.Rois.Clear();
                    RunFlow();
                    RequestAutoSave();
                    return;
                }
    
                // Multi ROI edit labels are index-based: SC1,SC2,... and SCX1,SCX2,...
                var scIsExclude = kind.StartsWith("SCX", StringComparison.OrdinalIgnoreCase);
                var scNumPart = scIsExclude ? kind.Substring(3) : kind.Substring(2);
                if (!int.TryParse(scNumPart, out var scIdx1) || scIdx1 <= 0)
                {
                    return;
                }
    
                var scIdx = scIdx1 - 1;
                if (scIdx < 0 || scIdx >= sc.Rois.Count)
                {
                    return;
                }
    
                sc.Rois.RemoveAt(scIdx);
                sc.InspectRoi = ComputeSurfaceCompareInspectRoi(sc);
                RunFlow();
                RequestAutoSave();
                return;
            }
    
            if (!kind.StartsWith("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
    
            var b = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (b is null)
            {
                return;
            }
    
            if (string.Equals(kind, "B", StringComparison.OrdinalIgnoreCase))
            {
                b.InspectRoi = new Roi();
                b.Rois.Clear();
                RunFlow();
                RequestAutoSave();
                return;
            }
    
            // Multi ROI edit labels are index-based: B1,B2,... and BX1,BX2,...
            var isExclude = kind.StartsWith("BX", StringComparison.OrdinalIgnoreCase);
            var numPart = isExclude ? kind.Substring(2) : kind.Substring(1);
            if (!int.TryParse(numPart, out var idx1) || idx1 <= 0)
            {
                return;
            }
    
            var idx = idx1 - 1;
            if (idx < 0 || idx >= b.Rois.Count)
            {
                return;
            }
    
            b.Rois.RemoveAt(idx);
            b.InspectRoi = ComputeBlobInspectRoi(b);
            RunFlow();
            RequestAutoSave();
        }
    
        private static Roi ComputeSurfaceCompareInspectRoi(SurfaceCompareDefinition sc)
        {
            if (sc.Rois is null || sc.Rois.Count == 0)
            {
                return sc.InspectRoi;
            }
    
            var inc = sc.Rois.Where(x => x.Mode == BlobRoiMode.Include && x.Roi.Width > 0 && x.Roi.Height > 0).Select(x => x.Roi).ToList();
            if (inc.Count == 0)
            {
                return sc.InspectRoi;
            }
    
            var minX = inc.Min(x => x.X);
            var minY = inc.Min(x => x.Y);
            var maxX = inc.Max(x => x.X + x.Width);
            var maxY = inc.Max(x => x.Y + x.Height);
            return new Roi
            {
                X = minX,
                Y = minY,
                Width = Math.Max(1, maxX - minX),
                Height = Math.Max(1, maxY - minY)
            };
        }
    
        private void BuildFinalOverlayFromRunWithConfig(InspectionResult run, List<OverlayItem> dst)
        {
            BuildFinalOverlayFromRun(run, dst, _config);
            // Angle overlays need image bounds for full infinite-line rendering.
            if (_lastPreviewImageWidth > 0 && _lastPreviewImageHeight > 0)
            {
                foreach (var a in run.Angles)
                {
                    if (double.IsNaN(a.ValueDeg) || !a.Found)
                    {
                        continue;
                    }
    
                    var ip = new System.Windows.Point(a.Intersection.X, a.Intersection.Y);
                    var aDir = new System.Windows.Point(a.ADir.X, a.ADir.Y);
                    var bDir = new System.Windows.Point(a.BDir.X, a.BDir.Y);
                    if (TryClipInfiniteLineToImage(ip, aDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var a1, out var a2))
                    {
                        dst.Add(new OverlayLineItem { X1 = a1.X, Y1 = a1.Y, X2 = a2.X, Y2 = a2.Y, Stroke = Brushes.MediumPurple, Label = a.LineA });
                    }
    
                    if (TryClipInfiniteLineToImage(ip, bDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var b1, out var b2))
                    {
                        dst.Add(new OverlayLineItem { X1 = b1.X, Y1 = b1.Y, X2 = b2.X, Y2 = b2.Y, Stroke = Brushes.Gold, Label = a.LineB });
                    }
    
                    AddAngleArc(dst, a.Intersection.X, a.Intersection.Y, a.ADir.X, a.ADir.Y, a.BDir.X, a.BDir.Y, radius: 35.0, stroke: a.Pass ? Brushes.Lime : Brushes.Red);
                    dst.Add(new OverlayPointItem { X = a.Intersection.X, Y = a.Intersection.Y, Radius = 3.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}∩┐╜" });
                }
            }
    
            if (_config is null)
            {
                return;
            }
    
            foreach (var c in _config.CircleFinders)
            {
                if (c.SearchRoi.Width <= 0 || c.SearchRoi.Height <= 0)
                {
                    continue;
                }
    
                dst.Add(CreateRotatedRoi(c.SearchRoi, Brushes.MediumPurple, $"{c.Name} CIR"));
            }
    
            foreach (var e in _config.EdgePairDetections)
            {
                if (e.SearchRoi.Width <= 0 || e.SearchRoi.Height <= 0)
                {
                    continue;
                }
    
                dst.Add(CreateRotatedRoi(e.SearchRoi, Brushes.MediumPurple, $"{e.Name} EPD"));
                var stripCount = Math.Clamp(e.StripCount, 1, 100);
                var stripLength = Math.Max(3, e.StripLength);
                if (stripCount > 0)
                {
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
            }
    
            foreach (var b in _config.BlobDetections)
            {
                if (b.InspectRoi.Width <= 0 || b.InspectRoi.Height <= 0)
                {
                    continue;
                }
    
                var r = run.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                if (r is null)
                {
                    continue;
                }
    
                dst.Add(new OverlayPointItem { X = b.InspectRoi.X + 2, Y = b.InspectRoi.Y + 2, Radius = 1.0, Stroke = Brushes.Gold, Label = $"{b.Name}: {r.Count}" });
                if (r.Blobs is null || r.Blobs.Count == 0)
                {
                    continue;
                }
    
                var n = Math.Min(r.Blobs.Count, MaxBlobOverlayCount);
                for (var i = 0; i < n; i++)
                {
                    var bi = r.Blobs[i];
                    var br = bi.BoundingBox;
                    if (br.Width > 0 && br.Height > 0)
                    {
                        dst.Add(CreateRotatedRoi(br, Brushes.Gold, string.Empty));
                    }
    
                    dst.Add(new OverlayPointItem { X = bi.Centroid.X, Y = bi.Centroid.Y, Radius = 3.0, Stroke = Brushes.Gold, Label = string.Empty });
                }
    
                if (r.Blobs.Count > MaxBlobOverlayCount)
                {
                    dst.Add(new OverlayPointItem { X = b.InspectRoi.X + 2, Y = b.InspectRoi.Y + 16, Radius = 1.0, Stroke = Brushes.Gold, Label = $"+{r.Blobs.Count - MaxBlobOverlayCount}" });
                }
            }
    
            foreach (var sc in _config.SurfaceCompares)
            {
                if (sc.InspectRoi.Width <= 0 || sc.InspectRoi.Height <= 0)
                {
                    continue;
                }
    
                var r = run.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, sc.Name, StringComparison.OrdinalIgnoreCase));
                if (r is null)
                {
                    continue;
                }
    
                dst.Add(new OverlayPointItem { X = sc.InspectRoi.X + 2, Y = sc.InspectRoi.Y + 2, Radius = 1.0, Stroke = Brushes.DeepSkyBlue, Label = $"{sc.Name}: {r.Count} / {r.MaxArea:0}" });
                if (r.Defects is null || r.Defects.Count == 0)
                {
                    continue;
                }
    
                var n = Math.Min(r.Defects.Count, MaxBlobOverlayCount);
                for (var i = 0; i < n; i++)
                {
                    var d = r.Defects[i];
                    var br = d.BoundingBox;
                    if (br.Width > 0 && br.Height > 0)
                    {
                        dst.Add(CreateRotatedRoi(br, Brushes.DeepSkyBlue, string.Empty));
                    }
    
                    dst.Add(new OverlayPointItem { X = d.Centroid.X, Y = d.Centroid.Y, Radius = 3.0, Stroke = Brushes.DeepSkyBlue, Label = string.Empty });
                }
    
                if (r.Defects.Count > MaxBlobOverlayCount)
                {
                    dst.Add(new OverlayPointItem { X = sc.InspectRoi.X + 2, Y = sc.InspectRoi.Y + 16, Radius = 1.0, Stroke = Brushes.DeepSkyBlue, Label = $"+{r.Defects.Count - MaxBlobOverlayCount}" });
                }
            }
        }
    
        private Mat ResolveToolPreprocessForPreview(Mat raw, ToolGraphNodeViewModel toolNode)
        {
            if (_config is null)
            {
                return raw.Clone();
            }
    
            var edges = _config.ToolGraph?.Edges ?? new();
            var nodesById = Nodes.Where(n => !string.IsNullOrWhiteSpace(n.Id)).ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
            var preprocessSettingsByName = (_config.PreprocessNodes ?? new()).Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToDictionary(p => p.Name, p => p.Settings ?? new PreprocessSettings(), StringComparer.OrdinalIgnoreCase);
            var cache = new System.Collections.Generic.Dictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);
            var matsToDispose = new System.Collections.Generic.List<Mat>();
            try
            {
                Mat GetPreprocessNodeOutput(string preprocessNodeId)
                {
                    if (cache.TryGetValue(preprocessNodeId, out var cached))
                    {
                        return cached;
                    }
    
                    if (!nodesById.TryGetValue(preprocessNodeId, out var node) || !string.Equals(node.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                    {
                        var fallback = _preprocessor.Run(raw, _config.Preprocess);
                        matsToDispose.Add(fallback);
                        cache[preprocessNodeId] = fallback;
                        return fallback;
                    }
    
                    var settings = preprocessSettingsByName.TryGetValue(node.RefName, out var s) ? s : new PreprocessSettings();
                    var inEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, preprocessNodeId, StringComparison.OrdinalIgnoreCase) && (string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase)));
                    Mat inputMat;
                    if (inEdge is null)
                    {
                        inputMat = raw;
                    }
                    else if (nodesById.TryGetValue(inEdge.FromNodeId, out var fromPreNode))
                    {
                        if (string.Equals(fromPreNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                        {
                            inputMat = GetPreprocessNodeOutput(fromPreNode.Id);
                        }
                        else if (string.Equals(fromPreNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                        {
                            var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, fromPreNode.RefName, StringComparison.OrdinalIgnoreCase));
                            if (imgSourceDef is not null)
                            {
                                var loadedMat = LoadImageFromSourceForPreview(imgSourceDef);
                                if (loadedMat is not null && !loadedMat.Empty())
                                {
                                    inputMat = _preprocessor.Run(loadedMat, _config.Preprocess);
                                    matsToDispose.Add(inputMat);
                                }
                                else
                                {
                                    inputMat = raw;
                                }
                            }
                            else
                            {
                                inputMat = raw;
                            }
                        }
                        else
                        {
                            inputMat = raw;
                        }
                    }
                    else
                    {
                        inputMat = raw;
                    }
    
                    var output = _preprocessor.Run(inputMat, settings);
                    matsToDispose.Add(output);
                    cache[preprocessNodeId] = output;
                    return output;
                }
    
                if (string.Equals(toolNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    return GetPreprocessNodeOutput(toolNode.Id).Clone();
                }
    
                var imageEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, toolNode.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase));
                if (imageEdge is null || !nodesById.TryGetValue(imageEdge.FromNodeId, out var fromNode))
                {
                    return _preprocessor.Run(raw, _config.Preprocess);
                }
    
                if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    return GetPreprocessNodeOutput(fromNode.Id).Clone();
                }
                else if (string.Equals(fromNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                {
                    var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, fromNode.RefName, StringComparison.OrdinalIgnoreCase));
                    if (imgSourceDef is not null)
                    {
                        var loadedMat = LoadImageFromSourceForPreview(imgSourceDef);
                        if (loadedMat is not null && !loadedMat.Empty())
                        {
                            var outputMat = _preprocessor.Run(loadedMat, _config.Preprocess);
                            matsToDispose.Add(outputMat);
                            return outputMat.Clone();
                        }
                    }
                }
    
                return _preprocessor.Run(raw, _config.Preprocess);
            }
            finally
            {
                foreach (var m in matsToDispose)
                {
                    m.Dispose();
                }
            }
        }
    
        private Mat ResolveToolImageForPreview(Mat raw, ToolGraphNodeViewModel toolNode)
        {
            if (_config is null)
            {
                return raw.Clone();
            }
    
            var edges = _config.ToolGraph?.Edges ?? new();
            var nodesById = Nodes.Where(n => !string.IsNullOrWhiteSpace(n.Id)).ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
            var imageEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, toolNode.Id, StringComparison.OrdinalIgnoreCase) && (string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase)));
            if (imageEdge is null || !nodesById.TryGetValue(imageEdge.FromNodeId, out var fromNode))
            {
                return raw.Clone();
            }
    
            if (string.Equals(fromNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
            {
                var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, fromNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (imgSourceDef is not null)
                {
                    var loadedMat = LoadImageFromSourceForPreview(imgSourceDef);
                    if (loadedMat is not null && !loadedMat.Empty())
                    {
                        return loadedMat.Clone();
                    }
                }
            }
            else if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveToolImageForPreview(raw, fromNode);
            }
    
            return raw.Clone();
        }
    
        private void RequestBlobThresholdPreviewUpdate()
        {
            if (!_blobThresholdPreviewTimer.IsEnabled)
            {
                _blobThresholdPreviewTimer.Start();
                return;
            }
    
            _blobThresholdPreviewTimer.Stop();
            _blobThresholdPreviewTimer.Start();
        }
    
        private void UpdateBlobThresholdPreviewFromSnapshot()
        {
            _blobThresholdPreviewTimer.Stop();
            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new Mat();
            _lastPreviewImageWidth = snap.Width;
            _lastPreviewImageHeight = snap.Height;
            UpdateBlobThresholdPreview(snap);
        }
    
        private void OnRoiSelected(object? arg)
        {
            if (_config is null)
            {
                return;
            }
    
            if (arg is RoiSelection rs)
            {
                // Treat drawing as "set this ROI" for the active label (S/T/L/DefectROI)
                // Special case: BlobDetection supports multi ROI include/exclude
                if (SelectedNode is not null && string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(SelectedNode.RefName))
                {
                    var def = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                    if (def is not null)
                    {
                        if (rs.Modifiers.HasFlag(ModifierKeys.Control))
                        {
                            def.Rois.Add(new BlobRoiDefinition { Mode = BlobRoiMode.Include, Roi = rs.Roi });
                            def.InspectRoi = ComputeBlobInspectRoi(def);
                        }
                        else if (rs.Modifiers.HasFlag(ModifierKeys.Shift))
                        {
                            def.Rois.Add(new BlobRoiDefinition { Mode = BlobRoiMode.Exclude, Roi = rs.Roi });
                            def.InspectRoi = ComputeBlobInspectRoi(def);
                        }
                        else
                        {
                            ApplyRoiForLabel(rs.Label, rs.Roi);
                            def.InspectRoi = ComputeBlobInspectRoi(def);
                        }
                    }
                }
                else
                {
                    ApplyRoiForLabel(rs.Label, rs.Roi);
                }
    
                RefreshPreviews();
                RaiseToolPropertyPanelsChanged();
                RequestAutoSave();
                return;
            }
    
            if (arg is Roi roi)
            {
                // Fallback: when no label is available, apply to the selected node's primary ROI
                if (SelectedNode is null)
                {
                    return;
                }
    
                if (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase))
                {
                    _config.Origin.SearchRoi = roi;
                }
                else if (string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
                {
                    var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                    if (p is not null)
                        p.SearchRoi = roi;
                }
                else if (string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
                {
                    var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                    if (l is not null)
                        l.SearchRoi = roi;
                }
                else if (string.Equals(SelectedNode.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
                {
                    _config.DefectConfig.InspectRoi = roi;
                }
                else if (string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
                {
                    var b = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                    if (b is not null)
                        b.InspectRoi = roi;
                }
    
                RefreshPreviews();
                RaiseToolPropertyPanelsChanged();
                RequestAutoSave();
            }
        }
    
        private void RefreshLineRoiPreview(Mat image)
        {
            if (!LinePreviewEnabled)
            {
                LinePreviewImage = null;
                return;
            }
    
            var def = SelectedLineDef();
            if (def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
            {
                LinePreviewImage = null;
                return;
            }
    
            var r = new OpenCvSharp.Rect(def.SearchRoi.X, def.SearchRoi.Y, def.SearchRoi.Width, def.SearchRoi.Height);
            r = r.Intersect(new OpenCvSharp.Rect(0, 0, image.Width, image.Height));
            if (r.Width <= 0 || r.Height <= 0)
            {
                LinePreviewImage = null;
                return;
            }
    
            using var processed = _preprocessor.Run(image, _config!.Preprocess);
            using var crop = new Mat(processed, r);
            using var view = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
            var det = _lineDetector.DetectLongestLine(processed, def.SearchRoi, def.Canny1, def.Canny2, def.HoughThreshold, def.MinLineLength, def.MaxLineGap);
            if (det.Found)
            {
                var p1 = new OpenCvSharp.Point((int)Math.Round(det.P1.X) - r.X, (int)Math.Round(det.P1.Y) - r.Y);
                var p2 = new OpenCvSharp.Point((int)Math.Round(det.P2.X) - r.X, (int)Math.Round(det.P2.Y) - r.Y);
                Cv2.Line(view, p1, p2, Scalar.White, 2);
            }
    
            LinePreviewImage = view.ToBitmapSource();
        }
    
        private void RefreshPointEdgePreview(Mat snap)
        {
            if (!PointEdgePreviewEnabled)
            {
                PointEdgePreviewImage = null;
                return;
            }
    
            if (_config is null || SelectedNode is null || !string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
            {
                PointEdgePreviewImage = null;
                return;
            }
    
            var def = SelectedPointDef();
            if (def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
            {
                PointEdgePreviewImage = null;
                return;
            }
    
            if (def.Algorithm != PointFindAlgorithm.EdgePoint)
            {
                PointEdgePreviewImage = null;
                return;
            }
    
            using var matForPoint = ResolveToolPreprocessForPreview(snap, SelectedNode);
            var rect = new OpenCvSharp.Rect(def.SearchRoi.X, def.SearchRoi.Y, def.SearchRoi.Width, def.SearchRoi.Height);
            rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, matForPoint.Width, matForPoint.Height));
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                PointEdgePreviewImage = null;
                return;
            }
    
            using var crop = new Mat(matForPoint, rect);
            using var gray = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var view = crop.Clone();
            var n = Math.Clamp(def.EdgePoint.StripCount, 1, 200);
            var stripW = Math.Max(1, def.EdgePoint.StripWidth);
            var stripL = Math.Max(3, def.EdgePoint.StripLength);
            var minG = Math.Max(0.0, def.EdgePoint.MinEdgeStrength);
            var foundPts = new System.Collections.Generic.List<Point2d>();
            if (def.EdgePoint.Orientation == CaliperOrientation.Vertical)
            {
                var y0 = (int)Math.Round((gray.Rows - (n - 1) * stripW) / 2.0);
                var xMid = gray.Cols / 2;
                for (var i = 0; i < n; i++)
                {
                    var y = y0 + i * stripW;
                    var rr = new OpenCvSharp.Rect(Math.Max(0, xMid - stripL / 2), Math.Max(0, y), Math.Min(stripL, gray.Cols), Math.Min(stripW, gray.Rows - y));
                    if (rr.Width <= 1 || rr.Height <= 0)
                        continue;
                    Cv2.Rectangle(view, rr, new Scalar(255, 200, 0), 1);
                    var edge = FindEdgeOnStrip(gray, rr, scanAlongX: true, def.EdgePoint.Polarity, minG);
                    if (edge.HasValue)
                    {
                        foundPts.Add(new Point2d(edge.Value.X, edge.Value.Y));
                        Cv2.Circle(view, new OpenCvSharp.Point((int)Math.Round(edge.Value.X), (int)Math.Round(edge.Value.Y)), 3, new Scalar(0, 255, 0), 2);
                    }
                }
            }
            else
            {
                var x0 = (int)Math.Round((gray.Cols - (n - 1) * stripW) / 2.0);
                var yMid = gray.Rows / 2;
                for (var i = 0; i < n; i++)
                {
                    var x = x0 + i * stripW;
                    var rr = new OpenCvSharp.Rect(Math.Max(0, x), Math.Max(0, yMid - stripL / 2), Math.Min(stripW, gray.Cols - x), Math.Min(stripL, gray.Rows));
                    if (rr.Width <= 0 || rr.Height <= 1)
                        continue;
                    Cv2.Rectangle(view, rr, new Scalar(255, 200, 0), 1);
                    var edge = FindEdgeOnStrip(gray, rr, scanAlongX: false, def.EdgePoint.Polarity, minG);
                    if (edge.HasValue)
                    {
                        foundPts.Add(new Point2d(edge.Value.X, edge.Value.Y));
                        Cv2.Circle(view, new OpenCvSharp.Point((int)Math.Round(edge.Value.X), (int)Math.Round(edge.Value.Y)), 3, new Scalar(0, 255, 0), 2);
                    }
                }
            }
    
            if (foundPts.Count > 0)
            {
                var avgX = foundPts.Average(p => p.X);
                var avgY = foundPts.Average(p => p.Y);
                Cv2.DrawMarker(view, new OpenCvSharp.Point((int)Math.Round(avgX), (int)Math.Round(avgY)), new Scalar(0, 0, 255), MarkerTypes.Cross, 20, 2);
            }
    
            PointEdgePreviewImage = view.ToBitmapSource();
        }
    
        private static Point2dModel RoiCenterToWorld(Roi roi)
        {
            return new Point2dModel
            {
                X = roi.X + roi.Width / 2.0,
                Y = roi.Y + roi.Height / 2.0
            };
        }
    
        private void ApplyRoiForLabel(string labelRaw, Roi roi)
        {
            if (_config is null)
            {
                return;
            }
    
            if (string.IsNullOrWhiteSpace(labelRaw))
            {
                return;
            }
    
            var label = labelRaw.Trim();
            if (string.Equals(label, "DefectROI", StringComparison.OrdinalIgnoreCase))
            {
                _config.DefectConfig.InspectRoi = roi;
                return;
            }
    
            if (string.Equals(label, "Origin S", StringComparison.OrdinalIgnoreCase))
            {
                _config.Origin.SearchRoi = roi;
                return;
            }
    
            if (string.Equals(label, "Origin T", StringComparison.OrdinalIgnoreCase))
            {
                _config.Origin.TemplateRoi = roi;
                _config.Origin.WorldPosition = RoiCenterToWorld(roi);
                TrySaveTemplateImage("origin", roi, isOrigin: true, pointName: null);
                return;
            }
    
            var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return;
            }
    
            var name = parts[0];
            var kind = parts[1];
            if (string.Equals(kind, "S", StringComparison.OrdinalIgnoreCase))
            {
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p is not null)
                    p.SearchRoi = roi;
                return;
            }
    
            if (string.Equals(kind, "T", StringComparison.OrdinalIgnoreCase))
            {
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p is not null)
                {
                    p.TemplateRoi = roi;
                    p.WorldPosition = RoiCenterToWorld(roi);
                    TrySaveTemplateImage(name, roi, isOrigin: false, pointName: name);
                }
    
                return;
            }
    
            if (string.Equals(kind, "L", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (l is not null)
                    l.SearchRoi = roi;
                return;
            }
    
            if (string.Equals(kind, "Cal", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is not null)
                    c.SearchRoi = roi;
                return;
            }
    
            if (string.Equals(kind, "LP", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (l is not null)
                    l.SearchRoi = roi;
                return;
            }
    
            if (string.Equals(kind, "EPD", StringComparison.OrdinalIgnoreCase))
            {
                var e = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (e is not null)
                    e.SearchRoi = roi;
                return;
            }
    
            if (string.Equals(kind, "CIR", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is not null)
                    c.SearchRoi = roi;
                return;
            }
    
            if (string.Equals(kind, "C", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is not null)
                    c.SearchRoi = roi;
                return;
            }
    
            if (kind.StartsWith("B", StringComparison.OrdinalIgnoreCase))
            {
                var b = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (b is null)
                {
                    return;
                }
    
                if (string.Equals(kind, "B", StringComparison.OrdinalIgnoreCase))
                {
                    b.InspectRoi = roi;
                    return;
                }
    
                // Multi ROI edit labels:
                // - Include:  B1, B2, ...
                // - Exclude:  BX1, BX2, ...
                var isExclude = kind.StartsWith("BX", StringComparison.OrdinalIgnoreCase);
                var numPart = isExclude ? kind.Substring(2) : kind.Substring(1);
                if (!int.TryParse(numPart, out var idx1) || idx1 <= 0)
                {
                    return;
                }
    
                var idx = idx1 - 1;
                if (idx < 0)
                {
                    return;
                }
    
                while (b.Rois.Count <= idx)
                {
                    b.Rois.Add(new BlobRoiDefinition());
                }
    
                b.Rois[idx].Mode = isExclude ? BlobRoiMode.Exclude : BlobRoiMode.Include;
                b.Rois[idx].Roi = roi;
                b.InspectRoi = ComputeBlobInspectRoi(b);
                return;
            }
    
            if (kind.StartsWith("SC", StringComparison.OrdinalIgnoreCase))
            {
                var sc = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (sc is null)
                {
                    return;
                }
    
                if (string.Equals(kind, "SC", StringComparison.OrdinalIgnoreCase))
                {
                    sc.InspectRoi = roi;
                    if (sc.TemplateRoi.Width <= 0 || sc.TemplateRoi.Height <= 0)
                    {
                        sc.TemplateRoi = roi;
                    }
    
                    if (string.IsNullOrWhiteSpace(sc.TemplateImageFile))
                    {
                        TrySaveSurfaceCompareTemplateImage(name, sc.TemplateRoi);
                    }
    
                    return;
                }
    
                if (string.Equals(kind, "SCT", StringComparison.OrdinalIgnoreCase))
                {
                    sc.TemplateRoi = roi;
                    TrySaveSurfaceCompareTemplateImage(name, roi);
                    return;
                }
    
                // Multi ROI edit labels:
                // - Include:  SC1, SC2, ...
                // - Exclude:  SCX1, SCX2, ...
                var isExclude = kind.StartsWith("SCX", StringComparison.OrdinalIgnoreCase);
                var numPart = isExclude ? kind.Substring(3) : kind.Substring(2);
                if (!int.TryParse(numPart, out var idx1) || idx1 <= 0)
                {
                    return;
                }
    
                var idx = idx1 - 1;
                if (idx < 0)
                {
                    return;
                }
    
                while (sc.Rois.Count <= idx)
                {
                    sc.Rois.Add(new SurfaceCompareRoiDefinition());
                }
    
                sc.Rois[idx].Mode = isExclude ? BlobRoiMode.Exclude : BlobRoiMode.Include;
                sc.Rois[idx].Roi = roi;
                sc.InspectRoi = ComputeSurfaceCompareInspectRoi(sc);
                return;
            }
        }
    
        private static Roi ComputeBlobInspectRoi(BlobDetectionDefinition b)
        {
            if (b.Rois is null || b.Rois.Count == 0)
            {
                return b.InspectRoi;
            }
    
            var inc = b.Rois.Where(x => x.Mode == BlobRoiMode.Include && x.Roi.Width > 0 && x.Roi.Height > 0).Select(x => x.Roi).ToList();
            if (inc.Count == 0)
            {
                return b.InspectRoi;
            }
    
            var minX = inc.Min(x => x.X);
            var minY = inc.Min(x => x.Y);
            var maxX = inc.Max(x => x.X + x.Width);
            var maxY = inc.Max(x => x.Y + x.Height);
            return new Roi
            {
                X = minX,
                Y = minY,
                Width = Math.Max(1, maxX - minX),
                Height = Math.Max(1, maxY - minY)
            };
        }
    
        private void BuildOverlayForNodeFromRunWithConfig(ToolGraphNodeViewModel node, InspectionResult run, List<OverlayItem> dst)
        {
            BuildOverlayForNodeFromRun(node, run, dst);
            if (_config is null)
            {
                return;
            }
    
            if (string.Equals(node.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is null || def.InspectRoi.Width <= 0 || def.InspectRoi.Height <= 0)
                {
                    return;
                }
    
                var r = run.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null)
                {
                    return;
                }
    
                dst.Add(new OverlayPointItem { X = def.InspectRoi.X + 2, Y = def.InspectRoi.Y + 2, Radius = 1.0, Stroke = Brushes.Gold, Label = $"{def.Name}: {r.Count}" });
                if (r.Blobs is null || r.Blobs.Count == 0)
                {
                    return;
                }
    
                var n = Math.Min(r.Blobs.Count, MaxBlobOverlayCount);
                for (var i = 0; i < n; i++)
                {
                    var bi = r.Blobs[i];
                    var br = bi.BoundingBox;
                    if (br.Width > 0 && br.Height > 0)
                    {
                        dst.Add(CreateRotatedRoi(br, Brushes.Gold, string.Empty));
                    }
    
                    dst.Add(new OverlayPointItem { X = bi.Centroid.X, Y = bi.Centroid.Y, Radius = 3.0, Stroke = Brushes.Gold, Label = string.Empty });
                }
    
                if (r.Blobs.Count > MaxBlobOverlayCount)
                {
                    dst.Add(new OverlayPointItem { X = def.InspectRoi.X + 2, Y = def.InspectRoi.Y + 16, Radius = 1.0, Stroke = Brushes.Gold, Label = $"+{r.Blobs.Count - MaxBlobOverlayCount}" });
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is null || def.InspectRoi.Width <= 0 || def.InspectRoi.Height <= 0)
                {
                    return;
                }
    
                var r = run.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null)
                {
                    return;
                }
    
                var stroke = r.Pass ? Brushes.Lime : Brushes.Red;
                var status = r.Pass ? "OK" : "NG";
                dst.Add(new OverlayPointItem { X = def.InspectRoi.X + 2, Y = def.InspectRoi.Y + 2, Radius = 1.0, Stroke = stroke, Label = $"{def.Name} [{status}]: S\u1ed1 l\u1ed7i: {r.Count}, S.L\u1edbn nh\u1ea5t: {r.MaxArea:0}" });
                if (r.Defects is null || r.Defects.Count == 0)
                {
                    return;
                }
    
                var n = Math.Min(r.Defects.Count, MaxBlobOverlayCount);
                for (var i = 0; i < n; i++)
                {
                    var d = r.Defects[i];
                    var br = d.BoundingBox;
                    if (br.Width > 0 && br.Height > 0)
                    {
                        dst.Add(new OverlayRectItem { X = br.X, Y = br.Y, Width = br.Width, Height = br.Height, Stroke = stroke, StrokeThickness = 2.0, Angle = d.Angle, // Thicker boxes for better visibility
     Label = string.Empty });
                    }
                }
    
                if (r.Defects.Count > MaxBlobOverlayCount)
                {
                    dst.Add(new OverlayPointItem { X = def.InspectRoi.X + 2, Y = def.InspectRoi.Y + 16, Radius = 1.0, Stroke = stroke, Label = $"+{r.Defects.Count - MaxBlobOverlayCount}" });
                }
    
                return;
            }
        }
    
        private void OnRoiEdited(RoiSelection? sel)
        {
            if (sel is null || _config is null)
            {
                return;
            }
    
            if (string.IsNullOrWhiteSpace(sel.Label))
            {
                return;
            }
    
            var label = sel.Label.Trim();
            var roi = UnTransformRoi(sel.Roi, label);
            if (string.Equals(label, "DefectROI", StringComparison.OrdinalIgnoreCase))
            {
                _config.DefectConfig.InspectRoi = roi;
                RefreshPreviews();
                RequestAutoSave();
                return;
            }
    
            if (string.Equals(label, "Origin S", StringComparison.OrdinalIgnoreCase))
            {
                _config.Origin.SearchRoi = roi;
                if (_config.Origin.TemplateRoi.Width <= 0 || _config.Origin.TemplateRoi.Height <= 0)
                {
                    _config.Origin.TemplateRoi = roi;
                    _config.Origin.WorldPosition = RoiCenterToWorld(roi);
                }
    
                RefreshPreviews();
                RequestAutoSave();
                return;
            }
    
            if (string.Equals(label, "Origin T", StringComparison.OrdinalIgnoreCase))
            {
                _config.Origin.TemplateRoi = roi;
                _config.Origin.WorldPosition = RoiCenterToWorld(roi);
                RefreshPreviews();
                RequestAutoSave();
                return;
            }
    
            var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var name = parts[0];
                var kind = parts[1];
                if (string.Equals(kind, "S", StringComparison.OrdinalIgnoreCase))
                {
                    var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (p is not null)
                    {
                        p.SearchRoi = roi;
                        if (p.TemplateRoi.Width <= 0 || p.TemplateRoi.Height <= 0)
                        {
                            p.TemplateRoi = roi;
                            p.WorldPosition = RoiCenterToWorld(roi);
                        }
    
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (string.Equals(kind, "T", StringComparison.OrdinalIgnoreCase))
                {
                    var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (p is not null)
                    {
                        p.TemplateRoi = roi;
                        p.WorldPosition = RoiCenterToWorld(roi);
                        TrySaveTemplateImage(name, roi, isOrigin: false, pointName: name);
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (string.Equals(kind, "L", StringComparison.OrdinalIgnoreCase))
                {
                    var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (l is not null)
                    {
                        l.SearchRoi = roi;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (string.Equals(kind, "LP", StringComparison.OrdinalIgnoreCase))
                {
                    var l = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (l is not null)
                    {
                        l.SearchRoi = roi;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (string.Equals(kind, "Cal", StringComparison.OrdinalIgnoreCase))
                {
                    var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (c is not null)
                    {
                        c.SearchRoi = roi;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (string.Equals(kind, "EPD", StringComparison.OrdinalIgnoreCase))
                {
                    var e = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (e is not null)
                    {
                        e.SearchRoi = roi;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (string.Equals(kind, "C", StringComparison.OrdinalIgnoreCase))
                {
                    var c = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (c is not null)
                    {
                        c.SearchRoi = roi;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (string.Equals(kind, "CIR", StringComparison.OrdinalIgnoreCase))
                {
                    var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (c is not null)
                    {
                        c.SearchRoi = roi;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }
    
                if (kind.StartsWith("B", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyRoiForLabel(label, roi);
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }
    
                if (kind.StartsWith("SC", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyRoiForLabel(label, roi);
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }
            }
    
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
            RequestAutoSave();
        }
    
        private void RequestSpecEditPreviewRefresh()
        {
            _specEditPreviewTimer.Stop();
            _specEditPreviewTimer.Start();
        }
    
        [ObservableProperty]
        private bool _isLivePreviewMode = true;
        [ObservableProperty]
        private ImageSource? _selectedNodePreviewImage;
        [ObservableProperty]
        private ImageSource? _finalPreviewImage;
        [ObservableProperty]
        private ImageSource? _linePreviewImage;
        [ObservableProperty]
        private ImageSource? _pointEdgePreviewImage;
        [ObservableProperty]
        private ImageSource? _blobThresholdPreviewImage;
        [ObservableProperty]
        private List<OverlayItem> _finalOverlayItems = new();
        public ICommand LoadPreviewImageCommand { get; internal set; }
        public ICommand CaptureCameraImageCommand { get; internal set; }
        public ICommand RunFlowCommand { get; internal set; }
        public ICommand RunOnceCommand { get; internal set; }
        public ICommand RunContinuousCommand { get; internal set; }
        public ICommand RoiSelectedCommand { get; internal set; }
        public ICommand RoiEditedCommand { get; internal set; }
        public ICommand RoiDeletedCommand { get; internal set; }
    
        [ObservableProperty]
        private bool _linePreviewEnabled = true;
        [ObservableProperty]
        private bool _pointEdgePreviewEnabled = true;
        [ObservableProperty]
        private bool _preprocessPreviewEnabled = true;
        [ObservableProperty]
        private bool _showRoisInSelectedPreview = true;
        [ObservableProperty]
        private bool _showRoisInFinalPreview = true;
        partial void OnShowRoisInSelectedPreviewChanged(bool value)
        {
            RefreshSelectedPreview();
            RaiseToolPropertyPanelsChanged();
        }
    
        partial void OnShowRoisInFinalPreviewChanged(bool value)
        {
            _finalPreviewDirty = true;
            RefreshFinalPreview();
            RaiseToolPropertyPanelsChanged();
        }
    
        partial void OnLinePreviewEnabledChanged(bool value)
        {
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
        }
    
        partial void OnPointEdgePreviewEnabledChanged(bool value)
        {
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
        }
    
        partial void OnPreprocessPreviewEnabledChanged(bool value)
        {
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
        }
    
        private Mat? LoadImageFromSourceForPreview(ImageSourceDefinition source)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"LoadImageFromSourceForPreview: SourceType={source.SourceType}, FilePath={source.FilePath}, FolderPath={source.FolderPath}");
                if (source.SourceType == ImageSourceType.File)
                {
                    if (!string.IsNullOrWhiteSpace(source.FilePath) && File.Exists(source.FilePath))
                    {
                        if (_imageSourcePreviewCache.TryGetValue(source.Name ?? "", out var cached) && cached.SourcePath == source.FilePath && cached.Image is not null && !cached.Image.IsDisposed)
                        {
                            return cached.Image.Clone();
                        }
    
                        System.Diagnostics.Debug.WriteLine($"Loading image from file: {source.FilePath}");
                        var mat = Cv2.ImRead(source.FilePath);
                        if (mat is not null && !mat.Empty())
                        {
                            System.Diagnostics.Debug.WriteLine($"Successfully loaded image: {mat.Width}x{mat.Height}");
                            if (_imageSourcePreviewCache.TryGetValue(source.Name ?? "", out var old))
                                old.Image?.Dispose();
                            _imageSourcePreviewCache[source.Name ?? ""] = (source.FilePath, mat.Clone());
                            return mat;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to load image or image is empty");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"File does not exist or path is empty");
                    }
                }
                else if (source.SourceType == ImageSourceType.Folder)
                {
                    if (!string.IsNullOrWhiteSpace(source.FolderPath) && Directory.Exists(source.FolderPath))
                    {
                        var files = Directory.GetFiles(source.FolderPath, "*.*", SearchOption.TopDirectoryOnly).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToArray();
                        if (files.Length > 0)
                        {
                            var targetFile = files[_folderImageIndex % files.Length];
                            if (_imageSourcePreviewCache.TryGetValue(source.Name ?? "", out var cached) && cached.SourcePath == targetFile && cached.Image is not null && !cached.Image.IsDisposed)
                            {
                                return cached.Image.Clone();
                            }

                            System.Diagnostics.Debug.WriteLine($"Loading image from folder: {targetFile}");
                            var mat = Cv2.ImRead(targetFile);
                            if (mat is not null && !mat.Empty())
                            {
                                System.Diagnostics.Debug.WriteLine($"Successfully loaded image: {mat.Width}x{mat.Height}");
                                if (_imageSourcePreviewCache.TryGetValue(source.Name ?? "", out var old))
                                    old.Image?.Dispose();
                                _imageSourcePreviewCache[source.Name ?? ""] = (targetFile, mat.Clone());
                                return mat;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in LoadImageFromSourceForPreview: {ex.Message}");
            }
    
            return null;
        }

        private CancellationTokenSource? _folderFlowCts;
        private int _folderImageIndex = 0;
        private bool _isRunningFolderFlow;

        public bool IsRunningFolderFlow
        {
            get => _isRunningFolderFlow;
            set
            {
                if (_isRunningFolderFlow != value)
                {
                    _isRunningFolderFlow = value;
                    OnPropertyChanged();
                    UpdateRunFlowButtonProperties();
                }
            }
        }

        public string RunFlowButtonIcon => IsRunningFolderFlow ? "⏹" : "▶";
        public string RunFlowButtonText => IsRunningFolderFlow ? "STOP" : "Run Flow";
        public Brush RunFlowButtonBackgroundBrush => IsRunningFolderFlow
            ? new SolidColorBrush(Color.FromRgb(211, 47, 47))
            : new SolidColorBrush(Color.FromRgb(16, 124, 16));
        public string RunFlowButtonToolTip => IsRunningFolderFlow ? "Dừng chạy luồng thư mục" : "Run Flow";

        public string RunContinuousButtonIcon => IsRunningFolderFlow ? "⏹" : "🔁";
        public string RunContinuousButtonText => IsRunningFolderFlow ? "STOP" : "Run Continuous";
        public Brush RunContinuousButtonBackgroundBrush => IsRunningFolderFlow
            ? new SolidColorBrush(Color.FromRgb(211, 47, 47))
            : new SolidColorBrush(Color.FromRgb(46, 125, 50));
        public string RunContinuousButtonToolTip => IsRunningFolderFlow ? "Dừng chạy luồng thư mục liên tục" : "Chạy liên tục qua các ảnh trong thư mục (Loop & Interval)";

        private void UpdateRunFlowButtonProperties()
        {
            OnPropertyChanged(nameof(RunFlowButtonIcon));
            OnPropertyChanged(nameof(RunFlowButtonText));
            OnPropertyChanged(nameof(RunFlowButtonBackgroundBrush));
            OnPropertyChanged(nameof(RunFlowButtonToolTip));
            OnPropertyChanged(nameof(RunContinuousButtonIcon));
            OnPropertyChanged(nameof(RunContinuousButtonText));
            OnPropertyChanged(nameof(RunContinuousButtonBackgroundBrush));
            OnPropertyChanged(nameof(RunContinuousButtonToolTip));
        }

        private void OnRunOnceClicked()
        {
            if (IsRunningFolderFlow)
            {
                StopFolderFlow();
            }

            var imageSourceNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
            if (imageSourceNode is not null && _config is not null)
            {
                var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, imageSourceNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (imgSourceDef is not null && imgSourceDef.SourceType == ImageSourceType.Folder)
                {
                    if (string.IsNullOrWhiteSpace(imgSourceDef.FolderPath) || !Directory.Exists(imgSourceDef.FolderPath))
                    {
                        MessageBox.Show($"Thư mục chứa ảnh không tồn tại:\n{imgSourceDef.FolderPath}", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        RunFlow();
                        return;
                    }

                    var files = Directory.GetFiles(imgSourceDef.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f).ToArray();

                    if (files.Length == 0)
                    {
                        MessageBox.Show($"Thư mục không có tệp ảnh hợp lệ:\n{imgSourceDef.FolderPath}", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        RunFlow();
                        return;
                    }

                    if (_folderImageIndex >= files.Length)
                    {
                        _folderImageIndex = 0;
                    }

                    var filePath = files[_folderImageIndex];
                    RunSingleFlowFromImageFile(filePath, imgSourceDef.Name);
                    _folderImageIndex = (_folderImageIndex + 1) % files.Length;
                    return;
                }
            }

            RunFlow();
        }

        private void OnRunContinuousClicked()
        {
            if (IsRunningFolderFlow)
            {
                StopFolderFlow();
                return;
            }

            var imageSourceNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
            if (imageSourceNode is not null && _config is not null)
            {
                var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, imageSourceNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (imgSourceDef is not null && imgSourceDef.SourceType == ImageSourceType.Folder)
                {
                    if (string.IsNullOrWhiteSpace(imgSourceDef.FolderPath) || !Directory.Exists(imgSourceDef.FolderPath))
                    {
                        MessageBox.Show($"Thư mục chứa ảnh không tồn tại:\n{imgSourceDef.FolderPath}", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var files = Directory.GetFiles(imgSourceDef.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f).ToArray();

                    if (files.Length == 0)
                    {
                        MessageBox.Show($"Thư mục không có tệp ảnh hợp lệ:\n{imgSourceDef.FolderPath}", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    StartFolderFlow(imgSourceDef, files);
                    return;
                }
            }

            RunFlow();
        }

        private void OnRunFlowClicked()
        {
            OnRunOnceClicked();
        }

        private void StartFolderFlow(ImageSourceDefinition sourceDef, string[] imageFiles)
        {
            _folderFlowCts?.Cancel();
            _folderFlowCts = new CancellationTokenSource();
            IsRunningFolderFlow = true;

            var token = _folderFlowCts.Token;
            Task.Run(async () =>
            {
                int index = _folderImageIndex;
                int interval = Math.Max(50, sourceDef.FolderIntervalMs);
                bool loop = sourceDef.LoopFolder;

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (index >= imageFiles.Length)
                        {
                            if (loop)
                            {
                                index = 0;
                            }
                            else
                            {
                                break;
                            }
                        }

                        var filePath = imageFiles[index];
                        _folderImageIndex = index;

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            RunSingleFlowFromImageFile(filePath, sourceDef.Name);
                        });

                        index++;

                        try
                        {
                            await Task.Delay(interval, token);
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StartFolderFlow exception: {ex.Message}");
                }
                finally
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _folderImageIndex = index % Math.Max(1, imageFiles.Length);
                        IsRunningFolderFlow = false;
                    });
                }
            }, token);
        }

        private void StopFolderFlow()
        {
            _folderFlowCts?.Cancel();
            IsRunningFolderFlow = false;
        }

        private void RunSingleFlowFromImageFile(string filePath, string sourceNodeName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || _config is null)
                return;

            using var mat = Cv2.ImRead(filePath);
            if (mat is null || mat.Empty())
                return;

            _inspectionService.ResetTracking();
            var __sw = System.Diagnostics.Stopwatch.StartNew();

            if (_imageSourcePreviewCache.TryGetValue(sourceNodeName ?? "", out var old))
                old.Image?.Dispose();
            _imageSourcePreviewCache[sourceNodeName ?? ""] = (filePath, mat.Clone());

            SyncToolGraphToConfig();
            EnsureTemplatePathsAbsolute(_config);

            try
            {
                _lastRunError = null;
                _lastRun = _inspectionService.Inspect(mat, _config);
                __sw.Stop();

                if (_lastRun != null)
                {
                    _lastRun.Timings.NodeTimings[sourceNodeName] = (int)__sw.ElapsedMilliseconds;
                    if (_config.PreprocessNodes != null)
                    {
                        foreach (var preNode in _config.PreprocessNodes)
                        {
                            if (!string.IsNullOrWhiteSpace(preNode.Name))
                            {
                                _lastRun.Timings.NodeTimings[preNode.Name] = 0;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _lastRun = null;
                _lastRunError = "Lỗi khi chạy Flow: " + ex.Message;
            }

            LastResult = _lastRun;
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
            OnPropertyChanged(nameof(Blob_LastRunCount));
        }
    
        private void LoadPreviewImage()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|All Files|*.*"
            };
            if (dlg.ShowDialog() != true)
            {
                return;
            }
    
            using var mat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
            _sharedImage.SetImage(mat);
        }
    
        private void OnCameraFrameCaptured(object? sender, Mat frame)
        {
            if (IsLivePreviewMode && _cameraService.IsRunning)
            {
                if (frame != null && !frame.Empty())
                {
                    _sharedImage.SetImage(frame);
                }
            }
        }
    
        private async Task CaptureCameraImageAsync()
        {
            if (IsLivePreviewMode)
            {
                // ANG LIVE -> B?m nt d? CHUYP V GI? ?NH TINH
                try
                {
                    var mat = await _cameraService.CaptureSnapshotAsync();
                    if (mat != null && !mat.Empty())
                    {
                        IsLivePreviewMode = false;
                        CaptureButtonText = "Live Preview"; // Nhß║Ñn lß║ºn sau ─æß╗â quay lß║íi chß║┐ ─æß╗Ö Live
                        _sharedImage.SetImage(mat);
                        mat.Dispose();
                    }
                    else
                    {
                        MessageBox.Show("Kh├┤ng thß╗â chß╗Ñp ß║únh tß╗½ camera. Vui l├▓ng kiß╗âm tra lß║íi kß║┐t nß╗æi camera trong tab Live Camera.", "Lß╗ùi camera", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lß╗ùi chß╗Ñp ß║únh: {ex.Message}", "Lß╗ùi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // ─ÉANG T─¿NH -> Bß║Ñm n├║t ─æß╗â QUAY Lß║áI LIVE PREVIEW
                IsLivePreviewMode = true;
                CaptureButtonText = "Capture Camera";
                if (!_cameraService.IsRunning)
                {
                    MessageBox.Show("Camera ─æang tß║»t. Vui l├▓ng bß║¡t camera ß╗ƒ tab Live Camera tr╞░ß╗¢c ─æß╗â xem h├¼nh ß║únh trß╗▒c tiß║┐p.", "Th├┤ng b├ío", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    
        private void RunFlow()
        {
            _inspectionService.ResetTracking();
            Mat? snap = null;
            System.Diagnostics.Debug.WriteLine($"RunFlow: Checking for ImageSource nodes. Total nodes: {Nodes.Count}, ImageSources in config: {_config?.ImageSources.Count ?? 0}");
            // Check if there's an ImageSource node and use its image
            int? imageSourceMs = null;
            string? imageSourceNodeRefName = null;
            
            if (_config is not null && _config.ImageSources.Count > 0)
            {
                var imageSourceNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
                if (imageSourceNode is not null)
                {
                    imageSourceNodeRefName = imageSourceNode.RefName;
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    System.Diagnostics.Debug.WriteLine($"RunFlow: Found ImageSource node: {imageSourceNode.RefName}");
                    var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, imageSourceNode.RefName, StringComparison.OrdinalIgnoreCase));
                    if (imgSourceDef is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"RunFlow: Found ImageSourceDef: {imgSourceDef.Name}, SourceType={imgSourceDef.SourceType}");
                        snap = LoadImageFromSourceForPreview(imgSourceDef);
                        if (snap is not null && !snap.Empty())
                        {
                            System.Diagnostics.Debug.WriteLine($"RunFlow: Successfully loaded image from ImageSource: {snap.Width}x{snap.Height}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("RunFlow: Failed to load image from ImageSource");
                            snap?.Dispose();
                            snap = null;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"RunFlow: ImageSourceDef not found for RefName: {imageSourceNode.RefName}");
                    }
                    __sw.Stop();
                    imageSourceMs = (int)__sw.ElapsedMilliseconds;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("RunFlow: No ImageSource node found in graph");
                }
            }
    
            // Fallback to shared image if no ImageSource or failed to load
            if (snap is null)
            {
                System.Diagnostics.Debug.WriteLine("RunFlow: Using shared image as fallback");
                snap = _sharedImage.GetSnapshot();
            }
    
            if (snap is null || _config is null)
            {
                _lastRun = null;
                _lastRunError = "Kh├┤ng c├│ ß║únh hoß║╖c cß║Ñu h├¼nh (config).";
                RefreshPreviews();
                return;
            }
    
            SyncToolGraphToConfig();
            EnsureTemplatePathsAbsolute(_config);
            // Guard: auto-run may happen while templates are not taught yet.
            // In that case, do not attempt to inspect (PatternMatcher may throw); just refresh previews.
            bool HasTemplate(PointDefinition p)
            {
                if (p.TemplateRoi.Width <= 0 || p.TemplateRoi.Height <= 0)
                    return false;
                if (string.IsNullOrWhiteSpace(p.TemplateImageFile))
                    return false;
                return File.Exists(p.TemplateImageFile);
            }
    
            var originOk = HasTemplate(_config.Origin);
            var anyPointNeedsTemplate = _config.Points.Any(p => p.Algorithm == PointFindAlgorithm.TemplateMatch && (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0) && !HasTemplate(p));
            var graphNeedsOrigin = Nodes.Any(n => string.Equals(n.Type, "Origin", StringComparison.OrdinalIgnoreCase));
            var graphNeedsPoint = Nodes.Any(n => string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase));
            if ((graphNeedsOrigin && !originOk) || (graphNeedsPoint && anyPointNeedsTemplate))
            {
                _lastRun = null;
                _lastRunError = "Flow bß╗ï dß╗½ng v├¼ node Origin hoß║╖c Point ─æang chß╗¥ khß╗ƒi tß║ío Template ß║únh.";
                RefreshPreviews();
                RaiseToolPropertyPanelsChanged();
                OnPropertyChanged(nameof(Blob_LastRunCount));
                return;
            }
    
            try
            {
                _lastRunError = null;
                _lastRun = _inspectionService.Inspect(snap, _config);
                if (_lastRun != null)
                {
                    if (imageSourceMs.HasValue && !string.IsNullOrWhiteSpace(imageSourceNodeRefName))
                    {
                        _lastRun.Timings.NodeTimings[imageSourceNodeRefName] = imageSourceMs.Value;
                    }
                    if (_config.PreprocessNodes != null)
                    {
                        foreach (var preNode in _config.PreprocessNodes)
                        {
                            if (!string.IsNullOrWhiteSpace(preNode.Name))
                            {
                                _lastRun.Timings.NodeTimings[preNode.Name] = 0; // Preprocess time is distributed/negligible
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _lastRun = null;
                _lastRunError = "Lß╗ùi khi chß║íy Flow: " + ex.Message;
            }
    
            UpdateNodeExecutionTimes();
            LastResult = _lastRun;
            RefreshInspectionDashboard(_lastRun);
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
            OnPropertyChanged(nameof(Blob_LastRunCount));
        }

    
        private void RefreshPreviews()
        {
            _finalPreviewDirty = true;
            RefreshFinalPreview();
            RefreshSelectedPreview();
        }
    
        private void RefreshFinalPreview()
        {
            if (!_finalPreviewDirty)
            {
                return;
            }
    
            var newFinalItems = new List<OverlayItem>();
            using var rawSnap = _sharedImage.GetSnapshot();
            Mat snapToUse;
            if (rawSnap is not null && !rawSnap.Empty())
            {
                snapToUse = rawSnap;
            }
            else
            {
                var firstImgSource = _config?.ImageSources?.FirstOrDefault();
                if (firstImgSource is not null)
                {
                    snapToUse = LoadImageFromSourceForPreview(firstImgSource) ?? new Mat();
                }
                else
                {
                    snapToUse = new Mat();
                }
            }
    
            using var snap = snapToUse;
            if (_config is not null && PreprocessPreviewEnabled)
            {
                using var processedFinal = _preprocessor.Run(snap, _config.Preprocess);
                _cachedFinalPreviewImage = processedFinal.Empty() ? null : processedFinal.ToBitmapSource();
                FinalPreviewImage = _cachedFinalPreviewImage;
            }
            else
            {
                _cachedFinalPreviewImage = snap.Empty() ? null : snap.ToBitmapSource();
                FinalPreviewImage = _cachedFinalPreviewImage;
            }
    
            if (_config is null)
            {
                _finalPreviewDirty = false;
                return;
            }
            // If user ran the flow, prefer showing overlays from the inspection result
            if (_lastRun is not null)
            {
                AddConfigRois(newFinalItems);
                BuildFinalOverlayFromRunWithConfig(_lastRun, newFinalItems);
            }
            else
            {
                BuildFinalOverlay(snap, newFinalItems);
            }
            
            FinalOverlayItems = newFinalItems;

            _finalPreviewDirty = false;
        }
    
        private void RefreshSelectedPreview()
        {
            var newSelectedNodeOverlayItems = new List<OverlayItem>();
            System.Diagnostics.Debug.WriteLine($"RefreshSelectedPreview: SelectedNode={SelectedNode?.Type}, RefName={SelectedNode?.RefName}");
            
            if (SelectedNode is not null && string.Equals(SelectedNode.Type, "ResultView", StringComparison.OrdinalIgnoreCase))
            {
                using var resultSnap = _sharedImage.GetSnapshot();
                SelectedNodePreviewImage = FinalPreviewImage ?? _cachedFinalPreviewImage ?? (resultSnap is null || resultSnap.Empty() ? null : resultSnap.ToBitmapSource());
                SelectedNodeOverlayItems = FinalOverlayItems;
                ActiveRoiLabel = string.Empty;
                return;
            }

            // Special handling for ImageSource - always load from source regardless of PreprocessPreviewEnabled
            if (SelectedNode is not null && string.Equals(SelectedNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine("Processing ImageSource node preview (special case)");
                var imgSourceDef = SelectedImageSourceDef();
                if (imgSourceDef is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"ImageSourceDef found: Name={imgSourceDef.Name}, SourceType={imgSourceDef.SourceType}");
                    var loadedMat = LoadImageFromSourceForPreview(imgSourceDef);
                    if (loadedMat is not null && !loadedMat.Empty())
                    {
                        System.Diagnostics.Debug.WriteLine($"Setting SelectedNodePreviewImage from ImageSource: {loadedMat.Width}x{loadedMat.Height}");
                        if (_config is not null && PreprocessPreviewEnabled)
                        {
                            using var processed = _preprocessor.Run(loadedMat, _config.Preprocess);
                            SelectedNodePreviewImage = processed.ToBitmapSource();
                        }
                        else
                        {
                            SelectedNodePreviewImage = loadedMat.ToBitmapSource();
                        }
    
                        System.Diagnostics.Debug.WriteLine($"SelectedNodePreviewImage set successfully");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to load image from ImageSource, setting to null");
                        SelectedNodePreviewImage = null;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ImageSourceDef is null, setting to null");
                    SelectedNodePreviewImage = null;
                }
    
                UpdateBlobThresholdPreview(new Mat());
                return;
            }
    
            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new Mat();
            if (_config is not null && PreprocessPreviewEnabled)
            {
                if (SelectedNode is not null && string.Equals(SelectedNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    using var processedSel = ResolveToolPreprocessForPreview(snap, SelectedNode);
                    SelectedNodePreviewImage = processedSel.Empty() ? null : processedSel.ToBitmapSource();
                }
                else
                {
                    if (SelectedNode is not null && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Text", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var processedSel = ResolveToolPreprocessForPreview(snap, SelectedNode);
                        SelectedNodePreviewImage = processedSel.Empty() ? null : processedSel.ToBitmapSource();
                    }
                    else
                    {
                        SelectedNodePreviewImage = _cachedFinalPreviewImage ?? (snap.Empty() ? null : snap.ToBitmapSource());
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("PreprocessPreviewEnabled is false, using raw snap");
                SelectedNodePreviewImage = snap.Empty() ? null : snap.ToBitmapSource();
            }
    
            UpdateBlobThresholdPreview(snap);
            if (_config is null)
            {
                LinePreviewImage = null;
                PointEdgePreviewImage = null;
                BlobThresholdPreviewImage = null;
                return;
            }
    
            RefreshLineRoiPreview(snap);
            RefreshPointEdgePreview(snap);
            if (_lastRun is not null)
            {
                if (SelectedNode is not null)
                {
                    AddConfigRoisForNode(SelectedNode, newSelectedNodeOverlayItems);
                    BuildOverlayForNodeFromRunWithConfig(SelectedNode, _lastRun, newSelectedNodeOverlayItems);
                }
            }
            else
            {
                if (SelectedNode is not null)
                {
                    BuildOverlayForNode(SelectedNode, snap, newSelectedNodeOverlayItems);
                }
            }
            SelectedNodeOverlayItems = newSelectedNodeOverlayItems;
        }
    
        private void UpdateBlobThresholdPreview(Mat snap)
        {
            if (_config is null || SelectedNode is null || !string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                BlobThresholdPreviewImage = null;
                return;
            }
    
            var def = SelectedBlobDetectionDef();
            if (def is null || def.InspectRoi.Width <= 0 || def.InspectRoi.Height <= 0)
            {
                BlobThresholdPreviewImage = null;
                return;
            }
    
            using var matForBlob = ResolveToolPreprocessForPreview(snap, SelectedNode);
            var previewRoi = def.InspectRoi;
            if (def.Rois is not null && def.Rois.Count > 0)
            {
                previewRoi = ComputeBlobInspectRoi(def);
            }
    
            var rect = new OpenCvSharp.Rect(previewRoi.X, previewRoi.Y, previewRoi.Width, previewRoi.Height);
            rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, matForBlob.Width, matForBlob.Height));
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                BlobThresholdPreviewImage = null;
                return;
            }
    
            using var crop = new Mat(matForBlob, rect);
            using var gray = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var bw = new Mat();
            var thr = Math.Clamp(def.Threshold, 0, 255);
            var thrType = def.Polarity == BlobPolarity.DarkOnLight ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
            Cv2.Threshold(gray, bw, thr, 255, thrType);
            if (def.Rois is not null && def.Rois.Count > 0)
            {
                using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);
                var anyInclude = false;
                foreach (var rr in def.Rois)
                {
                    if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                    {
                        continue;
                    }
    
                    var rx = rr.Roi.X - rect.X;
                    var ry = rr.Roi.Y - rect.Y;
                    var r = new OpenCvSharp.Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                    r = r.Intersect(new OpenCvSharp.Rect(0, 0, bw.Cols, bw.Rows));
                    if (r.Width <= 0 || r.Height <= 0)
                    {
                        continue;
                    }
    
                    if (rr.Mode == BlobRoiMode.Include)
                    {
                        anyInclude = true;
                        using var sub = new Mat(mask, r);
                        sub.SetTo(Scalar.White);
                    }
                }
    
                if (!anyInclude)
                {
                    mask.SetTo(Scalar.White);
                }
    
                foreach (var rr in def.Rois)
                {
                    if (rr.Mode != BlobRoiMode.Exclude || rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                    {
                        continue;
                    }
    
                    var rx = rr.Roi.X - rect.X;
                    var ry = rr.Roi.Y - rect.Y;
                    var r = new OpenCvSharp.Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                    r = r.Intersect(new OpenCvSharp.Rect(0, 0, bw.Cols, bw.Rows));
                    if (r.Width <= 0 || r.Height <= 0)
                    {
                        continue;
                    }
    
                    using var sub = new Mat(mask, r);
                    sub.SetTo(Scalar.Black);
                }
    
                Cv2.BitwiseAnd(bw, mask, bw);
            }
    
            Mat view = bw;
            if (bw.Width > 260)
            {
                var scale = 260.0 / bw.Width;
                var h = Math.Max(1, (int)Math.Round(bw.Height * scale));
                var resized = new Mat();
                Cv2.Resize(bw, resized, new OpenCvSharp.Size(260, h), 0, 0, InterpolationFlags.Nearest);
                view = resized;
            }
    
            try
            {
                BlobThresholdPreviewImage = view.ToBitmapSource();
            }
            finally
            {
                if (!ReferenceEquals(view, bw))
                {
                    view.Dispose();
                }
            }
        }
    
        private void AddConfigRois(List<OverlayItem> dst)
        {
            if (_config is null)
            {
                return;
            }
    
            var config = _config;
            if (!ShowRoisInFinalPreview)
            {
                return;
            }
    
            if (config.Origin.SearchRoi.Width > 0 && config.Origin.SearchRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoiWithPose(config.Origin.SearchRoi, Brushes.Lime, "Origin S"));
            }
    
            if (config.Origin.TemplateRoi.Width > 0 && config.Origin.TemplateRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoiWithPose(config.Origin.TemplateRoi, Brushes.Lime, "Origin T"));
            }
    
            foreach (var p in config.Points)
            {
                if (p.SearchRoi.Width <= 0 || p.SearchRoi.Height <= 0)
                {
                    continue;
                }
    
                dst.Add(CreateRotatedRoi(p.SearchRoi, Brushes.DeepSkyBlue, $"{p.Name} S"));
                if (p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(p.TemplateRoi, Brushes.DeepSkyBlue, $"{p.Name} T"));
                }
            }
    
            foreach (var l in config.Lines)
            {
                if (l.SearchRoi.Width <= 0 || l.SearchRoi.Height <= 0)
                {
                    continue;
                }
    
                dst.Add(CreateRotatedRoi(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} L"));
            }
    
            foreach (var c in config.Calipers)
            {
                if (c.SearchRoi.Width <= 0 || c.SearchRoi.Height <= 0)
                {
                    continue;
                }
    
                dst.Add(CreateRotatedRoiWithPose(c.SearchRoi, Brushes.Lime, $"{c.Name} Cal"));
                var stripCount = Math.Clamp(c.StripCount, 1, 100);
                var stripLength = Math.Max(3, c.StripLength);
                if (stripCount > 0)
                {
                    var hasOriginPose = _lastRun?.Origin is not null && (_lastRun.Origin.MatchRect.Width > 0 || _lastRun.Origin.Position.X != 0 || _lastRun.Origin.Position.Y != 0);
                    var originTeach = (config.Origin.TemplateRoi.Width > 0 && config.Origin.TemplateRoi.Height > 0)
                        ? new Point2d(config.Origin.TemplateRoi.X + config.Origin.TemplateRoi.Width / 2.0, config.Origin.TemplateRoi.Y + config.Origin.TemplateRoi.Height / 2.0)
                        : new Point2d(config.Origin.SearchRoi.X + config.Origin.SearchRoi.Width / 2.0, config.Origin.SearchRoi.Y + config.Origin.SearchRoi.Height / 2.0);
                    if (config.Origin.WorldPosition.X != 0 || config.Origin.WorldPosition.Y != 0)
                    {
                        originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
                    }
    
                    var mr = _lastRun?.Origin?.MatchRect ?? default;
                    var originFound = (mr.Width > 0 && mr.Height > 0)
                        ? new Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                        : new Point2d(_lastRun?.Origin?.Position.X ?? originTeach.X, _lastRun?.Origin?.Position.Y ?? originTeach.Y);
                    var angleDeg = hasOriginPose ? _lastRun!.Origin.AngleDeg : 0.0;
    
                    if (c.Orientation == CaliperOrientation.Vertical)
                    {
                        var y1 = c.SearchRoi.Y + (c.SearchRoi.Height - stripLength) / 2.0;
                        var y2 = y1 + stripLength;
                        for (var i = 0; i < stripCount; i++)
                        {
                            var x = c.SearchRoi.X + (i + 0.5) * c.SearchRoi.Width / stripCount;
                            var p1 = new Point2d(x, y1);
                            var p2 = new Point2d(x, y2);
    
                            if (hasOriginPose)
                            {
                                p1 = TransformPose(p1, originTeach, originFound, angleDeg);
                                p2 = TransformPose(p2, originTeach, originFound, angleDeg);
                            }
    
                            dst.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                        }
                    }
                    else
                    {
                        var x1 = c.SearchRoi.X + (c.SearchRoi.Width - stripLength) / 2.0;
                        var x2 = x1 + stripLength;
                        for (var i = 0; i < stripCount; i++)
                        {
                            var y = c.SearchRoi.Y + (i + 0.5) * c.SearchRoi.Height / stripCount;
                            var p1 = new Point2d(x1, y);
                            var p2 = new Point2d(x2, y);
    
                            if (hasOriginPose)
                            {
                                p1 = TransformPose(p1, originTeach, originFound, angleDeg);
                                p2 = TransformPose(p2, originTeach, originFound, angleDeg);
                            }
    
                            dst.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                        }
                    }
                }
            }
    
            foreach (var b in _config.BlobDetections)
            {
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
    
                    continue;
                }
    
                if (b.InspectRoi.Width <= 0 || b.InspectRoi.Height <= 0)
                {
                    continue;
                }
    
                dst.Add(CreateRotatedRoi(b.InspectRoi, Brushes.Gold, $"{b.Name} B"));
            }
    
            void AddSurfaceCompareRoi(string surfaceCompareName)
            {
                var sc = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
                if (sc is null)
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
    
            foreach (var sc in config.SurfaceCompares)
            {
                AddSurfaceCompareRoi(sc.Name);
            }
    
            foreach (var c in config.CodeDetections)
            {
                if (c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(c.SearchRoi, Brushes.Lime, $"{c.Name} C"));
                }
            }
    
            if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoi(_config.DefectConfig.InspectRoi, Brushes.Orange, "DefectROI"));
            }
        }
    
        private static void BuildFinalOverlayFromRun(InspectionResult run, List<OverlayItem> dst, VisionConfig? config)
        {
            if (run.Origin is not null)
            {
                var mr = run.Origin.MatchRect;
                if (mr.Width > 0 && mr.Height > 0)
                {
                    var cx = mr.X + mr.Width / 2.0;
                    var cy = mr.Y + mr.Height / 2.0;
                    var angleDeg = run.Origin.AngleDeg;
                    var a = angleDeg * Math.PI / 180.0;
                    var cos = Math.Cos(a);
                    var sin = Math.Sin(a);
                    var hw = mr.Width / 2.0;
                    var hh = mr.Height / 2.0;
                    var hx = new Point2d(hw * cos, hw * sin);
                    var hy = new Point2d(-hh * sin, hh * cos);
                    var cp1 = new Point2d(cx - hx.X, cy - hx.Y);
                    var cp2 = new Point2d(cx + hx.X, cy + hx.Y);
                    var cp3 = new Point2d(cx - hy.X, cy - hy.Y);
                    var cp4 = new Point2d(cx + hy.X, cy + hy.Y);
                    dst.Add(new OverlayLineItem { X1 = cp1.X, Y1 = cp1.Y, X2 = cp2.X, Y2 = cp2.Y, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
                    dst.Add(new OverlayLineItem { X1 = cp3.X, Y1 = cp3.Y, X2 = cp4.X, Y2 = cp4.Y, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
                }
    
                dst.Add(new OverlayPointItem { X = mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : run.Origin.Position.X, Y = mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : run.Origin.Position.Y, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red, Label = $"Origin: {run.Origin.Score:0.00}" });
            }
    
            foreach (var p in run.Points)
            {
                var mr = p.MatchRect;
                if (mr.Width > 0 && mr.Height > 0)
                {
                    var cx = p.Position.X;
                    var cy = p.Position.Y;
                    dst.Add(new OverlayLineItem { X1 = cx - mr.Width / 2.0, Y1 = cy, X2 = cx + mr.Width / 2.0, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - mr.Height / 2.0, X2 = cx, Y2 = cy + mr.Height / 2.0, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                }
    
                dst.Add(new OverlayPointItem { X = p.Position.X, Y = p.Position.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red, Label = p.Name });
            }
    
            var distanceAnchorMap = new System.Collections.Generic.Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in run.Points)
            {
                distanceAnchorMap[p.Name] = p.Position;
            }
    
            foreach (var c in run.CircleFinders)
            {
                if (c.Found)
                {
                    distanceAnchorMap[c.Name] = c.Center;
                }
            }
    
            foreach (var d in run.Diameters)
            {
                if (d.Found)
                {
                    distanceAnchorMap[d.Name] = d.Center;
                }
            }
    
            foreach (var l in run.Lines)
            {
                if (!l.Found)
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = l.P1.X, Y1 = l.P1.Y, X2 = l.P2.X, Y2 = l.P2.Y, Stroke = Brushes.MediumPurple, Label = l.Name });
            }
    
            foreach (var lpd in run.LinePairDetections)
            {
                if (!lpd.Found)
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = lpd.L1P1.X, Y1 = lpd.L1P1.Y, X2 = lpd.L1P2.X, Y2 = lpd.L1P2.Y, Stroke = Brushes.MediumPurple, Label = lpd.Name });
                dst.Add(new OverlayLineItem { X1 = lpd.L2P1.X, Y1 = lpd.L2P1.Y, X2 = lpd.L2P2.X, Y2 = lpd.L2P2.Y, Stroke = Brushes.MediumPurple, Label = string.Empty });
                dst.Add(new OverlayLineItem { X1 = lpd.ClosestA.X, Y1 = lpd.ClosestA.Y, X2 = lpd.ClosestB.X, Y2 = lpd.ClosestB.Y, Stroke = lpd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{lpd.Name}: {lpd.Value:0.###}" });
            }
    
            foreach (var epd in run.EdgePairDetections)
            {
                if (!epd.Found || double.IsNaN(epd.Value))
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = epd.L1P1.X, Y1 = epd.L1P1.Y, X2 = epd.L1P2.X, Y2 = epd.L1P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E1" });
                dst.Add(new OverlayLineItem { X1 = epd.L2P1.X, Y1 = epd.L2P1.Y, X2 = epd.L2P2.X, Y2 = epd.L2P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E2" });
                dst.Add(new OverlayLineItem { X1 = epd.ClosestA.X, Y1 = epd.ClosestA.Y, X2 = epd.ClosestB.X, Y2 = epd.ClosestB.Y, Stroke = epd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{epd.Name}: {epd.Value:0.###}" });
            }
    
            foreach (var ep in run.EdgePairs)
            {
                if (!ep.Found || double.IsNaN(ep.Value))
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = ep.L1P1.X, Y1 = ep.L1P1.Y, X2 = ep.L1P2.X, Y2 = ep.L1P2.Y, Stroke = Brushes.MediumPurple, Label = ep.RefA });
                dst.Add(new OverlayLineItem { X1 = ep.L2P1.X, Y1 = ep.L2P1.Y, X2 = ep.L2P2.X, Y2 = ep.L2P2.Y, Stroke = Brushes.MediumPurple, Label = ep.RefB });
                dst.Add(new OverlayLineItem { X1 = ep.ClosestA.X, Y1 = ep.ClosestA.Y, X2 = ep.ClosestB.X, Y2 = ep.ClosestB.Y, Stroke = ep.Pass ? Brushes.Lime : Brushes.Red, Label = $"{ep.Name}: {ep.Value:0.###}" });
            }
    
            foreach (var cal in run.Calipers)
            {
                if (!cal.Found)
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = cal.LineP1.X, Y1 = cal.LineP1.Y, X2 = cal.LineP2.X, Y2 = cal.LineP2.Y, Stroke = Brushes.Gold, Label = cal.Name });
                var step = Math.Max(1, cal.Points.Count / 60);
                for (var i = 0; i < cal.Points.Count; i += step)
                {
                    var p = cal.Points[i];
                    dst.Add(new OverlayPointItem { X = p.X, Y = p.Y, Radius = 2.0, Stroke = Brushes.Gold, Label = string.Empty });
                }
            }
    
            foreach (var cdt in run.CodeDetections)
            {
                if (!cdt.Found)
                {
                    continue;
                }
    
                var bb = cdt.BoundingBox;
                if (bb.Width > 0 && bb.Height > 0)
                {
                    var angleDeg = run.Origin?.AngleDeg ?? 0.0;
                    dst.Add(new OverlayRectItem
                    {
                        X = bb.X,
                        Y = bb.Y,
                        Width = bb.Width,
                        Height = bb.Height,
                        Angle = angleDeg,
                        Stroke = Brushes.Lime,
                        Label = $"{cdt.Name}: {cdt.Text}"
                    });
                }
            }
    
            foreach (var d in run.Distances)
            {
                if (!distanceAnchorMap.TryGetValue(d.PointA, out var pa) || !distanceAnchorMap.TryGetValue(d.PointB, out var pb))
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, Stroke = d.Pass ? Brushes.Lime : Brushes.Red, Label = $"{d.Name}: {d.Value:0.###}" });
            }
    
            foreach (var dd in run.LineToLineDistances)
            {
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.00}" });
            }
    
            foreach (var dd in run.PointToLineDistances)
            {
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.00}" });
            }
    
            foreach (var c in run.CircleFinders)
            {
                if (!c.Found || c.RadiusPx <= 0)
                {
                    continue;
                }
    
                AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                AddCross(dst, c.Center.X, c.Center.Y, size: 10.0, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.MediumPurple, Label = c.Name });
            }
    
            foreach (var d in run.Diameters)
            {
                if (!d.Found || double.IsNaN(d.Value) || d.RadiusPx <= 0)
                {
                    continue;
                }
    
                var stroke = d.Pass ? Brushes.Lime : Brushes.Red;
                AddCircle(dst, d.Center.X, d.Center.Y, d.RadiusPx, stroke: stroke, strokeThickness: 2.0);
                AddCross(dst, d.Center.X, d.Center.Y, size: 12.0, stroke: stroke, strokeThickness: 2.0);
                dst.Add(new OverlayPointItem { X = d.Center.X, Y = d.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{d.Name}: {d.Value:0.###} mm" });
            }
    
            foreach (var a in run.Angles)
            {
                if (double.IsNaN(a.ValueDeg))
                {
                    continue;
                }
    
                if (!a.Found)
                {
                    dst.Add(new OverlayPointItem { X = 12, Y = 12, Radius = 1.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}∩┐╜" });
                    continue;
                }
    
                // In final overlay we may not know the current preview image size, so draw short rays.
                var len = 60.0;
                dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.ADir.X * len, Y2 = a.Intersection.Y + a.ADir.Y * len, Stroke = Brushes.MediumPurple, Label = a.LineA });
                dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.BDir.X * len, Y2 = a.Intersection.Y + a.BDir.Y * len, Stroke = Brushes.Gold, Label = a.LineB });
                AddAngleArc(dst, a.Intersection.X, a.Intersection.Y, a.ADir.X, a.ADir.Y, a.BDir.X, a.BDir.Y, radius: 35.0, stroke: a.Pass ? Brushes.Lime : Brushes.Red);
                dst.Add(new OverlayPointItem { X = a.Intersection.X, Y = a.Intersection.Y, Radius = 3.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}∩┐╜" });
            }
    
            if (run.SurfaceCompares is not null)
            {
                foreach (var sc in run.SurfaceCompares)
                {
                    var stroke = sc.Pass ? Brushes.Lime : Brushes.Red;
                    var status = sc.Pass ? "OK" : "NG";
                    if (sc.Defects is not null && sc.Defects.Count > 0)
                    {
                        var n = Math.Min(sc.Defects.Count, 300);
                        for (var i = 0; i < n; i++)
                        {
                            var d = sc.Defects[i];
                            var r = d.BoundingBox;
                            if (r.Width > 0 && r.Height > 0)
                            {
                                dst.Add(new OverlayRectItem { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, Stroke = stroke, StrokeThickness = 2.0, Angle = d.Angle, Label = string.Empty });
                            }
                        }
                    }
    
                    var lx = 12.0;
                    var ly = 12.0;
                    if (config is not null)
                    {
                        var scDef = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, sc.Name, StringComparison.OrdinalIgnoreCase));
                        if (scDef is not null && scDef.InspectRoi.Width > 0 && scDef.InspectRoi.Height > 0)
                        {
                            if (run.Origin is not null)
                            {
                                var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
                                var tr = TransformPose(new Point2d(scDef.InspectRoi.X, scDef.InspectRoi.Y), originTeach, run.Origin.Position, run.Origin.AngleDeg);
                                lx = tr.X + 2;
                                ly = tr.Y + 2;
                            }
                            else
                            {
                                lx = scDef.InspectRoi.X + 2;
                                ly = scDef.InspectRoi.Y + 2;
                            }
                        }
                    }
    
                    dst.Add(new OverlayPointItem { X = lx, Y = ly, Radius = 1.0, Stroke = stroke, Label = $"{sc.Name} [{status}]: S\u1ed1 l\u1ed7i: {sc.Count}, S.L\u1edbn nh\u1ea5t: {sc.MaxArea:0}" });
                }
            }
    
            if (run.Conditions.Count > 0)
            {
                var y = 14.0;
                foreach (var c in run.Conditions)
                {
                    var okText = c.Pass ? "OK" : "NG";
                    dst.Add(new OverlayPointItem { X = 12, Y = y, Radius = 1.0, Stroke = c.Pass ? Brushes.Lime : Brushes.Red, Label = $"{c.Name}: {okText}" + (string.IsNullOrWhiteSpace(c.Error) ? string.Empty : $" ({c.Error})") });
                    y += 16.0;
                }
            }
    
            if (config is not null && config.TextNodes is not null && config.TextNodes.Count > 0)
            {
                Dictionary<string, ConditionEvaluator.Variable>? vars = null;
                try
                {
                    vars = ConditionEvaluator.BuildVariableMap(run);
                }
                catch
                {
                    vars = null;
                }
    
                foreach (var t in config.TextNodes)
                {
                    if (t is null || string.IsNullOrWhiteSpace(t.Name))
                    {
                        continue;
                    }
    
                    var text = EvaluateTextTemplate(t.Text ?? string.Empty, vars);
                    var brush = TryParseHexBrush(t.DefaultColor) ?? Brushes.White;
                    if (vars is not null && t.Conditions is not null)
                    {
                        foreach (var c in t.Conditions)
                        {
                            if (c is null || string.IsNullOrWhiteSpace(c.Expression))
                                continue;
                            try
                            {
                                if (ConditionEvaluator.Evaluate(c.Expression, vars))
                                {
                                    brush = TryParseHexBrush(c.Color) ?? brush;
                                    break;
                                }
                            }
                            catch
                            {
                            // ignore bad expressions
                            }
                        }
                    }
    
                    dst.Add(new OverlayTextItem { X = t.X, Y = t.Y, Text = text, Foreground = brush, Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)) });
                }
            }
        }
    
        private void BuildOverlayForNodeFromRun(ToolGraphNodeViewModel node, InspectionResult run, List<OverlayItem> dst)
        {
            if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
            {
                if (run.Origin is null)
                {
                    return;
                }
    
                var mr = run.Origin.MatchRect;
                if (mr.Width > 0 && mr.Height > 0)
                {
                    var cx = mr.X + mr.Width / 2.0;
                    var cy = mr.Y + mr.Height / 2.0;
                    var angleDeg = run.Origin.AngleDeg;
                    var a = angleDeg * Math.PI / 180.0;
                    var cos = Math.Cos(a);
                    var sin = Math.Sin(a);
                    var hw = mr.Width / 2.0;
                    var hh = mr.Height / 2.0;
                    var hx = new Point2d(hw * cos, hw * sin);
                    var hy = new Point2d(-hh * sin, hh * cos);
                    var cp1 = new Point2d(cx - hx.X, cy - hx.Y);
                    var cp2 = new Point2d(cx + hx.X, cy + hx.Y);
                    var cp3 = new Point2d(cx - hy.X, cy - hy.Y);
                    var cp4 = new Point2d(cx + hy.X, cy + hy.Y);
                    dst.Add(new OverlayLineItem { X1 = cp1.X, Y1 = cp1.Y, X2 = cp2.X, Y2 = cp2.Y, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
                    dst.Add(new OverlayLineItem { X1 = cp3.X, Y1 = cp3.Y, X2 = cp4.X, Y2 = cp4.Y, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
                }
    
                dst.Add(new OverlayPointItem { X = mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : run.Origin.Position.X, Y = mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : run.Origin.Position.Y, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red, Label = $"Origin: {run.Origin.Score:0.00}" });
                if (run.Origin.FeaturePoints != null && run.Origin.Pass)
                {
                    var ptBrush = Brushes.LawnGreen;
                    foreach (var fp in run.Origin.FeaturePoints)
                    {
                        dst.Add(new OverlayPointItem { X = fp.X, Y = fp.Y, Radius = 1.0, Stroke = ptBrush, StrokeThickness = 1.0, Label = string.Empty });
                    }
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Point", StringComparison.OrdinalIgnoreCase))
            {
                var p = run.Points.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (p is null)
                {
                    return;
                }
    
                var mr = p.MatchRect;
                if (mr.Width > 0 && mr.Height > 0)
                {
                    var cx = p.Position.X;
                    var cy = p.Position.Y;
                    dst.Add(new OverlayLineItem { X1 = cx - mr.Width / 2.0, Y1 = cy, X2 = cx + mr.Width / 2.0, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - mr.Height / 2.0, X2 = cx, Y2 = cy + mr.Height / 2.0, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                }
    
                dst.Add(new OverlayPointItem { X = p.Position.X, Y = p.Position.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red, Label = p.Name });
                if (p.FeaturePoints != null && p.Pass)
                {
                    var ptBrush = Brushes.DeepSkyBlue;
                    foreach (var fp in p.FeaturePoints)
                    {
                        dst.Add(new OverlayPointItem { X = fp.X, Y = fp.Y, Radius = 1.0, Stroke = ptBrush, StrokeThickness = 1.0, Label = string.Empty });
                    }
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                var l = run.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (l is null || !l.Found)
                {
                    return;
                }
    
                dst.Add(new OverlayLineItem { X1 = l.P1.X, Y1 = l.P1.Y, X2 = l.P2.X, Y2 = l.P2.Y, Stroke = Brushes.MediumPurple, Label = l.Name });
                return;
            }
    
            if (string.Equals(node.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
            {
                var r = run.Calipers.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null)
                {
                    return;
                }
    
                if (r.Found)
                {
                    dst.Add(new OverlayLineItem { X1 = r.LineP1.X, Y1 = r.LineP1.Y, X2 = r.LineP2.X, Y2 = r.LineP2.Y, Stroke = Brushes.Lime, Label = r.Name });
                }
    
                if (r.Points is not null)
                {
                    var n = Math.Min(r.Points.Count, 60);
                    for (var i = 0; i < n; i++)
                    {
                        var p = r.Points[i];
                        dst.Add(new OverlayPointItem { X = p.X, Y = p.Y, Radius = 2.0, Stroke = Brushes.Gold, Label = string.Empty });
                    }
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
            {
                var r = run.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null || !r.Found || double.IsNaN(r.Value))
                {
                    return;
                }
    
                dst.Add(new OverlayLineItem { X1 = r.L1P1.X, Y1 = r.L1P1.Y, X2 = r.L1P2.X, Y2 = r.L1P2.Y, Stroke = Brushes.MediumPurple, Label = $"{r.Name} E1" });
                dst.Add(new OverlayLineItem { X1 = r.L2P1.X, Y1 = r.L2P1.Y, X2 = r.L2P2.X, Y2 = r.L2P2.Y, Stroke = Brushes.MediumPurple, Label = $"{r.Name} E2" });
                dst.Add(new OverlayLineItem { X1 = r.ClosestA.X, Y1 = r.ClosestA.Y, X2 = r.ClosestB.X, Y2 = r.ClosestB.Y, Stroke = r.Pass ? Brushes.Lime : Brushes.Red, Label = $"{r.Name}: {r.Value:0.###}" });
                return;
            }
    
            if (string.Equals(node.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                var c = run.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (c is null || !c.Found || c.RadiusPx <= 0)
                {
                    return;
                }
    
                AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                AddCross(dst, c.Center.X, c.Center.Y, size: 12.0, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.MediumPurple, Label = c.Name });
                return;
            }
    
            if (string.Equals(node.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
            {
                var d = run.Diameters.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (d is null || !d.Found || double.IsNaN(d.Value) || d.RadiusPx <= 0)
                {
                    return;
                }
    
                var stroke = d.Pass ? Brushes.Lime : Brushes.Red;
                AddCircle(dst, d.Center.X, d.Center.Y, d.RadiusPx, stroke: stroke, strokeThickness: 2.0);
                AddCross(dst, d.Center.X, d.Center.Y, size: 12.0, stroke: stroke, strokeThickness: 2.0);
                dst.Add(new OverlayPointItem { X = d.Center.X, Y = d.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{d.Name}: {d.Value:0.###} mm" });
                return;
            }
    
            if (string.Equals(node.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var a = run.Angles.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (a is null || double.IsNaN(a.ValueDeg))
                {
                    return;
                }
    
                if (a.Found)
                {
                    if (_lastPreviewImageWidth > 0 && _lastPreviewImageHeight > 0)
                    {
                        var ip = new System.Windows.Point(a.Intersection.X, a.Intersection.Y);
                        var aDir = new System.Windows.Point(a.ADir.X, a.ADir.Y);
                        var bDir = new System.Windows.Point(a.BDir.X, a.BDir.Y);
                        if (TryClipInfiniteLineToImage(ip, aDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var a1, out var a2))
                        {
                            dst.Add(new OverlayLineItem { X1 = a1.X, Y1 = a1.Y, X2 = a2.X, Y2 = a2.Y, Stroke = Brushes.MediumPurple, Label = a.LineA });
                        }
                        else
                        {
                            var len = 60.0;
                            dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.ADir.X * len, Y2 = a.Intersection.Y + a.ADir.Y * len, Stroke = Brushes.MediumPurple, Label = a.LineA });
                        }
    
                        if (TryClipInfiniteLineToImage(ip, bDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var b1, out var b2))
                        {
                            dst.Add(new OverlayLineItem { X1 = b1.X, Y1 = b1.Y, X2 = b2.X, Y2 = b2.Y, Stroke = Brushes.Gold, Label = a.LineB });
                        }
                        else
                        {
                            var len = 60.0;
                            dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.BDir.X * len, Y2 = a.Intersection.Y + a.BDir.Y * len, Stroke = Brushes.Gold, Label = a.LineB });
                        }
                    }
                    else
                    {
                        var len = 60.0;
                        dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.ADir.X * len, Y2 = a.Intersection.Y + a.ADir.Y * len, Stroke = Brushes.MediumPurple, Label = a.LineA });
                        dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.BDir.X * len, Y2 = a.Intersection.Y + a.BDir.Y * len, Stroke = Brushes.Gold, Label = a.LineB });
                    }
    
                    AddAngleArc(dst, a.Intersection.X, a.Intersection.Y, a.ADir.X, a.ADir.Y, a.BDir.X, a.BDir.Y, radius: 35.0, stroke: a.Pass ? Brushes.Lime : Brushes.Red);
                    dst.Add(new OverlayPointItem { X = a.Intersection.X, Y = a.Intersection.Y, Radius = 3.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}∩┐╜" });
                }
                else
                {
                    dst.Add(new OverlayPointItem { X = 12, Y = 12, Radius = 1.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}∩┐╜" });
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                var d = run.Distances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (d is null)
                {
                    return;
                }
    
                var anchorMap = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in run.Points)
                {
                    anchorMap[p.Name] = p.Position;
                }
    
                foreach (var c in run.CircleFinders)
                {
                    if (c.Found)
                    {
                        anchorMap[c.Name] = c.Center;
                    }
                }
    
                foreach (var dia in run.Diameters)
                {
                    if (dia.Found)
                    {
                        anchorMap[dia.Name] = dia.Center;
                    }
                }
    
                if (!anchorMap.TryGetValue(d.PointA, out var a) || !anchorMap.TryGetValue(d.PointB, out var b))
                {
                    return;
                }
    
                void AddAnchorOverlay(string anchorName)
                {
                    if (string.IsNullOrWhiteSpace(anchorName))
                    {
                        return;
                    }
    
                    var p = run.Points.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
                    if (p is not null)
                    {
                        dst.Add(new OverlayPointItem { X = p.Position.X, Y = p.Position.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red, Label = p.Name });
                        return;
                    }
    
                    var c = run.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
                    if (c is not null && c.Found && c.RadiusPx > 0)
                    {
                        AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                        AddCross(dst, c.Center.X, c.Center.Y, size: 12.0, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                        dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.MediumPurple, Label = c.Name });
                        return;
                    }
    
                    var dia = run.Diameters.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
                    if (dia is not null && dia.Found && dia.RadiusPx > 0)
                    {
                        var stroke = dia.Pass ? Brushes.Lime : Brushes.Red;
                        AddCircle(dst, dia.Center.X, dia.Center.Y, dia.RadiusPx, stroke: stroke, strokeThickness: 2.0);
                        AddCross(dst, dia.Center.X, dia.Center.Y, size: 12.0, stroke: stroke, strokeThickness: 2.0);
                        dst.Add(new OverlayPointItem { X = dia.Center.X, Y = dia.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{dia.Name}: {dia.Value:0.###} mm" });
                    }
                }
    
                AddAnchorOverlay(d.PointA);
                AddAnchorOverlay(d.PointB);
                dst.Add(new OverlayLineItem { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = d.Pass ? Brushes.Lime : Brushes.Red, Label = $"{d.Name}: {d.Value:0.###}" });
                return;
            }
    
            if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var dd = run.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (dd is null)
                {
                    return;
                }
    
                static LineDetectResult? ResolveLineRef(InspectionResult r, string name)
                {
                    var l = r.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (l is not null)
                        return l;
                    var c = r.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (c is not null && c.Found)
                    {
                        var dx = c.LineP2.X - c.LineP1.X;
                        var dy = c.LineP2.Y - c.LineP1.Y;
                        var len = Math.Sqrt(dx * dx + dy * dy);
                        return new LineDetectResult(c.Name, c.LineP1, c.LineP2, len, Found: true);
                    }
    
                    return null;
                }
    
                var la = ResolveLineRef(run, dd.RefA);
                var lb = ResolveLineRef(run, dd.RefB);
                if (la is null || lb is null || !la.Found || !lb.Found)
                {
                    return;
                }
    
                dst.Add(new OverlayLineItem { X1 = la.P1.X, Y1 = la.P1.Y, X2 = la.P2.X, Y2 = la.P2.Y, Stroke = Brushes.MediumPurple, Label = la.Name });
                dst.Add(new OverlayLineItem { X1 = lb.P1.X, Y1 = lb.P1.Y, X2 = lb.P2.X, Y2 = lb.P2.Y, Stroke = Brushes.MediumPurple, Label = lb.Name });
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.###}" });
                return;
            }
    
            if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var dd = run.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (dd is null)
                {
                    return;
                }
    
                var p = run.Points.FirstOrDefault(x => string.Equals(x.Name, dd.RefA, StringComparison.OrdinalIgnoreCase));
                LineDetectResult? l;
                {
                    var ll = run.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.RefB, StringComparison.OrdinalIgnoreCase));
                    if (ll is not null)
                        l = ll;
                    else
                    {
                        var c = run.Calipers.FirstOrDefault(x => string.Equals(x.Name, dd.RefB, StringComparison.OrdinalIgnoreCase));
                        if (c is not null && c.Found)
                        {
                            var dx = c.LineP2.X - c.LineP1.X;
                            var dy = c.LineP2.Y - c.LineP1.Y;
                            var len = Math.Sqrt(dx * dx + dy * dy);
                            l = new LineDetectResult(c.Name, c.LineP1, c.LineP2, len, Found: true);
                        }
                        else
                        {
                            l = null;
                        }
                    }
                }
    
                if (p is null || l is null || !l.Found)
                {
                    return;
                }
    
                dst.Add(new OverlayPointItem { X = p.Position.X, Y = p.Position.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red, Label = p.Name });
                dst.Add(new OverlayLineItem { X1 = l.P1.X, Y1 = l.P1.Y, X2 = l.P2.X, Y2 = l.P2.Y, Stroke = Brushes.MediumPurple, Label = l.Name });
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.###}" });
                return;
            }
    
            if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
            {
                var sc = run.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (sc is null)
                {
                    return;
                }
    
                var scDef = _config?.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, sc.Name, StringComparison.OrdinalIgnoreCase));
                var stroke = sc.Pass ? Brushes.Lime : Brushes.Red;
                var status = sc.Pass ? "OK" : "NG";
                if (sc.Defects is not null && sc.Defects.Count > 0)
                {
                    var n = Math.Min(sc.Defects.Count, 300);
                    for (var i = 0; i < n; i++)
                    {
                        var d = sc.Defects[i];
                        var r = d.BoundingBox;
                        if (r.Width > 0 && r.Height > 0)
                        {
                            dst.Add(new OverlayRectItem { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, Stroke = stroke, StrokeThickness = 2.0, Angle = d.Angle, Label = string.Empty });
                        }
                    }
                }
    
                double lx = 12, ly = 12;
                if (scDef is not null)
                {
                    lx = scDef.InspectRoi.X + 2;
                    ly = scDef.InspectRoi.Y + 2;
                }
    
                dst.Add(new OverlayPointItem { X = lx, Y = ly, Radius = 1.0, Stroke = stroke, Label = $"{sc.Name} [{status}]: S\u1ed1 l\u1ed7i: {sc.Count}, Di\u1ec7n t\u00edch l\u1edbn nh\u1ea5t: {sc.MaxArea:0}" });
                return;
            }
    
            if (string.Equals(node.Type, "Condition", StringComparison.OrdinalIgnoreCase))
            {
                var c = run.Conditions.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (c is null)
                {
                    return;
                }
    
                var okText = c.Pass ? "OK" : "NG";
                dst.Add(new OverlayPointItem { X = 12, Y = 12, Radius = 1.0, Stroke = c.Pass ? Brushes.Lime : Brushes.Red, Label = $"{c.Name}: {okText}" + (string.IsNullOrWhiteSpace(c.Error) ? string.Empty : $" ({c.Error})") });
                return;
            }
    
            if (string.Equals(node.Type, "Text", StringComparison.OrdinalIgnoreCase))
            {
                if (_config is null)
                    return;
                var t = _config.TextNodes.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (t is null)
                    return;
                Dictionary<string, ConditionEvaluator.Variable>? vars = null;
                try
                {
                    vars = ConditionEvaluator.BuildVariableMap(run);
                }
                catch
                {
                    vars = null;
                }
    
                var text = EvaluateTextTemplate(t.Text ?? string.Empty, vars);
                var brush = TryParseHexBrush(t.DefaultColor) ?? Brushes.White;
                if (vars is not null && t.Conditions is not null)
                {
                    foreach (var c in t.Conditions)
                    {
                        if (c is null || string.IsNullOrWhiteSpace(c.Expression))
                            continue;
                        try
                        {
                            if (ConditionEvaluator.Evaluate(c.Expression, vars))
                            {
                                brush = TryParseHexBrush(c.Color) ?? brush;
                                break;
                            }
                        }
                        catch
                        { /* ignore bad expressions */
                        }
                    }
                }

                dst.Add(new OverlayTextItem { X = t.X, Y = t.Y, Text = text, Foreground = brush, Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)) });
                return;
            }
        }
    
        private void BuildOverlayForNode(ToolGraphNodeViewModel node, Mat image, List<OverlayItem> dst)
        {
            if (_config is null)
            {
                return;
            }
    
            var showRois = ShowRoisInSelectedPreview;
            if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
            {
                if (showRois && _config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(_config.Origin.SearchRoi, Brushes.Lime, "Origin S"));
                }
    
                if (showRois && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(_config.Origin.TemplateRoi, Brushes.Gold, "Origin T"));
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
    
                if (showRois && p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(p.SearchRoi, Brushes.DeepSkyBlue, $"{p.Name} S"));
                }
    
                if (showRois && p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(p.TemplateRoi, Brushes.Gold, $"{p.Name} T"));
                }
    
                if (p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                {
                    var cx = p.TemplateRoi.X + p.TemplateRoi.Width / 2.0;
                    var cy = p.TemplateRoi.Y + p.TemplateRoi.Height / 2.0;
                    dst.Add(new OverlayPointItem { X = cx + p.OffsetPx.X, Y = cy + p.OffsetPx.Y, Stroke = Brushes.DeepSkyBlue, Label = p.Name });
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (l is null)
                {
                    return;
                }
    
                if (showRois && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} L"));
                }
    
                if (!LinePreviewEnabled)
                {
                    return;
                }
    
                using var processed = _preprocessor.Run(image, _config.Preprocess);
                var det = _lineDetector.DetectLongestLine(processed, l.SearchRoi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
                if (det.Found)
                {
                    dst.Add(new OverlayLineItem { X1 = det.P1.X, Y1 = det.P1.Y, X2 = det.P2.X, Y2 = det.P2.Y, Stroke = Brushes.MediumPurple, Label = l.Name });
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
    
                var pa = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointA, StringComparison.OrdinalIgnoreCase));
                var pb = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointB, StringComparison.OrdinalIgnoreCase));
                if (pa is null || pb is null)
                {
                    return;
                }
    
                dst.Add(new OverlayPointItem { X = pa.WorldPosition.X, Y = pa.WorldPosition.Y, Stroke = Brushes.DeepSkyBlue, Label = pa.Name });
                dst.Add(new OverlayPointItem { X = pb.WorldPosition.X, Y = pb.WorldPosition.Y, Stroke = Brushes.DeepSkyBlue, Label = pb.Name });
                var distPx = Geometry2D.Distance(new Point2d(pa.WorldPosition.X, pa.WorldPosition.Y), new Point2d(pb.WorldPosition.X, pb.WorldPosition.Y));
                var value = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
                dst.Add(new OverlayLineItem { X1 = pa.WorldPosition.X, Y1 = pa.WorldPosition.Y, X2 = pb.WorldPosition.X, Y2 = pb.WorldPosition.Y, Stroke = Brushes.Lime, Label = $"{d.Name}: {value:0.###}" });
                return;
            }
    
            if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var dd = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (dd is null)
                {
                    return;
                }
    
                var a = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                var b = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.LineB, StringComparison.OrdinalIgnoreCase));
                if (a is null || b is null)
                {
                    return;
                }
    
                using var processed = _preprocessor.Run(image, _config.Preprocess);
                var la = _lineDetector.DetectLongestLine(processed, a.SearchRoi, a.Canny1, a.Canny2, a.HoughThreshold, a.MinLineLength, a.MaxLineGap);
                var lb = _lineDetector.DetectLongestLine(processed, b.SearchRoi, b.Canny1, b.Canny2, b.HoughThreshold, b.MinLineLength, b.MaxLineGap);
                if (!la.Found || !lb.Found)
                {
                    return;
                }
    
                dst.Add(new OverlayLineItem { X1 = la.P1.X, Y1 = la.P1.Y, X2 = la.P2.X, Y2 = la.P2.Y, Stroke = Brushes.MediumPurple, Label = a.Name });
                dst.Add(new OverlayLineItem { X1 = lb.P1.X, Y1 = lb.P1.Y, X2 = lb.P2.X, Y2 = lb.P2.Y, Stroke = Brushes.MediumPurple, Label = b.Name });
                var(distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
                var value = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
                dst.Add(new OverlayLineItem { X1 = ca.X, Y1 = ca.Y, X2 = cb.X, Y2 = cb.Y, Stroke = Brushes.Lime, Label = $"{dd.Name}: {value:0.###}" });
                return;
            }
    
            if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var dd = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (dd is null)
                {
                    return;
                }
    
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, dd.Point, StringComparison.OrdinalIgnoreCase));
                var ldef = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.Line, StringComparison.OrdinalIgnoreCase));
                if (p is null || ldef is null)
                {
                    return;
                }
    
                using var processed = _preprocessor.Run(image, _config.Preprocess);
                var l = _lineDetector.DetectLongestLine(processed, ldef.SearchRoi, ldef.Canny1, ldef.Canny2, ldef.HoughThreshold, ldef.MinLineLength, ldef.MaxLineGap);
                if (!l.Found)
                {
                    return;
                }
    
                var pp = new Point2d(p.WorldPosition.X, p.WorldPosition.Y);
                var(distPx, closestOnSeg) = Geometry2D.PointToSegmentDistance(pp, l.P1, l.P2);
                var value = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
                dst.Add(new OverlayPointItem { X = pp.X, Y = pp.Y, Stroke = Brushes.DeepSkyBlue, Label = p.Name });
                dst.Add(new OverlayLineItem { X1 = l.P1.X, Y1 = l.P1.Y, X2 = l.P2.X, Y2 = l.P2.Y, Stroke = Brushes.MediumPurple, Label = ldef.Name });
                dst.Add(new OverlayLineItem { X1 = pp.X, Y1 = pp.Y, X2 = closestOnSeg.X, Y2 = closestOnSeg.Y, Stroke = Brushes.Lime, Label = $"{dd.Name}: {value:0.###}" });
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
                    dst.Add(CreateRotatedRoiWithPose(c.SearchRoi, Brushes.Lime, $"{c.Name} C"));
                }

                if (_lastRun is not null && _lastRun.CodeDetections is not null)
                {
                    var cdt = _lastRun.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (cdt is not null && cdt.Found && cdt.BoundingBox.Width > 0 && cdt.BoundingBox.Height > 0)
                    {
                        var bb = cdt.BoundingBox;
                        var angleDeg = _lastRun.Origin?.AngleDeg ?? 0.0;
                        dst.Add(new OverlayRectItem
                        {
                            X = bb.X,
                            Y = bb.Y,
                            Width = bb.Width,
                            Height = bb.Height,
                            Angle = angleDeg,
                            Stroke = Brushes.Lime,
                            Label = $"{cdt.Name}: {cdt.Text}"
                        });
                    }
                }
                return;
            }

            if (string.Equals(node.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
            {
                if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(_config.DefectConfig.InspectRoi, Brushes.Orange, "DefectROI"));
                }
    
                return;
            }

            BuildFinalOverlay(image, dst);
        }

        private OverlayRectItem CreateRotatedRoiWithPose(Roi roi, System.Windows.Media.Brush? stroke, string? label)
        {
            return CreateRotatedRoiWithPose(new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height), stroke, label);
        }

        private OverlayRectItem CreateRotatedRoiWithPose(OpenCvSharp.Rect roi, System.Windows.Media.Brush? stroke, string? label)
        {
            if (_lastRun is not null && _config is not null && _lastRun.Origin is not null && (_lastRun.Origin.MatchRect.Width > 0 || _lastRun.Origin.Position.X != 0 || _lastRun.Origin.Position.Y != 0))
            {
                var originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
                if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.TemplateRoi.Width > 0)
                {
                    originTeach = new OpenCvSharp.Point2d(_config.Origin.TemplateRoi.X + _config.Origin.TemplateRoi.Width / 2.0, _config.Origin.TemplateRoi.Y + _config.Origin.TemplateRoi.Height / 2.0);
                }
                else if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.SearchRoi.Width > 0)
                {
                    originTeach = new OpenCvSharp.Point2d(_config.Origin.SearchRoi.X + _config.Origin.SearchRoi.Width / 2.0, _config.Origin.SearchRoi.Y + _config.Origin.SearchRoi.Height / 2.0);
                }

                var mr = _lastRun.Origin.MatchRect;
                var originFound = (mr.Width > 0 && mr.Height > 0)
                    ? new OpenCvSharp.Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                    : new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);

                var angleDeg = _lastRun.Origin.AngleDeg;

                var centerTeach = new OpenCvSharp.Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);
                var centerFound = TransformPose(centerTeach, originTeach, originFound, angleDeg);

                var finalLabel = label;
                if (!string.IsNullOrWhiteSpace(label) && label.StartsWith("Origin", StringComparison.OrdinalIgnoreCase))
                {
                    var status = _lastRun.Origin.Pass ? "OK" : "NG";
                    finalLabel = $"{label} [{status}] Score: {_lastRun.Origin.Score:0.00} (Thr: {_lastRun.Origin.Threshold:0.00}) Ang: {_lastRun.Origin.AngleDeg:0.0}°";
                }

                return new OverlayRectItem
                {
                    X = (int)Math.Round(centerFound.X - roi.Width / 2.0),
                    Y = (int)Math.Round(centerFound.Y - roi.Height / 2.0),
                    Width = roi.Width,
                    Height = roi.Height,
                    Angle = angleDeg,
                    Stroke = _lastRun.Origin.Pass ? stroke : System.Windows.Media.Brushes.Red,
                    Label = finalLabel
                };
            }

            return new OverlayRectItem
            {
                X = roi.X,
                Y = roi.Y,
                Width = roi.Width,
                Height = roi.Height,
                Angle = 0,
                Stroke = stroke,
                Label = label
            };
        }

        private OverlayRectItem CreateRotatedRoi(OpenCvSharp.Rect roi, System.Windows.Media.Brush? stroke, string? label)
        {
            return CreateRotatedRoi(new Roi { X = roi.X, Y = roi.Y, Width = roi.Width, Height = roi.Height }, stroke, label);
        }

        private static bool IsRawImageRoi(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return true;
            var l = label.Trim();
            return l.StartsWith("Origin", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(l, "DefectROI", StringComparison.OrdinalIgnoreCase);
        }

        private Roi UnTransformRoi(Roi roi, string? label = null)
        {
            if (IsRawImageRoi(label) || _lastRun is null || _config is null || _lastRun.Origin is null)
            {
                return roi;
            }
    
            var originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
            var originFound = new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);
            var angleDeg = _lastRun.Origin.AngleDeg;
            if (Math.Abs(angleDeg) < 0.0001 && Math.Abs(originFound.X - originTeach.X) < 0.0001 && Math.Abs(originFound.Y - originTeach.Y) < 0.0001)
            {
                return roi;
            }
    
            var centerFoundX = roi.X + roi.Width / 2.0;
            var centerFoundY = roi.Y + roi.Height / 2.0;
            var rotX = centerFoundX - (originFound.X - originTeach.X);
            var rotY = centerFoundY - (originFound.Y - originTeach.Y);
            var a = -angleDeg * Math.PI / 180.0;
            var cos = Math.Cos(a);
            var sin = Math.Sin(a);
            var dx = rotX - originTeach.X;
            var dy = rotY - originTeach.Y;
            var centerTeachX = dx * cos - dy * sin + originTeach.X;
            var centerTeachY = dx * sin + dy * cos + originTeach.Y;
            return new Roi
            {
                X = (int)Math.Round(centerTeachX - roi.Width / 2.0),
                Y = (int)Math.Round(centerTeachY - roi.Height / 2.0),
                Width = roi.Width,
                Height = roi.Height
            };
        }
    
        private OverlayRectItem CreateRotatedRoi(Roi roi, System.Windows.Media.Brush? stroke, string? label)
        {
            if (!IsRawImageRoi(label) && _lastRun is not null && _config is not null && _lastRun.Origin is not null)
            {
                var originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
                var originFound = new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);
                var angleDeg = _lastRun.Origin.AngleDeg;
                if (Math.Abs(angleDeg) < 0.0001 && Math.Abs(originFound.X - originTeach.X) < 0.0001 && Math.Abs(originFound.Y - originTeach.Y) < 0.0001)
                {
                    return new OverlayRectItem
                    {
                        X = roi.X,
                        Y = roi.Y,
                        Width = roi.Width,
                        Height = roi.Height,
                        Stroke = stroke,
                        Label = label
                    };
                }
    
                var centerTeachX = roi.X + roi.Width / 2.0;
                var centerTeachY = roi.Y + roi.Height / 2.0;
                var a = angleDeg * Math.PI / 180.0;
                var cos = Math.Cos(a);
                var sin = Math.Sin(a);
                var dx = centerTeachX - originTeach.X;
                var dy = centerTeachY - originTeach.Y;
                var rotX = dx * cos - dy * sin + originTeach.X;
                var rotY = dx * sin + dy * cos + originTeach.Y;
                var centerFoundX = rotX + (originFound.X - originTeach.X);
                var centerFoundY = rotY + (originFound.Y - originTeach.Y);
                return new OverlayRectItem
                {
                    X = (int)Math.Round(centerFoundX - roi.Width / 2.0),
                    Y = (int)Math.Round(centerFoundY - roi.Height / 2.0),
                    Width = roi.Width,
                    Height = roi.Height,
                    Angle = angleDeg,
                    Stroke = stroke,
                    Label = label ?? string.Empty
                };
            }
    
            return new OverlayRectItem
            {
                X = roi.X,
                Y = roi.Y,
                Width = roi.Width,
                Height = roi.Height,
                Stroke = stroke,
                Label = label ?? string.Empty
            };
        }
    
        private void BuildFinalOverlay(Mat image, List<OverlayItem> dst)
        {
            if (_config is null)
            {
                return;
            }
    
            var showRois = ShowRoisInFinalPreview;
            if (showRois && _config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoi(_config.Origin.SearchRoi, Brushes.Lime, "Origin S"));
            }
    
            if (showRois && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoi(_config.Origin.TemplateRoi, Brushes.Gold, "Origin T"));
            }
    
            foreach (var p in _config.Points)
            {
                if (showRois && p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(p.SearchRoi, Brushes.DeepSkyBlue, $"{p.Name} S"));
                }
    
                if (showRois && p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(p.TemplateRoi, Brushes.Gold, $"{p.Name} T"));
                }
    
                dst.Add(new OverlayPointItem { X = p.WorldPosition.X, Y = p.WorldPosition.Y, Stroke = Brushes.DeepSkyBlue, Label = p.Name });
            }
    
            foreach (var l in _config.Lines)
            {
                if (showRois && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} L"));
                }
            }
    
            foreach (var b in _config.BlobDetections)
            {
                if (showRois && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(b.InspectRoi, Brushes.Gold, $"{b.Name} B"));
                }
            }
    
            foreach (var c in _config.CircleFinders)
            {
                if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(c.SearchRoi, Brushes.MediumPurple, $"{c.Name} CIR"));
                }
            }
    
            foreach (var e in _config.EdgePairDetections)
            {
                if (showRois && e.SearchRoi.Width > 0 && e.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoi(e.SearchRoi, Brushes.MediumPurple, $"{e.Name} EPD"));
                }
            }
    
            if (showRois && _config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoi(_config.DefectConfig.InspectRoi, Brushes.Orange, "DefectROI"));
            }
    
            using var processed = _preprocessor.Run(image, _config.Preprocess);
            var detectedLines = new System.Collections.Generic.Dictionary<string, LineDetectResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in _config.Lines)
            {
                if (l.SearchRoi.Width <= 0 || l.SearchRoi.Height <= 0)
                {
                    continue;
                }
    
                var det = _lineDetector.DetectLongestLine(processed, l.SearchRoi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
                var named = det with
                {
                    Name = l.Name
                };
                detectedLines[l.Name] = named;
                if (named.Found)
                {
                    dst.Add(new OverlayLineItem { X1 = named.P1.X, Y1 = named.P1.Y, X2 = named.P2.X, Y2 = named.P2.Y, Stroke = Brushes.MediumPurple, Label = named.Name });
                }
            }
    
            foreach (var dd in _config.LineToLineDistances)
            {
                if (string.IsNullOrWhiteSpace(dd.Name) || string.IsNullOrWhiteSpace(dd.LineA) || string.IsNullOrWhiteSpace(dd.LineB))
                {
                    continue;
                }
    
                if (!detectedLines.TryGetValue(dd.LineA, out var la) || !detectedLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    continue;
                }
    
                var(distPx, ca, cb) = CalculateLineLineDistance(la, lb, dd.Mode);
                var mm = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
                var pass = mm >= (dd.Nominal - dd.ToleranceMinus) && mm <= (dd.Nominal + dd.TolerancePlus);
                dst.Add(new OverlayLineItem { X1 = ca.X, Y1 = ca.Y, X2 = cb.X, Y2 = cb.Y, Stroke = pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {mm:0.00} mm" });
            }
    
            foreach (var dd in _config.PointToLineDistances)
            {
                if (string.IsNullOrWhiteSpace(dd.Name) || string.IsNullOrWhiteSpace(dd.Point) || string.IsNullOrWhiteSpace(dd.Line))
                {
                    continue;
                }
    
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, dd.Point, StringComparison.OrdinalIgnoreCase));
                if (p is null)
                {
                    continue;
                }
    
                if (!detectedLines.TryGetValue(dd.Line, out var l) || !l.Found)
                {
                    continue;
                }
    
                var pp = new Point2d(p.WorldPosition.X, p.WorldPosition.Y);
                var(distPx, closest) = CalculatePointLineDistance(pp, l, dd.Mode);
                var mm = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
                var pass = mm >= (dd.Nominal - dd.ToleranceMinus) && mm <= (dd.Nominal + dd.TolerancePlus);
                dst.Add(new OverlayLineItem { X1 = pp.X, Y1 = pp.Y, X2 = closest.X, Y2 = closest.Y, Stroke = pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {mm:0.00} mm" });
            }
    
            foreach (var d in _config.Distances)
            {
                var pa = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointA, StringComparison.OrdinalIgnoreCase));
                var pb = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointB, StringComparison.OrdinalIgnoreCase));
                if (pa is null || pb is null)
                {
                    continue;
                }
    
                var dx = pb.WorldPosition.X - pa.WorldPosition.X;
                var dy = pb.WorldPosition.Y - pa.WorldPosition.Y;
                var distPx = Math.Sqrt(dx * dx + dy * dy);
                var mm = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
                dst.Add(new OverlayLineItem { X1 = pa.WorldPosition.X, Y1 = pa.WorldPosition.Y, X2 = pb.WorldPosition.X, Y2 = pb.WorldPosition.Y, Stroke = Brushes.Yellow, Label = $"{d.Name}: {mm:0.00} mm" });
            }
    
            if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoi(_config.DefectConfig.InspectRoi, Brushes.Orange, "DefectROI"));
            }
        }
    }
}

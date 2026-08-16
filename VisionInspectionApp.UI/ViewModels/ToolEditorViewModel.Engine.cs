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
using VisionInspectionApp.Application.Services;
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
        private readonly object _cacheLock = new();

        private void SetImageSourceCache(string? nodeName, string sourcePath, Mat? mat)
        {
            if (string.IsNullOrWhiteSpace(nodeName) || mat is null || mat.IsDisposed || mat.Empty()) return;

            lock (_cacheLock)
            {
                if (_imageSourcePreviewCache.TryGetValue(nodeName, out var old))
                {
                    try { old.Image?.Dispose(); } catch { }
                }
                try
                {
                    _imageSourcePreviewCache[nodeName] = (sourcePath, mat.Clone());
                }
                catch { }
            }
        }

        private void ClearImageSourceCache(string? nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName)) return;

            lock (_cacheLock)
            {
                if (_imageSourcePreviewCache.TryGetValue(nodeName, out var old))
                {
                    try { old.Image?.Dispose(); } catch { }
                    _imageSourcePreviewCache.Remove(nodeName);
                }
            }
        }

        private Mat? GetImageSourceCache(string? nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName)) return null;

            lock (_cacheLock)
            {
                if (_imageSourcePreviewCache.TryGetValue(nodeName, out var cached))
                {
                    if (cached.Image is not null && !cached.Image.IsDisposed && !cached.Image.Empty())
                    {
                        try
                        {
                            return cached.Image.Clone();
                        }
                        catch
                        {
                            _imageSourcePreviewCache.Remove(nodeName);
                        }
                    }
                }
            }
            return null;
        }

        private void ClearAllImageSourceCache()
        {
            lock (_cacheLock)
            {
                foreach (var kv in _imageSourcePreviewCache.Values)
                {
                    try { kv.Image?.Dispose(); } catch { }
                }
                _imageSourcePreviewCache.Clear();
            }
        }
        private readonly DispatcherTimer _specEditPreviewTimer;
        private readonly DispatcherTimer _blobThresholdPreviewTimer;
        private readonly DispatcherTimer _continuousStatsTimer;
        private readonly System.Diagnostics.Stopwatch _continuousStopwatch = new();
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
            if (!ShowResultOverlay)
            {
                return;
            }

            BuildFinalOverlayFromRun(run, dst, _config, ShowRoisInSelectedPreview && ShowRoisInFinalPreview);

            foreach (var d in run.SegmentLineDistances)
            {
                if (double.IsNaN(d.Value)) continue;
                dst.Add(new OverlayLineItem { X1 = d.ClosestA.X, Y1 = d.ClosestA.Y, X2 = d.ClosestB.X, Y2 = d.ClosestB.Y, Stroke = d.Pass ? Brushes.Lime : Brushes.Red, Label = $"{d.Name}: {d.Value:0.###}" });
                dst.Add(new OverlayPointItem { X = d.ClosestA.X, Y = d.ClosestA.Y, Radius = 3.0, Stroke = d.Pass ? Brushes.Lime : Brushes.Red, Label = string.Empty });
                dst.Add(new OverlayPointItem { X = d.ClosestB.X, Y = d.ClosestB.Y, Radius = 3.0, Stroke = d.Pass ? Brushes.Lime : Brushes.Red, Label = string.Empty });
            }

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
                        dst.Add(new OverlayRectItem
                        {
                            X = br.X,
                            Y = br.Y,
                            Width = br.Width,
                            Height = br.Height,
                            Angle = bi.Angle,
                            Stroke = Brushes.Gold,
                            Label = string.Empty
                        });
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
    
        private Mat GetNodeOutputImageForPreview(Mat raw, ToolGraphNodeViewModel node)
        {
            if (_config is null || node is null) return raw.Clone();

            if (string.Equals(node.Type, "Crop", StringComparison.OrdinalIgnoreCase))
            {
                var cropDef = _config.Crops?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                using var inputMat = GetNodeInputImageForPreview(raw, node, "Image");
                return (cropDef != null && cropDef.CropRoi != null && cropDef.CropRoi.Width > 0 && cropDef.CropRoi.Height > 0)
                    ? CropProcessor.Run(inputMat, cropDef.CropRoi)
                    : inputMat.Clone();
            }

            if (string.Equals(node.Type, "ImgArithmetic", StringComparison.OrdinalIgnoreCase))
            {
                var arithDef = _config.ImgArithmetics?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                using var matA = GetNodeInputImageForPreview(raw, node, "InA");
                using var matB = GetNodeInputImageForPreview(raw, node, "InB");
                return arithDef != null ? ImgArithmeticProcessor.Run(matA, matB, arithDef) : matA.Clone();
            }

            if (string.Equals(node.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
            {
                var imgSourceDef = _config.ImageSources?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (imgSourceDef is not null)
                {
                    var loadedMat = LoadImageFromSourceForPreview(imgSourceDef);
                    if (loadedMat is not null && !loadedMat.Empty())
                    {
                        return loadedMat;
                    }
                }
                return raw.Clone();
            }

            if (string.Equals(node.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                var preDef = _config.PreprocessNodes?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                var settings = preDef?.Settings ?? _config.Preprocess;
                var rois = preDef?.Rois;
                using var inputMat = GetNodeInputImageForPreview(raw, node, "In");
                return _preprocessor.Run(inputMat, settings, rois);
            }

            using var inMat = GetNodeInputImageForPreview(raw, node, "Image");
            return inMat.Clone();
        }

        private Mat GetNodeInputImageForPreview(Mat raw, ToolGraphNodeViewModel node, string targetPort = "Image")
        {
            if (_config is null || node is null) return raw.Clone();

            var edges = _config.ToolGraph?.Edges ?? new();
            var nodesById = Nodes.Where(n => !string.IsNullOrWhiteSpace(n.Id)).ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

            var inEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, node.Id, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(e.ToPort, targetPort, StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase)));

            if (inEdge is not null && nodesById.TryGetValue(inEdge.FromNodeId, out var fromNode))
            {
                return GetNodeOutputImageForPreview(raw, fromNode);
            }

            return raw.Clone();
        }

        private Mat ResolveToolPreprocessForPreview(Mat raw, ToolGraphNodeViewModel toolNode)
        {
            if (_config is null || toolNode is null) return raw.Clone();

            if (string.Equals(toolNode.Type, "Crop", StringComparison.OrdinalIgnoreCase))
            {
                return GetNodeInputImageForPreview(raw, toolNode, "Image");
            }

            if (string.Equals(toolNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolNode.Type, "ImgArithmetic", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
            {
                return GetNodeOutputImageForPreview(raw, toolNode);
            }

            return GetNodeInputImageForPreview(raw, toolNode, "Image");
        }

        private Mat ResolveToolImageForPreview(Mat raw, ToolGraphNodeViewModel toolNode)
        {
            return ResolveToolPreprocessForPreview(raw, toolNode);
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
                else if (string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
                {
                    var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                    if (c is not null)
                        c.SearchRoi = roi;
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
    
            OpenCvSharp.Point2d originTeach = default;
            OpenCvSharp.Point2d originFound = default;
            double angleDeg = 0;
            if (_config?.Origin is not null && _lastRun?.Origin is not null && (_lastRun.Origin.MatchRect.Width > 0 || _lastRun.Origin.Position.X != 0 || _lastRun.Origin.Position.Y != 0))
            {
                originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
                if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.TemplateRoi.Width > 0)
                {
                    originTeach = new OpenCvSharp.Point2d(_config.Origin.TemplateRoi.X + _config.Origin.TemplateRoi.Width / 2.0, _config.Origin.TemplateRoi.Y + _config.Origin.TemplateRoi.Height / 2.0);
                }
                else if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.SearchRoi.Width > 0)
                {
                    originTeach = new OpenCvSharp.Point2d(_config.Origin.SearchRoi.X + _config.Origin.SearchRoi.Width / 2.0, _config.Origin.SearchRoi.Y + _config.Origin.SearchRoi.Height / 2.0);
                }

                var mr = _lastRun.Origin.MatchRect;
                originFound = (mr.Width > 0 && mr.Height > 0)
                    ? new OpenCvSharp.Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                    : new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);

                angleDeg = _lastRun.Origin.AngleDeg;
            }

            Roi targetRoi;
            if (Math.Abs(angleDeg) > 0.001 || originFound.X != 0 || originFound.Y != 0)
            {
                var centerTeach = new OpenCvSharp.Point2d(def.SearchRoi.X + def.SearchRoi.Width / 2.0, def.SearchRoi.Y + def.SearchRoi.Height / 2.0);
                var centerFound = TransformPose(centerTeach, originTeach, originFound, angleDeg);
                targetRoi = new Roi
                {
                    X = (int)Math.Round(centerFound.X - def.SearchRoi.Width / 2.0),
                    Y = (int)Math.Round(centerFound.Y - def.SearchRoi.Height / 2.0),
                    Width = def.SearchRoi.Width,
                    Height = def.SearchRoi.Height,
                    Angle = def.SearchRoi.Angle + angleDeg
                };
            }
            else
            {
                targetRoi = def.SearchRoi;
            }

            // Tối ưu: Trích xuất ROI patch trực tiếp từ ảnh gốc (chỉ vài trăm pixel thay vì 20 Megapixels)
            using var rawCrop = ExtractRoiPatch(image, targetRoi);
            if (rawCrop.Empty() || rawCrop.Width <= 0 || rawCrop.Height <= 0)
            {
                LinePreviewImage = null;
                return;
            }

            using var crop = _config is not null && PreprocessPreviewEnabled ? _preprocessor.Run(rawCrop, _config.Preprocess) : rawCrop.Clone();
            using var view = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
            var det = _lineDetector.DetectLongestLine(view, new Roi { X = 0, Y = 0, Width = view.Width, Height = view.Height }, def.Canny1, def.Canny2, def.HoughThreshold, def.MinLineLength, def.MaxLineGap);
            if (det.Found)
            {
                var p1 = new OpenCvSharp.Point((int)Math.Round(det.P1.X), (int)Math.Round(det.P1.Y));
                var p2 = new OpenCvSharp.Point((int)Math.Round(det.P2.X), (int)Math.Round(det.P2.Y));
                Cv2.Line(view, p1, p2, Scalar.White, 2);
            }

            LinePreviewImage = view.ToBitmapSourceForDisplay();
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
    
            var rect = new OpenCvSharp.Rect(def.SearchRoi.X, def.SearchRoi.Y, def.SearchRoi.Width, def.SearchRoi.Height);
            rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, snap.Width, snap.Height));
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                PointEdgePreviewImage = null;
                return;
            }
    
            using var rawCrop = new Mat(snap, rect);
            using var crop = _config is not null && PreprocessPreviewEnabled ? _preprocessor.Run(rawCrop, _config.Preprocess) : rawCrop.Clone();
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
    
            PointEdgePreviewImage = view.ToBitmapSourceForDisplay();
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

            if (string.Equals(kind, "Crop", StringComparison.OrdinalIgnoreCase))
            {
                var cropDef = _config.Crops.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cropDef is not null)
                    cropDef.CropRoi = roi;
                return;
            }

            if (string.Equals(kind, "Sample", StringComparison.OrdinalIgnoreCase))
            {
                var cdDef = _config.ColorDiffs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cdDef is not null)
                    cdDef.InspectRoi = roi;
                return;
            }

            if (string.Equals(kind, "Ref", StringComparison.OrdinalIgnoreCase))
            {
                var cdDef = _config.ColorDiffs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cdDef is not null)
                    cdDef.RefRoi = roi;
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

            if (kind.StartsWith("CC", StringComparison.OrdinalIgnoreCase))
            {
                var cc = _config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cc is null) return;

                if (string.Equals(kind, "CC", StringComparison.OrdinalIgnoreCase))
                {
                    cc.InspectRoi = roi;
                    if (cc.TemplateRoi.Width <= 0 || cc.TemplateRoi.Height <= 0) cc.TemplateRoi = roi;
                    if (string.IsNullOrWhiteSpace(cc.TemplateImageFile)) TrySaveContourCompareTemplateImage(name, cc.TemplateRoi);
                    return;
                }

                if (string.Equals(kind, "CCT", StringComparison.OrdinalIgnoreCase))
                {
                    cc.TemplateRoi = roi;
                    TrySaveContourCompareTemplateImage(name, roi);
                    return;
                }
            }

            if (kind.StartsWith("PR", StringComparison.OrdinalIgnoreCase))
            {
                var preDef = _config.PreprocessNodes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (preDef is not null && preDef.Rois is not null)
                {
                    var vParts = kind.Split("_V", StringSplitOptions.RemoveEmptyEntries);
                    var roiKind = vParts[0];

                    var digitsStr = new string(roiKind.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digitsStr, out var idx) && idx >= 1 && idx <= preDef.Rois.Count)
                    {
                        var targetRoi = preDef.Rois[idx - 1];

                        // If dragging an individual vertex handle V1, V2, ... Vn:
                        if (vParts.Length == 2 && int.TryParse(vParts[1], out var vIdx) && vIdx >= 1 && targetRoi.PolygonPoints != null && vIdx <= targetRoi.PolygonPoints.Count)
                        {
                            targetRoi.PolygonPoints[vIdx - 1] = new Point2dModel
                            {
                                X = Math.Round(roi.X + roi.Width / 2.0),
                                Y = Math.Round(roi.Y + roi.Height / 2.0)
                            };
                            return;
                        }

                        if (targetRoi.Shape == PreprocessRoiShape.Circle)
                        {
                            targetRoi.CircleRadius = (int)Math.Max(5, Math.Max(roi.Width, roi.Height) / 2.0);
                            targetRoi.CircleCenterX = (int)(roi.X + roi.Width / 2.0);
                            targetRoi.CircleCenterY = (int)(roi.Y + roi.Height / 2.0);
                        }
                        else if (targetRoi.Shape == PreprocessRoiShape.Polygon)
                        {
                            if (targetRoi.PolygonPoints != null && targetRoi.PolygonPoints.Count >= 3)
                            {
                                double oldMinX = targetRoi.PolygonPoints.Min(p => p.X);
                                double oldMinY = targetRoi.PolygonPoints.Min(p => p.Y);
                                double oldW = Math.Max(1.0, targetRoi.PolygonPoints.Max(p => p.X) - oldMinX);
                                double oldH = Math.Max(1.0, targetRoi.PolygonPoints.Max(p => p.Y) - oldMinY);

                                double scaleX = roi.Width / oldW;
                                double scaleY = roi.Height / oldH;

                                for (int pIdx = 0; pIdx < targetRoi.PolygonPoints.Count; pIdx++)
                                {
                                    var p = targetRoi.PolygonPoints[pIdx];
                                    targetRoi.PolygonPoints[pIdx] = new Point2dModel
                                    {
                                        X = Math.Round(roi.X + (p.X - oldMinX) * scaleX),
                                        Y = Math.Round(roi.Y + (p.Y - oldMinY) * scaleY)
                                    };
                                }
                            }
                        }
                        else
                        {
                            targetRoi.X = (int)roi.X;
                            targetRoi.Y = (int)roi.Y;
                            targetRoi.Width = (int)roi.Width;
                            targetRoi.Height = (int)roi.Height;
                            targetRoi.Angle = roi.Angle;
                        }
                    }
                }
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
            if (!ShowResultOverlay)
            {
                return;
            }
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
                        dst.Add(new OverlayRectItem
                        {
                            X = br.X,
                            Y = br.Y,
                            Width = br.Width,
                            Height = br.Height,
                            Angle = bi.Angle,
                            Stroke = Brushes.Gold,
                            Label = string.Empty
                        });
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
                    return;
                }
            }

            if (string.Equals(node.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is null || def.InspectRoi.Width <= 0 || def.InspectRoi.Height <= 0) return;

                var r = run.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null) return;

                var stroke = r.Pass ? Brushes.Lime : Brushes.Red;
                var status = r.Pass ? "OK" : "NG";
                dst.Add(new OverlayPointItem { X = def.InspectRoi.X + 2, Y = def.InspectRoi.Y + 2, Radius = 1.0, Stroke = stroke, Label = $"{def.Name} [{status}]: Score: {r.MatchScore:0.####}, MaxDist: {r.MaxDistancePx:0.##}px" });
                return;
            }

            if (string.Equals(node.Type, "CreatePoint", StringComparison.OrdinalIgnoreCase))
            {
                var cp = run.CreatePoints?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cp is not null && cp.Success)
                {
                    if (ShowRoisInSelectedPreview)
                    {
                        dst.Add(CreateRotatedRoi(new Roi { X = (int)cp.X - 10, Y = (int)cp.Y - 10, Width = 20, Height = 20 }, Brushes.LimeGreen, $"{cp.Name} Point ({cp.X:F1}, {cp.Y:F1})"));
                    }
                    AddCross(dst, cp.X, cp.Y, 20, Brushes.LimeGreen, 2.0);
                    AddCircle(dst, cp.X, cp.Y, 6, Brushes.LimeGreen, 1.5);
                }
                else
                {
                    var cpDef = _config.CreatePoints?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (cpDef is not null)
                    {
                        var pts = GetCurrentPointsMap();
                        var res = GeometryCreationProcessor.EvaluateCreatePoint(cpDef, pts);
                        if (ShowRoisInSelectedPreview)
                        {
                            dst.Add(CreateRotatedRoi(new Roi { X = (int)res.X - 10, Y = (int)res.Y - 10, Width = 20, Height = 20 }, Brushes.LimeGreen, $"{cpDef.Name} Point ({res.X:F1}, {res.Y:F1})"));
                        }
                        AddCross(dst, res.X, res.Y, 20, Brushes.LimeGreen, 2.0);
                        AddCircle(dst, res.X, res.Y, 6, Brushes.LimeGreen, 1.5);
                    }
                }
                return;
            }

            if (string.Equals(node.Type, "CreateLine", StringComparison.OrdinalIgnoreCase))
            {
                var cl = run.CreateLines?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cl is not null && cl.Success)
                {
                    if (ShowRoisInSelectedPreview)
                    {
                        double minX = Math.Min(cl.X1, cl.X2);
                        double minY = Math.Min(cl.Y1, cl.Y2);
                        double w = Math.Max(10, Math.Abs(cl.X2 - cl.X1));
                        double h = Math.Max(10, Math.Abs(cl.Y2 - cl.Y1));
                        dst.Add(CreateRotatedRoi(new Roi { X = (int)minX, Y = (int)minY, Width = (int)w, Height = (int)h, Angle = cl.Angle }, Brushes.LimeGreen, $"{cl.Name} Line"));
                    }
                    dst.Add(new OverlayLineItem { X1 = cl.X1, Y1 = cl.Y1, X2 = cl.X2, Y2 = cl.Y2, Stroke = Brushes.LimeGreen, StrokeThickness = 2.5, Label = $"{cl.Name} (L={cl.Length:F1}px)" });
                    AddCross(dst, cl.X1, cl.Y1, 10, Brushes.LimeGreen, 1.5);
                    AddCross(dst, cl.X2, cl.Y2, 10, Brushes.LimeGreen, 1.5);
                }
                else
                {
                    var clDef = _config.CreateLines?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (clDef is not null)
                    {
                        var pts = GetCurrentPointsMap();
                        var res = GeometryCreationProcessor.EvaluateCreateLine(clDef, pts);
                        if (ShowRoisInSelectedPreview)
                        {
                            double minX = Math.Min(res.X1, res.X2);
                            double minY = Math.Min(res.Y1, res.Y2);
                            double w = Math.Max(10, Math.Abs(res.X2 - res.X1));
                            double h = Math.Max(10, Math.Abs(res.Y2 - res.Y1));
                            dst.Add(CreateRotatedRoi(new Roi { X = (int)minX, Y = (int)minY, Width = (int)w, Height = (int)h, Angle = res.Angle }, Brushes.LimeGreen, $"{clDef.Name} Line"));
                        }
                        dst.Add(new OverlayLineItem { X1 = res.X1, Y1 = res.Y1, X2 = res.X2, Y2 = res.Y2, Stroke = Brushes.LimeGreen, StrokeThickness = 2.5, Label = $"{clDef.Name} (L={res.Length:F1}px)" });
                        AddCross(dst, res.X1, res.Y1, 10, Brushes.LimeGreen, 1.5);
                        AddCross(dst, res.X2, res.Y2, 10, Brushes.LimeGreen, 1.5);
                    }
                }
                return;
            }

            if (string.Equals(node.Type, "CreateRect", StringComparison.OrdinalIgnoreCase))
            {
                var cr = run.CreateRects?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cr is not null && cr.Success)
                {
                    if (ShowRoisInSelectedPreview)
                    {
                        dst.Add(CreateRotatedRoi(new Roi { X = (int)cr.TopLeftX, Y = (int)cr.TopLeftY, Width = (int)cr.Width, Height = (int)cr.Height, Angle = cr.Angle }, Brushes.LimeGreen, $"{cr.Name} ROI"));
                    }
                    dst.Add(new OverlayRectItem { X = (int)cr.TopLeftX, Y = (int)cr.TopLeftY, Width = (int)cr.Width, Height = (int)cr.Height, Angle = cr.Angle, Stroke = Brushes.LimeGreen, StrokeThickness = 2.0, Label = $"{cr.Name} Rect ({cr.Width:F1}x{cr.Height:F1})" });
                    AddCross(dst, cr.X, cr.Y, 12, Brushes.LimeGreen, 1.5);
                }
                else
                {
                    var crDef = _config.CreateRects?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (crDef is not null)
                    {
                        var pts = GetCurrentPointsMap();
                        var res = GeometryCreationProcessor.EvaluateCreateRect(crDef, pts);
                        if (ShowRoisInSelectedPreview)
                        {
                            dst.Add(CreateRotatedRoi(new Roi { X = (int)res.TopLeftX, Y = (int)res.TopLeftY, Width = (int)res.Width, Height = (int)res.Height, Angle = res.Angle }, Brushes.LimeGreen, $"{crDef.Name} ROI"));
                        }
                        dst.Add(new OverlayRectItem { X = (int)res.TopLeftX, Y = (int)res.TopLeftY, Width = (int)res.Width, Height = (int)res.Height, Angle = res.Angle, Stroke = Brushes.LimeGreen, StrokeThickness = 2.0, Label = $"{crDef.Name} Rect ({res.Width:F1}x{res.Height:F1})" });
                        AddCross(dst, res.X, res.Y, 12, Brushes.LimeGreen, 1.5);
                    }
                }
                return;
            }

            if (string.Equals(node.Type, "CreateCircle", StringComparison.OrdinalIgnoreCase))
            {
                var cc = run.CreateCircles?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cc is not null && cc.Success)
                {
                    if (ShowRoisInSelectedPreview)
                    {
                        int r = (int)cc.Radius;
                        dst.Add(CreateRotatedRoi(new Roi { X = (int)cc.CenterX - r, Y = (int)cc.CenterY - r, Width = r * 2, Height = r * 2 }, Brushes.LimeGreen, $"{cc.Name} Circle"));
                    }
                    AddCircle(dst, cc.CenterX, cc.CenterY, cc.Radius, Brushes.LimeGreen, 2.5);
                    AddCross(dst, cc.CenterX, cc.CenterY, 15, Brushes.LimeGreen, 1.5);
                }
                else
                {
                    var ccDef = _config.CreateCircles?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (ccDef is not null)
                    {
                        var pts = GetCurrentPointsMap();
                        var res = GeometryCreationProcessor.EvaluateCreateCircle(ccDef, pts);
                        if (ShowRoisInSelectedPreview)
                        {
                            int r = (int)res.Radius;
                            dst.Add(CreateRotatedRoi(new Roi { X = (int)res.CenterX - r, Y = (int)res.CenterY - r, Width = r * 2, Height = r * 2 }, Brushes.LimeGreen, $"{ccDef.Name} Circle"));
                        }
                        AddCircle(dst, res.CenterX, res.CenterY, res.Radius, Brushes.LimeGreen, 2.5);
                        AddCross(dst, res.CenterX, res.CenterY, 15, Brushes.LimeGreen, 1.5);
                    }
                }
                return;
            }

            if (string.Equals(node.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null && ShowRoisInSelectedPreview && def.SearchRoi.Width > 0 && def.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(def.SearchRoi, Brushes.Lime, $"{def.Name} C"));
                }

                var cdt = run.CodeDetections?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cdt is not null && cdt.Found && cdt.BoundingBox.Width > 0 && cdt.BoundingBox.Height > 0)
                {
                    var bb = cdt.BoundingBox;
                    dst.Add(new OverlayRectItem
                    {
                        X = bb.X,
                        Y = bb.Y,
                        Width = bb.Width,
                        Height = bb.Height,
                        Angle = cdt.Angle,
                        Stroke = Brushes.Lime,
                        Label = $"{cdt.Name}: {cdt.Text}"
                    });
                }
                return;
            }
        }

        private Roi? GetRoiForLabel(string labelRaw)
        {
            if (_config is null || string.IsNullOrWhiteSpace(labelRaw)) return null;
            var label = labelRaw.Trim();
            if (string.Equals(label, "DefectROI", StringComparison.OrdinalIgnoreCase)) return _config.DefectConfig.InspectRoi;
            if (string.Equals(label, "Origin S", StringComparison.OrdinalIgnoreCase)) return _config.Origin.SearchRoi;
            if (string.Equals(label, "Origin T", StringComparison.OrdinalIgnoreCase)) return _config.Origin.TemplateRoi;
            var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;
            var name = parts[0];
            var kind = parts[1];
            if (string.Equals(kind, "Point", StringComparison.OrdinalIgnoreCase))
            {
                var cp = _config.CreatePoints?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cp != null) return new Roi { X = (int)cp.X - 10, Y = (int)cp.Y - 10, Width = 20, Height = 20 };
            }
            if (string.Equals(kind, "Line", StringComparison.OrdinalIgnoreCase))
            {
                var cl = _config.CreateLines?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cl != null)
                {
                    double minX = Math.Min(cl.X1, cl.X2);
                    double minY = Math.Min(cl.Y1, cl.Y2);
                    double w = Math.Max(10, Math.Abs(cl.X2 - cl.X1));
                    double h = Math.Max(10, Math.Abs(cl.Y2 - cl.Y1));
                    return new Roi { X = (int)minX, Y = (int)minY, Width = (int)w, Height = (int)h, Angle = cl.Angle };
                }
            }
            if (string.Equals(kind, "Rect", StringComparison.OrdinalIgnoreCase))
            {
                var cr = _config.CreateRects?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cr != null)
                {
                    var (tlX, tlY) = GeometryCreationProcessor.CalculateRectTopLeft(cr.X, cr.Y, cr.Width, cr.Height, cr.Anchor);
                    return new Roi { X = (int)tlX, Y = (int)tlY, Width = (int)cr.Width, Height = (int)cr.Height, Angle = cr.Angle };
                }
            }
            if (string.Equals(kind, "Circle", StringComparison.OrdinalIgnoreCase))
            {
                var cc = _config.CreateCircles?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cc != null)
                {
                    int r = (int)cc.Radius;
                    return new Roi { X = (int)cc.CenterX - r, Y = (int)cc.CenterY - r, Width = r * 2, Height = r * 2 };
                }
            }
            if (string.Equals(kind, "S", StringComparison.OrdinalIgnoreCase)) return _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.SearchRoi;
            if (string.Equals(kind, "T", StringComparison.OrdinalIgnoreCase)) return _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.TemplateRoi;
            if (string.Equals(kind, "L", StringComparison.OrdinalIgnoreCase)) return _config.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.SearchRoi;
            if (string.Equals(kind, "Cal", StringComparison.OrdinalIgnoreCase)) return _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.SearchRoi;
            if (string.Equals(kind, "LP", StringComparison.OrdinalIgnoreCase)) return _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.SearchRoi;
            if (string.Equals(kind, "EPD", StringComparison.OrdinalIgnoreCase)) return _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.SearchRoi;
            if (string.Equals(kind, "CIR", StringComparison.OrdinalIgnoreCase)) return _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.SearchRoi;
            if (string.Equals(kind, "Crop", StringComparison.OrdinalIgnoreCase)) return _config.Crops.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.CropRoi;
            if (string.Equals(kind, "Sample", StringComparison.OrdinalIgnoreCase)) return _config.ColorDiffs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.InspectRoi;
            if (string.Equals(kind, "Ref", StringComparison.OrdinalIgnoreCase)) return _config.ColorDiffs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.RefRoi;
            if (string.Equals(kind, "C", StringComparison.OrdinalIgnoreCase)) return _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.SearchRoi;
            if (kind.StartsWith("B", StringComparison.OrdinalIgnoreCase)) return _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.InspectRoi;
            if (string.Equals(kind, "SCT", StringComparison.OrdinalIgnoreCase)) return _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.TemplateRoi;
            if (kind.StartsWith("SC", StringComparison.OrdinalIgnoreCase)) return _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.InspectRoi;
            if (string.Equals(kind, "CCT", StringComparison.OrdinalIgnoreCase)) return _config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.TemplateRoi;
            if (kind.StartsWith("PR", StringComparison.OrdinalIgnoreCase))
            {
                var preDef = _config.PreprocessNodes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (preDef is not null && preDef.Rois is not null)
                {
                    var vParts = kind.Split("_V", StringSplitOptions.RemoveEmptyEntries);
                    var roiKind = vParts[0];

                    var digitsStr = new string(roiKind.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digitsStr, out var idx) && idx >= 1 && idx <= preDef.Rois.Count)
                    {
                        var r = preDef.Rois[idx - 1];
                        if (vParts.Length == 2 && int.TryParse(vParts[1], out var vIdx) && vIdx >= 1 && r.PolygonPoints != null && vIdx <= r.PolygonPoints.Count)
                        {
                            var pt = r.PolygonPoints[vIdx - 1];
                            int handleSize = 14;
                            return new Roi { X = (int)(pt.X - handleSize / 2.0), Y = (int)(pt.Y - handleSize / 2.0), Width = handleSize, Height = handleSize };
                        }

                        if (r.Shape == PreprocessRoiShape.Circle)
                        {
                            int rad = Math.Max(5, r.CircleRadius);
                            return new Roi { X = r.CircleCenterX - rad, Y = r.CircleCenterY - rad, Width = rad * 2, Height = rad * 2 };
                        }
                        if (r.Shape == PreprocessRoiShape.Polygon && r.PolygonPoints != null && r.PolygonPoints.Count >= 3)
                        {
                            double minX = r.PolygonPoints.Min(p => p.X);
                            double minY = r.PolygonPoints.Min(p => p.Y);
                            double maxX = r.PolygonPoints.Max(p => p.X);
                            double maxY = r.PolygonPoints.Max(p => p.Y);
                            return new Roi { X = (int)minX, Y = (int)minY, Width = Math.Max(10, (int)(maxX - minX)), Height = Math.Max(10, (int)(maxY - minY)) };
                        }
                        return new Roi { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, Angle = r.Angle };
                    }
                }
                return null;
            }
            return null;
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

            var rawLabel = sel.Label.Trim();
            var label = rawLabel.Split('[')[0].Trim();
            var oldRoi = GetRoiForLabel(rawLabel);
            var oldRoiCopy = oldRoi is not null ? new Roi { X = oldRoi.X, Y = oldRoi.Y, Width = oldRoi.Width, Height = oldRoi.Height, Angle = oldRoi.Angle } : null;
            var roi = UnTransformRoi(sel.Roi, label);

            if (oldRoiCopy is not null && (Math.Abs(oldRoiCopy.X - roi.X) > 0.1 || Math.Abs(oldRoiCopy.Y - roi.Y) > 0.1 || Math.Abs(oldRoiCopy.Width - roi.Width) > 0.1 || Math.Abs(oldRoiCopy.Height - roi.Height) > 0.1 || Math.Abs(oldRoiCopy.Angle - roi.Angle) > 0.1))
            {
                var newRoiCopy = new Roi { X = roi.X, Y = roi.Y, Width = roi.Width, Height = roi.Height, Angle = roi.Angle };
                var labelToUpdate = rawLabel;
                UndoManager.Execute(new UndoRedoManager.DelegateAction(
                    doAction: () =>
                    {
                        ApplyRoiForLabel(labelToUpdate, newRoiCopy);
                        RefreshPreviews();
                        RequestAutoSave();
                    },
                    undoAction: () =>
                    {
                        ApplyRoiForLabel(labelToUpdate, oldRoiCopy);
                        RefreshPreviews();
                        RequestAutoSave();
                    }
                ));
            }

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
                // Khung Template ROI là Read-Only trên Canvas chính (việc Train/chỉnh sửa Template ROI được thực hiện trong cửa sổ Train Template)
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
                    if (string.Equals(kind, "SCT", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "SC", StringComparison.OrdinalIgnoreCase))
                    {
                        TrySaveSurfaceCompareTemplateImage(name, roi, sel.Roi);
                    }
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }

                if (kind.StartsWith("CC", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyRoiForLabel(label, roi);
                    if (string.Equals(kind, "CCT", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "CC", StringComparison.OrdinalIgnoreCase))
                    {
                        TrySaveContourCompareTemplateImage(name, roi, sel.Roi);
                    }
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }

                if (kind.StartsWith("PR", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyRoiForLabel(label, roi);
                    OnPropertyChanged(nameof(PreprocessRois));
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }

                if (string.Equals(kind, "Point", StringComparison.OrdinalIgnoreCase))
                {
                    var cp = _config.CreatePoints?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (cp is not null)
                    {
                        cp.X = roi.X + roi.Width / 2.0;
                        cp.Y = roi.Y + roi.Height / 2.0;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }

                if (string.Equals(kind, "Line", StringComparison.OrdinalIgnoreCase))
                {
                    var cl = _config.CreateLines?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (cl is not null)
                    {
                        cl.X1 = roi.X;
                        cl.Y1 = roi.Y;
                        cl.X2 = roi.X + roi.Width;
                        cl.Y2 = roi.Y + roi.Height;
                        cl.Angle = roi.Angle;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }

                if (string.Equals(kind, "Rect", StringComparison.OrdinalIgnoreCase))
                {
                    var cr = _config.CreateRects?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (cr is not null)
                    {
                        cr.Width = roi.Width;
                        cr.Height = roi.Height;
                        cr.Angle = roi.Angle;
                        var (anchorX, anchorY) = GeometryCreationProcessor.CalculateAnchorFromTopLeft(roi.X, roi.Y, roi.Width, roi.Height, cr.Anchor);
                        cr.X = anchorX;
                        cr.Y = anchorY;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
                }

                if (string.Equals(kind, "Circle", StringComparison.OrdinalIgnoreCase))
                {
                    var cc = _config.CreateCircles?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (cc is not null)
                    {
                        cc.CenterX = roi.X + roi.Width / 2.0;
                        cc.CenterY = roi.Y + roi.Height / 2.0;
                        cc.Radius = Math.Min(roi.Width, roi.Height) / 2.0;
                        RefreshPreviews();
                        RequestAutoSave();
                        return;
                    }
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
        [ObservableProperty]
        private int _processedImageCount = 0;
        [ObservableProperty]
        private string _continuousElapsedAndSpeedText = "Time: 00:00:00 (0.0 pcs/s)";

        private void UpdateContinuousStats()
        {
            if (!_continuousStopwatch.IsRunning)
            {
                ContinuousElapsedAndSpeedText = "Time: 00:00:00 (0.0 pcs/s)";
                return;
            }

            var elapsed = _continuousStopwatch.Elapsed;
            var elapsedSec = elapsed.TotalSeconds;
            double speed = elapsedSec > 0.05 ? ProcessedImageCount / elapsedSec : 0.0;
            ContinuousElapsedAndSpeedText = $"Time: {elapsed:hh\\:mm\\:ss} ({speed:F1} pcs/s)";
        }
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
        [ObservableProperty]
        private bool _showResultOverlay = true;
        [ObservableProperty]
        private bool _enableCanvasRendering = true;

        partial void OnEnableCanvasRenderingChanged(bool value)
        {
            if (!value)
            {
                SelectedNodePreviewImage = null;
                FinalPreviewImage = null;
                _cachedFinalPreviewImage = null;
                SelectedNodeOverlayItems = null;
                FinalOverlayItems = null;
            }
            else
            {
                _finalPreviewDirty = true;
                RefreshPreviews();
            }
            RaiseToolPropertyPanelsChanged();
        }

        partial void OnShowResultOverlayChanged(bool value)
        {
            _finalPreviewDirty = true;
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
        }

        partial void OnShowRoisInSelectedPreviewChanged(bool value)
        {
            if (_showRoisInFinalPreview != value)
            {
                _showRoisInFinalPreview = value;
                OnPropertyChanged(nameof(ShowRoisInFinalPreview));
            }
            _finalPreviewDirty = true;
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
        }
    
        partial void OnShowRoisInFinalPreviewChanged(bool value)
        {
            if (_showRoisInSelectedPreview != value)
            {
                _showRoisInSelectedPreview = value;
                OnPropertyChanged(nameof(ShowRoisInSelectedPreview));
            }
            _finalPreviewDirty = true;
            RefreshPreviews();
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
                var cachedMat = GetImageSourceCache(source.Name);
                if (cachedMat is not null && !cachedMat.Empty())
                {
                    return cachedMat;
                }

                if (source.SourceType == ImageSourceType.File)
                {
                    if (!string.IsNullOrWhiteSpace(source.FilePath) && File.Exists(source.FilePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Loading image from file: {source.FilePath}");
                        var mat = Cv2.ImRead(source.FilePath);
                        if (mat is not null && !mat.Empty())
                        {
                            SetImageSourceCache(source.Name, source.FilePath, mat);
                            return mat;
                        }
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
                            System.Diagnostics.Debug.WriteLine($"Loading image from folder: {targetFile}");
                            var mat = Cv2.ImRead(targetFile);
                            if (mat is not null && !mat.Empty())
                            {
                                SetImageSourceCache(source.Name, targetFile, mat);
                                return mat;
                            }
                        }
                    }
                }
                else if (source.SourceType == ImageSourceType.Camera)
                {
                    try
                    {
                        var cameraMat = CaptureCameraSnapshotSafe(source.CameraIndex, string.IsNullOrWhiteSpace(source.RtspUrl) ? null : source.RtspUrl);
                        if (cameraMat is not null && !cameraMat.Empty())
                        {
                            SetImageSourceCache(source.Name, "camera", cameraMat);
                            return cameraMat;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Exception in LoadImageFromSourceForPreview camera capture: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in LoadImageFromSourceForPreview: {ex.Message}");
            }

            return null;
        }

        private Mat? CaptureCameraSnapshotSafe(int cameraIndex, string? rtspUrl)
        {
            try
            {
                if (_cameraService.IsRunning)
                {
                    var liveMat = _cameraService.TryGetLatestFrameClone();
                    if (liveMat is not null && !liveMat.Empty())
                    {
                        return liveMat;
                    }
                }

                var task = Task.Run(async () => await _cameraService.CaptureSnapshotAsync(cameraIndex, rtspUrl));
                if (task.Wait(2000))
                {
                    return task.Result;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Camera snapshot capture timed out (2000ms limit)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Camera capture exception: {ex.Message}");
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

                    if (value)
                    {
                        _plcManagerService.AcquirePollingLock("RunContinuousMode");
                        ProcessedImageCount = 0;
                        _continuousStopwatch.Restart();
                        _continuousStatsTimer?.Start();
                        UpdateContinuousStats();
                    }
                    else
                    {
                        _plcManagerService.ReleasePollingLock("RunContinuousMode");
                        _continuousStopwatch.Reset();
                        _continuousStatsTimer?.Stop();
                        ProcessedImageCount = 0;
                        UpdateContinuousStats();
                    }
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
        public string RunContinuousButtonToolTip
        {
            get
            {
                if (IsRunningFolderFlow)
                {
                    var imgSourceNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
                    var def = _config?.ImageSources.FirstOrDefault(x => string.Equals(x.Name, imgSourceNode?.RefName, StringComparison.OrdinalIgnoreCase));
                    if (def != null && def.TriggerMode == ImageSourceTriggerMode.PlcTrigger)
                    {
                        return "Dừng chờ PLC Trigger";
                    }
                    return "Dừng chạy luồng liên tục";
                }
                return "Chạy liên tục qua Camera / Thư mục / PLC Trigger";
            }
        }

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

        public void OnRunOnceClicked()
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
                if (imgSourceDef is not null)
                {
                    if (imgSourceDef.SourceType == ImageSourceType.Folder)
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
                    else if (imgSourceDef.TriggerMode == ImageSourceTriggerMode.PlcTrigger)
                    {
                        IsRunningFolderFlow = true;
                        StatusBarText = $"Đang chạy liên tục chế độ PLC Trigger ({imgSourceDef.PlcTriggerPlcId}.{imgSourceDef.PlcTriggerTagName})...";
                        return;
                    }
                    else if (imgSourceDef.SourceType == ImageSourceType.Camera || imgSourceDef.SourceType == ImageSourceType.File)
                    {
                        StartCameraContinuousFlow(imgSourceDef);
                        return;
                    }
                }
            }

            RunFlow();
        }

        private void StartCameraContinuousFlow(ImageSourceDefinition sourceDef)
        {
            _folderFlowCts?.Cancel();
            _folderFlowCts = new CancellationTokenSource();
            IsRunningFolderFlow = true;

            var token = _folderFlowCts.Token;
            Task.Run(async () =>
            {
                int interval = Math.Max(50, sourceDef.FolderIntervalMs);

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            ClearImageSourceCache(sourceDef.Name);
                            await RunFlowAsync();
                        });

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
                    System.Diagnostics.Debug.WriteLine($"StartCameraContinuousFlow exception: {ex.Message}");
                }
                finally
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsRunningFolderFlow = false;
                    });
                }
            }, token);
        }

        private DateTime _lastPlcTriggerTime = DateTime.MinValue;

        private void OnPlcTagChangedForTrigger(object? sender, Application.PLC.Services.TagChangedEventArgs e)
        {
            if (!IsRunningFolderFlow || _config is null)
            {
                return;
            }

            var imageSourceNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
            if (imageSourceNode is null)
            {
                return;
            }

            var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, imageSourceNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (imgSourceDef is null || imgSourceDef.TriggerMode != ImageSourceTriggerMode.PlcTrigger)
            {
                return;
            }

            string targetPlcId = (imgSourceDef.PlcTriggerPlcId ?? "").Trim();
            string targetTagName = (imgSourceDef.PlcTriggerTagName ?? "").Trim();

            // Khớp PLC theo ID hoặc theo Tên PLC
            bool matchPlc = string.IsNullOrWhiteSpace(targetPlcId) || 
                            string.Equals(e.PlcId, targetPlcId, StringComparison.OrdinalIgnoreCase) ||
                            _plcManagerService.Plcs.Any(p => (string.Equals(p.Id, e.PlcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, e.PlcId, StringComparison.OrdinalIgnoreCase)) &&
                                                              (string.Equals(p.Id, targetPlcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, targetPlcId, StringComparison.OrdinalIgnoreCase)));

            // Khớp Tag theo Tên Tag hoặc theo Địa chỉ Tag (ví dụ: X0 vs X0_Trigger)
            var plcTag = _plcManagerService.Tags.FirstOrDefault(t => string.Equals(t.Name, e.TagName, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Address, e.TagName, StringComparison.OrdinalIgnoreCase));
            string tagAddress = plcTag?.Address ?? "";

            bool matchTag = string.Equals(e.TagName?.Trim(), targetTagName, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(tagAddress) && string.Equals(tagAddress.Trim(), targetTagName, StringComparison.OrdinalIgnoreCase));

            if (!matchPlc || !matchTag)
            {
                return;
            }

            bool isTriggered = false;
            switch (imgSourceDef.PlcTriggerEdge)
            {
                case PlcTriggerEdge.RisingEdge:
                    bool oldBoolRising = ToBoolValue(e.OldValue);
                    bool newBoolRising = ToBoolValue(e.NewValue);
                    isTriggered = (e.OldValue == null && newBoolRising) || (!oldBoolRising && newBoolRising);
                    break;

                case PlcTriggerEdge.FallingEdge:
                    bool oldBoolFalling = ToBoolValue(e.OldValue);
                    bool newBoolFalling = ToBoolValue(e.NewValue);
                    isTriggered = oldBoolFalling && !newBoolFalling;
                    break;

                case PlcTriggerEdge.Changed:
                    isTriggered = !ValuesEqual(e.OldValue, e.NewValue);
                    break;
            }

            if (isTriggered)
            {
                var now = DateTime.Now;
                if ((now - _lastPlcTriggerTime).TotalMilliseconds < 100)
                {
                    return; // Skip duplicate rapid triggers (100ms debouncing)
                }
                _lastPlcTriggerTime = now;

                System.Diagnostics.Debug.WriteLine($"PLC Trigger fired: Tag '{e.TagName}' on PLC '{e.PlcId}' changed from '{e.OldValue}' to '{e.NewValue}'. Running Job Flow!");
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(async () =>
                {
                    ClearImageSourceCache(imgSourceDef.Name);
                    await RunFlowAsync();
                });
            }
        }

        private static bool ToBoolValue(object? val)
        {
            if (val == null) return false;
            if (val is bool b) return b;
            if (int.TryParse(val.ToString(), out int i)) return i != 0;
            if (double.TryParse(val.ToString(), out double d)) return d != 0.0;
            if (bool.TryParse(val.ToString(), out bool bParsed)) return bParsed;
            return false;
        }

        private static bool ValuesEqual(object? v1, object? v2)
        {
            if (v1 == null && v2 == null) return true;
            if (v1 == null || v2 == null) return false;
            return string.Equals(v1.ToString(), v2.ToString(), StringComparison.OrdinalIgnoreCase);
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

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await RunSingleFlowFromImageFileAsync(filePath, sourceDef.Name);
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
            _continuousStopwatch.Reset();
            _continuousStatsTimer?.Stop();
            ProcessedImageCount = 0;
            UpdateContinuousStats();
        }

        private async Task RunSingleFlowFromImageFileAsync(string filePath, string sourceNodeName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || _config is null)
                return;

            var mat = await Task.Run(() => Cv2.ImRead(filePath));
            if (mat is null || mat.Empty())
                return;

            _inspectionService.ResetTracking();
            var __sw = System.Diagnostics.Stopwatch.StartNew();

            SetImageSourceCache(sourceNodeName, filePath, mat);
            _sharedImage.SetImage(mat);

            SyncToolGraphToConfig();
            EnsureTemplatePathsAbsolute(_config);

            var configCopy = _config;
            InspectionResult? inspectionResult = null;
            try
            {
                _lastRunError = null;
                // mat is owned by this method and remains read-only during inspection.
                // Passing it directly removes one full-resolution clone per file run.
                inspectionResult = await Task.Run(() => _inspectionService.Inspect(mat, configCopy, _dbManagerService));
                __sw.Stop();

                if (inspectionResult != null)
                {
                    inspectionResult.Timings.NodeTimings[sourceNodeName] = 0;
                    if (configCopy.PreprocessNodes != null)
                    {
                        foreach (var preNode in configCopy.PreprocessNodes)
                        {
                            if (!string.IsNullOrWhiteSpace(preNode.Name))
                            {
                                inspectionResult.Timings.NodeTimings[preNode.Name] = 0;
                            }
                        }
                    }
                    if (configCopy.ResultTransfers != null && configCopy.ResultTransfers.Count > 0)
                    {
                        _ = Application.PLC.Services.PlcResultTransferRunner.ExecuteResultTransfersAsync(configCopy, inspectionResult, _plcManagerService);
                    }
                }
            }
            catch (Exception ex)
            {
                inspectionResult = null;
                _lastRunError = "Lỗi khi chạy Flow: " + ex.Message;
            }
            finally
            {
                mat?.Dispose();
            }

            _lastRun = inspectionResult;
            LastResult = _lastRun;
            if (IsRunningFolderFlow)
            {
                ProcessedImageCount++;
                UpdateContinuousStats();
            }
            RefreshInspectionDashboard(_lastRun);
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
            OnPropertyChanged(nameof(Blob_LastRunCount));
        }

        private void RunSingleFlowFromImageFile(string filePath, string sourceNodeName) => _ = RunSingleFlowFromImageFileAsync(filePath, sourceNodeName);
    
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
            try
            {
                var imgSourceDef = SelectedImageSourceDef();
                int camIndex = imgSourceDef?.CameraIndex ?? 0;
                string? rtsp = (imgSourceDef != null && !string.IsNullOrWhiteSpace(imgSourceDef.RtspUrl)) ? imgSourceDef.RtspUrl : null;
                var mat = await _cameraService.CaptureSnapshotAsync(camIndex, rtsp);
                if (mat != null && !mat.Empty())
                {
                    if (imgSourceDef is not null)
                    {
                        SetImageSourceCache(imgSourceDef.Name, "camera", mat);
                    }
                    _sharedImage.SetImage(mat);
                    mat.Dispose();
                    RefreshPreviews();
                }
                else
                {
                    MessageBox.Show("Không thể chụp ảnh từ camera. Vui lòng kiểm tra lại kết nối camera trong tab Live Camera.", "Lỗi camera", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi chụp ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool _isExecutingRunFlow;

        private async Task RunFlowAsync()
        {
            if (_isExecutingRunFlow)
            {
                System.Diagnostics.Debug.WriteLine("[RunFlow] Skipped re-entrant flow execution request.");
                return;
            }
            _isExecutingRunFlow = true;

            _inspectionService.ResetTracking();
            Mat? snap = null;
            try
            {
                System.Diagnostics.Debug.WriteLine($"RunFlow: Checking for ImageSource nodes. Total nodes: {Nodes.Count}, ImageSources in config: {_config?.ImageSources.Count ?? 0}");
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
                            if (imgSourceDef.SourceType == ImageSourceType.Camera)
                            {
                                try
                                {
                                    var cameraMat = CaptureCameraSnapshotSafe(imgSourceDef.CameraIndex, string.IsNullOrWhiteSpace(imgSourceDef.RtspUrl) ? null : imgSourceDef.RtspUrl);
                                    if (cameraMat is not null && !cameraMat.Empty())
                                    {
                                        SetImageSourceCache(imgSourceDef.Name, "camera", cameraMat);
                                        _sharedImage.SetImage(cameraMat);
                                        snap = cameraMat;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"RunFlow Camera Exception: {ex.Message}");
                                }
                            }
                            else
                            {
                                snap = LoadImageFromSourceForPreview(imgSourceDef);
                                if (snap is not null && !snap.Empty())
                                {
                                    _sharedImage.SetImage(snap);
                                }
                            }
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
                        imageSourceMs = (imgSourceDef?.SourceType == ImageSourceType.File) ? 0 : (int)__sw.ElapsedMilliseconds;
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
                    _lastRunError = "Không có ảnh hoặc cấu hình (config).";
                    RefreshPreviews();
                    return;
                }
        
                SyncToolGraphToConfig();
                EnsureTemplatePathsAbsolute(_config);
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
                    _lastRunError = "Flow bị dừng vì node Origin hoặc Point đang chờ khởi tạo Template ảnh.";
                    RefreshPreviews();
                    RaiseToolPropertyPanelsChanged();
                    OnPropertyChanged(nameof(Blob_LastRunCount));
                    return;
                }
        
                var configCopy = _config;
                InspectionResult? inspectionResult = null;
                try
                {
                    _lastRunError = null;
                    // snap is owned by this method and is disposed after the read-only inspection.
                    // Do not clone a 20 MP frame solely for the Task boundary.
                    inspectionResult = await Task.Run(() => _inspectionService.Inspect(snap, configCopy, _dbManagerService));
                    if (inspectionResult != null)
                    {
                        if (imageSourceMs.HasValue && !string.IsNullOrWhiteSpace(imageSourceNodeRefName))
                        {
                            inspectionResult.Timings.NodeTimings[imageSourceNodeRefName] = imageSourceMs.Value;
                        }
                        if (configCopy.PreprocessNodes != null)
                        {
                            foreach (var preNode in configCopy.PreprocessNodes)
                            {
                                if (!string.IsNullOrWhiteSpace(preNode.Name))
                                {
                                    inspectionResult.Timings.NodeTimings[preNode.Name] = 0;
                                }
                            }
                        }

                        if (configCopy.ResultTransfers != null && configCopy.ResultTransfers.Count > 0)
                        {
                            _ = Application.PLC.Services.PlcResultTransferRunner.ExecuteResultTransfersAsync(configCopy, inspectionResult, _plcManagerService);
                        }
                    }
                }
                catch (Exception ex)
                {
                    inspectionResult = null;
                    _lastRunError = "Lỗi khi chạy Flow: " + ex.Message;
                }
                _lastRun = inspectionResult;
                UpdateNodeExecutionTimes();
                LastResult = _lastRun;
                if (IsRunningFolderFlow)
                {
                    ProcessedImageCount++;
                    UpdateContinuousStats();
                }
                RefreshInspectionDashboard(_lastRun);
                RefreshPreviews();
                RaiseToolPropertyPanelsChanged();
                OnPropertyChanged(nameof(Blob_LastRunCount));
            }
            finally
            {
                snap?.Dispose();
                _isExecutingRunFlow = false;
            }
        }

        private void RunFlow() => _ = RunFlowAsync();

    
        private void RefreshPreviews()
        {
            if (!EnableCanvasRendering)
            {
                SelectedNodePreviewImage = null;
                FinalPreviewImage = null;
                _cachedFinalPreviewImage = null;
                SelectedNodeOverlayItems = null;
                FinalOverlayItems = null;
                _finalPreviewDirty = false;
                return;
            }
            _finalPreviewDirty = true;
            RefreshFinalPreview();
            RefreshSelectedPreview();
        }
    
        private void RefreshFinalPreview()
        {
            if (!EnableCanvasRendering)
            {
                FinalPreviewImage = null;
                _cachedFinalPreviewImage = null;
                FinalOverlayItems = null;
                _finalPreviewDirty = false;
                return;
            }

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
            if (snap is not null && !snap.Empty())
            {
                _lastPreviewImageWidth = snap.Width;
                _lastPreviewImageHeight = snap.Height;
            }
            if (_config is not null && PreprocessPreviewEnabled)
            {
                using var processedFinal = _preprocessor.Run(snap, _config.Preprocess);
                _cachedFinalPreviewImage = processedFinal.Empty() ? null : processedFinal.ToBitmapSourceForDisplay();
                FinalPreviewImage = _cachedFinalPreviewImage;
            }
            else
            {
                _cachedFinalPreviewImage = snap.Empty() ? null : snap.ToBitmapSourceForDisplay();
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
            if (!EnableCanvasRendering)
            {
                SelectedNodePreviewImage = null;
                SelectedNodeOverlayItems = null;
                return;
            }

            var newSelectedNodeOverlayItems = new List<OverlayItem>();
            System.Diagnostics.Debug.WriteLine($"RefreshSelectedPreview: SelectedNode={SelectedNode?.Type}, RefName={SelectedNode?.RefName}");
            
            if (SelectedNode is not null && string.Equals(SelectedNode.Type, "ResultView", StringComparison.OrdinalIgnoreCase))
            {
                using var resultSnap = _sharedImage.GetSnapshot();
                SelectedNodePreviewImage = FinalPreviewImage ?? _cachedFinalPreviewImage ?? (resultSnap is null || resultSnap.Empty() ? null : resultSnap.ToBitmapSourceForDisplay());
                SelectedNodeOverlayItems = FinalOverlayItems;
                ActiveRoiLabel = string.Empty;
                return;
            }

            if (IsImageOutputNode && SelectedNode is not null)
            {
                using var rawSnapIO = _sharedImage.GetSnapshot();
                using var snapIO = rawSnapIO ?? new Mat();
                
                var ioDef = SelectedImageOutputDef();
                var inputName = ioDef?.InputNodeName;
                var targetNode = Nodes.FirstOrDefault(n => string.Equals(n.RefName, inputName, StringComparison.OrdinalIgnoreCase));
                if (targetNode is null)
                {
                    var inEdge = Edges.FirstOrDefault(e => string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase));
                    if (inEdge is not null)
                    {
                        targetNode = Nodes.FirstOrDefault(n => string.Equals(n.Id, inEdge.FromNodeId, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (targetNode is null || string.Equals(targetNode.Type, "ResultView", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedNodePreviewImage = FinalPreviewImage ?? _cachedFinalPreviewImage ?? (snapIO.Empty() ? null : snapIO.ToBitmapSourceForDisplay());
                    SelectedNodeOverlayItems = FinalOverlayItems;
                    return;
                }

                if (string.Equals(targetNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedNodePreviewImage = snapIO.Empty() ? null : snapIO.ToBitmapSourceForDisplay();
                }
                else
                {
                    using var processedSel = ResolveToolPreprocessForPreview(snapIO, targetNode);
                    SelectedNodePreviewImage = processedSel.Empty() ? null : processedSel.ToBitmapSourceForDisplay();
                }

                AddConfigRoisForNode(targetNode, newSelectedNodeOverlayItems);
                if (_lastRun is not null)
                {
                    BuildOverlayForNodeFromRunWithConfig(targetNode, _lastRun, newSelectedNodeOverlayItems);
                }
                else
                {
                    BuildOverlayForNode(targetNode, snapIO, newSelectedNodeOverlayItems);
                }
                SelectedNodeOverlayItems = newSelectedNodeOverlayItems;
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
                    using var loadedMat = LoadImageFromSourceForPreview(imgSourceDef);
                    if (loadedMat is not null && !loadedMat.Empty())
                    {
                        System.Diagnostics.Debug.WriteLine($"Setting SelectedNodePreviewImage from ImageSource: {loadedMat.Width}x{loadedMat.Height}");
                        if (_config is not null && PreprocessPreviewEnabled)
                        {
                            using var processed = _preprocessor.Run(loadedMat, _config.Preprocess);
                            SelectedNodePreviewImage = processed.ToBitmapSourceForDisplay();
                        }
                        else
                        {
                            SelectedNodePreviewImage = loadedMat.ToBitmapSourceForDisplay();
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
                    SelectedNodePreviewImage = processedSel.Empty() ? null : processedSel.ToBitmapSourceForDisplay();
                }
                else
                {
                    if (SelectedNode is not null && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Text", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Crop", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "ColorDiff", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "ImgArithmetic", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var processedSel = ResolveToolPreprocessForPreview(snap, SelectedNode);
                        SelectedNodePreviewImage = processedSel.Empty() ? null : processedSel.ToBitmapSourceForDisplay();
                    }
                    else
                    {
                        SelectedNodePreviewImage = _cachedFinalPreviewImage ?? (snap.Empty() ? null : snap.ToBitmapSourceForDisplay());
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("PreprocessPreviewEnabled is false, using cached or raw snap");
                SelectedNodePreviewImage = _cachedFinalPreviewImage ?? (snap.Empty() ? null : snap.ToBitmapSourceForDisplay());
            }
    
            if (SelectedNode is not null && string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                UpdateBlobThresholdPreview(snap);
            }
            else
            {
                BlobThresholdPreviewImage = null;
            }

            if (_config is null)
            {
                LinePreviewImage = null;
                PointEdgePreviewImage = null;
                BlobThresholdPreviewImage = null;
                return;
            }

            if (SelectedNode is not null && string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                RefreshLineRoiPreview(snap);
            }
            else
            {
                LinePreviewImage = null;
            }

            if (SelectedNode is not null && string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
            {
                RefreshPointEdgePreview(snap);
            }
            else
            {
                PointEdgePreviewImage = null;
            }

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
    
            var previewRoi = def.InspectRoi;
            if (def.Rois is not null && def.Rois.Count > 0)
            {
                previewRoi = ComputeBlobInspectRoi(def);
            }

            OpenCvSharp.Point2d originTeach = default;
            OpenCvSharp.Point2d originFound = default;
            double angleDeg = 0;
            if (_lastRun?.Origin is not null && (_lastRun.Origin.MatchRect.Width > 0 || _lastRun.Origin.Position.X != 0 || _lastRun.Origin.Position.Y != 0))
            {
                originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
                if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.TemplateRoi.Width > 0)
                {
                    originTeach = new OpenCvSharp.Point2d(_config.Origin.TemplateRoi.X + _config.Origin.TemplateRoi.Width / 2.0, _config.Origin.TemplateRoi.Y + _config.Origin.TemplateRoi.Height / 2.0);
                }
                else if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.SearchRoi.Width > 0)
                {
                    originTeach = new OpenCvSharp.Point2d(_config.Origin.SearchRoi.X + _config.Origin.SearchRoi.Width / 2.0, _config.Origin.SearchRoi.Y + _config.Origin.SearchRoi.Height / 2.0);
                }

                var mr = _lastRun.Origin.MatchRect;
                originFound = (mr.Width > 0 && mr.Height > 0)
                    ? new OpenCvSharp.Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                    : new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);

                angleDeg = _lastRun.Origin.AngleDeg;
            }

            Roi targetRoi;
            if (Math.Abs(angleDeg) > 0.001 || originFound.X != 0 || originFound.Y != 0)
            {
                var centerTeach = new OpenCvSharp.Point2d(previewRoi.X + previewRoi.Width / 2.0, previewRoi.Y + previewRoi.Height / 2.0);
                var centerFound = TransformPose(centerTeach, originTeach, originFound, angleDeg);
                targetRoi = new Roi
                {
                    X = (int)Math.Round(centerFound.X - previewRoi.Width / 2.0),
                    Y = (int)Math.Round(centerFound.Y - previewRoi.Height / 2.0),
                    Width = previewRoi.Width,
                    Height = previewRoi.Height,
                    Angle = previewRoi.Angle + angleDeg
                };
            }
            else
            {
                targetRoi = previewRoi;
            }

            using var rawCrop = ExtractRoiPatch(snap, targetRoi);
            if (rawCrop.Empty() || rawCrop.Width <= 0 || rawCrop.Height <= 0)
            {
                BlobThresholdPreviewImage = null;
                return;
            }

            using var crop = _config is not null && PreprocessPreviewEnabled ? _preprocessor.Run(rawCrop, _config.Preprocess) : rawCrop.Clone();

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
    
                    var rx = rr.Roi.X - previewRoi.X;
                    var ry = rr.Roi.Y - previewRoi.Y;
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
    
                    var rx = rr.Roi.X - previewRoi.X;
                    var ry = rr.Roi.Y - previewRoi.Y;
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
                BlobThresholdPreviewImage = view.ToBitmapSourceForDisplay();
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
                dst.Add(new OverlayRectItem
                {
                    X = config.Origin.SearchRoi.X,
                    Y = config.Origin.SearchRoi.Y,
                    Width = config.Origin.SearchRoi.Width,
                    Height = config.Origin.SearchRoi.Height,
                    Angle = 0,
                    Stroke = Brushes.Lime,
                    Label = "Origin S"
                });
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
    
                dst.Add(CreateRotatedRoiWithPose(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} L"));
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

            void AddContourCompareRoi(string contourCompareName)
            {
                var cc = config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, contourCompareName, StringComparison.OrdinalIgnoreCase));
                if (cc is null) return;

                if (cc.InspectRoi.Width > 0 && cc.InspectRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(cc.InspectRoi, Brushes.MediumSpringGreen, $"{cc.Name} CC"));
                }

                if (cc.TemplateRoi.Width > 0 && cc.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(cc.TemplateRoi, Brushes.MediumSpringGreen, $"{cc.Name} CCT"));
                }
            }

            foreach (var cc in config.ContourCompares)
            {
                AddContourCompareRoi(cc.Name);
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
    
        private static void BuildFinalOverlayFromRun(InspectionResult run, List<OverlayItem> dst, VisionConfig? config, bool showRois = true)
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
                    var halfW = mr.Width / 2.0;
                    var halfH = mr.Height / 2.0;
                    if (Math.Abs(p.AngleDeg) > 1e-4)
                    {
                        var rad = p.AngleDeg * Math.PI / 180.0;
                        var cos = Math.Cos(rad);
                        var sin = Math.Sin(rad);
    
                        var hx = new OpenCvSharp.Point2d(halfW * cos, halfW * sin);
                        var hy = new OpenCvSharp.Point2d(-halfH * sin, halfH * cos);
    
                        dst.Add(new OverlayLineItem { X1 = cx - hx.X, Y1 = cy - hx.Y, X2 = cx + hx.X, Y2 = cy + hx.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                        dst.Add(new OverlayLineItem { X1 = cx - hy.X, Y1 = cy - hy.Y, X2 = cx + hy.X, Y2 = cy + hy.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    }
                    else
                    {
                        dst.Add(new OverlayLineItem { X1 = cx - halfW, Y1 = cy, X2 = cx + halfW, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                        dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - halfH, X2 = cx, Y2 = cy + halfH, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    }
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
    
            var isCalibrated = config is not null && config.PixelsPerMm > 0 && Math.Abs(config.PixelsPerMm - 1.0) > 1e-6;
            var unitStr = isCalibrated ? "mm" : "px";

            foreach (var lpd in run.LinePairDetections)
            {
                if (!lpd.Found)
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = lpd.L1P1.X, Y1 = lpd.L1P1.Y, X2 = lpd.L1P2.X, Y2 = lpd.L1P2.Y, Stroke = Brushes.MediumPurple, Label = lpd.Name });
                dst.Add(new OverlayLineItem { X1 = lpd.L2P1.X, Y1 = lpd.L2P1.Y, X2 = lpd.L2P2.X, Y2 = lpd.L2P2.Y, Stroke = Brushes.MediumPurple, Label = string.Empty });
                dst.Add(new OverlayLineItem { X1 = lpd.ClosestA.X, Y1 = lpd.ClosestA.Y, X2 = lpd.ClosestB.X, Y2 = lpd.ClosestB.Y, Stroke = lpd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{lpd.Name}: {lpd.Value:0.###} {unitStr}" });
            }
    
            foreach (var epd in run.EdgePairDetections)
            {
                if (!epd.Found || double.IsNaN(epd.Value))
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = epd.L1P1.X, Y1 = epd.L1P1.Y, X2 = epd.L1P2.X, Y2 = epd.L1P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E1" });
                dst.Add(new OverlayLineItem { X1 = epd.L2P1.X, Y1 = epd.L2P1.Y, X2 = epd.L2P2.X, Y2 = epd.L2P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E2" });
                dst.Add(new OverlayLineItem { X1 = epd.ClosestA.X, Y1 = epd.ClosestA.Y, X2 = epd.ClosestB.X, Y2 = epd.ClosestB.Y, Stroke = epd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{epd.Name}: {epd.Value:0.###} {unitStr}" });
            }
    
            foreach (var ep in run.EdgePairs)
            {
                if (!ep.Found || double.IsNaN(ep.Value))
                {
                    continue;
                }
    
                dst.Add(new OverlayLineItem { X1 = ep.L1P1.X, Y1 = ep.L1P1.Y, X2 = ep.L1P2.X, Y2 = ep.L1P2.Y, Stroke = Brushes.MediumPurple, Label = ep.RefA });
                dst.Add(new OverlayLineItem { X1 = ep.L2P1.X, Y1 = ep.L2P1.Y, X2 = ep.L2P2.X, Y2 = ep.L2P2.Y, Stroke = Brushes.MediumPurple, Label = ep.RefB });
                dst.Add(new OverlayLineItem { X1 = ep.ClosestA.X, Y1 = ep.ClosestA.Y, X2 = ep.ClosestB.X, Y2 = ep.ClosestB.Y, Stroke = ep.Pass ? Brushes.Lime : Brushes.Red, Label = $"{ep.Name}: {ep.Value:0.###} {unitStr}" });
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
                    dst.Add(new OverlayRectItem
                    {
                        X = bb.X,
                        Y = bb.Y,
                        Width = bb.Width,
                        Height = bb.Height,
                        Angle = cdt.Angle,
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
    
                dst.Add(new OverlayLineItem { X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, Stroke = d.Pass ? Brushes.Lime : Brushes.Red, Label = $"{d.Name}: {d.Value:0.###} {unitStr}" });
            }
    
            foreach (var dd in run.LineToLineDistances)
            {
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.00} {unitStr}" });
            }
    
            foreach (var dd in run.PointToLineDistances)
            {
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.00} {unitStr}" });
            }
    
            foreach (var sld in run.SegmentLineDistances)
            {
                dst.Add(new OverlayLineItem { X1 = sld.ClosestA.X, Y1 = sld.ClosestA.Y, X2 = sld.ClosestB.X, Y2 = sld.ClosestB.Y, Stroke = sld.Pass ? Brushes.Lime : Brushes.Red, Label = $"{sld.Name}: {sld.Value:0.00} {unitStr}" });
            }
    
            foreach (var c in run.CircleFinders)
            {
                if (!c.Found || c.RadiusPx <= 0)
                {
                    continue;
                }
    
                if (c.EdgePoints is not null && c.EdgePoints.Count > 0)
                {
                    for (var i = 0; i < c.EdgePoints.Count; i++)
                    {
                        var pt = c.EdgePoints[i];
                        var isInlier = c.InlierFlags is not null && i < c.InlierFlags.Count && c.InlierFlags[i];
                        var ptStroke = isInlier ? Brushes.Lime : Brushes.Red;
                        AddCross(dst, pt.X, pt.Y, size: 6.0, stroke: ptStroke, strokeThickness: 1.5);
                    }
                }
    
                AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.Lime, strokeThickness: 2.0);
                AddCross(dst, c.Center.X, c.Center.Y, size: 10.0, stroke: Brushes.Lime, strokeThickness: 2.0);
                var rVal = (isCalibrated && config!.PixelsPerMm > 0) ? c.RadiusPx / config.PixelsPerMm : c.RadiusPx;
                dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.Lime, Label = $"{c.Name}: R={rVal:0.##} {unitStr}" });
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

            if (run.ContourCompares is not null)
            {
                foreach (var cc in run.ContourCompares)
                {
                    var stroke = cc.Pass ? Brushes.Lime : Brushes.Red;
                    var status = cc.Pass ? "OK" : "NG";

                    var lx = 12.0;
                    var ly = 12.0;
                    if (config is not null)
                    {
                        var ccDef = config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, cc.Name, StringComparison.OrdinalIgnoreCase));
                        if (ccDef is not null && ccDef.InspectRoi.Width > 0 && ccDef.InspectRoi.Height > 0)
                        {
                            if (run.Origin is not null)
                            {
                                var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
                                var tr = TransformPose(new Point2d(ccDef.InspectRoi.X, ccDef.InspectRoi.Y), originTeach, run.Origin.Position, run.Origin.AngleDeg);
                                lx = tr.X + 2;
                                ly = tr.Y + 2;
                            }
                            else
                            {
                                lx = ccDef.InspectRoi.X + 2;
                                ly = ccDef.InspectRoi.Y + 2;
                            }
                        }
                    }

                    var tplList = cc.TemplateContours ?? (cc.TemplateContour is not null ? new List<List<Point2d>> { cc.TemplateContour } : null);
                    if (tplList is not null)
                    {
                        foreach (var c in tplList)
                        {
                            if (c.Count > 1)
                            {
                                dst.Add(new OverlayPolylineItem
                                {
                                    Points = c.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                    IsClosed = true,
                                    Stroke = Brushes.Gold,
                                    StrokeThickness = 1.5,
                                    Label = string.Empty
                                });
                            }
                        }
                    }

                    if (cc.PassSegments is not null)
                    {
                        foreach (var seg in cc.PassSegments)
                        {
                            if (seg.Points.Count > 1)
                            {
                                dst.Add(new OverlayPolylineItem
                                {
                                    Points = seg.Points.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                    IsClosed = seg.IsClosed,
                                    Stroke = Brushes.Lime,
                                    StrokeThickness = 2.0,
                                    Label = string.Empty
                                });
                            }
                        }
                    }
                    else if (cc.PassContours is not null)
                    {
                        foreach (var c in cc.PassContours)
                        {
                            if (c.Count > 1)
                            {
                                dst.Add(new OverlayPolylineItem
                                {
                                    Points = c.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                    IsClosed = true,
                                    Stroke = Brushes.Lime,
                                    StrokeThickness = 2.0,
                                    Label = string.Empty
                                });
                            }
                        }
                    }

                    if (cc.FailSegments is not null)
                    {
                        foreach (var seg in cc.FailSegments)
                        {
                            if (seg.Points.Count > 1)
                            {
                                dst.Add(new OverlayPolylineItem
                                {
                                    Points = seg.Points.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                    IsClosed = seg.IsClosed,
                                    Stroke = Brushes.Red,
                                    StrokeThickness = 2.0,
                                    Label = string.Empty
                                });
                            }
                        }
                    }
                    else if (cc.FailContours is not null)
                    {
                        foreach (var c in cc.FailContours)
                        {
                            if (c.Count > 1)
                            {
                                dst.Add(new OverlayPolylineItem
                                {
                                    Points = c.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                    IsClosed = false,
                                    Stroke = Brushes.Red,
                                    StrokeThickness = 2.0,
                                    Label = string.Empty
                                });
                            }
                        }
                    }

                    dst.Add(new OverlayPointItem { X = lx, Y = ly, Radius = 1.0, Stroke = stroke, Label = $"{cc.Name} [{status}]: Score: {cc.MatchScore:0.####}, MaxDist: {cc.MaxDistancePx:0.##}px" });
                }
            }

            var showRoisInFinal = showRois;

            if (run.CreatePoints is not null)
            {
                foreach (var cp in run.CreatePoints)
                {
                    if (!cp.Success) continue;
                    if (showRoisInFinal)
                    {
                        dst.Add(new OverlayRectItem { X = (int)cp.X - 10, Y = (int)cp.Y - 10, Width = 20, Height = 20, Stroke = Brushes.LimeGreen, StrokeThickness = 1.5, Label = $"{cp.Name} Point ({cp.X:F1}, {cp.Y:F1})" });
                    }
                    AddCross(dst, cp.X, cp.Y, 20, Brushes.LimeGreen, 2.0);
                    AddCircle(dst, cp.X, cp.Y, 6, Brushes.LimeGreen, 1.5);
                }
            }

            if (run.CreateLines is not null)
            {
                foreach (var cl in run.CreateLines)
                {
                    if (!cl.Success) continue;
                    if (showRoisInFinal)
                    {
                        double minX = Math.Min(cl.X1, cl.X2);
                        double minY = Math.Min(cl.Y1, cl.Y2);
                        double w = Math.Max(10, Math.Abs(cl.X2 - cl.X1));
                        double h = Math.Max(10, Math.Abs(cl.Y2 - cl.Y1));
                        dst.Add(new OverlayRectItem { X = (int)minX, Y = (int)minY, Width = (int)w, Height = (int)h, Angle = cl.Angle, Stroke = Brushes.LimeGreen, StrokeThickness = 1.5, Label = $"{cl.Name} Line" });
                    }
                    dst.Add(new OverlayLineItem { X1 = cl.X1, Y1 = cl.Y1, X2 = cl.X2, Y2 = cl.Y2, Stroke = Brushes.LimeGreen, StrokeThickness = 2.5, Label = $"{cl.Name} (L={cl.Length:F1}px)" });
                    AddCross(dst, cl.X1, cl.Y1, 10, Brushes.LimeGreen, 1.5);
                    AddCross(dst, cl.X2, cl.Y2, 10, Brushes.LimeGreen, 1.5);
                }
            }

            if (run.CreateRects is not null)
            {
                foreach (var cr in run.CreateRects)
                {
                    if (!cr.Success) continue;
                    dst.Add(new OverlayRectItem { X = (int)cr.TopLeftX, Y = (int)cr.TopLeftY, Width = (int)cr.Width, Height = (int)cr.Height, Angle = cr.Angle, Stroke = Brushes.LimeGreen, StrokeThickness = 2.0, Label = $"{cr.Name} Rect ({cr.Width:F1}x{cr.Height:F1})" });
                    AddCross(dst, cr.X, cr.Y, 12, Brushes.LimeGreen, 1.5);
                }
            }

            if (run.CreateCircles is not null)
            {
                foreach (var cc in run.CreateCircles)
                {
                    if (!cc.Success) continue;
                    if (showRoisInFinal)
                    {
                        int r = (int)cc.Radius;
                        dst.Add(new OverlayRectItem { X = (int)cc.CenterX - r, Y = (int)cc.CenterY - r, Width = r * 2, Height = r * 2, Stroke = Brushes.LimeGreen, StrokeThickness = 1.5, Label = $"{cc.Name} Circle" });
                    }
                    AddCircle(dst, cc.CenterX, cc.CenterY, cc.Radius, Brushes.LimeGreen, 2.5);
                    AddCross(dst, cc.CenterX, cc.CenterY, 15, Brushes.LimeGreen, 1.5);
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
            var isCalibrated = _config is not null && _config.PixelsPerMm > 0 && Math.Abs(_config.PixelsPerMm - 1.0) > 1e-6;
            var unitStr = isCalibrated ? "mm" : "px";

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
                    var halfW = mr.Width / 2.0;
                    var halfH = mr.Height / 2.0;
                    if (Math.Abs(p.AngleDeg) > 1e-4)
                    {
                        var rad = p.AngleDeg * Math.PI / 180.0;
                        var cos = Math.Cos(rad);
                        var sin = Math.Sin(rad);

                        var hx = new OpenCvSharp.Point2d(halfW * cos, halfW * sin);
                        var hy = new OpenCvSharp.Point2d(-halfH * sin, halfH * cos);

                        dst.Add(new OverlayLineItem { X1 = cx - hx.X, Y1 = cy - hx.Y, X2 = cx + hx.X, Y2 = cy + hx.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                        dst.Add(new OverlayLineItem { X1 = cx - hy.X, Y1 = cy - hy.Y, X2 = cx + hy.X, Y2 = cy + hy.Y, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    }
                    else
                    {
                        dst.Add(new OverlayLineItem { X1 = cx - halfW, Y1 = cy, X2 = cx + halfW, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                        dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - halfH, X2 = cx, Y2 = cy + halfH, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    }
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
                dst.Add(new OverlayLineItem { X1 = r.ClosestA.X, Y1 = r.ClosestA.Y, X2 = r.ClosestB.X, Y2 = r.ClosestB.Y, Stroke = r.Pass ? Brushes.Lime : Brushes.Red, Label = $"{r.Name}: {r.Value:0.###} {unitStr}" });
                return;
            }
    
            if (string.Equals(node.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                var c = run.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (c is null || !c.Found || c.RadiusPx <= 0)
                {
                    return;
                }

                if (c.EdgePoints is not null && c.EdgePoints.Count > 0)
                {
                    for (var i = 0; i < c.EdgePoints.Count; i++)
                    {
                        var pt = c.EdgePoints[i];
                        var isInlier = c.InlierFlags is not null && i < c.InlierFlags.Count && c.InlierFlags[i];
                        var ptStroke = isInlier ? Brushes.Lime : Brushes.Red;
                        AddCross(dst, pt.X, pt.Y, size: 6.0, stroke: ptStroke, strokeThickness: 1.5);
                    }
                }

                AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.Lime, strokeThickness: 2.0);
                AddCross(dst, c.Center.X, c.Center.Y, size: 12.0, stroke: Brushes.Lime, strokeThickness: 2.0);
                var rVal = (isCalibrated && _config!.PixelsPerMm > 0) ? c.RadiusPx / _config.PixelsPerMm : c.RadiusPx;
                dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.Lime, Label = $"{c.Name}: R={rVal:0.##} {unitStr}" });
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
                dst.Add(new OverlayPointItem { X = d.Center.X, Y = d.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{d.Name}: {d.Value:0.###} {unitStr}" });
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
                dst.Add(new OverlayLineItem { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = d.Pass ? Brushes.Lime : Brushes.Red, Label = $"{d.Name}: {d.Value:0.###} {unitStr}" });
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
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.###} {unitStr}" });
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
                dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.###} {unitStr}" });
                return;
            }

            if (string.Equals(node.Type, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var dd = run.SegmentLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (dd is null)
                {
                    return;
                }

                LineDetectResult? ResolveLineRef(InspectionResult r, string name)
                {
                    var l = r.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (l is not null) return l;
                    var c = r.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (c is not null && c.Found)
                    {
                        var dx = c.LineP2.X - c.LineP1.X;
                        var dy = c.LineP2.Y - c.LineP1.Y;
                        var len = Math.Sqrt(dx * dx + dy * dy);
                        return new LineDetectResult(c.Name, c.LineP1, c.LineP2, len, Found: true);
                    }
                    var lpd = r.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (lpd is not null && lpd.Found)
                    {
                        var dx = lpd.L1P2.X - lpd.L1P1.X;
                        var dy = lpd.L1P2.Y - lpd.L1P1.Y;
                        var len = Math.Sqrt(dx * dx + dy * dy);
                        return new LineDetectResult(lpd.Name, lpd.L1P1, lpd.L1P2, len, Found: true);
                    }
                    var epd = r.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (epd is not null && epd.Found)
                    {
                        var dx = epd.L1P2.X - epd.L1P1.X;
                        var dy = epd.L1P2.Y - epd.L1P1.Y;
                        var len = Math.Sqrt(dx * dx + dy * dy);
                        return new LineDetectResult(epd.Name, epd.L1P1, epd.L1P2, len, Found: true);
                    }
                    return null;
                }

                var la = ResolveLineRef(run, dd.RefA);
                var lb = ResolveLineRef(run, dd.RefB);
                if (la is not null && la.Found)
                {
                    dst.Add(new OverlayLineItem { X1 = la.P1.X, Y1 = la.P1.Y, X2 = la.P2.X, Y2 = la.P2.Y, Stroke = Brushes.DeepSkyBlue, Label = $"{la.Name} Seg" });
                }
                if (lb is not null && lb.Found)
                {
                    var ip = new System.Windows.Point(lb.P1.X, lb.P1.Y);
                    var dir = new System.Windows.Point(lb.P2.X - lb.P1.X, lb.P2.Y - lb.P1.Y);
                    if (_lastPreviewImageWidth > 0 && _lastPreviewImageHeight > 0 && TryClipInfiniteLineToImage(ip, dir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var p1, out var p2))
                    {
                        dst.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.Gold, Label = $"{lb.Name} (Inf)" });
                    }
                    else
                    {
                        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                        if (len > 1e-6)
                        {
                            var uX = dir.X / len;
                            var uY = dir.Y / len;
                            dst.Add(new OverlayLineItem { X1 = lb.P1.X - 5000 * uX, Y1 = lb.P1.Y - 5000 * uY, X2 = lb.P1.X + 5000 * uX, Y2 = lb.P1.Y + 5000 * uY, Stroke = Brushes.Gold, Label = $"{lb.Name} (Inf)" });
                        }
                        else
                        {
                            dst.Add(new OverlayLineItem { X1 = lb.P1.X, Y1 = lb.P1.Y, X2 = lb.P2.X, Y2 = lb.P2.Y, Stroke = Brushes.Gold, Label = $"{lb.Name} (Inf)" });
                        }
                    }
                }

                if (!double.IsNaN(dd.Value))
                {
                    dst.Add(new OverlayLineItem { X1 = dd.ClosestA.X, Y1 = dd.ClosestA.Y, X2 = dd.ClosestB.X, Y2 = dd.ClosestB.Y, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{dd.Name}: {dd.Value:0.###} {unitStr}" });
                    dst.Add(new OverlayPointItem { X = dd.ClosestA.X, Y = dd.ClosestA.Y, Radius = 3.0, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = string.Empty });
                    dst.Add(new OverlayPointItem { X = dd.ClosestB.X, Y = dd.ClosestB.Y, Radius = 3.0, Stroke = dd.Pass ? Brushes.Lime : Brushes.Red, Label = string.Empty });
                }
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

            if (string.Equals(node.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
            {
                var cc = run.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cc is null) return;

                var ccDef = _config?.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, cc.Name, StringComparison.OrdinalIgnoreCase));
                var stroke = cc.Pass ? Brushes.Lime : Brushes.Red;
                var status = cc.Pass ? "OK" : "NG";

                double lx = 12, ly = 12;
                if (ccDef is not null)
                {
                    lx = ccDef.InspectRoi.X + 2;
                    ly = ccDef.InspectRoi.Y + 2;
                }

                var tplList = cc.TemplateContours ?? (cc.TemplateContour is not null ? new List<List<Point2d>> { cc.TemplateContour } : null);
                if (tplList is not null)
                {
                    foreach (var c in tplList)
                    {
                        if (c.Count > 1)
                        {
                            dst.Add(new OverlayPolylineItem
                            {
                                Points = c.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                IsClosed = true,
                                Stroke = Brushes.Gold,
                                StrokeThickness = 1.5,
                                Label = string.Empty
                            });
                        }
                    }
                }

                if (cc.PassSegments is not null)
                {
                    foreach (var seg in cc.PassSegments)
                    {
                        if (seg.Points.Count > 1)
                        {
                            dst.Add(new OverlayPolylineItem
                            {
                                Points = seg.Points.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                IsClosed = seg.IsClosed,
                                Stroke = Brushes.Lime,
                                StrokeThickness = 2.0,
                                Label = string.Empty
                            });
                        }
                    }
                }
                else if (cc.PassContours is not null)
                {
                    foreach (var c in cc.PassContours)
                    {
                        if (c.Count > 1)
                        {
                            dst.Add(new OverlayPolylineItem
                            {
                                Points = c.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                IsClosed = true,
                                Stroke = Brushes.Lime,
                                StrokeThickness = 2.0,
                                Label = string.Empty
                            });
                        }
                    }
                }

                if (cc.FailSegments is not null)
                {
                    foreach (var seg in cc.FailSegments)
                    {
                        if (seg.Points.Count > 1)
                        {
                            dst.Add(new OverlayPolylineItem
                            {
                                Points = seg.Points.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                IsClosed = seg.IsClosed,
                                Stroke = Brushes.Red,
                                StrokeThickness = 2.0,
                                Label = string.Empty
                            });
                        }
                    }
                }
                else if (cc.FailContours is not null)
                {
                    foreach (var c in cc.FailContours)
                    {
                        if (c.Count > 1)
                        {
                            dst.Add(new OverlayPolylineItem
                            {
                                Points = c.Select(p => new System.Windows.Point(p.X, p.Y)).ToList(),
                                IsClosed = false,
                                Stroke = Brushes.Red,
                                StrokeThickness = 2.0,
                                Label = string.Empty
                            });
                        }
                    }
                }

                dst.Add(new OverlayPointItem { X = lx, Y = ly, Radius = 1.0, Stroke = stroke, Label = $"{cc.Name} [{status}]: Score: {cc.MatchScore:0.####}, MaxDist: {cc.MaxDistancePx:0.##}px" });
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

            if (string.Equals(node.Type, "ColorDiff", StringComparison.OrdinalIgnoreCase))
            {
                var cdRes = run.ColorDiffs.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                var cdDef = _config?.ColorDiffs?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cdDef is not null)
                {
                    var brush = (cdRes?.Pass ?? false) ? Brushes.Lime : Brushes.Red;
                    dst.Add(CreateRotatedRoi(cdDef.InspectRoi, brush, $"{cdDef.Name} Sample"));

                    if (cdRes is not null)
                    {
                        var text = $"{cdRes.Name}: ΔE = {cdRes.DeltaE:F2} (L={cdRes.MeasuredL:F1}, a={cdRes.MeasuredA:F1}, b={cdRes.MeasuredB:F1})";
                        dst.Add(new OverlayTextItem
                        {
                            X = cdDef.InspectRoi.X + 4,
                            Y = cdDef.InspectRoi.Y + 4,
                            Text = text,
                            Foreground = brush,
                            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0))
                        });
                    }
                }
                return;
            }

            if (string.Equals(node.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
            {
                var cdt = run.CodeDetections?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (cdt is not null && cdt.Found && cdt.BoundingBox.Width > 0 && cdt.BoundingBox.Height > 0)
                {
                    var bb = cdt.BoundingBox;
                    dst.Add(new OverlayRectItem
                    {
                        X = bb.X,
                        Y = bb.Y,
                        Width = bb.Width,
                        Height = bb.Height,
                        Angle = cdt.Angle,
                        Stroke = Brushes.Lime,
                        Label = $"{cdt.Name}: {cdt.Text}"
                    });
                }
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
                    dst.Add(new OverlayRectItem
                    {
                        X = _config.Origin.SearchRoi.X,
                        Y = _config.Origin.SearchRoi.Y,
                        Width = _config.Origin.SearchRoi.Width,
                        Height = _config.Origin.SearchRoi.Height,
                        Angle = 0,
                        Stroke = Brushes.Lime,
                        Label = "Origin S"
                    });
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
                    var (patternCenter, patternAngle) = GetCurrentPointPatternCenterAndAngle(p);
                    var rad = patternAngle * Math.PI / 180.0;
                    var rotX = p.OffsetPx.X * Math.Cos(rad) - p.OffsetPx.Y * Math.Sin(rad);
                    var rotY = p.OffsetPx.X * Math.Sin(rad) + p.OffsetPx.Y * Math.Cos(rad);
                    dst.Add(new OverlayPointItem { X = patternCenter.X + rotX, Y = patternCenter.Y + rotY, Stroke = Brushes.DeepSkyBlue, Label = p.Name });
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
                    dst.Add(CreateRotatedRoiWithPose(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} L"));
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
                        dst.Add(new OverlayRectItem
                        {
                            X = bb.X,
                            Y = bb.Y,
                            Width = bb.Width,
                            Height = bb.Height,
                            Angle = cdt.Angle,
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
            return CreateRotatedRoiWithPose(new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height), stroke, label, roi.Angle);
        }

        private OverlayRectItem CreateRotatedRoiWithPose(OpenCvSharp.Rect roi, System.Windows.Media.Brush? stroke, string? label, double roiAngle = 0)
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
                    Angle = roiAngle + angleDeg,
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
                Angle = roiAngle,
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
                   string.Equals(l, "DefectROI", StringComparison.OrdinalIgnoreCase) ||
                   l.EndsWith("Crop", StringComparison.OrdinalIgnoreCase) ||
                   l.Contains("Crop", StringComparison.OrdinalIgnoreCase);
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
                Height = roi.Height,
                Angle = Math.Round(roi.Angle - angleDeg, 1)
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
                        Angle = roi.Angle,
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
                    Angle = angleDeg + roi.Angle,
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
                Angle = roi.Angle,
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
                dst.Add(new OverlayRectItem
                {
                    X = _config.Origin.SearchRoi.X,
                    Y = _config.Origin.SearchRoi.Y,
                    Width = _config.Origin.SearchRoi.Width,
                    Height = _config.Origin.SearchRoi.Height,
                    Angle = 0,
                    Stroke = Brushes.Lime,
                    Label = "Origin S"
                });
            }

            if (showRois && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
            {
                dst.Add(CreateRotatedRoiWithPose(_config.Origin.TemplateRoi, Brushes.Gold, "Origin T"));
            }
    
            foreach (var p in _config.Points)
            {
                if (showRois && p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(p.SearchRoi, Brushes.DeepSkyBlue, $"{p.Name} S"));
                }
    
                if (showRois && p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(p.TemplateRoi, Brushes.Gold, $"{p.Name} T"));
                }
    
                dst.Add(new OverlayPointItem { X = p.WorldPosition.X, Y = p.WorldPosition.Y, Stroke = Brushes.DeepSkyBlue, Label = p.Name });
            }
    
            foreach (var l in _config.Lines)
            {
                if (showRois && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(l.SearchRoi, Brushes.MediumPurple, $"{l.Name} L"));
                }
            }
    
            foreach (var b in _config.BlobDetections)
            {
                if (showRois && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(b.InspectRoi, Brushes.Gold, $"{b.Name} B"));
                }
            }
    
            foreach (var c in _config.CircleFinders)
            {
                if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                {
                    dst.Add(CreateRotatedRoiWithPose(c.SearchRoi, Brushes.MediumPurple, $"{c.Name} CIR"));
                }
            }
    
            foreach (var e in _config.EdgePairDetections)
            {
                AddEpdSearchStripsOverlay(dst, e, showRois);
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
        }
    
        private void AddEpdSearchStripsOverlay(List<OverlayItem> dst, EdgePairDetectDefinition e, bool showRois)
        {
            if (!showRois || e.SearchRoi.Width <= 0 || e.SearchRoi.Height <= 0)
            {
                return;
            }

            dst.Add(CreateRotatedRoiWithPose(e.SearchRoi, Brushes.MediumPurple, $"{e.Name} EPD"));

            var stripCount = Math.Clamp(e.StripCount, 1, 100);
            var stripLength = Math.Max(3, e.StripLength);
            if (stripCount <= 0)
            {
                return;
            }

            var roiCenter = new OpenCvSharp.Point2d(e.SearchRoi.X + e.SearchRoi.Width / 2.0, e.SearchRoi.Y + e.SearchRoi.Height / 2.0);
            var roiAngle = e.SearchRoi.Angle;

            var hasOriginPose = _lastRun?.Origin is not null && (_lastRun.Origin.MatchRect.Width > 0 || _lastRun.Origin.Position.X != 0 || _lastRun.Origin.Position.Y != 0);
            OpenCvSharp.Point2d originTeach = default;
            OpenCvSharp.Point2d originFound = default;
            double originAngleDeg = 0.0;

            if (hasOriginPose && _config?.Origin is not null)
            {
                originTeach = (_config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
                    ? new OpenCvSharp.Point2d(_config.Origin.TemplateRoi.X + _config.Origin.TemplateRoi.Width / 2.0, _config.Origin.TemplateRoi.Y + _config.Origin.TemplateRoi.Height / 2.0)
                    : new OpenCvSharp.Point2d(_config.Origin.SearchRoi.X + _config.Origin.SearchRoi.Width / 2.0, _config.Origin.SearchRoi.Y + _config.Origin.SearchRoi.Height / 2.0);
                if (_config.Origin.WorldPosition.X != 0 || _config.Origin.WorldPosition.Y != 0)
                {
                    originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
                }

                var mr = _lastRun!.Origin.MatchRect;
                originFound = (mr.Width > 0 && mr.Height > 0)
                    ? new OpenCvSharp.Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                    : new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);
                originAngleDeg = _lastRun.Origin.AngleDeg;
            }

            if (e.Orientation == CaliperOrientation.Vertical)
            {
                var y1 = e.SearchRoi.Y + (e.SearchRoi.Height - stripLength) / 2.0;
                var y2 = y1 + stripLength;
                for (var i = 0; i < stripCount; i++)
                {
                    var x = e.SearchRoi.X + (i + 0.5) * e.SearchRoi.Width / stripCount;
                    var p1 = new OpenCvSharp.Point2d(x, y1);
                    var p2 = new OpenCvSharp.Point2d(x, y2);

                    if (Math.Abs(roiAngle) > 0.0001)
                    {
                        p1 = Rotate(p1, roiCenter, roiAngle);
                        p2 = Rotate(p2, roiCenter, roiAngle);
                    }

                    if (hasOriginPose)
                    {
                        p1 = TransformPose(p1, originTeach, originFound, originAngleDeg);
                        p2 = TransformPose(p2, originTeach, originFound, originAngleDeg);
                    }

                    dst.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.MediumPurple, StrokeThickness = 1.0 });
                }
            }
            else
            {
                var x1 = e.SearchRoi.X + (e.SearchRoi.Width - stripLength) / 2.0;
                var x2 = x1 + stripLength;
                for (var i = 0; i < stripCount; i++)
                {
                    var y = e.SearchRoi.Y + (i + 0.5) * e.SearchRoi.Height / stripCount;
                    var p1 = new OpenCvSharp.Point2d(x1, y);
                    var p2 = new OpenCvSharp.Point2d(x2, y);

                    if (Math.Abs(roiAngle) > 0.0001)
                    {
                        p1 = Rotate(p1, roiCenter, roiAngle);
                        p2 = Rotate(p2, roiCenter, roiAngle);
                    }

                    if (hasOriginPose)
                    {
                        p1 = TransformPose(p1, originTeach, originFound, originAngleDeg);
                        p2 = TransformPose(p2, originTeach, originFound, originAngleDeg);
                    }

                }
            }
        }
    }
}

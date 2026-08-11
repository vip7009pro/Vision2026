using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;
using ZXing;
using ZXing.Common;

namespace VisionInspectionApp.Application;

public partial class InspectionService
{
    public InspectionResult Inspect(Mat image, VisionConfig config, DB.Services.IDbManagerService? dbManagerOverride = null)
    {
        var effectiveDbManager = dbManagerOverride ?? _dbManager;
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var result = new InspectionResult();

        var swTotal = Stopwatch.StartNew();

        var matsToDispose = new List<Mat>();
        var matsLock = new object();
        try
        {
            const int guidedRadiusPx = 50;
            var track = _trackByProductCode.GetOrAdd(config.ProductCode ?? string.Empty, _ => new TrackState());

            static Roi IntersectRoi(Roi a, Roi b)
            {
                if (a.Width <= 0 || a.Height <= 0) return new Roi();
                if (b.Width <= 0 || b.Height <= 0) return new Roi();

                var ax2 = a.X + a.Width;
                var ay2 = a.Y + a.Height;
                var bx2 = b.X + b.Width;
                var by2 = b.Y + b.Height;

                var x1 = Math.Max(a.X, b.X);
                var y1 = Math.Max(a.Y, b.Y);
                var x2 = Math.Min(ax2, bx2);
                var y2 = Math.Min(ay2, by2);

                var w = x2 - x1;
                var h = y2 - y1;
                if (w <= 0 || h <= 0) return new Roi();
                return new Roi { X = x1, Y = y1, Width = w, Height = h };
            }

            static Roi ClampRoiToImage(Roi roi, Mat img)
            {
                if (roi.Width <= 0 || roi.Height <= 0) return new Roi();

                var x1 = Math.Clamp(roi.X, 0, Math.Max(0, img.Width - 1));
                var y1 = Math.Clamp(roi.Y, 0, Math.Max(0, img.Height - 1));
                var x2 = Math.Clamp(roi.X + roi.Width, 0, img.Width);
                var y2 = Math.Clamp(roi.Y + roi.Height, 0, img.Height);

                var w = x2 - x1;
                var h = y2 - y1;
                if (w <= 0 || h <= 0) return new Roi();
                return new Roi { X = x1, Y = y1, Width = w, Height = h };
            }

            static Roi WindowRoi(Point2d center, int radius)
            {
                var x = (int)Math.Round(center.X - radius);
                var y = (int)Math.Round(center.Y - radius);
                var s = radius * 2;
                return new Roi { X = x, Y = y, Width = s, Height = s };
            }
            var nodesById = (config.ToolGraph?.Nodes ?? new List<ToolGraphNode>())
                .Where(n => !string.IsNullOrWhiteSpace(n.Id))
                .ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

            var imageSourcesByName = (config.ImageSources ?? new List<ImageSourceDefinition>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            var preprocessNodesByName = (config.PreprocessNodes ?? new List<PreprocessNodeDefinition>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            var edges = config.ToolGraph?.Edges ?? new List<ToolGraphEdge>();

            // Default (backward-compatible) processing path (lazy + thread-safe).
            var processedDefault = new Lazy<Mat>(() =>
            {
                var m = _preprocessor.Run(image, config.Preprocess);
                lock (matsLock) matsToDispose.Add(m);
                return m;
            });
            Mat GetProcessedDefault() => processedDefault.Value;

            var preprocessMatCache = new ConcurrentDictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);

            var templateCache = new ConcurrentDictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);

            Mat GetTemplateGray(string? templatePath)
            {
                if (string.IsNullOrWhiteSpace(templatePath))
                {
                    return new Mat();
                }

                return templateCache.GetOrAdd(templatePath, p =>
                {
                    var t = Cv2.ImRead(p, ImreadModes.Grayscale);
                    lock (matsLock) matsToDispose.Add(t);
                    return t;
                });
            }

            Mat GetPreprocessNodeOutput(string preprocessNodeId)
            {
                return preprocessMatCache.GetOrAdd(preprocessNodeId, id =>
                {
                    if (!nodesById.TryGetValue(id, out var node)
                        || !string.Equals(node.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                    {
                        return image;
                    }

                    preprocessNodesByName.TryGetValue(node.RefName ?? string.Empty, out var preDef);
                    var settings = preDef?.Settings ?? new PreprocessSettings();
                    var rois = preDef?.Rois;

                    // Preprocess node input: either raw image or another preprocess output connected to "In" or "Image".
                    var inEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, id, StringComparison.OrdinalIgnoreCase)
                                                          && (string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase)));
                    Mat baseMat = image;
                    if (inEdge is not null && nodesById.TryGetValue(inEdge.FromNodeId, out var fromNode))
                    {
                        if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                        {
                            baseMat = GetPreprocessNodeOutput(fromNode.Id);
                        }
                        else if (string.Equals(fromNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                        {
                            baseMat = image;
                        }
                    }

                    var __sw = Stopwatch.StartNew();
                    var m = _preprocessor.Run(baseMat, settings, rois);
                    __sw.Stop();
                    if (!string.IsNullOrWhiteSpace(node.RefName))
                    {
                        result.Timings.NodeTimings[node.RefName] = (int)__sw.ElapsedMilliseconds;
                    }
                    lock (matsLock) matsToDispose.Add(m);
                    return m;
                });
            }

            (Mat ImageMat, PreprocessSettings Settings) ResolveToolPreprocess(string toolType, string toolRefName)
            {
                var defaultSettings = config.Preprocess;

                var toolNode = nodesById.Values.FirstOrDefault(n => string.Equals(n.Type, toolType, StringComparison.OrdinalIgnoreCase)
                                                                    && string.Equals(n.RefName, toolRefName, StringComparison.OrdinalIgnoreCase));
                if (toolNode is null)
                {
                    return (GetProcessedDefault(), defaultSettings);
                }

                var imageEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, toolNode.Id, StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase));
                
                if (imageEdge is null || !nodesById.TryGetValue(imageEdge.FromNodeId, out var fromNode))
                {
                    return (GetProcessedDefault(), defaultSettings);
                }

                if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    var ppSettings = preprocessNodesByName.TryGetValue(fromNode.RefName ?? string.Empty, out var preDef) ? (preDef.Settings ?? new PreprocessSettings()) : new PreprocessSettings();
                    var ppMat = GetPreprocessNodeOutput(fromNode.Id);
                    return (ppMat, ppSettings);
                }
                else if (string.Equals(fromNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                {
                    var preprocessedMat = _preprocessor.Run(image, defaultSettings);
                    lock (matsLock) matsToDispose.Add(preprocessedMat);
                    return (preprocessedMat, defaultSettings);
                }

                return (GetProcessedDefault(), defaultSettings);
            }

            static List<BlobInfo> DetectBlobsInCrop(Mat crop, Roi inspectRoi, List<BlobRoiDefinition>? rois, BlobPolarity polarity, int threshold, int minArea, int maxArea, Point2d centerFound, double totalAngle)
            {
                var blobs = new List<BlobInfo>();
                if (crop is null || crop.Empty())
                {
                    return blobs;
                }

                Mat gray = crop;
                using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (grayOwned is not null)
                {
                    gray = grayOwned;
                }

                threshold = Math.Clamp(threshold, 0, 255);
                using var bw = new Mat();
                var thrType = polarity == BlobPolarity.DarkOnLight ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                Cv2.Threshold(gray, bw, threshold, 255, thrType);

                var hasMulti = rois is not null && rois.Count > 0;
                if (hasMulti)
                {
                    using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);

                    var anyInclude = false;
                    foreach (var rr in rois!)
                    {
                        if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var rx = rr.Roi.X - inspectRoi.X;
                        var ry = rr.Roi.Y - inspectRoi.Y;
                        var r = new Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                        r = r.Intersect(new Rect(0, 0, bw.Cols, bw.Rows));
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

                    foreach (var rr in rois!)
                    {
                        if (rr.Mode != BlobRoiMode.Exclude || rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var rx = rr.Roi.X - inspectRoi.X;
                        var ry = rr.Roi.Y - inspectRoi.Y;
                        var r = new Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                        r = r.Intersect(new Rect(0, 0, bw.Cols, bw.Rows));
                        if (r.Width <= 0 || r.Height <= 0)
                        {
                            continue;
                        }

                        using var sub = new Mat(mask, r);
                        sub.SetTo(Scalar.Black);
                    }

                    Cv2.BitwiseAnd(bw, mask, bw);
                }

                minArea = Math.Max(0, minArea);
                maxArea = Math.Max(minArea, maxArea);

                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                var nLabels = Cv2.ConnectedComponentsWithStats(
                    bw,
                    labels,
                    stats,
                    centroids,
                    PixelConnectivity.Connectivity8,
                    MatType.CV_32S);

                for (var i = 1; i < nLabels; i++)
                {
                    var left = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                    var top = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                    var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                    var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                    var areaPx = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);

                    if (areaPx < minArea || areaPx > maxArea)
                    {
                        continue;
                    }

                    var cx = centroids.Get<double>(i, 0);
                    var cy = centroids.Get<double>(i, 1);

                    var globalCentroid = MapToGlobal(new Point2d(cx, cy), inspectRoi.Width, inspectRoi.Height, centerFound, totalAngle);
                    var bboxCenterLocal = new Point2d(left + width / 2.0, top + height / 2.0);
                    var bboxCenterGlobal = MapToGlobal(bboxCenterLocal, inspectRoi.Width, inspectRoi.Height, centerFound, totalAngle);
                    var fullRect = new Rect((int)Math.Round(bboxCenterGlobal.X - width / 2.0), (int)Math.Round(bboxCenterGlobal.Y - height / 2.0), width, height);

                    blobs.Add(new BlobInfo(fullRect, globalCentroid, areaPx));
                }
                return blobs;
            }

            static SurfaceCompareResult RunSurfaceCompare(
                Mat matBgrOrGray, 
                Point2d originTeach, 
                Point2d originFound, 
                double angleDeg, 
                SurfaceCompareDefinition def,
                ImagePreprocessor preprocessor,
                PreprocessSettings settings)
            {
                if (matBgrOrGray is null)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                if (def is null || string.IsNullOrWhiteSpace(def.Name))
                {
                    return new SurfaceCompareResult(string.Empty, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                var templateRoiTeach = def.TemplateRoi;
                var inspectRoiTeach = def.InspectRoi;

                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0)
                {
                    templateRoiTeach = inspectRoiTeach;
                }

                if (inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0)
                {
                    inspectRoiTeach = templateRoiTeach;
                }

                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0 || inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                if (string.IsNullOrWhiteSpace(def.TemplateImageFile) || !File.Exists(def.TemplateImageFile))
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                // Convert input to grayscale.
                Mat testGray = matBgrOrGray;
                using var testGrayOwned = matBgrOrGray.Channels() == 1 ? null : matBgrOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (testGrayOwned is not null)
                {
                    testGray = testGrayOwned;
                }

                // Load and Preprocess template exactly like the current image.
                using var templRaw = Cv2.ImRead(def.TemplateImageFile, ImreadModes.Grayscale);
                if (templRaw.Empty())
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                // Apply the same preprocessing steps to the template crop.
                using var templCrop0 = preprocessor.Run(templRaw, settings);
                // We need to un-rotate the target image to match the teach template orientation.
                var trTeach = new Roi { X = templateRoiTeach.X, Y = templateRoiTeach.Y, Width = templateRoiTeach.Width, Height = templateRoiTeach.Height, Angle = templateRoiTeach.Angle };
                using var testCropRaw = ExtractStraightRoi(testGray, trTeach, originTeach, originFound, angleDeg, out var centerFoundTpl);
                if (testCropRaw.Width <= 0 || testCropRaw.Height <= 0)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                // Apply the same preprocessing steps to the target crop.
                using var testCrop = preprocessor.Run(testCropRaw, settings);

                // Make sure template crop has the exact size
                using var tplCrop = new Mat();
                if (templCrop0.Width != templateRoiTeach.Width || templCrop0.Height != templateRoiTeach.Height)
                {
                    Cv2.Resize(templCrop0, tplCrop, new Size(templateRoiTeach.Width, templateRoiTeach.Height), 0, 0, InterpolationFlags.Area);
                }
                else
                {
                    templCrop0.CopyTo(tplCrop);
                }

                if (def.AutoAlign && def.AutoAlignMaxShiftPx > 0 && testCrop.Width > def.AutoAlignMaxShiftPx * 2 && testCrop.Height > def.AutoAlignMaxShiftPx * 2)
                {
                    var shift = Math.Clamp(def.AutoAlignMaxShiftPx, 1, 30);
                    var innerRect = new Rect(shift, shift, tplCrop.Width - shift * 2, tplCrop.Height - shift * 2);
                    using var tplInner = new Mat(tplCrop, innerRect);
                    using var matchRes = new Mat();
                    Cv2.MatchTemplate(testCrop, tplInner, matchRes, TemplateMatchModes.SqDiffNormed);
                    Cv2.MinMaxLoc(matchRes, out double minVal, out _, out Point minLoc, out _);

                    int dx = minLoc.X - shift;
                    int dy = minLoc.Y - shift;
                    if (dx != 0 || dy != 0)
                    {
                        using var M = new Mat(2, 3, MatType.CV_32FC1);
                        M.Set(0, 0, 1.0f); M.Set(0, 1, 0.0f); M.Set(0, 2, (float)-dx);
                        M.Set(1, 0, 0.0f); M.Set(1, 1, 1.0f); M.Set(1, 2, (float)-dy);
                        using var alignedTest = new Mat();
                        Cv2.WarpAffine(testCrop, alignedTest, M, testCrop.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
                        alignedTest.CopyTo(testCrop);
                    }
                }

                using var bw = new Mat();
                var thr = Math.Clamp(def.DiffThreshold, 0, 255);

                if (def.Algorithm == SurfaceCompareAlgorithm.SSIM)
                {
                    var winSize = Math.Clamp(def.SsimWindowSize > 0 ? def.SsimWindowSize : 7, 3, 21);
                    if (winSize % 2 == 0) winSize += 1;

                    using var img1Float = new Mat();
                    using var img2Float = new Mat();
                    testCrop.ConvertTo(img1Float, MatType.CV_32FC1);
                    tplCrop.ConvertTo(img2Float, MatType.CV_32FC1);

                    using var mu1 = new Mat();
                    using var mu2 = new Mat();
                    var kSize = new Size(winSize, winSize);
                    Cv2.GaussianBlur(img1Float, mu1, kSize, 1.5);
                    Cv2.GaussianBlur(img2Float, mu2, kSize, 1.5);

                    using var mu1Sq = new Mat();
                    using var mu2Sq = new Mat();
                    using var mu1Mu2 = new Mat();
                    Cv2.Multiply(mu1, mu1, mu1Sq);
                    Cv2.Multiply(mu2, mu2, mu2Sq);
                    Cv2.Multiply(mu1, mu2, mu1Mu2);

                    using var sigma1Sq = new Mat();
                    using var sigma2Sq = new Mat();
                    using var sigma12 = new Mat();

                    using var t1 = new Mat();
                    using var t2 = new Mat();
                    using var t3 = new Mat();
                    Cv2.Multiply(img1Float, img1Float, t1);
                    Cv2.GaussianBlur(t1, t1, kSize, 1.5);
                    Cv2.Subtract(t1, mu1Sq, sigma1Sq);

                    Cv2.Multiply(img2Float, img2Float, t2);
                    Cv2.GaussianBlur(t2, t2, kSize, 1.5);
                    Cv2.Subtract(t2, mu2Sq, sigma2Sq);

                    Cv2.Multiply(img1Float, img2Float, t3);
                    Cv2.GaussianBlur(t3, t3, kSize, 1.5);
                    Cv2.Subtract(t3, mu1Mu2, sigma12);

                    const double C1 = 6.5025, C2 = 58.5225;
                    using var num1 = new Mat();
                    using var num2 = new Mat();
                    using var den1 = new Mat();
                    using var den2 = new Mat();

                    Cv2.AddWeighted(mu1Mu2, 2.0, mu1Mu2, 0, C1, num1);
                    Cv2.AddWeighted(sigma12, 2.0, sigma12, 0, C2, num2);

                    Cv2.AddWeighted(mu1Sq, 1.0, mu2Sq, 1.0, C1, den1);
                    Cv2.AddWeighted(sigma1Sq, 1.0, sigma2Sq, 1.0, C2, den2);

                    using var num = new Mat();
                    using var den = new Mat();
                    Cv2.Multiply(num1, num2, num);
                    Cv2.Multiply(den1, den2, den);

                    using var ssimMap = new Mat();
                    Cv2.Divide(num, den, ssimMap);

                    using var dissim = new Mat();
                    using var ones = new Mat(ssimMap.Rows, ssimMap.Cols, MatType.CV_32FC1, Scalar.All(1.0));
                    Cv2.Subtract(ones, ssimMap, dissim);
                    Cv2.Multiply(dissim, Scalar.All(255.0), dissim);

                    using var dissim8u = new Mat();
                    dissim.ConvertTo(dissim8u, MatType.CV_8UC1);

                    var ssimThr = Math.Clamp(def.SsimThreshold > 0 ? (1.0 - def.SsimThreshold) * 255.0 : 38.0, 5.0, 250.0);
                    if (def.DiffThreshold > 0 && def.DiffThreshold != 25)
                    {
                        ssimThr = (1.0 - Math.Clamp(def.DiffThreshold / 100.0, 0.05, 0.95)) * 255.0;
                    }
                    Cv2.Threshold(dissim8u, bw, ssimThr, 255, ThresholdTypes.Binary);
                }
                else if (def.Algorithm == SurfaceCompareAlgorithm.GradientAdaptive)
                {
                    using var grad1X = new Mat();
                    using var grad1Y = new Mat();
                    using var grad2X = new Mat();
                    using var grad2Y = new Mat();
                    Cv2.Sobel(testCrop, grad1X, MatType.CV_16S, 1, 0);
                    Cv2.Sobel(testCrop, grad1Y, MatType.CV_16S, 0, 1);
                    Cv2.Sobel(tplCrop, grad2X, MatType.CV_16S, 1, 0);
                    Cv2.Sobel(tplCrop, grad2Y, MatType.CV_16S, 0, 1);

                    using var absGrad1X = new Mat();
                    using var absGrad1Y = new Mat();
                    using var absGrad2X = new Mat();
                    using var absGrad2Y = new Mat();
                    Cv2.ConvertScaleAbs(grad1X, absGrad1X);
                    Cv2.ConvertScaleAbs(grad1Y, absGrad1Y);
                    Cv2.ConvertScaleAbs(grad2X, absGrad2X);
                    Cv2.ConvertScaleAbs(grad2Y, absGrad2Y);

                    using var mag1 = new Mat();
                    using var mag2 = new Mat();
                    Cv2.AddWeighted(absGrad1X, 0.5, absGrad1Y, 0.5, 0, mag1);
                    Cv2.AddWeighted(absGrad2X, 0.5, absGrad2Y, 0.5, 0, mag2);

                    using var gradDiff = new Mat();
                    Cv2.Absdiff(mag1, mag2, gradDiff);

                    using var grayDiff = new Mat();
                    Cv2.Absdiff(testCrop, tplCrop, grayDiff);

                    using var combinedDiff = new Mat();
                    var wGrad = Math.Clamp(def.GradientWeight > 0 ? def.GradientWeight : 0.5, 0.0, 1.0);
                    Cv2.AddWeighted(grayDiff, 1.0 - wGrad, gradDiff, wGrad, 0, combinedDiff);

                    Cv2.Threshold(combinedDiff, bw, thr, 255, ThresholdTypes.Binary);
                }
                else
                {
                    var edgeTol = Math.Max(0, def.EdgeTolerancePx);
                    if (edgeTol > 0)
                    {
                        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(edgeTol * 2 + 1, edgeTol * 2 + 1));
                        using var maxImg = new Mat();
                        using var minImg = new Mat();
                        Cv2.MorphologyEx(tplCrop, maxImg, MorphTypes.Dilate, kernel);
                        Cv2.MorphologyEx(tplCrop, minImg, MorphTypes.Erode, kernel);

                        using var diffHigh = new Mat();
                        using var diffLow = new Mat();
                        Cv2.Subtract(testCrop, maxImg, diffHigh);
                        Cv2.Subtract(minImg, testCrop, diffLow);

                        using var defectRaw = new Mat();
                        Cv2.BitwiseOr(diffHigh, diffLow, defectRaw);
                        Cv2.Threshold(defectRaw, bw, thr, 255, ThresholdTypes.Binary);
                    }
                    else
                    {
                        using var diffGray = new Mat();
                        Cv2.Absdiff(testCrop, tplCrop, diffGray);
                        Cv2.Threshold(diffGray, bw, thr, 255, ThresholdTypes.Binary);
                    }
                }

                // Apply include/exclude multi-ROI mask (definitions are in teach space).
                var rois = def.Rois;
                if (rois is not null && rois.Count > 0)
                {
                    using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);

                    var anyInclude = false;
                    foreach (var rr0 in rois)
                    {
                        if (rr0 is null || rr0.Roi.Width <= 0 || rr0.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var ptsI = new Point[4];
                        ptsI[0] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[1] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[2] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);
                        ptsI[3] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);

                        if (rr0.Mode == BlobRoiMode.Include)
                        {
                            anyInclude = true;
                            Cv2.FillPoly(mask, new[] { ptsI }, Scalar.White);
                        }
                    }

                    if (!anyInclude)
                    {
                        mask.SetTo(Scalar.White);
                    }

                    foreach (var rr0 in rois)
                    {
                        if (rr0 is null || rr0.Mode != BlobRoiMode.Exclude || rr0.Roi.Width <= 0 || rr0.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var ptsI = new Point[4];
                        ptsI[0] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[1] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[2] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);
                        ptsI[3] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);

                        Cv2.FillPoly(mask, new[] { ptsI }, Scalar.Black);
                    }

                    Cv2.BitwiseAnd(bw, mask, bw);
                }
                else
                {
                    // If no sub-ROIs are defined, default to masking with the TemplateRoi transformed.
                    using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);
                    var tr0 = def.TemplateRoi;
                    if (tr0.Width <= 0 || tr0.Height <= 0) tr0 = def.InspectRoi;

                    var ptsI = new Point[4];
                    ptsI[0] = new Point(tr0.X - templateRoiTeach.X, tr0.Y - templateRoiTeach.Y);
                    ptsI[1] = new Point(tr0.X + tr0.Width - templateRoiTeach.X, tr0.Y - templateRoiTeach.Y);
                    ptsI[2] = new Point(tr0.X + tr0.Width - templateRoiTeach.X, tr0.Y + tr0.Height - templateRoiTeach.Y);
                    ptsI[3] = new Point(tr0.X - templateRoiTeach.X, tr0.Y + tr0.Height - templateRoiTeach.Y);
                    
                    Cv2.FillPoly(mask, new[] { ptsI }, Scalar.White);
                    Cv2.BitwiseAnd(bw, mask, bw);
                }

                // Capture debug previews (Raw Diff)
                byte[]? imgBin = null;
                try { Cv2.ImEncode(".png", bw, out imgBin); } catch { }

                var k = Math.Max(1, def.MorphKernel);
                if (k % 2 == 0) k += 1;
                if (k >= 3)
                {
                    using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(k, k));
                    Cv2.MorphologyEx(bw, bw, MorphTypes.Close, kernel);
                    Cv2.MorphologyEx(bw, bw, MorphTypes.Open, kernel);
                }

                var minArea = Math.Max(0, def.MinBlobArea);
                var maxArea = Math.Max(minArea, def.MaxBlobArea);

                var defects = new List<SurfaceCompareDefect>();
                double maxFoundArea = 0.0;

                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                var nLabels = Cv2.ConnectedComponentsWithStats(
                    bw,
                    labels,
                    stats,
                    centroids,
                    PixelConnectivity.Connectivity8,
                    MatType.CV_32S);

                for (var i = 1; i < nLabels; i++)
                {
                    var left = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                    var top = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                    var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                    var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                    var areaPx = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);

                    // Track max area found before filtering (for debug/tuning)
                    if (areaPx > maxFoundArea) maxFoundArea = areaPx;

                    if (areaPx < minArea || areaPx > maxArea)
                    {
                        continue;
                    }

                    var cx = centroids.Get<double>(i, 0);
                    var cy = centroids.Get<double>(i, 1);

                    // Map local coordinates from straight patch back to global found image
                    var localCenterX = left + width / 2.0;
                    var localCenterY = top + height / 2.0;
                    var globalCenterForRect = MapToGlobal(new Point2d(localCenterX, localCenterY), templateRoiTeach.Width, templateRoiTeach.Height, centerFoundTpl, angleDeg);
                    
                    var fullRect = new Rect((int)Math.Round(globalCenterForRect.X - width / 2.0), (int)Math.Round(globalCenterForRect.Y - height / 2.0), width, height);
                    var centroid = MapToGlobal(new Point2d(cx, cy), templateRoiTeach.Width, templateRoiTeach.Height, centerFoundTpl, angleDeg);

                    defects.Add(new SurfaceCompareDefect(fullRect, angleDeg, centroid, areaPx));
                }

                var pass = defects.Count >= def.MinCount && defects.Count <= def.MaxCount;

                // Capture debug previews if needed (for troubleshooting)
                byte[]? imgTpl = null;
                byte[]? imgCur = null;
                byte[]? imgDif = null;
                try
                {
                    Cv2.ImEncode(".png", tplCrop, out imgTpl);
                    Cv2.ImEncode(".png", testCrop, out imgCur);
                    Cv2.ImEncode(".png", bw, out imgDif);      // Resulting diff after morphology
                }
                catch { /* Ignore encoding errors */ }

                return new SurfaceCompareResult(def.Name, defects.Count, maxFoundArea, defects, pass, imgTpl, imgCur, imgBin, imgDif);
            }

            static ContourCompareResult RunContourCompare(
                Mat matBgrOrGray,
                Point2d originTeach,
                Point2d originFound,
                double angleDeg,
                ContourCompareDefinition def,
                ImagePreprocessor preprocessor,
                PreprocessSettings settings)
            {
                if (matBgrOrGray is null || def is null || string.IsNullOrWhiteSpace(def.Name))
                {
                    return new ContourCompareResult(def?.Name ?? string.Empty, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                var templateRoiTeach = def.TemplateRoi;
                var inspectRoiTeach = def.InspectRoi;
                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0) templateRoiTeach = inspectRoiTeach;
                if (inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0) inspectRoiTeach = templateRoiTeach;

                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0 || inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                if (string.IsNullOrWhiteSpace(def.TemplateImageFile) || !File.Exists(def.TemplateImageFile))
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                using var templRaw = Cv2.ImRead(def.TemplateImageFile, ImreadModes.Grayscale);
                if (templRaw.Empty())
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                Mat testGray = matBgrOrGray;
                using var testGrayOwned = matBgrOrGray.Channels() == 1 ? null : matBgrOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (testGrayOwned is not null) testGray = testGrayOwned;

                // Calculate global center of Template ROI when pose transform is applied
                var centerTemplateTeach = new Point2d(templateRoiTeach.X + templateRoiTeach.Width / 2.0, templateRoiTeach.Y + templateRoiTeach.Height / 2.0);
                var centerTemplateRot = Rotate(centerTemplateTeach, originTeach, angleDeg);
                var deltaX = originFound.X - originTeach.X;
                var deltaY = originFound.Y - originTeach.Y;
                var centerFoundTemplate = new Point2d(centerTemplateRot.X + deltaX, centerTemplateRot.Y + deltaY);

                // Expand Template ROI by padding to search for sub-pixel/local shift
                var pad = 20;
                var trPadTeach = new Roi
                {
                    X = templateRoiTeach.X - pad,
                    Y = templateRoiTeach.Y - pad,
                    Width = templateRoiTeach.Width + pad * 2,
                    Height = templateRoiTeach.Height + pad * 2,
                    Angle = templateRoiTeach.Angle
                };

                using var searchCropPadRaw = ExtractStraightRoi(testGray, trPadTeach, originTeach, originFound, angleDeg, out _);
                if (searchCropPadRaw.Width <= 0 || searchCropPadRaw.Height <= 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                using var searchCropPad = preprocessor.Run(searchCropPadRaw, settings);
                using var templCrop = preprocessor.Run(templRaw, settings);

                var testRect = new Rect(pad, pad, templCrop.Width, templCrop.Height).Intersect(new Rect(0, 0, searchCropPad.Width, searchCropPad.Height));
                if (testRect.Width <= 0 || testRect.Height <= 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                using var testCrop = new Mat(searchCropPad, testRect).Clone();

                static List<Point[]> FindAllContours(Mat img, double c1, double c2, int minArea)
                {
                    using var canny = new Mat();
                    Cv2.Canny(img, canny, c1, c2);
                    Cv2.FindContours(canny, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    if (contours is null || contours.Length == 0) return new List<Point[]>();
                    return contours.Where(c => c.Length >= 4 && (Cv2.ContourArea(c) >= minArea || Cv2.ArcLength(c, true) >= minArea / 2.0)).OrderByDescending(c => Cv2.ArcLength(c, true)).ToList();
                }

                var c1 = def.CannyThreshold1 > 0 ? def.CannyThreshold1 : 50;
                var c2 = def.CannyThreshold2 > 0 ? def.CannyThreshold2 : 150;
                var minArea = Math.Max(1, def.MinContourArea);

                var tplContours = FindAllContours(templCrop, c1, c2, minArea);
                var testContours = FindAllContours(testCrop, c1, c2, minArea);

                if (tplContours.Count == 0 || testContours.Count == 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                var allTplLocalPoints = tplContours.SelectMany(c => c).Select(p => new Point2d(p.X, p.Y)).ToList();
                var allTestLocalPoints = testContours.SelectMany(c => c).Select(p => new Point2d(p.X, p.Y)).ToList();

                double maxAllowedDist = def.MaxHausdorffDistPx > 0 ? def.MaxHausdorffDistPx : 4.0;
                double maxAllowedAreaDiff = def.MaxAreaDiffPercent > 0 ? def.MaxAreaDiffPercent : 5.0;
                double maxAllowedShapeScore = def.MaxShapeMatchScore > 0 ? def.MaxShapeMatchScore : 0.10;

                // 1. Robust ICP Alignment: find optimal translation (alignDx, alignDy) in range [-15, 15]
                double alignDx = 0.0, alignDy = 0.0;
                double minError = double.MaxValue;
                double clipThreshSq = maxAllowedDist * maxAllowedDist * 2.0;

                for (double tX = -15; tX <= 15; tX += 1.0)
                {
                    for (double tY = -15; tY <= 15; tY += 1.0)
                    {
                        double errSum = 0.0;
                        int sampleCount = 0;
                        int step = Math.Max(1, allTestLocalPoints.Count / 100);

                        for (int k = 0; k < allTestLocalPoints.Count; k += step)
                        {
                            var p = allTestLocalPoints[k];
                            double px = p.X + tX;
                            double py = p.Y + tY;
                            double dMinSq = double.MaxValue;

                            foreach (var q in allTplLocalPoints)
                            {
                                double dX = px - q.X;
                                double dY = py - q.Y;
                                double dSq = dX * dX + dY * dY;
                                if (dSq < dMinSq) dMinSq = dSq;
                            }

                            errSum += Math.Min(clipThreshSq, dMinSq);
                            sampleCount++;
                        }

                        if (sampleCount > 0 && errSum < minError)
                        {
                            minError = errSum;
                            alignDx = tX;
                            alignDy = tY;
                        }
                    }
                }

                var shiftRot = Rotate(new Point2d(alignDx, alignDy), new Point2d(0, 0), angleDeg);
                var centerFoundTestCrop = new Point2d(centerFoundTemplate.X + shiftRot.X, centerFoundTemplate.Y + shiftRot.Y);

                // Convert contours to global space for UI canvas overlay drawing
                var tplPointsGlobalList = tplContours.Select(c => c.Select(p => MapToGlobal(new Point2d(p.X, p.Y), templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg)).ToList()).ToList();

                var passSegmentsList = new List<ContourSegment>();
                var failSegmentsList = new List<ContourSegment>();
                var passContoursGlobalList = new List<List<Point2d>>();
                var failContoursGlobalList = new List<List<Point2d>>();
                double maxDistPx = 0.0;

                static void ClassifyAndAddSubSegments(
                    List<Point2d> localPoints,
                    double[] distances,
                    double maxAllowedDist,
                    Func<Point2d, Point2d> mapToGlobal,
                    List<ContourSegment>? passSegDst,
                    List<ContourSegment> failSegDst,
                    List<List<Point2d>>? passDst,
                    List<List<Point2d>> failDst)
                {
                    if (localPoints.Count < 2) return;

                    List<Point2d>? currentSeg = null;
                    bool? currentSegOk = null;

                    for (int i = 0; i < localPoints.Count; i++)
                    {
                        bool ok = distances[i] <= maxAllowedDist;
                        var ptGlobal = mapToGlobal(localPoints[i]);

                        if (currentSegOk is null || currentSegOk != ok)
                        {
                            if (currentSeg is not null && currentSeg.Count > 1)
                            {
                                var seg = new ContourSegment(currentSeg, IsClosed: false);
                                if (currentSegOk == true)
                                {
                                    passSegDst?.Add(seg);
                                    passDst?.Add(currentSeg);
                                }
                                else
                                {
                                    failSegDst.Add(seg);
                                    failDst.Add(currentSeg);
                                }
                            }

                            currentSeg = new List<Point2d>();
                            currentSegOk = ok;

                            if (i > 0)
                            {
                                currentSeg.Add(mapToGlobal(localPoints[i - 1]));
                            }
                        }

                        currentSeg!.Add(ptGlobal);
                    }

                    if (currentSeg is not null && currentSeg.Count > 1)
                    {
                        var seg = new ContourSegment(currentSeg, IsClosed: false);
                        if (currentSegOk == true)
                        {
                            passSegDst?.Add(seg);
                            passDst?.Add(currentSeg);
                        }
                        else
                        {
                            failSegDst.Add(seg);
                            failDst.Add(currentSeg);
                        }
                    }
                }

                // 2. Classify Test Contours (Intact closed contours vs partial extra/deformed strokes)
                foreach (var cTest in testContours)
                {
                    var localPts = cTest.Select(p => new Point2d(p.X + alignDx, p.Y + alignDy)).ToList();
                    var globalPts = localPts.Select(pt => MapToGlobal(pt, templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg)).ToList();

                    int okCount = 0;
                    var ptDistances = new double[localPts.Count];
                    for (int k = 0; k < localPts.Count; k++)
                    {
                        var pt = localPts[k];
                        double minDist = double.MaxValue;
                        foreach (var q in allTplLocalPoints)
                        {
                            double dX = pt.X - q.X;
                            double dY = pt.Y - q.Y;
                            double d = Math.Sqrt(dX * dX + dY * dY);
                            if (d < minDist) minDist = d;
                        }
                        ptDistances[k] = minDist;
                        if (minDist > maxDistPx) maxDistPx = minDist;
                        if (minDist <= maxAllowedDist) okCount++;
                    }

                    double okRatio = (double)okCount / localPts.Count;
                    if (okRatio >= 0.80)
                    {
                        var seg = new ContourSegment(globalPts, IsClosed: true);
                        passSegmentsList.Add(seg);
                        passContoursGlobalList.Add(globalPts);
                    }
                    else
                    {
                        ClassifyAndAddSubSegments(
                            localPts,
                            ptDistances,
                            maxAllowedDist,
                            pt => MapToGlobal(pt, templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg),
                            passSegmentsList,
                            failSegmentsList,
                            passContoursGlobalList,
                            failContoursGlobalList);
                    }
                }

                // 3. Classify Template Contours (Missing strokes)
                foreach (var cTpl in tplContours)
                {
                    var localPts = cTpl.Select(p => new Point2d(p.X, p.Y)).ToList();
                    var ptDistances = new double[localPts.Count];

                    int okCount = 0;
                    for (int k = 0; k < localPts.Count; k++)
                    {
                        var pt = localPts[k];
                        double minDist = double.MaxValue;
                        foreach (var pTest in allTestLocalPoints)
                        {
                            double dX = pt.X - (pTest.X + alignDx);
                            double dY = pt.Y - (pTest.Y + alignDy);
                            double d = Math.Sqrt(dX * dX + dY * dY);
                            if (d < minDist) minDist = d;
                        }
                        ptDistances[k] = minDist;
                        if (minDist > maxDistPx) maxDistPx = minDist;
                        if (minDist <= maxAllowedDist) okCount++;
                    }

                    double okRatio = (double)okCount / localPts.Count;
                    if (okRatio < 0.85)
                    {
                        ClassifyAndAddSubSegments(
                            localPts,
                            ptDistances,
                            maxAllowedDist,
                            pt => MapToGlobal(pt, templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg),
                            passSegDst: null,
                            failSegmentsList,
                            passDst: null,
                            failContoursGlobalList);
                    }
                }

                var tplCombined = tplContours.SelectMany(c => c).ToArray();
                var testCombined = testContours.SelectMany(c => c).ToArray();
                double matchScore = (tplCombined.Length > 0 && testCombined.Length > 0) ? Cv2.MatchShapes(tplCombined, testCombined, ShapeMatchModes.I1) : 999.0;

                double areaTpl = Math.Max(1.0, tplContours.Sum(c => Cv2.ContourArea(c)));
                double areaTest = testContours.Sum(c => Cv2.ContourArea(c));
                double areaDiffPercent = Math.Abs(areaTest - areaTpl) / areaTpl * 100.0;

                double perimTpl = Math.Max(1.0, tplContours.Sum(c => Cv2.ArcLength(c, true)));
                double perimTest = testContours.Sum(c => Cv2.ArcLength(c, true));
                double perimDiffPercent = Math.Abs(perimTest - perimTpl) / perimTpl * 100.0;

                bool pass = def.MatchMethod switch
                {
                    ContourMatchMethod.HausdorffDistance => failSegmentsList.Count == 0 && maxDistPx <= maxAllowedDist,
                    ContourMatchMethod.AreaPerimeterDiff => areaDiffPercent <= maxAllowedAreaDiff,
                    _ => failSegmentsList.Count == 0 || matchScore <= maxAllowedShapeScore
                };

                var testPointsGlobalList = testContours.Select(c => c.Select(p => MapToGlobal(new Point2d(p.X, p.Y), templCrop.Width, templCrop.Height, centerFoundTestCrop, angleDeg)).ToList()).ToList();

                return new ContourCompareResult(
                    def.Name,
                    Found: true,
                    Pass: pass,
                    MatchScore: matchScore,
                    MaxDistancePx: maxDistPx,
                    AreaDiffPercent: areaDiffPercent,
                    PerimeterDiffPercent: perimDiffPercent,
                    TemplateContour: tplPointsGlobalList.FirstOrDefault(),
                    TestContour: testPointsGlobalList.FirstOrDefault(),
                    TemplateContours: tplPointsGlobalList,
                    TestContours: testPointsGlobalList,
                    PassContours: passContoursGlobalList,
                    FailContours: failContoursGlobalList,
                    PassSegments: passSegmentsList,
                    FailSegments: failSegmentsList);
            }

            static CaliperResult DetectCaliper(Mat matBgrOrGray, Roi roiTeach, CaliperDefinition def, Point2d originTeach, Point2d originFound, double angleDeg)
            {
                if (matBgrOrGray is null || roiTeach.Width <= 0 || roiTeach.Height <= 0)
                {
                    return new CaliperResult(def.Name, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
                }

                using var patch = ExtractStraightRoi(matBgrOrGray, roiTeach, originTeach, originFound, angleDeg, out var centerFound);
                if (patch.Empty())
                {
                    return new CaliperResult(def.Name, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
                }

                using var patchGrayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
                Mat gray = patchGrayOwned ?? patch;

                var rect = new Rect(0, 0, patch.Width, patch.Height);

                var stripCount = Math.Clamp(def.StripCount, 1, 200);
                var stripWidth = Math.Clamp(def.StripWidth, 1, Math.Max(1, Math.Min(rect.Width, rect.Height)));
                var stripLength = Math.Clamp(def.StripLength, 3, Math.Max(3, Math.Max(rect.Width, rect.Height)));

                var points = new List<CaliperEdgePoint>(stripCount);
                var strengths = new List<double>(stripCount);

                static double InterpPeak(double a, double b, double c)
                {
                    var denom = (a - 2 * b + c);
                    if (Math.Abs(denom) < 1e-12) return 0.0;
                    return 0.5 * (a - c) / denom;
                }

                for (var i = 0; i < stripCount; i++)
                {
                    if (def.Orientation == CaliperOrientation.Vertical)
                    {
                        var xCenter = (i + 0.5) * rect.Width / stripCount;
                        var x0 = (int)Math.Round(xCenter - stripWidth / 2.0);
                        var y0 = (int)Math.Round((rect.Height - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripWidth, stripLength)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 0 || sr.Height <= 2) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);

                        var n = prof.Rows;
                        if (n < 3) continue;

                        var bestIdx = -1;
                        var bestVal = 0.0;
                        for (var y = 1; y < n - 1; y++)
                        {
                            var v0 = prof.Get<double>(y - 1, 0);
                            var v1 = prof.Get<double>(y, 0);
                            var v2 = prof.Get<double>(y + 1, 0);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > bestVal)
                            {
                                bestVal = g;
                                bestIdx = y;
                            }
                        }

                        if (bestIdx < 1 || bestIdx >= n - 1) continue;
                        if (bestVal < def.MinEdgeStrength) continue;

                        var gL = Math.Abs(prof.Get<double>(bestIdx, 0) - prof.Get<double>(bestIdx - 1, 0));
                        var gC = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx - 1, 0)) * 0.5;
                        var gR = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx, 0));
                        var sub = InterpPeak(gL, gC, gR);

                        var ySub = bestIdx + sub;
                        var xLocal = rect.X + sr.X + sr.Width / 2.0;
                        var yLocal = rect.Y + sr.Y + ySub;
                        var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        points.Add(new CaliperEdgePoint(ptGlobal.X, ptGlobal.Y, bestVal));
                        strengths.Add(bestVal);
                    }
                    else
                    {
                        var yCenter = (i + 0.5) * rect.Height / stripCount;
                        var y0 = (int)Math.Round(yCenter - stripWidth / 2.0);
                        var x0 = (int)Math.Round((rect.Width - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripLength, stripWidth)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 2 || sr.Height <= 0) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

                        var n = prof.Cols;
                        if (n < 3) continue;

                        var bestIdx = -1;
                        var bestVal = 0.0;
                        for (var x = 1; x < n - 1; x++)
                        {
                            var v0 = prof.Get<double>(0, x - 1);
                            var v1 = prof.Get<double>(0, x);
                            var v2 = prof.Get<double>(0, x + 1);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > bestVal)
                            {
                                bestVal = g;
                                bestIdx = x;
                            }
                        }

                        if (bestIdx < 1 || bestIdx >= n - 1) continue;
                        if (bestVal < def.MinEdgeStrength) continue;

                        var gL = Math.Abs(prof.Get<double>(0, bestIdx) - prof.Get<double>(0, bestIdx - 1));
                        var gC = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx - 1)) * 0.5;
                        var gR = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx));
                        var sub = InterpPeak(gL, gC, gR);

                        var xSub = bestIdx + sub;
                        var xLocal = rect.X + sr.X + xSub;
                        var yLocal = rect.Y + sr.Y + sr.Height / 2.0;
                        var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        points.Add(new CaliperEdgePoint(ptGlobal.X, ptGlobal.Y, bestVal));
                        strengths.Add(bestVal);
                    }
                }

                if (points.Count < 2)
                {
                    var avg0 = strengths.Count == 0 ? 0.0 : strengths.Average();
                    return new CaliperResult(def.Name, Found: false, points, default, default, avg0);
                }

                var meanX = points.Average(p => p.X);
                var meanY = points.Average(p => p.Y);

                var sxx = 0.0;
                var syy = 0.0;
                var sxy = 0.0;
                foreach (var p in points)
                {
                    var dx = p.X - meanX;
                    var dy = p.Y - meanY;
                    sxx += dx * dx;
                    syy += dy * dy;
                    sxy += dx * dy;
                }

                var theta = 0.5 * Math.Atan2(2 * sxy, (sxx - syy));
                var dir = new Point2d(Math.Cos(theta), Math.Sin(theta));

                var minT = double.PositiveInfinity;
                var maxT = double.NegativeInfinity;
                foreach (var p in points)
                {
                    var t = (p.X - meanX) * dir.X + (p.Y - meanY) * dir.Y;
                    if (t < minT) minT = t;
                    if (t > maxT) maxT = t;
                }

                if (!double.IsFinite(minT) || !double.IsFinite(maxT))
                {
                    var avg0 = strengths.Average();
                    return new CaliperResult(def.Name, Found: false, points, default, default, avg0);
                }

                var p1 = new Point2d(meanX + minT * dir.X, meanY + minT * dir.Y);
                var p2 = new Point2d(meanX + maxT * dir.X, meanY + maxT * dir.Y);
                var avg = strengths.Average();
                return new CaliperResult(def.Name, Found: true, points, p1, p2, avg);
            }

            // 0. Execute BeforeFlow DB Nodes (Read/Write before flow)
            ExecuteDbNodes(config, result, effectiveDbManager, DbExecutionTiming.BeforeFlow);

            // Origin
            var tOrigin0 = swTotal.ElapsedMilliseconds;
            // Origin template (origin.png) is saved from Image 1 (Global Preprocess only).
            // At runtime, both runtime image and template must go through the SAME full pipeline:
            //   Runtime image: Raw → Global Preprocess → Local Preprocess (node) = Image 2
            //   Template:      Image 1 (origin.png) → PreprocessTemplateForMatch(originPre) = Image 2
            // This ensures symmetric comparison in the same "space".
            var (originMat, originPre) = ResolveToolPreprocess("Origin", config.Origin.Name);
            var originTempl = GetTemplateGray(config.Origin.TemplateImageFile);

            var originDefBase = config.Origin;
            var originDef = originDefBase;
            var usedGuidedOrigin = false;
            if (track.LastOriginPos is not null)
            {
                var guide = ClampRoiToImage(WindowRoi(track.LastOriginPos.Value, guidedRadiusPx), originMat);
                var shrunk = IntersectRoi(originDefBase.SearchRoi, guide);
                if (shrunk.Width > 0 && shrunk.Height > 0)
                {
                    usedGuidedOrigin = true;
                    originDef = new PointDefinition
                    {
                        Name = originDefBase.Name,
                        MatchScoreThreshold = originDefBase.MatchScoreThreshold,
                        TemplateImageFile = originDefBase.TemplateImageFile,
                        TemplateRoi = originDefBase.TemplateRoi,
                        SearchRoi = shrunk,
                        WorldPosition = originDefBase.WorldPosition,
                        OffsetPx = originDefBase.OffsetPx,
                        Algorithm = originDefBase.Algorithm,
                        OriginAlgorithm = originDefBase.OriginAlgorithm,
                        MinAngle = originDefBase.MinAngle,
                        MaxAngle = originDefBase.MaxAngle,
                        AngleStep = originDefBase.AngleStep,
                        EdgePoint = originDefBase.EdgePoint,
                        ShapeModel = originDefBase.ShapeModel,
                        EdgeThresholdMin = originDefBase.EdgeThresholdMin,
                        EdgeThresholdMax = originDefBase.EdgeThresholdMax
                    };
                }
            }

            var stepDeg = originDef.AngleStep > 0 ? originDef.AngleStep : 1.0;
            var originMatch = _matcher.MatchWithRotation(originMat, originDef, originTempl, originPre, originDef.MinAngle, originDef.MaxAngle, stepDeg);
            if (usedGuidedOrigin && originMatch.Score < originDefBase.MatchScoreThreshold)
            {
                var stepDegBase = originDefBase.AngleStep > 0 ? originDefBase.AngleStep : 1.0;
                var retry = _matcher.MatchWithRotation(originMat, originDefBase, originTempl, originPre, originDefBase.MinAngle, originDefBase.MaxAngle, stepDegBase);

                if (retry.Score > originMatch.Score)
                {
                    originMatch = retry;
                }
            }
            var templateAngleDeg = originMatch.AngleDeg;
            var poseAngleDeg = templateAngleDeg - config.Origin.TemplateRoi.Angle;
            var originPass = originMatch.Score >= config.Origin.MatchScoreThreshold;
            result.Origin = new PointMatchResult(
                config.Origin.Name,
                originMatch.Position,
                originMatch.MatchRect,
                originMatch.Score,
                config.Origin.MatchScoreThreshold,
                originPass,
                poseAngleDeg,
                originMatch.FeaturePoints);
            
            System.Diagnostics.Debug.WriteLine($"[ORIGIN INSPECT] Tool='{config.Origin.Name ?? "Origin"}', Pass={originPass}, Score={originMatch.Score:F4} (Thr={config.Origin.MatchScoreThreshold:F4}) | Pos_px=({originMatch.Position.X:F2}, {originMatch.Position.Y:F2}), PoseAngle={poseAngleDeg:F2}°, MatchAngle={templateAngleDeg:F2}°");
            Console.WriteLine($"[ORIGIN INSPECT] Tool='{config.Origin.Name ?? "Origin"}', Pass={originPass}, Score={originMatch.Score:F4} (Thr={config.Origin.MatchScoreThreshold:F4}) | Pos_px=({originMatch.Position.X:F2}, {originMatch.Position.Y:F2}), PoseAngle={poseAngleDeg:F2}°, MatchAngle={templateAngleDeg:F2}°");
            if (config.PixelsPerMm > 0 && Math.Abs(config.PixelsPerMm - 1.0) > 1e-6)
            {
                double pxMm = config.PixelsPerMm;
                System.Diagnostics.Debug.WriteLine($"[ORIGIN INSPECT] Calibrated Pos_mm=({originMatch.Position.X / pxMm:F3}, {originMatch.Position.Y / pxMm:F3}) (Scale={pxMm:F4} px/mm)");
                Console.WriteLine($"[ORIGIN INSPECT] Calibrated Pos_mm=({originMatch.Position.X / pxMm:F3}, {originMatch.Position.Y / pxMm:F3}) (Scale={pxMm:F4} px/mm)");
            }

            result.Timings.OriginMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tOrigin0);
            result.Timings.NodeTimings[config.Origin.Name ?? "Origin"] = result.Timings.OriginMs;

            var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
            var originFound = originMatch.Position;
            var angleDeg = poseAngleDeg;

            var tTools0 = swTotal.ElapsedMilliseconds;
            var pointTasks = (config.Points ?? new List<PointDefinition>())
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var defBase = TransformPointDefinition(p, originTeach, originFound, angleDeg);
                    var def = defBase;

                    var (matForPoint, preForPoint) = ResolveToolPreprocess("Point", p.Name);

                    // Guided ROI (B): prioritize last known point position; otherwise use expected center from transformed SearchRoi.
                    Point2d center;
                    if (track.LastPointPos.TryGetValue(p.Name, out var lastP))
                    {
                        center = lastP;
                    }
                    else
                    {
                        center = new Point2d(def.SearchRoi.X + def.SearchRoi.Width / 2.0, def.SearchRoi.Y + def.SearchRoi.Height / 2.0);
                    }

                    var guide = ClampRoiToImage(WindowRoi(center, guidedRadiusPx), matForPoint);
                    var shrunk = IntersectRoi(def.SearchRoi, guide);
                    if (shrunk.Width > 0 && shrunk.Height > 0)
                    {
                        def = new PointDefinition
                        {
                            Name = def.Name,
                            MatchScoreThreshold = def.MatchScoreThreshold,
                            TemplateImageFile = def.TemplateImageFile,
                            TemplateRoi = def.TemplateRoi,
                            SearchRoi = shrunk,
                            WorldPosition = def.WorldPosition,
                            OffsetPx = def.OffsetPx,
                            Algorithm = def.Algorithm,
                            OriginAlgorithm = def.OriginAlgorithm,
                            MinAngle = def.MinAngle,
                            MaxAngle = def.MaxAngle,
                            AngleStep = def.AngleStep,
                            EdgePoint = def.EdgePoint,

                            ShapeModel = def.ShapeModel,
                            EdgeThresholdMin = def.EdgeThresholdMin,
                            EdgeThresholdMax = def.EdgeThresholdMax
                        };
                    }

                    static (bool Found, Point2d Position, double Score, Rect MatchRect) FindPointByEdge(Mat matBgrOrGray, Roi roiTeach, EdgePointSettings ep, Point2d originTeach, Point2d originFound, double angleDeg)
                    {
                        if (matBgrOrGray is null || roiTeach.Width <= 0 || roiTeach.Height <= 0)
                        {
                            return (false, default, 0.0, default);
                        }

                        using var patch = ExtractStraightRoi(matBgrOrGray, roiTeach, originTeach, originFound, angleDeg, out var centerFound);
                        if (patch.Empty())
                        {
                            return (false, default, 0.0, default);
                        }

                        using var patchGrayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
                        Mat gray = patchGrayOwned ?? patch;

                        var rect = new Rect(0, 0, patch.Width, patch.Height);

                        var stripCount = Math.Clamp(ep.StripCount, 1, 200);
                        var stripWidth = Math.Clamp(ep.StripWidth, 1, Math.Max(1, Math.Min(rect.Width, rect.Height)));
                        var stripLength = Math.Clamp(ep.StripLength, 3, Math.Max(3, Math.Max(rect.Width, rect.Height)));

                        var sumX = 0.0;
                        var sumY = 0.0;
                        var sumG = 0.0;
                        var foundN = 0;

                        static double InterpPeak(double a, double b, double c)
                        {
                            var denom = (a - 2 * b + c);
                            if (Math.Abs(denom) < 1e-12) return 0.0;
                            return 0.5 * (a - c) / denom;
                        }

                        for (var i = 0; i < stripCount; i++)
                        {
                            if (ep.Orientation == CaliperOrientation.Vertical)
                            {
                                var xCenter = (i + 0.5) * rect.Width / stripCount;
                                var x0 = (int)Math.Round(xCenter - stripWidth / 2.0);
                                var y0 = (int)Math.Round((rect.Height - stripLength) / 2.0);
                                var sr = new Rect(x0, y0, stripWidth, stripLength)
                                    .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                                if (sr.Width <= 0 || sr.Height <= 2) continue;

                                using var s = new Mat(gray, sr);
                                using var prof = new Mat();
                                Cv2.Reduce(s, prof, dim: ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);

                                var n = prof.Rows;
                                if (n < 3) continue;

                                var bestIdx = -1;
                                var bestVal = 0.0;
                                for (var y = 1; y < n - 1; y++)
                                {
                                    var v0 = prof.Get<double>(y - 1, 0);
                                    var v1 = prof.Get<double>(y, 0);
                                    var v2 = prof.Get<double>(y + 1, 0);
                                    var g = (v2 - v0) * 0.5;
                                    if (ep.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                                    else if (ep.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                                    else { g = Math.Abs(g); }

                                    if (g > bestVal)
                                    {
                                        bestVal = g;
                                        bestIdx = y;
                                    }
                                }

                                if (bestIdx < 1 || bestIdx >= n - 1) continue;
                                if (bestVal < ep.MinEdgeStrength) continue;

                                var gL = Math.Abs(prof.Get<double>(bestIdx, 0) - prof.Get<double>(bestIdx - 1, 0));
                                var gC = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx - 1, 0)) * 0.5;
                                var gR = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx, 0));
                                var sub = InterpPeak(gL, gC, gR);

                                var ySub = bestIdx + sub;
                                var xLocal = sr.X + sr.Width / 2.0;
                                var yLocal = sr.Y + ySub;
                                var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);

                                sumX += ptGlobal.X;
                                sumY += ptGlobal.Y;
                                sumG += bestVal;
                                foundN++;
                            }
                            else
                            {
                                var yCenter = (i + 0.5) * rect.Height / stripCount;
                                var y0 = (int)Math.Round(yCenter - stripWidth / 2.0);
                                var x0 = (int)Math.Round((rect.Width - stripLength) / 2.0);
                                var sr = new Rect(x0, y0, stripLength, stripWidth)
                                    .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                                if (sr.Width <= 2 || sr.Height <= 0) continue;

                                using var s = new Mat(gray, sr);
                                using var prof = new Mat();
                                Cv2.Reduce(s, prof, dim: ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

                                var n = prof.Cols;
                                if (n < 3) continue;

                                var bestIdx = -1;
                                var bestVal = 0.0;
                                for (var x = 1; x < n - 1; x++)
                                {
                                    var v0 = prof.Get<double>(0, x - 1);
                                    var v1 = prof.Get<double>(0, x);
                                    var v2 = prof.Get<double>(0, x + 1);
                                    var g = (v2 - v0) * 0.5;
                                    if (ep.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                                    else if (ep.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                                    else { g = Math.Abs(g); }

                                    if (g > bestVal)
                                    {
                                        bestVal = g;
                                        bestIdx = x;
                                    }
                                }

                                if (bestIdx < 1 || bestIdx >= n - 1) continue;
                                if (bestVal < ep.MinEdgeStrength) continue;

                                var gL = Math.Abs(prof.Get<double>(0, bestIdx) - prof.Get<double>(0, bestIdx - 1));
                                var gC = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx - 1)) * 0.5;
                                var gR = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx));
                                var sub = InterpPeak(gL, gC, gR);

                                var xSub = bestIdx + sub;
                                var xLocal = sr.X + xSub;
                                var yLocal = sr.Y + sr.Height / 2.0;
                                var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);

                                sumX += ptGlobal.X;
                                sumY += ptGlobal.Y;
                                sumG += bestVal;
                                foundN++;
                            }
                        }

                        if (foundN <= 0)
                        {
                            return (false, default, foundN == 0 ? 0.0 : sumG / foundN, default);
                        }

                        var pos = centerFound;
                        var score = sumG / foundN;
                        var matchRect = new Rect(
                            (int)Math.Round(centerFound.X - roiTeach.Width / 2.0),
                            (int)Math.Round(centerFound.Y - roiTeach.Height / 2.0),
                            roiTeach.Width,
                            roiTeach.Height);
                        return (true, pos, score, matchRect);
                    }

                    Point2d basePos;
                    Rect matchRect;
                    double score;
                    double thr;
                    bool pass;
                    List<Point2d>? featurePoints = null;

                    double detectedAngleDeg = templateAngleDeg;

                    if (p.Algorithm == PointFindAlgorithm.EdgePoint)
                    {
                        var r = FindPointByEdge(matForPoint, p.TemplateRoi, p.EdgePoint, originTeach, originFound, angleDeg);
                        basePos = r.Position;
                        matchRect = r.MatchRect;
                        score = r.Score;
                        thr = p.EdgePoint.MinEdgeStrength;
                        pass = r.Found;
                    }
                    else
                    {
                        var templ = GetTemplateGray(def.TemplateImageFile);
                        MatchResult m;
                        if (p.Algorithm == PointFindAlgorithm.MvpShapeMatch
                            || p.Algorithm == PointFindAlgorithm.MvpShapePyramid
                            || p.Algorithm == PointFindAlgorithm.ShapePyramid
                            || p.Algorithm == PointFindAlgorithm.ShapeBased)
                        {
                            var minA = p.MinAngle != 0 ? p.MinAngle : -15.0;
                            var maxA = p.MaxAngle != 0 ? p.MaxAngle : 15.0;
                            var stepA = p.AngleStep > 0 ? p.AngleStep : 1.0;
                            m = _matcher.MatchWithRotation(matForPoint, def, templ, preForPoint, minA, maxA, stepA);
                        }
                        else
                        {
                            m = _matcher.MatchWithFixedRotation(matForPoint, def, templ, templateAngleDeg, preForPoint);
                            if (!ReferenceEquals(def, defBase) && m.Score < defBase.MatchScoreThreshold)
                            {
                                var templ2 = GetTemplateGray(defBase.TemplateImageFile);
                                var retry = _matcher.MatchWithFixedRotation(matForPoint, defBase, templ2, templateAngleDeg, preForPoint);
                                if (retry.Score > m.Score)
                                {
                                    m = retry;
                                }
                            }
                        }

                        basePos = m.Position;
                        matchRect = m.MatchRect;
                        score = m.Score;
                        thr = p.MatchScoreThreshold;
                        pass = score >= thr;
                        featurePoints = m.FeaturePoints;
                        if (m.AngleDeg != 0) detectedAngleDeg = m.AngleDeg;
                    }

                    var off = new Point2d(p.OffsetPx.X, p.OffsetPx.Y);
                    var offRot = Rotate(off, new Point2d(0, 0), detectedAngleDeg);
                    var pos = new Point2d(basePos.X + offRot.X, basePos.Y + offRot.Y);
                    __sw.Stop();
                    result.Timings.NodeTimings[p.Name] = (int)__sw.ElapsedMilliseconds;
                    return new PointMatchResult(p.Name, pos, matchRect, score, thr, pass, detectedAngleDeg, featurePoints);
                }))
                .ToArray();

            var tPointsQueued = swTotal.ElapsedMilliseconds;

            var lineTasks = (config.Lines ?? new List<LineToolDefinition>())
                .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Name) && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                .Select(l => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(l.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForLine, _) = ResolveToolPreprocess("Line", l.Name);
                    var det = _lineDetector.DetectLongestLine(matForLine, roi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
                    __sw.Stop(); result.Timings.NodeTimings[l.Name] = (int)__sw.ElapsedMilliseconds; return det with { Name = l.Name };
                }))
                .ToArray();

            var tLinesQueued = swTotal.ElapsedMilliseconds;

            var blobTasks = (config.BlobDetections ?? new List<BlobDetectionDefinition>())
                .Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Name) && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                .Select(b => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var (matForBlob, _) = ResolveToolPreprocess("BlobDetection", b.Name);
                    using var crop = ExtractStraightRoi(matForBlob, b.InspectRoi, originTeach, originFound, angleDeg, out var centerFound);
                    var totalAngle = angleDeg + b.InspectRoi.Angle;
                    var blobs = DetectBlobsInCrop(crop, b.InspectRoi, b.Rois, b.Polarity, b.Threshold, b.MinBlobArea, b.MaxBlobArea, centerFound, totalAngle);
                    __sw.Stop();
                    result.Timings.NodeTimings[b.Name] = (int)__sw.ElapsedMilliseconds;
                    return new BlobDetectionResult(b.Name, blobs.Count, blobs);
                }))
                .ToArray();

            var tBlobsQueued = swTotal.ElapsedMilliseconds;

                var surfaceCompareTasks = (config.SurfaceCompares ?? new List<SurfaceCompareDefinition>())
                    .Where(sc => sc is not null && !string.IsNullOrWhiteSpace(sc.Name) && sc.InspectRoi.Width > 0 && sc.InspectRoi.Height > 0)
                    .Select(sc => Task.Run(() =>
                    {
                        var __sw = System.Diagnostics.Stopwatch.StartNew();
                        var (_, scSettings) = ResolveToolPreprocess("SurfaceCompare", sc.Name);
                        var res = RunSurfaceCompare(image, originTeach, originFound, angleDeg, sc, _preprocessor, scSettings);
                        __sw.Stop(); result.Timings.NodeTimings[sc.Name] = (int)__sw.ElapsedMilliseconds; return res;
                    }))
                    .ToArray();

                var contourCompareTasks = (config.ContourCompares ?? new List<ContourCompareDefinition>())
                    .Where(cc => cc is not null && !string.IsNullOrWhiteSpace(cc.Name) && cc.InspectRoi.Width > 0 && cc.InspectRoi.Height > 0)
                    .Select(cc => Task.Run(() =>
                    {
                        var __sw = System.Diagnostics.Stopwatch.StartNew();
                        var (_, ccSettings) = ResolveToolPreprocess("ContourCompare", cc.Name);
                        var res = RunContourCompare(image, originTeach, originFound, angleDeg, cc, _preprocessor, ccSettings);
                        __sw.Stop(); result.Timings.NodeTimings[cc.Name] = (int)__sw.ElapsedMilliseconds; return res;
                    }))
                    .ToArray();

            var tScQueued = swTotal.ElapsedMilliseconds;

            var lpdTasks = (config.LinePairDetections ?? new List<LinePairDetectionDefinition>())
                .Where(lpd => lpd is not null && !string.IsNullOrWhiteSpace(lpd.Name) && lpd.SearchRoi.Width > 0 && lpd.SearchRoi.Height > 0)
                .Select(lpd => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(lpd.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForLpd, _) = ResolveToolPreprocess("LinePairDetection", lpd.Name);
                    var top = _lineDetector.DetectTopLines(matForLpd, roi, lpd.Canny1, lpd.Canny2, lpd.HoughThreshold, lpd.MinLineLength, lpd.MaxLineGap, topN: 2);
                    if (top.Count < 2)
                    {
                        __sw.Stop(); result.Timings.NodeTimings[lpd.Name] = (int)__sw.ElapsedMilliseconds; return new LinePairDetectionResult(
                            lpd.Name,
                            Found: false,
                            default, default, default, default,
                            double.NaN,
                            lpd.Nominal,
                            lpd.TolerancePlus,
                            lpd.ToleranceMinus,
                            Pass: false,
                            default,
                            default);
                    }

                    var l1 = top[0];
                    var l2 = top[1];
                    var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(l1.P1, l1.P2, l2.P1, l2.P2);
                    var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                    var pass = value >= (lpd.Nominal - lpd.ToleranceMinus) && value <= (lpd.Nominal + lpd.TolerancePlus);

                    __sw.Stop(); result.Timings.NodeTimings[lpd.Name] = (int)__sw.ElapsedMilliseconds; return new LinePairDetectionResult(
                        lpd.Name,
                        Found: true,
                        l1.P1, l1.P2,
                        l2.P1, l2.P2,
                        value,
                        lpd.Nominal,
                        lpd.TolerancePlus,
                        lpd.ToleranceMinus,
                        pass,
                        ca,
                        cb);
                }))
                .ToArray();

            var tLpdQueued = swTotal.ElapsedMilliseconds;

            var caliperTasks = (config.Calipers ?? new List<CaliperDefinition>())
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name) && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                .Select(c => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(c.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForCal, _) = ResolveToolPreprocess("Caliper", c.Name);
                    var res = DetectCaliper(matForCal, c.SearchRoi, c, originTeach, originFound, angleDeg);
                    __sw.Stop(); result.Timings.NodeTimings[c.Name] = (int)__sw.ElapsedMilliseconds; return res;
                }))
                .ToArray();

            var tCalQueued = swTotal.ElapsedMilliseconds;

            Task.WaitAll(pointTasks);
            var tPointsDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(lineTasks);
            var tLinesDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(blobTasks);
            var tBlobsDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(surfaceCompareTasks);
            Task.WaitAll(contourCompareTasks);
            var tScDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(lpdTasks);
            var tLpdDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(caliperTasks);
            var tCalDone = swTotal.ElapsedMilliseconds;

            var tEpdQueued = swTotal.ElapsedMilliseconds;

            static EdgePairDetectResult DetectEdgePair(Mat matBgrOrGray, Roi roiTeach, EdgePairDetectDefinition def, double pixelsPerMm, Point2d originTeach, Point2d originFound, double angleDeg)
            {
                if (matBgrOrGray is null || roiTeach.Width <= 0 || roiTeach.Height <= 0)
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default,
                        new List<CaliperEdgePoint>(), new List<CaliperEdgePoint>());
                }

                using var patch = ExtractStraightRoi(matBgrOrGray, roiTeach, originTeach, originFound, angleDeg, out var centerFound);
                if (patch.Empty())
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default,
                        new List<CaliperEdgePoint>(), new List<CaliperEdgePoint>());
                }

                using var patchGrayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
                Mat gray = patchGrayOwned ?? patch;

                var rect = new Rect(0, 0, patch.Width, patch.Height);

                var stripCount = Math.Clamp(def.StripCount, 1, 200);
                var stripWidth = Math.Clamp(def.StripWidth, 1, Math.Max(1, Math.Min(rect.Width, rect.Height)));
                var stripLength = Math.Clamp(def.StripLength, 3, Math.Max(3, Math.Max(rect.Width, rect.Height)));
                var minSep = Math.Clamp(def.MinEdgeSeparationPx, 1, Math.Max(1, Math.Max(rect.Width, rect.Height)));

                var e1 = new List<CaliperEdgePoint>(stripCount);
                var e2 = new List<CaliperEdgePoint>(stripCount);

                static double InterpPeak(double a, double b, double c)
                {
                    var denom = (a - 2 * b + c);
                    if (Math.Abs(denom) < 1e-12) return 0.0;
                    return 0.5 * (a - c) / denom;
                }

                for (var i = 0; i < stripCount; i++)
                {
                    if (def.Orientation == CaliperOrientation.Vertical)
                    {
                        var xCenter = (i + 0.5) * rect.Width / stripCount;
                        var x0 = (int)Math.Round(xCenter - stripWidth / 2.0);
                        var y0 = (int)Math.Round((rect.Height - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripWidth, stripLength)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 0 || sr.Height <= 2) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);
                        var n = prof.Rows;
                        if (n < 3) continue;

                        double Sm(int y)
                        {
                            var y0 = Math.Max(0, y - 1);
                            var y1 = Math.Max(0, Math.Min(n - 1, y));
                            var y2 = Math.Min(n - 1, y + 1);
                            return (prof.Get<double>(y0, 0) + prof.Get<double>(y1, 0) + prof.Get<double>(y2, 0)) / 3.0;
                        }

                        var candidates = new List<(int idx, double g)>(n);
                        var maxG = 0.0;
                        for (var y = 1; y < n - 1; y++)
                        {
                            var v0 = Sm(y - 1);
                            var v2 = Sm(y + 1);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > maxG) maxG = g;
                            candidates.Add((y, g));
                        }

                        if (candidates.Count < 2) continue;

                        var effMin = Math.Max(0.0, Math.Min(def.MinEdgeStrength, maxG * 0.5));
                        candidates.Sort((a, b) => b.g.CompareTo(a.g));
                        if (candidates.Count > 40) candidates.RemoveRange(40, candidates.Count - 40);

                        var bestA = (-1, 0.0);
                        var bestB = (-1, 0.0);
                        var bestScore = double.NegativeInfinity;
                        for (var a = 0; a < candidates.Count; a++)
                        {
                            for (var b = a + 1; b < candidates.Count; b++)
                            {
                                var candA = candidates[a];
                                var candB = candidates[b];
                                if (Math.Abs(candA.idx - candB.idx) < minSep) continue;
                                var score = candA.g + candB.g;
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestA = candA;
                                    bestB = candB;
                                }
                            }
                        }

                        if (bestA.Item1 < 1 || bestB.Item1 < 1) continue;
                        if (bestA.Item2 < effMin || bestB.Item2 < effMin) continue;

                        var idxA = bestA.Item1;
                        var idxB = bestB.Item1;
                        var valA = bestA.Item2;
                        var valB = bestB.Item2;
                        if (idxA > idxB)
                        {
                            (idxA, idxB) = (idxB, idxA);
                            (valA, valB) = (valB, valA);
                        }

                        double SubAt(int idx)
                        {
                            var gL = Math.Abs(prof.Get<double>(idx, 0) - prof.Get<double>(idx - 1, 0));
                            var gC = Math.Abs(prof.Get<double>(idx + 1, 0) - prof.Get<double>(idx - 1, 0)) * 0.5;
                            var gR = Math.Abs(prof.Get<double>(idx + 1, 0) - prof.Get<double>(idx, 0));
                            return InterpPeak(gL, gC, gR);
                        }

                        var ySubA = idxA + SubAt(idxA);
                        var ySubB = idxB + SubAt(idxB);
                        var xLocal = rect.X + sr.X + sr.Width / 2.0;
                        var ptA = MapToGlobal(new Point2d(xLocal, rect.Y + sr.Y + ySubA), patch.Width, patch.Height, centerFound, angleDeg);
                        var ptB = MapToGlobal(new Point2d(xLocal, rect.Y + sr.Y + ySubB), patch.Width, patch.Height, centerFound, angleDeg);
                        e1.Add(new CaliperEdgePoint(ptA.X, ptA.Y, valA));
                        e2.Add(new CaliperEdgePoint(ptB.X, ptB.Y, valB));
                    }
                    else
                    {
                        var yCenter = (i + 0.5) * rect.Height / stripCount;
                        var y0 = (int)Math.Round(yCenter - stripWidth / 2.0);
                        var x0 = (int)Math.Round((rect.Width - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripLength, stripWidth)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 2 || sr.Height <= 0) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);
                        var n = prof.Cols;
                        if (n < 3) continue;

                        double Sm(int x)
                        {
                            var x0 = Math.Max(0, x - 1);
                            var x1 = Math.Max(0, Math.Min(n - 1, x));
                            var x2 = Math.Min(n - 1, x + 1);
                            return (prof.Get<double>(0, x0) + prof.Get<double>(0, x1) + prof.Get<double>(0, x2)) / 3.0;
                        }

                        var candidates = new List<(int idx, double g)>(n);
                        var maxG = 0.0;
                        for (var x = 1; x < n - 1; x++)
                        {
                            var v0 = Sm(x - 1);
                            var v2 = Sm(x + 1);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > maxG) maxG = g;
                            candidates.Add((x, g));
                        }

                        if (candidates.Count < 2) continue;

                        var effMin = Math.Max(0.0, Math.Min(def.MinEdgeStrength, maxG * 0.5));
                        candidates.Sort((a, b) => b.g.CompareTo(a.g));
                        if (candidates.Count > 40) candidates.RemoveRange(40, candidates.Count - 40);

                        var bestA = (-1, 0.0);
                        var bestB = (-1, 0.0);
                        var bestScore = double.NegativeInfinity;
                        for (var a = 0; a < candidates.Count; a++)
                        {
                            for (var b = a + 1; b < candidates.Count; b++)
                            {
                                var candA = candidates[a];
                                var candB = candidates[b];
                                if (Math.Abs(candA.idx - candB.idx) < minSep) continue;
                                var score = candA.g + candB.g;
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestA = candA;
                                    bestB = candB;
                                }
                            }
                        }

                        if (bestA.Item1 < 1 || bestB.Item1 < 1) continue;
                        if (bestA.Item2 < effMin || bestB.Item2 < effMin) continue;

                        var idxA = bestA.Item1;
                        var idxB = bestB.Item1;
                        var valA = bestA.Item2;
                        var valB = bestB.Item2;
                        if (idxA > idxB)
                        {
                            (idxA, idxB) = (idxB, idxA);
                            (valA, valB) = (valB, valA);
                        }

                        double SubAt(int idx)
                        {
                            var gL = Math.Abs(prof.Get<double>(0, idx) - prof.Get<double>(0, idx - 1));
                            var gC = Math.Abs(prof.Get<double>(0, idx + 1) - prof.Get<double>(0, idx - 1)) * 0.5;
                            var gR = Math.Abs(prof.Get<double>(0, idx + 1) - prof.Get<double>(0, idx));
                            return InterpPeak(gL, gC, gR);
                        }

                        var xSubA = idxA + SubAt(idxA);
                        var xSubB = idxB + SubAt(idxB);
                        var yLocal = rect.Y + sr.Y + sr.Height / 2.0;
                        var ptA = MapToGlobal(new Point2d(rect.X + sr.X + xSubA, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        var ptB = MapToGlobal(new Point2d(rect.X + sr.X + xSubB, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        e1.Add(new CaliperEdgePoint(ptA.X, ptA.Y, valA));
                        e2.Add(new CaliperEdgePoint(ptB.X, ptB.Y, valB));
                    }
                }

                if (e1.Count < 2 || e2.Count < 2)
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default, e1, e2);
                }

                static (Point2d p1, Point2d p2) FitLineFromPoints(List<CaliperEdgePoint> pts)
                {
                    var meanX = pts.Average(p => p.X);
                    var meanY = pts.Average(p => p.Y);
                    var sxx = 0.0;
                    var syy = 0.0;
                    var sxy = 0.0;
                    foreach (var p in pts)
                    {
                        var dx = p.X - meanX;
                        var dy = p.Y - meanY;
                        sxx += dx * dx;
                        syy += dy * dy;
                        sxy += dx * dy;
                    }
                    var theta = 0.5 * Math.Atan2(2 * sxy, (sxx - syy));
                    var dir = new Point2d(Math.Cos(theta), Math.Sin(theta));
                    var minT = double.PositiveInfinity;
                    var maxT = double.NegativeInfinity;
                    foreach (var p in pts)
                    {
                        var t = (p.X - meanX) * dir.X + (p.Y - meanY) * dir.Y;
                        if (t < minT) minT = t;
                        if (t > maxT) maxT = t;
                    }
                    return (new Point2d(meanX + minT * dir.X, meanY + minT * dir.Y), new Point2d(meanX + maxT * dir.X, meanY + maxT * dir.Y));
                }

                var (l1p1, l1p2) = FitLineFromPoints(e1);
                var (l2p1, l2p2) = FitLineFromPoints(e2);
                var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(l1p1, l1p2, l2p1, l2p2);
                var value = pixelsPerMm > 0 ? distPx / pixelsPerMm : distPx;
                var pass = value >= (def.Nominal - def.ToleranceMinus) && value <= (def.Nominal + def.TolerancePlus);

                return new EdgePairDetectResult(def.Name, Found: true, l1p1, l1p2, l2p1, l2p2, value, def.Nominal, def.TolerancePlus, def.ToleranceMinus, pass, ca, cb, e1, e2);
            }

            var epdTasks = (config.EdgePairDetections ?? new List<EdgePairDetectDefinition>())
                .Where(epd => epd is not null && !string.IsNullOrWhiteSpace(epd.Name) && epd.SearchRoi.Width > 0 && epd.SearchRoi.Height > 0)
                .Select(epd => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var (matForEpd, _) = ResolveToolPreprocess("EdgePairDetect", epd.Name);
                    var res = DetectEdgePair(matForEpd, epd.SearchRoi, epd, config.PixelsPerMm, originTeach, originFound, angleDeg);
                    __sw.Stop(); result.Timings.NodeTimings[epd.Name] = (int)__sw.ElapsedMilliseconds; return res;
                }))
                .ToArray();

            static CircleFinderResult DetectCircle(Mat matBgrOrGray, Roi roi, CircleFinderDefinition def)
            {
                var name = def.Name ?? string.Empty;
                if (matBgrOrGray is null || matBgrOrGray.Empty() || roi.Width <= 0 || roi.Height <= 0)
                {
                    return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                }

                var imgW = matBgrOrGray.Width;
                var imgH = matBgrOrGray.Height;

                if (def.Algorithm == CircleFindAlgorithm.RadialCaliper)
                {
                    using var grayOwnedR = matBgrOrGray.Channels() == 1 ? null : matBgrOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
                    var grayMat = grayOwnedR ?? matBgrOrGray;

                    var centerEst = new Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);
                    var nominalR = Math.Min(roi.Width, roi.Height) / 2.0;
                    if (nominalR < 3.0) nominalR = 20.0;

                    var stripCount = Math.Clamp(def.StripCount > 0 ? def.StripCount : 32, 4, 360);
                    var stripLength = Math.Max(5, def.StripLength > 0 ? def.StripLength : 40);
                    var stripWidth = Math.Max(1, def.StripWidth > 0 ? def.StripWidth : 10);
                    var minEdgeStrength = Math.Max(1, def.MinEdgeStrength);

                    var startAngleRad = def.MinAngleDeg * Math.PI / 180.0;
                    var endAngleRad = def.MaxAngleDeg * Math.PI / 180.0;
                    if (Math.Abs(endAngleRad - startAngleRad) < 1e-4)
                    {
                        endAngleRad = startAngleRad + 2.0 * Math.PI;
                    }

                    var angleStep = (endAngleRad - startAngleRad) / stripCount;
                    var halfL = stripLength / 2.0;
                    var halfW = stripWidth / 2.0;

                    var detectedPoints = new List<Point2d>();

                    byte SampleBilinear(double x, double y)
                    {
                        var ix = (int)Math.Floor(x);
                        var iy = (int)Math.Floor(y);
                        if (ix < 0 || ix >= imgW - 1 || iy < 0 || iy >= imgH - 1)
                        {
                            if (ix >= 0 && ix < imgW && iy >= 0 && iy < imgH)
                                return grayMat.At<byte>(iy, ix);
                            return 0;
                        }

                        var fx = x - ix;
                        var fy = y - iy;
                        var v00 = grayMat.At<byte>(iy, ix);
                        var v10 = grayMat.At<byte>(iy, ix + 1);
                        var v01 = grayMat.At<byte>(iy + 1, ix);
                        var v11 = grayMat.At<byte>(iy + 1, ix + 1);

                        var top = v00 + fx * (v10 - v00);
                        var bottom = v01 + fx * (v11 - v01);
                        return (byte)Math.Clamp(top + fy * (bottom - top), 0, 255);
                    }

                    var roiAngleRad = roi.Angle * Math.PI / 180.0;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var angle = roiAngleRad + startAngleRad + (i + 0.5) * angleStep;
                        var ux = Math.Cos(angle);
                        var uy = Math.Sin(angle);
                        var vx = -uy;
                        var vy = ux;

                        var radialSamples = stripLength + 1;
                        var profile = new double[radialSamples];
                        var widthStepCount = Math.Max(1, stripWidth);

                        for (var rIdx = 0; rIdx < radialSamples; rIdx++)
                        {
                            var rOffset = -halfL + (rIdx * stripLength / Math.Max(1, radialSamples - 1));
                            var sampleCenterPt = new Point2d(
                                centerEst.X + (nominalR + rOffset) * ux,
                                centerEst.Y + (nominalR + rOffset) * uy);

                            double sumVal = 0;
                            for (var wIdx = 0; wIdx < widthStepCount; wIdx++)
                            {
                                var wOffset = -halfW + (wIdx * stripWidth / Math.Max(1, widthStepCount - 1));
                                var px = sampleCenterPt.X + wOffset * vx;
                                var py = sampleCenterPt.Y + wOffset * vy;
                                sumVal += SampleBilinear(px, py);
                            }
                            profile[rIdx] = sumVal / widthStepCount;
                        }

                        var deriv = new double[radialSamples];
                        for (var rIdx = 1; rIdx < radialSamples - 1; rIdx++)
                        {
                            deriv[rIdx] = (profile[rIdx + 1] - profile[rIdx - 1]) / 2.0;
                        }

                        int bestIdx = -1;
                        double bestVal = 0;

                        for (var rIdx = 1; rIdx < radialSamples - 1; rIdx++)
                        {
                            var dVal = deriv[rIdx];
                            bool isCandidate = def.Polarity switch
                            {
                                EdgePolarity.LightToDark => dVal < -minEdgeStrength,
                                EdgePolarity.DarkToLight => dVal > minEdgeStrength,
                                _ => Math.Abs(dVal) > minEdgeStrength
                            };

                            if (!isCandidate) continue;

                            if (def.EdgeSelection == EdgeSelection.First)
                            {
                                bestIdx = rIdx;
                                bestVal = dVal;
                                break;
                            }

                            if (def.EdgeSelection == EdgeSelection.Last)
                            {
                                bestIdx = rIdx;
                                bestVal = dVal;
                            }
                            else // MaxStrength
                            {
                                if (Math.Abs(dVal) > Math.Abs(bestVal))
                                {
                                    bestIdx = rIdx;
                                    bestVal = dVal;
                                }
                            }
                        }

                        if (bestIdx > 0 && bestIdx < radialSamples - 1)
                        {
                            var y1 = deriv[bestIdx - 1];
                            var y2 = deriv[bestIdx];
                            var y3 = deriv[bestIdx + 1];
                            var denom = (y1 - 2.0 * y2 + y3);
                            var subOffset = 0.0;
                            if (Math.Abs(denom) > 1e-6)
                            {
                                subOffset = (y1 - y3) / (2.0 * denom);
                                subOffset = Math.Clamp(subOffset, -0.9, 0.9);
                            }

                            var subRIdx = bestIdx + subOffset;
                            var finalR = -halfL + (subRIdx * stripLength / Math.Max(1, radialSamples - 1));
                            var edgePt = new Point2d(
                                centerEst.X + (nominalR + finalR) * ux,
                                centerEst.Y + (nominalR + finalR) * uy);
                            detectedPoints.Add(edgePt);
                        }
                    }

                    if (detectedPoints.Count < 3)
                    {
                        return new CircleFinderResult(name, Found: false, centerEst, nominalR, 0.0, detectedPoints, new List<bool>());
                    }

                    var rnd = new Random(name.GetHashCode());
                    var bestInlierCount = -1;
                    Point2d bestCenter = centerEst;
                    double bestRadius = nominalR;
                    var inlierTol = 3.0;

                    static bool TryCircleFrom3Points(Point2d a, Point2d b, Point2d c, out Point2d center, out double r)
                    {
                        center = default;
                        r = 0;
                        var ax = a.X; var ay = a.Y;
                        var bx = b.X; var by = b.Y;
                        var cx = c.X; var cy = c.Y;
                        var d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
                        if (Math.Abs(d) < 1e-9) return false;
                        var ax2ay2 = ax * ax + ay * ay;
                        var bx2by2 = bx * bx + by * by;
                        var cx2cy2 = cx * cx + cy * cy;
                        var ux = (ax2ay2 * (by - cy) + bx2by2 * (cy - ay) + cx2cy2 * (ay - by)) / d;
                        var uy = (ax2ay2 * (cx - bx) + bx2by2 * (ax - cx) + cx2cy2 * (bx - ax)) / d;
                        center = new Point2d(ux, uy);
                        r = Math.Sqrt((ux - ax) * (ux - ax) + (uy - ay) * (uy - ay));
                        return double.IsFinite(r) && r > 0;
                    }

                    for (var iter = 0; iter < 100; iter++)
                    {
                        var ia = rnd.Next(detectedPoints.Count);
                        var ib = rnd.Next(detectedPoints.Count);
                        var ic = rnd.Next(detectedPoints.Count);
                        if (ia == ib || ia == ic || ib == ic) continue;

                        if (!TryCircleFrom3Points(detectedPoints[ia], detectedPoints[ib], detectedPoints[ic], out var c0, out var r0))
                            continue;

                        if (def.MinRadiusPx > 0 && r0 < def.MinRadiusPx) continue;
                        if (def.MaxRadiusPx > 0 && r0 > def.MaxRadiusPx) continue;

                        var inlCount = 0;
                        for (var pIdx = 0; pIdx < detectedPoints.Count; pIdx++)
                        {
                            var p = detectedPoints[pIdx];
                            var distToCenter = Math.Sqrt((p.X - c0.X) * (p.X - c0.X) + (p.Y - c0.Y) * (p.Y - c0.Y));
                            if (Math.Abs(distToCenter - r0) <= inlierTol)
                            {
                                inlCount++;
                            }
                        }

                        if (inlCount > bestInlierCount)
                        {
                            bestInlierCount = inlCount;
                            bestCenter = c0;
                            bestRadius = r0;
                        }
                    }

                    var inliers = new List<Point2d>();
                    var inlierFlags = new List<bool>();
                    for (var pIdx = 0; pIdx < detectedPoints.Count; pIdx++)
                    {
                        var p = detectedPoints[pIdx];
                        var distToCenter = Math.Sqrt((p.X - bestCenter.X) * (p.X - bestCenter.X) + (p.Y - bestCenter.Y) * (p.Y - bestCenter.Y));
                        var isInlier = Math.Abs(distToCenter - bestRadius) <= inlierTol;
                        inlierFlags.Add(isInlier);
                        if (isInlier)
                        {
                            inliers.Add(p);
                        }
                    }

                    if (inliers.Count >= 3)
                    {
                        double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0, sumXY = 0;
                        double sumR = 0, sumXR = 0, sumYR = 0;
                        var M = inliers.Count;

                        for (var k = 0; k < M; k++)
                        {
                            var px = inliers[k].X;
                            var py = inliers[k].Y;
                            var r2 = px * px + py * py;
                            sumX += px;
                            sumY += py;
                            sumX2 += px * px;
                            sumY2 += py * py;
                            sumXY += px * py;
                            sumR += r2;
                            sumXR += px * r2;
                            sumYR += py * r2;
                        }

                        var A1 = M * sumX2 - sumX * sumX;
                        var B1 = M * sumXY - sumX * sumY;
                        var C1 = 0.5 * (M * sumXR - sumX * sumR);

                        var A2 = M * sumXY - sumX * sumY;
                        var B2 = M * sumY2 - sumY * sumY;
                        var C2 = 0.5 * (M * sumYR - sumY * sumR);

                        var det = A1 * B2 - A2 * B1;
                        if (Math.Abs(det) > 1e-6)
                        {
                            var cx = (C1 * B2 - C2 * B1) / det;
                            var cy = (A1 * C2 - A2 * C1) / det;
                            var meanR2 = (sumR - 2.0 * cx * sumX - 2.0 * cy * sumY) / M + cx * cx + cy * cy;
                            if (meanR2 > 0)
                            {
                                bestCenter = new Point2d(cx, cy);
                                bestRadius = Math.Sqrt(meanR2);
                            }
                        }
                    }

                    var minPassInliers = Math.Max(3, stripCount / 4);
                    var isFound = inliers.Count >= minPassInliers && bestRadius > 0;
                    if (def.MinRadiusPx > 0 && bestRadius < def.MinRadiusPx) isFound = false;
                    if (def.MaxRadiusPx > 0 && bestRadius > def.MaxRadiusPx) isFound = false;

                    var score = inliers.Count / (double)stripCount;

                    return new CircleFinderResult(name, isFound, bestCenter, bestRadius, score, detectedPoints, inlierFlags);
                }

                var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height)
                    .Intersect(new Rect(0, 0, matBgrOrGray.Width, matBgrOrGray.Height));
                if (rect.Width <= 2 || rect.Height <= 2)
                {
                    return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                }

                using var crop = new Mat(matBgrOrGray, rect);
                Mat gray = crop;
                using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (grayOwned is not null) gray = grayOwned;

                var minR = Math.Max(0, def.MinRadiusPx);
                var maxR = Math.Max(0, def.MaxRadiusPx);

                if (def.Algorithm == CircleFindAlgorithm.HoughCircles)
                {
                    using var blur = new Mat();
                    Cv2.GaussianBlur(gray, blur, new Size(0, 0), 1.2);
                    var dp = Math.Max(1.0, def.HoughDp);
                    var minDist = Math.Max(1.0, def.HoughMinDistPx);
                    var p1 = Math.Max(1.0, def.HoughParam1);
                    var p2 = Math.Max(1.0, def.HoughParam2);
                    var circles = Cv2.HoughCircles(blur, HoughModes.Gradient, dp, minDist, p1, p2, minR, maxR);
                    if (circles is null || circles.Length == 0)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    var best = circles.OrderByDescending(c => c.Radius).First();
                    var center = new Point2d(rect.X + best.Center.X, rect.Y + best.Center.Y);
                    return new CircleFinderResult(name, Found: true, center, best.Radius, Score: 1.0);
                }

                if (def.Algorithm == CircleFindAlgorithm.ContourFit)
                {
                    using var edges = new Mat();
                    Cv2.Canny(gray, edges, def.Canny1, def.Canny2);
                    Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                    var bestScore = double.NegativeInfinity;
                    var bestCenter = new Point2d();
                    var bestR = 0.0;

                    foreach (var cnt in contours)
                    {
                        if (cnt is null || cnt.Length < 20) continue;
                        var area = Math.Abs(Cv2.ContourArea(cnt));
                        if (area <= 1.0) continue;

                        var peri = Cv2.ArcLength(cnt, closed: true);
                        if (peri <= 1e-9) continue;
                        var circ = 4.0 * Math.PI * area / (peri * peri);
                        if (circ < def.MinCircularity) continue;

                        Cv2.MinEnclosingCircle(cnt, out var c, out var r);
                        if (minR > 0 && r < minR) continue;
                        if (maxR > 0 && r > maxR) continue;

                        var score = circ * Math.Sqrt(area);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCenter = new Point2d(rect.X + c.X, rect.Y + c.Y);
                            bestR = r;
                        }
                    }

                    if (bestScore <= double.NegativeInfinity)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    return new CircleFinderResult(name, Found: true, bestCenter, bestR, Score: bestScore);
                }

                // RANSAC (simple): reuse contour edges as points (if any). If none, fail.
                {
                    using var edges = new Mat();
                    Cv2.Canny(gray, edges, def.Canny1, def.Canny2);
                    var pts = new List<Point2f>();
                    for (var y = 0; y < edges.Rows; y++)
                    {
                        for (var x = 0; x < edges.Cols; x++)
                        {
                            if (edges.Get<byte>(y, x) != 0) pts.Add(new Point2f(x, y));
                        }
                    }

                    if (pts.Count < 50)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    var rnd = new Random(def.Name?.GetHashCode() ?? 0);
                    var bestInliers = -1;
                    var bestCenter = new Point2d();
                    var bestR = 0.0;
                    var thresh = 2.5;
                    var iters = 80;

                    static bool TryCircleFrom3(Point2f a, Point2f b, Point2f c, out Point2d center, out double r)
                    {
                        center = default;
                        r = 0;
                        var ax = a.X; var ay = a.Y;
                        var bx = b.X; var by = b.Y;
                        var cx = c.X; var cy = c.Y;
                        var d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
                        if (Math.Abs(d) < 1e-9) return false;
                        var ax2ay2 = ax * ax + ay * ay;
                        var bx2by2 = bx * bx + by * by;
                        var cx2cy2 = cx * cx + cy * cy;
                        var ux = (ax2ay2 * (by - cy) + bx2by2 * (cy - ay) + cx2cy2 * (ay - by)) / d;
                        var uy = (ax2ay2 * (cx - bx) + bx2by2 * (ax - cx) + cx2cy2 * (bx - ax)) / d;
                        center = new Point2d(ux, uy);
                        r = Math.Sqrt((ux - ax) * (ux - ax) + (uy - ay) * (uy - ay));
                        return double.IsFinite(r) && r > 0;
                    }

                    for (var k = 0; k < iters; k++)
                    {
                        var ia = rnd.Next(pts.Count);
                        var ib = rnd.Next(pts.Count);
                        var ic = rnd.Next(pts.Count);
                        if (ia == ib || ia == ic || ib == ic) continue;

                        if (!TryCircleFrom3(pts[ia], pts[ib], pts[ic], out var c0, out var r0)) continue;
                        if (minR > 0 && r0 < minR) continue;
                        if (maxR > 0 && r0 > maxR) continue;

                        var inl = 0;
                        for (var i = 0; i < pts.Count; i += 2)
                        {
                            var p = pts[i];
                            var dx = p.X - c0.X;
                            var dy = p.Y - c0.Y;
                            var d0 = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - r0);
                            if (d0 <= thresh) inl++;
                        }

                        if (inl > bestInliers)
                        {
                            bestInliers = inl;
                            bestCenter = c0;
                            bestR = r0;
                        }
                    }

                    if (bestInliers < 0)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    var center = new Point2d(rect.X + bestCenter.X, rect.Y + bestCenter.Y);
                    return new CircleFinderResult(name, Found: true, center, bestR, Score: bestInliers);
                }
            }

            var circleTasks = (config.CircleFinders ?? new List<CircleFinderDefinition>())
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name) && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                .Select(c => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(c.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForCircle, _) = ResolveToolPreprocess("CircleFinder", c.Name);
                    var res = DetectCircle(matForCircle, roi, c);
                    __sw.Stop(); result.Timings.NodeTimings[c.Name] = (int)__sw.ElapsedMilliseconds; return res;
                }))
                .ToArray();

            var tEpdDone = swTotal.ElapsedMilliseconds;

            result.Timings.PointsMs = (int)Math.Max(0, tPointsDone - tPointsQueued);
            result.Timings.LinesMs = (int)Math.Max(0, tLinesDone - tLinesQueued);
            result.Timings.BlobsMs = (int)Math.Max(0, tBlobsDone - tBlobsQueued);
            result.Timings.SurfaceCompareMs = (int)Math.Max(0, tScDone - tScQueued);
            result.Timings.LpdMs = (int)Math.Max(0, tLpdDone - tLpdQueued);
            result.Timings.CalipersMs = (int)Math.Max(0, tCalDone - tCalQueued);

            var foundPoints = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in pointTasks)
            {
                var pr = t.Result;
                result.Points.Add(pr);
                foundPoints[pr.Name] = pr.Position;
            }

            var foundLines = new Dictionary<string, LineDetectResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in lineTasks)
            {
                var lr = t.Result;
                result.Lines.Add(lr);
                foundLines[lr.Name] = lr;
            }

            foreach (var t in blobTasks)
            {
                result.BlobDetections.Add(t.Result);
            }

            foreach (var t in surfaceCompareTasks)
            {
                result.SurfaceCompares.Add(t.Result);
            }

            foreach (var t in contourCompareTasks)
            {
                result.ContourCompares.Add(t.Result);
            }

            foreach (var t in lpdTasks)
            {
                result.LinePairDetections.Add(t.Result);
            }

            foreach (var t in caliperTasks)
            {
                result.Calipers.Add(t.Result);
            }

            Task.WaitAll(epdTasks);
            foreach (var t in epdTasks)
            {
                result.EdgePairDetections.Add(t.Result);
            }

            Task.WaitAll(circleTasks);
            foreach (var t in circleTasks)
            {
                result.CircleFinders.Add(t.Result);
            }

            result.Timings.EdgePairDetectMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tEpdQueued);

            foreach (var cal in result.Calipers)
            {
                if (!cal.Found)
                {
                    continue;
                }

                var dx = cal.LineP2.X - cal.LineP1.X;
                var dy = cal.LineP2.Y - cal.LineP1.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                foundLines[cal.Name] = new LineDetectResult(cal.Name, cal.LineP1, cal.LineP2, len, Found: true);
            }

            var foundCircles = new Dictionary<string, CircleFinderResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in result.CircleFinders)
            {
                foundCircles[c.Name] = c;
            }

            foreach (var d in (config.Diameters ?? new List<DiameterDefinition>()))
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (d is null || string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.CircleRef))
                {
                    continue;
                }

                if (!foundCircles.TryGetValue(d.CircleRef, out var c) || !c.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Diameters.Add(new DiameterResult(d.Name, d.CircleRef, Found: false, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, Pass: false, default, 0.0));
                    continue;
                }

                var diameterPx = 2.0 * c.RadiusPx;
                var value = config.PixelsPerMm > 0 ? diameterPx / config.PixelsPerMm : diameterPx;
                var pass = value >= (d.Nominal - d.ToleranceMinus) && value <= (d.Nominal + d.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                result.Diameters.Add(new DiameterResult(d.Name, d.CircleRef, Found: true, value, d.Nominal, d.TolerancePlus, d.ToleranceMinus, pass, c.Center, c.RadiusPx));
            }

            var tEdgePairs0 = swTotal.ElapsedMilliseconds;
            foreach (var ep in config.EdgePairs)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (string.IsNullOrWhiteSpace(ep.Name) || string.IsNullOrWhiteSpace(ep.RefA) || string.IsNullOrWhiteSpace(ep.RefB))
                {
                    continue;
                }

                if (!foundLines.TryGetValue(ep.RefA, out var la) || !foundLines.TryGetValue(ep.RefB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[ep.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.EdgePairs.Add(new EdgePairResult(
                        ep.Name,
                        ep.RefA,
                        ep.RefB,
                        Found: false,
                        default, default, default, default,
                        double.NaN,
                        ep.Nominal,
                        ep.TolerancePlus,
                        ep.ToleranceMinus,
                        Pass: false,
                        default,
                        default));
                    continue;
                }

                var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (ep.Nominal - ep.ToleranceMinus) && value <= (ep.Nominal + ep.TolerancePlus);

                __swNode.Stop();
                result.Timings.NodeTimings[ep.Name] = (int)__swNode.ElapsedMilliseconds;
                result.EdgePairs.Add(new EdgePairResult(
                    ep.Name,
                    ep.RefA,
                    ep.RefB,
                    Found: true,
                    la.P1, la.P2,
                    lb.P1, lb.P2,
                    value,
                    ep.Nominal,
                    ep.TolerancePlus,
                    ep.ToleranceMinus,
                    pass,
                    ca,
                    cb));
            }
            result.Timings.EdgePairsMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tEdgePairs0);

            static bool TryIntersectInfiniteLines(LineDetectResult a, LineDetectResult b, out Point2d inter)
            {
                var ax = a.P2.X - a.P1.X;
                var ay = a.P2.Y - a.P1.Y;
                var bx = b.P2.X - b.P1.X;
                var by = b.P2.Y - b.P1.Y;
                var denom = ax * by - ay * bx;
                if (Math.Abs(denom) < 1e-9)
                {
                    inter = default;
                    return false;
                }

                var cx = b.P1.X - a.P1.X;
                var cy = b.P1.Y - a.P1.Y;
                var t = (cx * by - cy * bx) / denom;
                inter = new Point2d(a.P1.X + t * ax, a.P1.Y + t * ay);
                return double.IsFinite(inter.X) && double.IsFinite(inter.Y);
            }

            var tAngles0 = swTotal.ElapsedMilliseconds;
            foreach (var a in config.Angles)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.LineA) || string.IsNullOrWhiteSpace(a.LineB))
                {
                    continue;
                }

                if (!foundLines.TryGetValue(a.LineA, out var la) || !foundLines.TryGetValue(a.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[a.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, double.NaN, a.Nominal, a.TolerancePlus, a.ToleranceMinus, Pass: false, Found: false, default, default, default));
                    continue;
                }

                var v1 = new Point2d(la.P2.X - la.P1.X, la.P2.Y - la.P1.Y);
                var v2 = new Point2d(lb.P2.X - lb.P1.X, lb.P2.Y - lb.P1.Y);
                var n1 = Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
                var n2 = Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y);
                if (n1 < 1e-9 || n2 < 1e-9)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[a.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, double.NaN, a.Nominal, a.TolerancePlus, a.ToleranceMinus, Pass: false, Found: false, default, default, default));
                    continue;
                }

                var dot = (v1.X * v2.X + v1.Y * v2.Y) / (n1 * n2);
                dot = Math.Clamp(dot, -1.0, 1.0);
                var angle = Math.Acos(dot) * 180.0 / Math.PI;

                var pass = angle >= (a.Nominal - a.ToleranceMinus) && angle <= (a.Nominal + a.TolerancePlus);
                var found = TryIntersectInfiniteLines(la, lb, out var inter);
                __swNode.Stop();
                result.Timings.NodeTimings[a.Name] = (int)__swNode.ElapsedMilliseconds;
                result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, angle, a.Nominal, a.TolerancePlus, a.ToleranceMinus, pass, found, inter, new Point2d(v1.X / n1, v1.Y / n1), new Point2d(v2.X / n2, v2.Y / n2)));
            }
            result.Timings.AnglesMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tAngles0);

            var tDistances0 = swTotal.ElapsedMilliseconds;
            var distanceAnchors = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in foundPoints)
            {
                distanceAnchors[kv.Key] = kv.Value;
            }

            foreach (var c in result.CircleFinders)
            {
                if (c is not null && c.Found)
                {
                    distanceAnchors[c.Name] = c.Center;
                }
            }

            foreach (var d in result.Diameters)
            {
                if (d is not null && d.Found)
                {
                    distanceAnchors[d.Name] = d.Center;
                }
            }

            foreach (var d in config.Distances)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!distanceAnchors.TryGetValue(d.PointA, out var a) || !distanceAnchors.TryGetValue(d.PointB, out var b))
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Distances.Add(new DistanceCheckResult(d.Name, d.PointA, d.PointB, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, false));
                    continue;
                }

                var checkRes = _distanceCalculator.CheckDistance(d, a, b, config.PixelsPerMm);
                __swNode.Stop();
                result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                result.Distances.Add(checkRes);
            }

            foreach (var dd in config.LineToLineDistances)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!foundLines.TryGetValue(dd.LineA, out var la) || !foundLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                var (distPx, ca, cb) = CalculateLineLineDistance(la, lb, dd.Mode);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, ca, cb));
            }

            foreach (var dd in config.PointToLineDistances)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!foundPoints.TryGetValue(dd.Point, out var p) || !foundLines.TryGetValue(dd.Line, out var l) || !l.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                var (distPx, closest) = CalculatePointLineDistance(p, l, dd.Mode);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, p, closest));
            }
            foreach (var dd in config.SegmentLineDistances)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!foundLines.TryGetValue(dd.LineA, out var la) || !foundLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.SegmentLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                Roi? searchRoiA = null;
                var lineADef = config.Lines?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                if (lineADef is not null) searchRoiA = lineADef.SearchRoi;
                else
                {
                    var calADef = config.Calipers?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                    if (calADef is not null) searchRoiA = calADef.SearchRoi;
                    else
                    {
                        var lpdADef = config.LinePairDetections?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                        if (lpdADef is not null) searchRoiA = lpdADef.SearchRoi;
                        else
                        {
                            var epdADef = config.EdgePairDetections?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                            if (epdADef is not null) searchRoiA = epdADef.SearchRoi;
                        }
                    }
                }

                var (distPx, ca, cb) = CalculateSegmentLineDistance(la, lb, dd.Mode, dd.ExtensionMode, searchRoiA, originTeach, originFound, angleDeg);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                result.SegmentLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, ca, cb));
            }
            result.Timings.DistancesMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tDistances0);

            var tCdt0 = swTotal.ElapsedMilliseconds;
            static BarcodeFormat[] ResolveFormats(List<CodeSymbology> sym)
            {
                if (sym is null || sym.Count == 0)
                {
                    return Array.Empty<BarcodeFormat>();
                }

                var fmts = new HashSet<BarcodeFormat>();
                foreach (var s in sym)
                {
                    switch (s)
                    {
                        case CodeSymbology.Qr:
                            fmts.Add(BarcodeFormat.QR_CODE);
                            break;
                        case CodeSymbology.DataMatrix:
                            fmts.Add(BarcodeFormat.DATA_MATRIX);
                            break;
                        case CodeSymbology.Pdf417:
                            fmts.Add(BarcodeFormat.PDF_417);
                            break;
                        case CodeSymbology.Aztec:
                            fmts.Add(BarcodeFormat.AZTEC);
                            break;
                        case CodeSymbology.Barcode1D:
                            fmts.Add(BarcodeFormat.CODE_128);
                            fmts.Add(BarcodeFormat.CODE_39);
                            fmts.Add(BarcodeFormat.CODE_93);
                            fmts.Add(BarcodeFormat.EAN_13);
                            fmts.Add(BarcodeFormat.EAN_8);
                            fmts.Add(BarcodeFormat.UPC_A);
                            fmts.Add(BarcodeFormat.UPC_E);
                            fmts.Add(BarcodeFormat.ITF);
                            fmts.Add(BarcodeFormat.CODABAR);
                            break;
                        default:
                            break;
                    }
                }

                return fmts.ToArray();
            }

            foreach (var cdt in config.CodeDetections)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (string.IsNullOrWhiteSpace(cdt.Name) || cdt.SearchRoi.Width <= 0 || cdt.SearchRoi.Height <= 0)
                {
                    continue;
                }

                var roi = TransformRoiKeepSize(cdt.SearchRoi, originTeach, originFound, angleDeg);
                var (matForCode, _) = ResolveToolPreprocess("CodeDetection", cdt.Name);

                var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height)
                    .Intersect(new Rect(0, 0, matForCode.Width, matForCode.Height));

                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[cdt.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.CodeDetections.Add(new CodeDetectionResult(cdt.Name, Found: false, Text: string.Empty, BoundingBox: default));
                    continue;
                }

                using var crop = new Mat(matForCode, rect);
                Mat gray0;
                if (crop.Channels() == 1)
                {
                    gray0 = crop;
                }
                else
                {
                    gray0 = crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                    matsToDispose.Add(gray0);
                }

                var options = new DecodingOptions
                {
                    TryHarder = cdt.TryHarder,
                    PossibleFormats = ResolveFormats(cdt.Symbologies).ToList()
                };

                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = options
                };

                options.TryInverted = true;

                // Convert gray Mat to byte[] and decode via LuminanceSource (no System.Drawing dependency).
                var gray = gray0.IsContinuous() ? gray0 : gray0.Clone();
                if (!ReferenceEquals(gray, gray0))
                {
                    matsToDispose.Add(gray);
                }

                var w0 = gray.Cols;
                var h0 = gray.Rows;
                var anglesToTry = new List<double> { 0 };
                if (Math.Abs(angleDeg) > 0.1)
                {
                    anglesToTry.Add(-angleDeg); // Try to straighten based on Origin
                }
                
                if (cdt.TryHarder)
                {
                    double[] sweep = { 45, -45, 15, -15, 30, -30, 60, -60, 75, -75 };
                    foreach (var a in sweep)
                    {
                        if (!anglesToTry.Contains(a)) anglesToTry.Add(a);
                    }
                }

                ZXing.Result? decoded = null;
                double successfulAngle = 0;
                int rotatedW = gray.Cols;
                int rotatedH = gray.Rows;

                foreach (var a in anglesToTry)
                {
                    using var rotatedGray = new Mat();
                    if (Math.Abs(a) < 0.1)
                    {
                        gray.CopyTo(rotatedGray);
                        rotatedW = gray.Cols;
                        rotatedH = gray.Rows;
                    }
                    else
                    {
                        var center = new Point2f(gray.Cols / 2.0f, gray.Rows / 2.0f);
                        using var rot = Cv2.GetRotationMatrix2D(center, a, 1.0);
                        var absCos = Math.Abs(rot.At<double>(0, 0));
                        var absSin = Math.Abs(rot.At<double>(0, 1));
                        rotatedW = (int)(gray.Rows * absSin + gray.Cols * absCos);
                        rotatedH = (int)(gray.Rows * absCos + gray.Cols * absSin);
                        
                        rot.Set<double>(0, 2, rot.At<double>(0, 2) + rotatedW / 2.0 - center.X);
                        rot.Set<double>(1, 2, rot.At<double>(1, 2) + rotatedH / 2.0 - center.Y);
                        
                        Cv2.WarpAffine(gray, rotatedGray, rot, new Size(rotatedW, rotatedH), InterpolationFlags.Linear, BorderTypes.Replicate);
                    }

                    var bufRot = new byte[rotatedW * rotatedH];
                    Marshal.Copy(rotatedGray.Data, bufRot, 0, bufRot.Length);
                    var srcRot = new RGBLuminanceSource(bufRot, rotatedW, rotatedH, RGBLuminanceSource.BitmapFormat.Gray8);

                    decoded = reader.Decode(srcRot);
                    if (decoded is not null && !string.IsNullOrWhiteSpace(decoded.Text))
                    {
                        successfulAngle = a;
                        break;
                    }
                }

                if (decoded is null || string.IsNullOrWhiteSpace(decoded.Text))
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[cdt.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.CodeDetections.Add(new CodeDetectionResult(cdt.Name, Found: false, Text: string.Empty, BoundingBox: default));
                    continue;
                }

                // Bounding box: start from decoded points, then pad slightly so the overlay covers the full symbol.
                var bb = rect;
                if (decoded.ResultPoints is not null && decoded.ResultPoints.Length > 0)
                {
                    var xs = new List<double>();
                    var ys = new List<double>();

                    if (Math.Abs(successfulAngle) > 0.1)
                    {
                        var center = new Point2f(gray.Cols / 2.0f, gray.Rows / 2.0f);
                        using var invRot = Cv2.GetRotationMatrix2D(new Point2f(rotatedW / 2.0f, rotatedH / 2.0f), -successfulAngle, 1.0);
                        invRot.Set<double>(0, 2, invRot.At<double>(0, 2) + center.X - rotatedW / 2.0f);
                        invRot.Set<double>(1, 2, invRot.At<double>(1, 2) + center.Y - rotatedH / 2.0f);

                        foreach (var p in decoded.ResultPoints)
                        {
                            var px = p.X * invRot.At<double>(0, 0) + p.Y * invRot.At<double>(0, 1) + invRot.At<double>(0, 2);
                            var py = p.X * invRot.At<double>(1, 0) + p.Y * invRot.At<double>(1, 1) + invRot.At<double>(1, 2);
                            xs.Add(px);
                            ys.Add(py);
                        }
                    }
                    else
                    {
                        xs.AddRange(decoded.ResultPoints.Select(p => (double)p.X));
                        ys.AddRange(decoded.ResultPoints.Select(p => (double)p.Y));
                    }

                    var minX = xs.Min();
                    var maxX = xs.Max();
                    var minY = ys.Min();
                    var maxY = ys.Max();
                    var baseW = Math.Max(1.0, maxX - minX);
                    var baseH = Math.Max(1.0, maxY - minY);
                    var padX = Math.Max(2.0, baseW * 0.12);
                    var padY = Math.Max(2.0, baseH * 0.12);

                    var x = rect.X + (int)Math.Floor(minX - padX);
                    var y = rect.Y + (int)Math.Floor(minY - padY);
                    var w = (int)Math.Ceiling(baseW + padX * 2);
                    var h = (int)Math.Ceiling(baseH + padY * 2);

                    bb = new Rect(x, y, w, h).Intersect(rect);
                }

                __swNode.Stop();
                result.Timings.NodeTimings[cdt.Name] = (int)__swNode.ElapsedMilliseconds;
                result.CodeDetections.Add(new CodeDetectionResult(cdt.Name, Found: true, Text: decoded.Text, BoundingBox: bb));
            }
            result.Timings.CdtMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tCdt0);

            var tCond0 = swTotal.ElapsedMilliseconds;
            EvaluateConditions(config, result);
            result.Timings.ConditionsMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tCond0);

            var tDef0 = swTotal.ElapsedMilliseconds;
            var defectConfig = TransformDefectConfig(config.DefectConfig, originTeach, originFound, angleDeg);
            // Defects remain on default preprocess for now (backward compatible).
            result.Defects = _defectDetector.Detect(GetProcessedDefault(), defectConfig);
            result.Timings.DefectsMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tDef0);

            result.Pass = originPass
            && result.Points.All(x => x.Pass)
            && result.Distances.All(x => x.Pass)
            && result.LineToLineDistances.All(x => x.Pass)
            && result.PointToLineDistances.All(x => x.Pass)
            && result.SegmentLineDistances.All(x => x.Pass)
            && result.Angles.All(x => x.Pass)
            && result.EdgePairs.All(x => x.Pass)
            && result.EdgePairDetections.All(x => x.Pass)
            && result.LinePairDetections.All(x => x.Pass)
            && result.Diameters.All(x => x.Pass)
            && result.SurfaceCompares.All(x => x.Pass)
            && result.CodeDetections.All(x => x.Found)
            && result.Conditions.All(x => x.Pass)
            && (result.Defects?.Defects?.Count ?? 0) == 0;

            if (originPass)
            {
                track.LastOriginPos = originMatch.Position;
                track.LastAngleDeg = poseAngleDeg;

                foreach (var pr in result.Points)
                {
                    if (pr.Pass)
                    {
                        track.LastPointPos[pr.Name] = pr.Position;
                    }
                }
            }

            ExecutePlcNodes(config, result, _plcManager);

            ExecuteDbNodes(config, result, effectiveDbManager, DbExecutionTiming.AfterFlow);

            ExecuteImageOutputs(config, result, image, GetPreprocessNodeOutput, nodesById, edges);

            result.Timings.TotalMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds);

            return result;
        }
        finally
        {
            foreach (var m in matsToDispose)
            {
                m.Dispose();
            }
        }
    }
}

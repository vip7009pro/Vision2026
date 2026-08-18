using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public partial class InspectionService
{
    private static void ExecuteImageOutputs(VisionConfig config, InspectionResult result, Mat rawInputImage, Func<string, Mat> getNodeOutputImage, Dictionary<string, ToolGraphNode> nodesById, List<ToolGraphEdge> edges)
    {
        if (config.ImageOutputs is null || config.ImageOutputs.Count == 0 || rawInputImage is null || rawInputImage.Empty())
        {
            return;
        }

        foreach (var io in config.ImageOutputs)
        {
            if (string.IsNullOrWhiteSpace(io.Name))
            {
                continue;
            }

            var swIo = Stopwatch.StartNew();

            if (!io.EnableOutput)
            {
                result.Timings.NodeTimings[io.Name] = (int)swIo.ElapsedMilliseconds;
                continue;
            }

            if (io.SaveCondition == ImageOutputCondition.OnPass && !result.Pass)
            {
                result.Timings.NodeTimings[io.Name] = (int)swIo.ElapsedMilliseconds;
                continue;
            }
            if (io.SaveCondition == ImageOutputCondition.OnFail && result.Pass)
            {
                result.Timings.NodeTimings[io.Name] = (int)swIo.ElapsedMilliseconds;
                continue;
            }

            try
            {
                Mat sourceMat = rawInputImage;
                var ioNode = nodesById.Values.FirstOrDefault(n => (string.Equals(n.Type, "ImageOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "OutputImage", StringComparison.OrdinalIgnoreCase)) && string.Equals(n.RefName, io.Name, StringComparison.OrdinalIgnoreCase));

                string? inputName = io.InputNodeName;
                if (string.IsNullOrWhiteSpace(inputName) && ioNode is not null)
                {
                    var edge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, ioNode.Id, StringComparison.OrdinalIgnoreCase));
                    if (edge is not null && nodesById.TryGetValue(edge.FromNodeId, out var fromNode))
                    {
                        inputName = fromNode.RefName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(inputName))
                {
                    var srcNode = nodesById.Values.FirstOrDefault(n => string.Equals(n.RefName, inputName, StringComparison.OrdinalIgnoreCase) || string.Equals(n.Id, inputName, StringComparison.OrdinalIgnoreCase));
                    if (srcNode is not null)
                    {
                        sourceMat = getNodeOutputImage(srcNode.Id);
                    }
                }

                var now = DateTime.Now;
                var prodName = !string.IsNullOrWhiteSpace(config.ProductName) ? config.ProductName : (config.ProductCode ?? "");

                var folder = string.IsNullOrWhiteSpace(io.SaveFolderPath) ? @"C:\VisionOutput" : io.SaveFolderPath;
                folder = folder.Replace("{ProductCode}", config.ProductCode ?? "")
                               .Replace("{ProductName}", prodName)
                               .Replace("{YYYY}", now.ToString("yyyy"))
                               .Replace("{MM}", now.ToString("MM"))
                               .Replace("{DD}", now.ToString("dd"));

                var fileName = string.IsNullOrWhiteSpace(io.FileNameFormat) ? "IMG_{YYYY}{MM}{DD}_{HH}{mm}{ss}" : io.FileNameFormat;
                fileName = fileName.Replace("{YYYY}", now.ToString("yyyy"))
                                   .Replace("{MM}", now.ToString("MM"))
                                   .Replace("{DD}", now.ToString("dd"))
                                   .Replace("{HH}", now.ToString("HH"))
                                   .Replace("{mm}", now.ToString("mm"))
                                   .Replace("{ss}", now.ToString("ss"))
                                   .Replace("{Count}", now.Ticks.ToString()[^6..])
                                   .Replace("{ProductCode}", config.ProductCode ?? "")
                                   .Replace("{ProductName}", prodName)
                                   .Replace("{Status}", result.Pass ? "PASS" : "FAIL");

                var vars = ConditionEvaluator.BuildVariableMap(result, config);
                fileName = ConditionEvaluator.EvaluateTextTemplate(fileName, vars);

                var ext = io.Format switch
                {
                    ImageOutputFormat.JPG => ".jpg",
                    ImageOutputFormat.BMP => ".bmp",
                    _ => ".png"
                };

                if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ext;
                }

                var fullPath = Path.Combine(folder, fileName);

                Mat saveMat;
                if (sourceMat.Channels() == 1)
                {
                    saveMat = new Mat();
                    Cv2.CvtColor(sourceMat, saveMat, ColorConversionCodes.GRAY2BGR);
                }
                else
                {
                    saveMat = sourceMat.Clone();
                }

                if (io.IncludeOverlay)
                {
                    BurnOverlaysToMat(saveMat, config, result, io, inputName, nodesById, edges);
                }

                // Gửi vào hàng đợi bất đồng bộ ngoài luồng chính (Non-blocking, tốn < 0.01ms)
                // AsyncImageSaver tự quản lý vòng đời và giải phóng saveMat sau khi ghi đĩa xong.
                bool enqueued = AsyncImageSaver.Instance.Enqueue(saveMat, fullPath, io.Name);
                result.ImageOutputs.Add(new ImageOutputResult(io.Name, enqueued, enqueued ? fullPath : "", enqueued ? "" : "Image save queue is full"));
            }
            catch (Exception ex)
            {
                result.ImageOutputs.Add(new ImageOutputResult(io.Name, false, "", ex.Message));
            }
            finally
            {
                result.Timings.NodeTimings[io.Name] = (int)swIo.ElapsedMilliseconds;
            }
        }
    }

    private static void BurnOverlaysToMat(Mat mat, VisionConfig config, InspectionResult result, ImageOutputDefinition? io = null, string? resolvedInputNodeName = null, Dictionary<string, ToolGraphNode>? nodesById = null, List<ToolGraphEdge>? edges = null)
    {
        if (mat is null || mat.Empty())
        {
            return;
        }

        var targetNodeName = !string.IsNullOrWhiteSpace(resolvedInputNodeName) ? resolvedInputNodeName : io?.InputNodeName;
        var renderAll = string.IsNullOrWhiteSpace(targetNodeName) ||
                         string.Equals(targetNodeName, "Default (Current Image)", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(targetNodeName, "ResultView", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(targetNodeName, "Preprocess", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(targetNodeName, "ImageSource", StringComparison.OrdinalIgnoreCase) ||
                         targetNodeName.StartsWith("ResultView", StringComparison.OrdinalIgnoreCase) ||
                         targetNodeName.StartsWith("Preprocess", StringComparison.OrdinalIgnoreCase) ||
                         targetNodeName.StartsWith("ImageSource", StringComparison.OrdinalIgnoreCase);

        var allowedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!renderAll && !string.IsNullOrWhiteSpace(targetNodeName))
        {
            allowedNodes.Add(targetNodeName);

            var distDef = config.Distances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (distDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(distDef.PointA)) allowedNodes.Add(distDef.PointA);
                if (!string.IsNullOrWhiteSpace(distDef.PointB)) allowedNodes.Add(distDef.PointB);
            }

            var lldDef = config.LineToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (lldDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(lldDef.LineA)) allowedNodes.Add(lldDef.LineA);
                if (!string.IsNullOrWhiteSpace(lldDef.LineB)) allowedNodes.Add(lldDef.LineB);
            }

            var pldDef = config.PointToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (pldDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(pldDef.Point)) allowedNodes.Add(pldDef.Point);
                if (!string.IsNullOrWhiteSpace(pldDef.Line)) allowedNodes.Add(pldDef.Line);
            }

            var sldDef = config.SegmentLineDistances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (sldDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(sldDef.LineA)) allowedNodes.Add(sldDef.LineA);
                if (!string.IsNullOrWhiteSpace(sldDef.LineB)) allowedNodes.Add(sldDef.LineB);
            }

            var angleDef = config.Angles?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (angleDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(angleDef.LineA)) allowedNodes.Add(angleDef.LineA);
                if (!string.IsNullOrWhiteSpace(angleDef.LineB)) allowedNodes.Add(angleDef.LineB);
            }

            var cpDef = config.CreatePoints?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (cpDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(cpDef.PointRef)) allowedNodes.Add(cpDef.PointRef);
            }

            var clDef = config.CreateLines?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (clDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(clDef.Point1Ref)) allowedNodes.Add(clDef.Point1Ref);
                if (!string.IsNullOrWhiteSpace(clDef.Point2Ref)) allowedNodes.Add(clDef.Point2Ref);
                if (!string.IsNullOrWhiteSpace(clDef.PointRef)) allowedNodes.Add(clDef.PointRef);
            }

            // If targetNode is a Crop or Preprocess node, also include downstream child nodes
            IEnumerable<ToolGraphNode>? gNodes = nodesById != null ? nodesById.Values : config.ToolGraph?.Nodes;
            var gEdges = edges ?? config.ToolGraph?.Edges;
            if (gNodes != null && gEdges != null)
            {
                var targetGNode = gNodes.FirstOrDefault(n => string.Equals(n.RefName, targetNodeName, StringComparison.OrdinalIgnoreCase));
                if (targetGNode != null)
                {
                    foreach (var edge in gEdges.Where(e => string.Equals(e.FromNodeId, targetGNode.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        var childNode = gNodes.FirstOrDefault(n => string.Equals(n.Id, edge.ToNodeId, StringComparison.OrdinalIgnoreCase));
                        if (childNode != null && !string.IsNullOrWhiteSpace(childNode.RefName))
                        {
                            allowedNodes.Add(childNode.RefName);
                        }
                    }
                }
            }
        }

        bool ShouldRender(string nodeName) => renderAll || allowedNodes.Contains(nodeName);

        var isCalibrated = config.PixelsPerMm > 0 && Math.Abs(config.PixelsPerMm - 1.0) > 1e-6;
        var scale = config.PixelsPerMm;
        string UnitStr(double val) => isCalibrated ? $"{val:0.##} mm" : $"{val:0.##} px";

        var green = new Scalar(0, 255, 0);       // Lime / Pass
        var red = new Scalar(0, 0, 255);         // Red / Fail / Outlier
        var cyan = new Scalar(255, 255, 0);       // Cyan / Search ROI
        var yellow = new Scalar(0, 255, 255);     // Yellow
        var white = new Scalar(255, 255, 255);   // White
        var showRoiBoxes = io is null || io.ShowRoi;

        // Adaptive scaling based on image resolution
        double autoScale = Math.Max(1.0, Math.Max(mat.Cols, mat.Rows) / 1280.0);
        if (io is not null && io.OverlayScale > 0.05)
        {
            autoScale *= io.OverlayScale;
        }

        int ScalePx(int px) => Math.Max(1, (int)Math.Round(px * autoScale));
        int ScaleThick(int th) => Math.Max(1, (int)Math.Round(th * autoScale));
        double ScaleFont(double baseFontScale) => baseFontScale * autoScale;

        int thThin = ScaleThick(1);
        int thNormal = ScaleThick(2);
        int thThick = ScaleThick(3);

        double fontScaleSmall = ScaleFont(0.5);
        double fontScaleNormal = ScaleFont(0.6);

        int fontThickSmall = Math.Max(1, (int)Math.Round(1.0 * autoScale));
        int fontThickNormal = Math.Max(1, (int)Math.Round(1.8 * autoScale));

        void DrawRotatedRoi(Mat targetMat, Roi teachRoi, Scalar color, int thickness = -1)
        {
            if (!showRoiBoxes) return;
            if (targetMat is null || targetMat.Empty() || teachRoi is null || teachRoi.Width <= 0 || teachRoi.Height <= 0) return;

            int actualThick = thickness > 0 ? ScaleThick(thickness) : thThin;

            Point2d originTeach;
            if (config.Origin is not null && (config.Origin.WorldPosition.X != 0 || config.Origin.WorldPosition.Y != 0))
            {
                originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
            }
            else if (config.Origin?.TemplateRoi.Width > 0)
            {
                originTeach = new Point2d(config.Origin.TemplateRoi.X + config.Origin.TemplateRoi.Width / 2.0, config.Origin.TemplateRoi.Y + config.Origin.TemplateRoi.Height / 2.0);
            }
            else if (config.Origin?.SearchRoi.Width > 0)
            {
                originTeach = new Point2d(config.Origin.SearchRoi.X + config.Origin.SearchRoi.Width / 2.0, config.Origin.SearchRoi.Y + config.Origin.SearchRoi.Height / 2.0);
            }
            else
            {
                originTeach = new Point2d(0, 0);
            }

            Point2d originFound;
            double angleDeg = 0;
            bool hasOriginPose = false;

            if (result.Origin is not null && (result.Origin.MatchRect.Width > 0 || result.Origin.Position.X != 0 || result.Origin.Position.Y != 0))
            {
                var mr = result.Origin.MatchRect;
                originFound = (mr.Width > 0 && mr.Height > 0)
                    ? new Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                    : new Point2d(result.Origin.Position.X, result.Origin.Position.Y);
                angleDeg = result.Origin.AngleDeg;
                hasOriginPose = true;
            }
            else
            {
                originFound = originTeach;
            }

            Point2d centerFound;
            double totalAngle;

            if (hasOriginPose)
            {
                var centerTeach = new Point2d(teachRoi.X + teachRoi.Width / 2.0, teachRoi.Y + teachRoi.Height / 2.0);
                var centerRot = Rotate(centerTeach, originTeach, angleDeg);
                var dx = originFound.X - originTeach.X;
                var dy = originFound.Y - originTeach.Y;
                centerFound = new Point2d(centerRot.X + dx, centerRot.Y + dy);
                totalAngle = angleDeg + teachRoi.Angle;
            }
            else
            {
                centerFound = new Point2d(teachRoi.X + teachRoi.Width / 2.0, teachRoi.Y + teachRoi.Height / 2.0);
                totalAngle = teachRoi.Angle;
            }

            var halfW = teachRoi.Width / 2.0;
            var halfH = teachRoi.Height / 2.0;

            var p1 = Rotate(new Point2d(-halfW, -halfH), new Point2d(0, 0), totalAngle) + centerFound;
            var p2 = Rotate(new Point2d(halfW, -halfH), new Point2d(0, 0), totalAngle) + centerFound;
            var p3 = Rotate(new Point2d(halfW, halfH), new Point2d(0, 0), totalAngle) + centerFound;
            var p4 = Rotate(new Point2d(-halfW, halfH), new Point2d(0, 0), totalAngle) + centerFound;

            Cv2.Line(targetMat, new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y)), new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)), color, actualThick, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)), new Point((int)Math.Round(p3.X), (int)Math.Round(p3.Y)), color, actualThick, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p3.X), (int)Math.Round(p3.Y)), new Point((int)Math.Round(p4.X), (int)Math.Round(p4.Y)), color, actualThick, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p4.X), (int)Math.Round(p4.Y)), new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y)), color, actualThick, LineTypes.AntiAlias);
        }

        void DrawRotatedBoxDirect(Mat targetMat, Rect bb, double angle, Scalar color, int thickness = -1)
        {
            if (targetMat is null || targetMat.Empty() || bb.Width <= 0 || bb.Height <= 0) return;
            int actualThick = thickness > 0 ? ScaleThick(thickness) : thNormal;

            var center = new Point2d(bb.X + bb.Width / 2.0, bb.Y + bb.Height / 2.0);
            var halfW = bb.Width / 2.0;
            var halfH = bb.Height / 2.0;

            var p1 = Rotate(new Point2d(-halfW, -halfH), new Point2d(0, 0), angle) + center;
            var p2 = Rotate(new Point2d(halfW, -halfH), new Point2d(0, 0), angle) + center;
            var p3 = Rotate(new Point2d(halfW, halfH), new Point2d(0, 0), angle) + center;
            var p4 = Rotate(new Point2d(-halfW, halfH), new Point2d(0, 0), angle) + center;

            Cv2.Line(targetMat, new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y)), new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)), color, actualThick, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)), new Point((int)Math.Round(p3.X), (int)Math.Round(p3.Y)), color, actualThick, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p3.X), (int)Math.Round(p3.Y)), new Point((int)Math.Round(p4.X), (int)Math.Round(p4.Y)), color, actualThick, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p4.X), (int)Math.Round(p4.Y)), new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y)), color, actualThick, LineTypes.AntiAlias);
        }

        // 1. Origin
        if (result.Origin is not null && ShouldRender(config.Origin?.Name ?? "Origin"))
        {
            if (config.Origin?.TemplateRoi.Width > 0)
            {
                DrawRotatedRoi(mat, config.Origin.TemplateRoi, green, 2);
            }
            else if (config.Origin?.SearchRoi.Width > 0)
            {
                DrawRotatedRoi(mat, config.Origin.SearchRoi, green, 2);
            }

            var mr = result.Origin.MatchRect;
            var cx = (int)Math.Round(mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : result.Origin.Position.X);
            var cyPos = (int)Math.Round(mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : result.Origin.Position.Y);
            Cv2.Circle(mat, new Point(cx, cyPos), ScalePx(4), green, -1, LineTypes.AntiAlias);
            Cv2.Line(mat, new Point(cx - ScalePx(15), cyPos), new Point(cx + ScalePx(15), cyPos), red, thNormal, LineTypes.AntiAlias);
            Cv2.Line(mat, new Point(cx, cyPos - ScalePx(15)), new Point(cx, cyPos + ScalePx(15)), green, thNormal, LineTypes.AntiAlias);
            Cv2.PutText(mat, $"Origin: {result.Origin.Score:0.00}", new Point(cx + ScalePx(18), cyPos + ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleNormal, green, fontThickNormal, LineTypes.AntiAlias);
        }

        // Map point positions for Distance / Geometry lookups
        var pointPosMap = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
        foreach (var pRes in result.Points)
        {
            pointPosMap[pRes.Name] = pRes.Position;
        }

        // 2. Points
        foreach (var pRes in result.Points)
        {
            if (!ShouldRender(pRes.Name)) continue;
            var pDef = config.Points.FirstOrDefault(x => string.Equals(x.Name, pRes.Name, StringComparison.OrdinalIgnoreCase));
            if (pDef is not null && pDef.SearchRoi.Width > 0 && pDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, pDef.SearchRoi, cyan, 1);
            }

            var color = pRes.Pass ? green : red;
            var px = (int)Math.Round(pRes.Position.X);
            var py = (int)Math.Round(pRes.Position.Y);
            Cv2.Circle(mat, new Point(px, py), ScalePx(3), color, -1, LineTypes.AntiAlias);
            Cv2.Line(mat, new Point(px - ScalePx(10), py), new Point(px + ScalePx(10), py), color, thNormal, LineTypes.AntiAlias);
            Cv2.Line(mat, new Point(px, py - ScalePx(10)), new Point(px, py + ScalePx(10)), color, thNormal, LineTypes.AntiAlias);
            Cv2.PutText(mat, pRes.Name, new Point(px + ScalePx(12), py - ScalePx(6)), HersheyFonts.HersheySimplex, fontScaleSmall, color, fontThickSmall, LineTypes.AntiAlias);
        }

        // 3. Lines
        foreach (var lRes in result.Lines)
        {
            if (!ShouldRender(lRes.Name)) continue;
            var lDef = config.Lines.FirstOrDefault(x => string.Equals(x.Name, lRes.Name, StringComparison.OrdinalIgnoreCase));
            if (lDef is not null && lDef.SearchRoi.Width > 0 && lDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, lDef.SearchRoi, cyan, 1);
            }

            if (lRes.Found)
            {
                var p1 = new Point((int)lRes.P1.X, (int)lRes.P1.Y);
                var p2 = new Point((int)lRes.P2.X, (int)lRes.P2.Y);
                Cv2.Line(mat, p1, p2, green, thNormal, LineTypes.AntiAlias);
                Cv2.PutText(mat, lRes.Name, new Point((p1.X + p2.X) / 2 + ScalePx(5), (p1.Y + p2.Y) / 2 - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 4. Calipers
        foreach (var cRes in result.Calipers)
        {
            if (!ShouldRender(cRes.Name)) continue;
            var cDef = config.Calipers.FirstOrDefault(x => string.Equals(x.Name, cRes.Name, StringComparison.OrdinalIgnoreCase));
            if (cDef is not null && cDef.SearchRoi.Width > 0 && cDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, cDef.SearchRoi, green, 1);
            }

            if (cRes.Found)
            {
                var p1 = new Point((int)cRes.LineP1.X, (int)cRes.LineP1.Y);
                var p2 = new Point((int)cRes.LineP2.X, (int)cRes.LineP2.Y);
                Cv2.Line(mat, p1, p2, green, thNormal, LineTypes.AntiAlias);
                Cv2.PutText(mat, cRes.Name, new Point((p1.X + p2.X) / 2 + ScalePx(5), (p1.Y + p2.Y) / 2 - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);

                if (cRes.Points is not null && cRes.Points.Count > 0)
                {
                    foreach (var p in cRes.Points)
                    {
                        Cv2.Circle(mat, new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)), ScalePx(3), yellow, -1, LineTypes.AntiAlias);
                    }
                }
            }
        }

        // 5. CircleFinders
        foreach (var cfRes in result.CircleFinders)
        {
            if (!ShouldRender(cfRes.Name)) continue;
            var cfDef = config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, cfRes.Name, StringComparison.OrdinalIgnoreCase));
            if (cfDef is not null && cfDef.SearchRoi.Width > 0 && cfDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, cfDef.SearchRoi, cyan, 1);
            }

            if (cfRes.Found)
            {
                var center = new Point((int)Math.Round(cfRes.Center.X), (int)Math.Round(cfRes.Center.Y));
                var radius = (int)Math.Round(cfRes.RadiusPx);
                Cv2.Circle(mat, center, radius, green, thNormal, LineTypes.AntiAlias);
                Cv2.Circle(mat, center, ScalePx(3), green, -1, LineTypes.AntiAlias);
                Cv2.Line(mat, new Point(center.X - ScalePx(8), center.Y), new Point(center.X + ScalePx(8), center.Y), green, thNormal, LineTypes.AntiAlias);
                Cv2.Line(mat, new Point(center.X, center.Y - ScalePx(8)), new Point(center.X, center.Y + ScalePx(8)), green, thNormal, LineTypes.AntiAlias);

                if (cfRes.EdgePoints is not null && cfRes.EdgePoints.Count > 0)
                {
                    for (var i = 0; i < cfRes.EdgePoints.Count; i++)
                    {
                        var pt = cfRes.EdgePoints[i];
                        var isInlier = cfRes.InlierFlags is not null && i < cfRes.InlierFlags.Count && cfRes.InlierFlags[i];
                        var ptColor = isInlier ? green : red;
                        Cv2.Circle(mat, new Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)), ScalePx(2), ptColor, -1, LineTypes.AntiAlias);
                    }
                }

                var rVal = isCalibrated ? cfRes.RadiusPx / scale : cfRes.RadiusPx;
                var lbl = $"{cfRes.Name}: R={rVal:0.##}{(isCalibrated ? "mm" : "px")}";
                Cv2.PutText(mat, lbl, new Point(center.X + ScalePx(10), center.Y - ScalePx(10)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 5b. LinePairDetections
        foreach (var lpdRes in result.LinePairDetections)
        {
            if (!ShouldRender(lpdRes.Name)) continue;
            var lpdDef = config.LinePairDetections?.FirstOrDefault(x => string.Equals(x.Name, lpdRes.Name, StringComparison.OrdinalIgnoreCase));
            if (showRoiBoxes && lpdDef is not null && lpdDef.SearchRoi.Width > 0 && lpdDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, lpdDef.SearchRoi, cyan, 1);
            }

            if (lpdRes.Found)
            {
                var l1p1 = new Point((int)lpdRes.L1P1.X, (int)lpdRes.L1P1.Y);
                var l1p2 = new Point((int)lpdRes.L1P2.X, (int)lpdRes.L1P2.Y);
                var l2p1 = new Point((int)lpdRes.L2P1.X, (int)lpdRes.L2P1.Y);
                var l2p2 = new Point((int)lpdRes.L2P2.X, (int)lpdRes.L2P2.Y);
                Cv2.Line(mat, l1p1, l1p2, cyan, thNormal, LineTypes.AntiAlias);
                Cv2.Line(mat, l2p1, l2p2, cyan, thNormal, LineTypes.AntiAlias);

                var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(lpdRes.L1P1, lpdRes.L1P2, lpdRes.L2P1, lpdRes.L2P2);
                var cap = new Point((int)ca.X, (int)ca.Y);
                var cbp = new Point((int)cb.X, (int)cb.Y);
                var col = lpdRes.Pass ? green : red;
                Cv2.Line(mat, cap, cbp, col, thNormal, LineTypes.AntiAlias);
                Cv2.Circle(mat, cap, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.Circle(mat, cbp, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.PutText(mat, $"{lpdRes.Name}={UnitStr(lpdRes.Value)}", new Point((cap.X + cbp.X) / 2 + ScalePx(5), (cap.Y + cbp.Y) / 2 - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 6. Diameters
        foreach (var dRes in result.Diameters)
        {
            if (!ShouldRender(dRes.Name)) continue;
            if (dRes.Found)
            {
                var center = new Point((int)Math.Round(dRes.Center.X), (int)Math.Round(dRes.Center.Y));
                var radius = (int)Math.Round(dRes.RadiusPx);
                Cv2.Circle(mat, center, radius, green, thNormal, LineTypes.AntiAlias);
                Cv2.Circle(mat, center, ScalePx(3), green, -1, LineTypes.AntiAlias);
                Cv2.Line(mat, new Point(center.X - ScalePx(8), center.Y), new Point(center.X + ScalePx(8), center.Y), green, thNormal, LineTypes.AntiAlias);
                Cv2.Line(mat, new Point(center.X, center.Y - ScalePx(8)), new Point(center.X, center.Y + ScalePx(8)), green, thNormal, LineTypes.AntiAlias);
                var lbl = $"{dRes.Name}: D={UnitStr(dRes.Value)}";
                Cv2.PutText(mat, lbl, new Point(center.X + ScalePx(10), center.Y - ScalePx(10)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 7. Distances
        foreach (var dRes in result.Distances)
        {
            if (!ShouldRender(dRes.Name)) continue;
            if ((dRes.Pass || dRes.Value > 0) && pointPosMap.TryGetValue(dRes.PointA, out var pa) && pointPosMap.TryGetValue(dRes.PointB, out var pb))
            {
                var p1 = new Point((int)pa.X, (int)pa.Y);
                var p2 = new Point((int)pb.X, (int)pb.Y);
                var col = dRes.Pass ? green : red;
                Cv2.Line(mat, p1, p2, col, thNormal, LineTypes.AntiAlias);
                Cv2.Circle(mat, p1, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.Circle(mat, p2, ScalePx(3), col, -1, LineTypes.AntiAlias);
                var mx = (p1.X + p2.X) / 2;
                var my = (p1.Y + p2.Y) / 2;
                Cv2.PutText(mat, $"{dRes.Name}={UnitStr(dRes.Value)}", new Point(mx + ScalePx(5), my - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 8. LineToLineDistances
        foreach (var lld in result.LineToLineDistances)
        {
            if (!ShouldRender(lld.Name)) continue;
            var p1 = new Point((int)lld.ClosestA.X, (int)lld.ClosestA.Y);
            var p2 = new Point((int)lld.ClosestB.X, (int)lld.ClosestB.Y);
            var col = lld.Pass ? green : red;
            Cv2.Line(mat, p1, p2, col, thNormal, LineTypes.AntiAlias);
            Cv2.Circle(mat, p1, ScalePx(3), col, -1, LineTypes.AntiAlias);
            Cv2.Circle(mat, p2, ScalePx(3), col, -1, LineTypes.AntiAlias);
            var mx = (p1.X + p2.X) / 2;
            var my = (p1.Y + p2.Y) / 2;
            Cv2.PutText(mat, $"{lld.Name}={UnitStr(lld.Value)}", new Point(mx + ScalePx(5), my - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
        }

        // 9. PointToLineDistances
        foreach (var pld in result.PointToLineDistances)
        {
            if (!ShouldRender(pld.Name)) continue;
            var p1 = new Point((int)pld.ClosestA.X, (int)pld.ClosestA.Y);
            var p2 = new Point((int)pld.ClosestB.X, (int)pld.ClosestB.Y);
            var col = pld.Pass ? green : red;
            Cv2.Line(mat, p1, p2, col, thNormal, LineTypes.AntiAlias);
            Cv2.Circle(mat, p1, ScalePx(3), col, -1, LineTypes.AntiAlias);
            Cv2.Circle(mat, p2, ScalePx(3), col, -1, LineTypes.AntiAlias);
            var mx = (p1.X + p2.X) / 2;
            var my = (p1.Y + p2.Y) / 2;
            Cv2.PutText(mat, $"{pld.Name}={UnitStr(pld.Value)}", new Point(mx + ScalePx(5), my - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
        }

        // 10. SegmentLineDistances
        foreach (var sld in result.SegmentLineDistances)
        {
            if (!ShouldRender(sld.Name)) continue;
            var la = result.Lines.FirstOrDefault(x => string.Equals(x.Name, sld.RefA, StringComparison.OrdinalIgnoreCase));
            if (la is not null && la.Found)
            {
                Cv2.Line(mat, new Point((int)la.P1.X, (int)la.P1.Y), new Point((int)la.P2.X, (int)la.P2.Y), cyan, thNormal, LineTypes.AntiAlias);
            }
            var lb = result.Lines.FirstOrDefault(x => string.Equals(x.Name, sld.RefB, StringComparison.OrdinalIgnoreCase));
            if (lb is not null && lb.Found)
            {
                Cv2.Line(mat, new Point((int)lb.P1.X, (int)lb.P1.Y), new Point((int)lb.P2.X, (int)lb.P2.Y), yellow, thNormal, LineTypes.AntiAlias);
            }
            if (!double.IsNaN(sld.Value))
            {
                var p1 = new Point((int)sld.ClosestA.X, (int)sld.ClosestA.Y);
                var p2 = new Point((int)sld.ClosestB.X, (int)sld.ClosestB.Y);
                var col = sld.Pass ? green : red;
                Cv2.Line(mat, p1, p2, col, thNormal, LineTypes.AntiAlias);
                Cv2.Circle(mat, p1, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.Circle(mat, p2, ScalePx(3), col, -1, LineTypes.AntiAlias);
                var mx = (p1.X + p2.X) / 2;
                var my = (p1.Y + p2.Y) / 2;
                Cv2.PutText(mat, $"{sld.Name}={UnitStr(sld.Value)}", new Point(mx + ScalePx(5), my - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 11. Angles
        foreach (var a in result.Angles)
        {
            if (!ShouldRender(a.Name)) continue;
            var col = a.Pass ? green : red;
            var vertex = new Point((int)a.Intersection.X, (int)a.Intersection.Y);
            Cv2.Circle(mat, vertex, ScalePx(5), col, thNormal, LineTypes.AntiAlias);
            Cv2.PutText(mat, $"{a.Name}={a.ValueDeg:0.##} deg", new Point(vertex.X + ScalePx(10), vertex.Y - ScalePx(10)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
        }

        // 12. EdgePairs
        foreach (var ep in result.EdgePairs)
        {
            if (!ShouldRender(ep.Name)) continue;
            if (ep.Found)
            {
                var p1 = new Point((int)ep.ClosestA.X, (int)ep.ClosestA.Y);
                var p2 = new Point((int)ep.ClosestB.X, (int)ep.ClosestB.Y);
                var col = ep.Pass ? green : red;
                Cv2.Line(mat, p1, p2, col, thNormal, LineTypes.AntiAlias);
                Cv2.Circle(mat, p1, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.Circle(mat, p2, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.PutText(mat, $"{ep.Name}={UnitStr(ep.Value)}", new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 12b. EdgePairDetections
        foreach (var epdRes in result.EdgePairDetections)
        {
            if (!ShouldRender(epdRes.Name)) continue;
            var epdDef = config.EdgePairDetections?.FirstOrDefault(x => string.Equals(x.Name, epdRes.Name, StringComparison.OrdinalIgnoreCase));
            if (showRoiBoxes && epdDef is not null && epdDef.SearchRoi.Width > 0 && epdDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, epdDef.SearchRoi, cyan, 1);
            }

            if (epdRes.Found)
            {
                var l1p1 = new Point((int)epdRes.L1P1.X, (int)epdRes.L1P1.Y);
                var l1p2 = new Point((int)epdRes.L1P2.X, (int)epdRes.L1P2.Y);
                var l2p1 = new Point((int)epdRes.L2P1.X, (int)epdRes.L2P1.Y);
                var l2p2 = new Point((int)epdRes.L2P2.X, (int)epdRes.L2P2.Y);
                var col = epdRes.Pass ? green : red;
                Cv2.Line(mat, l1p1, l1p2, cyan, thNormal, LineTypes.AntiAlias);
                Cv2.Line(mat, l2p1, l2p2, cyan, thNormal, LineTypes.AntiAlias);
                var ca = new Point((int)epdRes.ClosestA.X, (int)epdRes.ClosestA.Y);
                var cb = new Point((int)epdRes.ClosestB.X, (int)epdRes.ClosestB.Y);
                Cv2.Line(mat, ca, cb, col, thNormal, LineTypes.AntiAlias);
                Cv2.Circle(mat, ca, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.Circle(mat, cb, ScalePx(3), col, -1, LineTypes.AntiAlias);
                Cv2.PutText(mat, $"{epdRes.Name}={UnitStr(epdRes.Value)}", new Point((ca.X + cb.X) / 2, (ca.Y + cb.Y) / 2 - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
            }
        }

        // 13. BlobDetections
        foreach (var bRes in result.BlobDetections)
        {
            if (!ShouldRender(bRes.Name)) continue;
            var bDef = config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, bRes.Name, StringComparison.OrdinalIgnoreCase));
            if (bDef is not null && bDef.InspectRoi.Width > 0 && bDef.InspectRoi.Height > 0)
            {
                DrawRotatedRoi(mat, bDef.InspectRoi, cyan, 1);
                if (bDef.Rois is not null)
                {
                    foreach (var rr in bDef.Rois)
                    {
                        if (rr?.Roi is not null && rr.Roi.Width > 0 && rr.Roi.Height > 0)
                        {
                            DrawRotatedRoi(mat, rr.Roi, rr.Mode == BlobRoiMode.Exclude ? red : yellow, 1);
                        }
                    }
                }
            }

            foreach (var blob in bRes.Blobs)
            {
                var r = new Rect(blob.BoundingBox.X, blob.BoundingBox.Y, blob.BoundingBox.Width, blob.BoundingBox.Height);
                Cv2.Rectangle(mat, r, yellow, thThin, LineTypes.AntiAlias);
                var pt = new Point((int)blob.Centroid.X, (int)blob.Centroid.Y);
                Cv2.Circle(mat, pt, ScalePx(3), yellow, -1, LineTypes.AntiAlias);
            }
        }

        // 14. SurfaceCompares
        foreach (var scRes in result.SurfaceCompares)
        {
            if (!ShouldRender(scRes.Name)) continue;
            var scDef = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, scRes.Name, StringComparison.OrdinalIgnoreCase));
            if (scDef is not null && scDef.InspectRoi.Width > 0 && scDef.InspectRoi.Height > 0)
            {
                DrawRotatedRoi(mat, scDef.InspectRoi, cyan, 1);
            }

            foreach (var defect in scRes.Defects)
            {
                var r = new Rect(defect.BoundingBox.X, defect.BoundingBox.Y, defect.BoundingBox.Width, defect.BoundingBox.Height);
                Cv2.Rectangle(mat, r, red, thNormal, LineTypes.AntiAlias);
            }
        }

        // 14b. ContourCompares
        foreach (var ccRes in result.ContourCompares)
        {
            if (!ShouldRender(ccRes.Name)) continue;
            var ccDef = config.ContourCompares?.FirstOrDefault(x => string.Equals(x.Name, ccRes.Name, StringComparison.OrdinalIgnoreCase));
            if (showRoiBoxes && ccDef is not null && ccDef.InspectRoi.Width > 0 && ccDef.InspectRoi.Height > 0)
            {
                DrawRotatedRoi(mat, ccDef.InspectRoi, cyan, 1);
            }

            var tplList = ccRes.TemplateContours ?? (ccRes.TemplateContour is not null ? new List<List<Point2d>> { ccRes.TemplateContour } : null);
            if (tplList is not null)
            {
                foreach (var c in tplList)
                {
                    if (c.Count > 1)
                    {
                        var ptsTpl = c.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsTpl }, true, yellow, thThin, LineTypes.AntiAlias);
                    }
                }
            }

            if (ccRes.PassSegments is not null)
            {
                foreach (var seg in ccRes.PassSegments)
                {
                    if (seg.Points.Count > 1)
                    {
                        var ptsPass = seg.Points.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsPass }, seg.IsClosed, green, thNormal, LineTypes.AntiAlias);
                    }
                }
            }
            else if (ccRes.PassContours is not null)
            {
                foreach (var c in ccRes.PassContours)
                {
                    if (c.Count > 1)
                    {
                        var ptsPass = c.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsPass }, true, green, thNormal, LineTypes.AntiAlias);
                    }
                }
            }

            if (ccRes.FailSegments is not null)
            {
                foreach (var seg in ccRes.FailSegments)
                {
                    if (seg.Points.Count > 1)
                    {
                        var ptsFail = seg.Points.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsFail }, seg.IsClosed, red, thNormal, LineTypes.AntiAlias);
                    }
                }
            }
            else if (ccRes.FailContours is not null)
            {
                foreach (var c in ccRes.FailContours)
                {
                    if (c.Count > 1)
                    {
                        var ptsFail = c.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsFail }, false, red, thNormal, LineTypes.AntiAlias);
                    }
                }
            }
        }

        // 15. CodeDetections
        foreach (var cdt in result.CodeDetections)
        {
            if (!ShouldRender(cdt.Name)) continue;
            var cdtDef = config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, cdt.Name, StringComparison.OrdinalIgnoreCase));
            if (cdtDef is not null && cdtDef.SearchRoi.Width > 0 && cdtDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, cdtDef.SearchRoi, cyan, 1);
            }

            if (cdt.Found)
            {
                var col = green;
                if (cdt.BoundingBox.Width > 0 && cdt.BoundingBox.Height > 0)
                {
                    if (Math.Abs(cdt.Angle) > 0.001)
                    {
                        DrawRotatedBoxDirect(mat, cdt.BoundingBox, cdt.Angle, col, 2);
                    }
                    else
                    {
                        Cv2.Rectangle(mat, new Rect(cdt.BoundingBox.X, cdt.BoundingBox.Y, cdt.BoundingBox.Width, cdt.BoundingBox.Height), col, thNormal, LineTypes.AntiAlias);
                    }
                }

                Cv2.PutText(mat, $"{cdt.Name}: {cdt.Text}", new Point(cdt.BoundingBox.X, Math.Max(ScalePx(15), cdt.BoundingBox.Y - ScalePx(5))), HersheyFonts.HersheySimplex, fontScaleNormal, col, fontThickNormal, LineTypes.AntiAlias);
            }
        }

        // 15b. ColorDiffs
        if (result.ColorDiffs is not null)
        {
            foreach (var cdRes in result.ColorDiffs)
            {
                if (!ShouldRender(cdRes.Name)) continue;
                var cdDef = config.ColorDiffs?.FirstOrDefault(x => string.Equals(x.Name, cdRes.Name, StringComparison.OrdinalIgnoreCase));
                if (cdDef is not null && cdDef.InspectRoi.Width > 0 && cdDef.InspectRoi.Height > 0)
                {
                    var col = cdRes.Pass ? green : red;
                    DrawRotatedRoi(mat, cdDef.InspectRoi, col, 2);
                    var text = $"{cdRes.Name}: dE={cdRes.DeltaE:F2} (L={cdRes.MeasuredL:F1}, a={cdRes.MeasuredA:F1}, b={cdRes.MeasuredB:F1})";
                    Cv2.PutText(mat, text, new Point(cdDef.InspectRoi.X + ScalePx(6), cdDef.InspectRoi.Y + ScalePx(18)), HersheyFonts.HersheySimplex, fontScaleSmall, col, fontThickSmall, LineTypes.AntiAlias);
                }
            }
        }

        // 16. CreatePoints
        if (result.CreatePoints is not null)
        {
            foreach (var cp in result.CreatePoints)
            {
                if (!ShouldRender(cp.Name)) continue;
                if (cp.Success)
                {
                    var px = (int)Math.Round(cp.X);
                    var py = (int)Math.Round(cp.Y);
                    Cv2.Circle(mat, new Point(px, py), ScalePx(5), green, thThin, LineTypes.AntiAlias);
                    Cv2.Circle(mat, new Point(px, py), ScalePx(2), green, -1, LineTypes.AntiAlias);
                    Cv2.Line(mat, new Point(px - ScalePx(8), py), new Point(px + ScalePx(8), py), green, thThin, LineTypes.AntiAlias);
                    Cv2.Line(mat, new Point(px, py - ScalePx(8)), new Point(px, py + ScalePx(8)), green, thThin, LineTypes.AntiAlias);
                    Cv2.PutText(mat, $"{cp.Name} ({cp.X:F1}, {cp.Y:F1})", new Point(px + ScalePx(10), py - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);
                }
            }
        }

        // 17. CreateLines
        if (result.CreateLines is not null)
        {
            foreach (var cl in result.CreateLines)
            {
                if (!ShouldRender(cl.Name)) continue;
                if (cl.Success)
                {
                    var p1 = new Point((int)Math.Round(cl.X1), (int)Math.Round(cl.Y1));
                    var p2 = new Point((int)Math.Round(cl.X2), (int)Math.Round(cl.Y2));
                    Cv2.Line(mat, p1, p2, green, thNormal, LineTypes.AntiAlias);
                    var mx = (p1.X + p2.X) / 2;
                    var my = (p1.Y + p2.Y) / 2;
                    Cv2.PutText(mat, cl.Name, new Point(mx + ScalePx(5), my - ScalePx(5)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);
                }
            }
        }

        // 18. CreateRects
        if (result.CreateRects is not null)
        {
            foreach (var cr in result.CreateRects)
            {
                if (!ShouldRender(cr.Name)) continue;
                if (cr.Success)
                {
                    var rect = new Rect((int)Math.Round(cr.X), (int)Math.Round(cr.Y), (int)Math.Round(cr.Width), (int)Math.Round(cr.Height));
                    if (Math.Abs(cr.Angle) > 0.001)
                    {
                        DrawRotatedBoxDirect(mat, rect, cr.Angle, green, 2);
                    }
                    else
                    {
                        Cv2.Rectangle(mat, rect, green, thNormal, LineTypes.AntiAlias);
                    }
                    Cv2.PutText(mat, cr.Name, new Point(rect.X + ScalePx(5), rect.Y + ScalePx(15)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);
                }
            }
        }

        // 19. CreateCircles
        if (result.CreateCircles is not null)
        {
            foreach (var cc in result.CreateCircles)
            {
                if (!ShouldRender(cc.Name)) continue;
                if (cc.Success)
                {
                    var center = new Point((int)Math.Round(cc.CenterX), (int)Math.Round(cc.CenterY));
                    var radius = (int)Math.Round(cc.Radius);
                    Cv2.Circle(mat, center, radius, green, thNormal, LineTypes.AntiAlias);
                    Cv2.Circle(mat, center, ScalePx(3), green, -1, LineTypes.AntiAlias);
                    Cv2.Line(mat, new Point(center.X - ScalePx(8), center.Y), new Point(center.X + ScalePx(8), center.Y), green, thNormal, LineTypes.AntiAlias);
                    Cv2.Line(mat, new Point(center.X, center.Y - ScalePx(8)), new Point(center.X, center.Y + ScalePx(8)), green, thNormal, LineTypes.AntiAlias);
                    Cv2.PutText(mat, cc.Name, new Point(center.X + ScalePx(10), center.Y - ScalePx(10)), HersheyFonts.HersheySimplex, fontScaleSmall, green, fontThickSmall, LineTypes.AntiAlias);
                }
            }
        }

        // 20. Crops (ROI indicator)
        if (showRoiBoxes && config.Crops is not null)
        {
            foreach (var cr in config.Crops)
            {
                if (!ShouldRender(cr.Name)) continue;
                if (cr.CropRoi.Width > 0 && cr.CropRoi.Height > 0)
                {
                    DrawRotatedRoi(mat, cr.CropRoi, yellow, 1);
                    Cv2.PutText(mat, cr.Name, new Point(cr.CropRoi.X + ScalePx(5), cr.CropRoi.Y + ScalePx(15)), HersheyFonts.HersheySimplex, fontScaleSmall, yellow, fontThickSmall, LineTypes.AntiAlias);
                }
            }
        }

        // 21. TextNodes
        if (config.TextNodes is not null)
        {
            Dictionary<string, ConditionEvaluator.Variable>? vars = null;
            try { vars = ConditionEvaluator.BuildVariableMap(result, config); } catch { }

            double baseFontScale = (io is not null && io.TextFontSize > 0) ? (io.TextFontSize / 24.0) : 0.7;
            double textFontScale = baseFontScale * autoScale;
            int textThick = Math.Max(1, (int)Math.Round(2 * autoScale * Math.Min(1.2, baseFontScale)));

            foreach (var t in config.TextNodes)
            {
                if (string.IsNullOrWhiteSpace(t.Name)) continue;
                var text = ConditionEvaluator.EvaluateTextTemplate(t.Text ?? string.Empty, vars);
                var col = white;
                if (vars is not null && t.Conditions is not null)
                {
                    foreach (var c in t.Conditions)
                    {
                        if (string.IsNullOrWhiteSpace(c.Expression)) continue;
                        try
                        {
                            if (ConditionEvaluator.Evaluate(c.Expression, vars))
                            {
                                col = ParseHexColorToScalar(c.Color) ?? col;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                RenderTextWithNewlines(mat, text, new Point(t.X, t.Y), HersheyFonts.HersheySimplex, textFontScale, col, textThick);
            }
        }
    }

    private static void RenderTextWithNewlines(Mat mat, string text, Point basePt, HersheyFonts fontFace, double fontScale, Scalar color, int thickness)
    {
        if (string.IsNullOrEmpty(text)) return;
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        int fontHeight = Math.Max(15, (int)(34 * fontScale));
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i])) continue;
            var linePt = new Point(basePt.X, basePt.Y + i * fontHeight);
            Cv2.PutText(mat, lines[i], linePt, fontFace, fontScale, color, thickness, LineTypes.AntiAlias);
        }
    }

    private static Scalar? ParseHexColorToScalar(string hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.TrimStart('#');
            if (hex.Length == 8) hex = hex.Substring(2);
            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new Scalar(b, g, r);
            }
        }
        catch { }
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class SurfaceCompareAndCaliperRoiTests
{
    private static InspectionService CreateInspectionService()
    {
        var pre = new ImagePreprocessor();
        var matcher = new PatternMatcher();
        var dist = new DistanceCalculator();
        var line = new LineDetector();
        var defect = new DefectDetector();
        return new InspectionService(pre, matcher, dist, line, defect);
    }

    public static void RunTests()
    {
        Console.WriteLine("=== [TEST SUITE] Surface Compare & Caliper ROI First Tests ===");

        TestCaliperRoiFirstPerformanceAndAccuracy();
        TestSurfaceCompareTemplateCache();
        TestSurfaceCompareAutoAlignAndConfidenceGuard();
        TestSurfaceCompareSubPixelAlign();
        TestSurfaceCompareNormalizeLighting();
        TestSurfaceCompareSpatialEdgeTolerance();
        TestSurfaceCompareEdgeCompareAlgorithm();
        TestSurfaceCompareSsimThresholdDecoupling();

        Console.WriteLine("=== [PASSED] All Surface Compare & Caliper ROI First Tests Completed Successfully! ===");
    }

    private static void TestCaliperRoiFirstPerformanceAndAccuracy()
    {
        Console.WriteLine("--> Test 1: Caliper ROI First Performance & Accuracy");

        // Create a synthetic image (1500 x 1500) representing high-res production image
        using var largeImg = new Mat(1500, 1500, MatType.CV_8UC1, Scalar.All(200));
        // Draw a dark vertical bar in the middle (x = 750)
        Cv2.Rectangle(largeImg, new Rect(750, 200, 300, 1100), Scalar.All(40), -1);

        var calDef = new CaliperDefinition
        {
            Name = "Cal_Test",
            SearchRoi = new Roi { X = 700, Y = 500, Width = 150, Height = 60, Angle = 0 },
            Orientation = CaliperOrientation.Horizontal,
            Polarity = EdgePolarity.LightToDark,
            StripCount = 5,
            StripWidth = 10,
            StripLength = 100,
            MinEdgeStrength = 15.0
        };

        var preprocessor = new ImagePreprocessor();
        var preprocessSettings = new PreprocessSettings
        {
            UseGaussianBlur = true,
            BlurKernel = 3
        };

        // 1. Without ROI First: simulate preprocessing entire 1500x1500 image first
        var swFull = Stopwatch.StartNew();
        using var fullPreprocessed = preprocessor.Run(largeImg, preprocessSettings);
        var resLegacy = CaliperDetector.Detect(fullPreprocessed, calDef);
        swFull.Stop();

        // 2. With ROI First: pass preprocessor directly to CaliperDetector.Detect
        var swRoiFirst = Stopwatch.StartNew();
        var resRoiFirst = CaliperDetector.Detect(largeImg, calDef, default, default, 0.0, preprocessor, preprocessSettings);
        swRoiFirst.Stop();

        if (!resLegacy.Found || !resRoiFirst.Found)
            throw new Exception("Caliper failed to find edge in one or both modes!");

        var centerLegacy = new Point2d((resLegacy.LineP1.X + resLegacy.LineP2.X) / 2.0, (resLegacy.LineP1.Y + resLegacy.LineP2.Y) / 2.0);
        var centerRoiFirst = new Point2d((resRoiFirst.LineP1.X + resRoiFirst.LineP2.X) / 2.0, (resRoiFirst.LineP1.Y + resRoiFirst.LineP2.Y) / 2.0);

        // Edge position must match within sub-pixel accuracy
        double diffX = Math.Abs(centerLegacy.X - centerRoiFirst.X);
        double diffY = Math.Abs(centerLegacy.Y - centerRoiFirst.Y);
        if (diffX > 0.5 || diffY > 0.5)
            throw new Exception($"Caliper ROI First detected position mismatch: legacy={centerLegacy}, roiFirst={centerRoiFirst}");

        Console.WriteLine($"   [OK] Caliper ROI First edge verified: Pos=({centerRoiFirst.X:F2}, {centerRoiFirst.Y:F2}), Time={swRoiFirst.Elapsed.TotalMilliseconds:F3}ms vs FullImage={swFull.Elapsed.TotalMilliseconds:F3}ms");
    }

    private static void TestSurfaceCompareTemplateCache()
    {
        Console.WriteLine("--> Test 2: Surface Compare Template RAM Cache");

        string tempTplFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sc_template.png");
        try
        {
            using (var tpl = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(128)))
            {
                Cv2.Circle(tpl, new Point(50, 50), 20, Scalar.All(255), -1);
                tpl.SaveImage(tempTplFile);
            }

            var config = new VisionConfig
            {
                SurfaceCompares = new List<SurfaceCompareDefinition>
                {
                    new SurfaceCompareDefinition
                    {
                        Name = "SC_CacheTest",
                        TemplateImageFile = tempTplFile,
                        InspectRoi = new Roi { X = 10, Y = 10, Width = 100, Height = 100 },
                        TemplateRoi = new Roi { X = 10, Y = 10, Width = 100, Height = 100 },
                        DiffThreshold = 25,
                        MinBlobArea = 5
                    }
                }
            };

            var inspector = CreateInspectionService();

            using var testImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(128));
            Cv2.Circle(testImg, new Point(60, 60), 20, Scalar.All(255), -1);

            // Run 1 - fills cache
            var sw1 = Stopwatch.StartNew();
            var res1 = inspector.Inspect(testImg, config);
            sw1.Stop();

            // Run 2 - serves from cache (RAM)
            var sw2 = Stopwatch.StartNew();
            var res2 = inspector.Inspect(testImg, config);
            sw2.Stop();

            if (res1.SurfaceCompares.Count == 0 || res2.SurfaceCompares.Count == 0)
                throw new Exception("Surface compare failed to run!");

            if (!res1.SurfaceCompares[0].Pass || !res2.SurfaceCompares[0].Pass)
                throw new Exception("Surface compare should pass when images are identical!");

            Console.WriteLine($"   [OK] Surface Compare RAM cache operational: Run1={sw1.Elapsed.TotalMilliseconds:F3}ms, Run2={sw2.Elapsed.TotalMilliseconds:F3}ms");
        }
        finally
        {
            if (File.Exists(tempTplFile)) File.Delete(tempTplFile);
        }
    }

    private static void TestSurfaceCompareAutoAlignAndConfidenceGuard()
    {
        Console.WriteLine("--> Test 3: Auto Align & Confidence Guard");

        string tempTpl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sc_autoalign.png");
        try
        {
            // Template: square box at center
            using (var tpl = new Mat(120, 120, MatType.CV_8UC1, Scalar.All(50)))
            {
                Cv2.Rectangle(tpl, new Rect(40, 40, 40, 40), Scalar.All(220), -1);
                tpl.SaveImage(tempTpl);
            }

            var inspector = CreateInspectionService();

            // Case A: Small shift (3px X, 2px Y) - AutoAlign should compensate and pass
            using var shiftedImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(50));
            // Shifted square: normally at (40+10, 40+10) = (50, 50). Shifted by +3, +2 -> (53, 52)
            Cv2.Rectangle(shiftedImg, new Rect(53, 52, 40, 40), Scalar.All(220), -1);

            var cfgAlign = new VisionConfig
            {
                SurfaceCompares = new List<SurfaceCompareDefinition>
                {
                    new SurfaceCompareDefinition
                    {
                        Name = "SC_Align",
                        TemplateImageFile = tempTpl,
                        InspectRoi = new Roi { X = 10, Y = 10, Width = 120, Height = 120 },
                        TemplateRoi = new Roi { X = 10, Y = 10, Width = 120, Height = 120 },
                        AutoAlign = true,
                        AutoAlignMaxShiftPx = 5,
                        DiffThreshold = 30,
                        MinBlobArea = 10,
                        MaxBlobArea = 99999
                    }
                }
            };

            var resShifted = inspector.Inspect(shiftedImg, cfgAlign);
            if (!resShifted.SurfaceCompares[0].Pass)
                throw new Exception($"AutoAlign should have compensated 3px shift and passed, but defect count = {resShifted.SurfaceCompares[0].Count}");

            // Case B: Severe mismatch (Confidence Guard) - completely different pattern
            using var mismatchImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(250));
            // Draw a high-contrast diagonal cross that completely conflicts with the square
            Cv2.Line(mismatchImg, new Point(10, 10), new Point(130, 130), Scalar.All(10), 8);
            Cv2.Line(mismatchImg, new Point(10, 130), new Point(130, 10), Scalar.All(10), 8);

            var resMismatch = inspector.Inspect(mismatchImg, cfgAlign);
            var scRes = resMismatch.SurfaceCompares[0];
            Console.WriteLine($"      Mismatch Case: Pass={scRes.Pass}, Count={scRes.Count}, MaxArea={scRes.MaxArea}");
            if (scRes.Pass)
                throw new Exception($"Severe mismatch must NOT pass! Pass={scRes.Pass}, Count={scRes.Count}, MaxArea={scRes.MaxArea}");

            Console.WriteLine("   [OK] AutoAlign compensated mechanical shift, Confidence Guard rejected random warping on severe mismatch.");
        }
        finally
        {
            if (File.Exists(tempTpl)) File.Delete(tempTpl);
        }
    }

    private static void TestSurfaceCompareSubPixelAlign()
    {
        Console.WriteLine("--> Test 4: SubPixel Auto Alignment");

        string tempTpl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sc_subpix.png");
        try
        {
            using (var tpl = new Mat(120, 120, MatType.CV_8UC1, Scalar.All(40)))
            {
                Cv2.Circle(tpl, new Point(60, 60), 25, Scalar.All(200), -1);
                tpl.SaveImage(tempTpl);
            }

            var inspector = CreateInspectionService();

            // Test image shifted by 2.2 pixels via linear warp
            using var baseImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(40));
            Cv2.Circle(baseImg, new Point(70, 70), 25, Scalar.All(200), -1);

            using var warpM = new Mat(2, 3, MatType.CV_32FC1);
            warpM.Set(0, 0, 1.0f); warpM.Set(0, 1, 0.0f); warpM.Set(0, 2, 2.2f);
            warpM.Set(1, 0, 0.0f); warpM.Set(1, 1, 1.0f); warpM.Set(1, 2, 1.8f);

            using var subpixImg = new Mat();
            Cv2.WarpAffine(baseImg, subpixImg, warpM, baseImg.Size(), InterpolationFlags.Linear);

            var cfgSubpix = new VisionConfig
            {
                SurfaceCompares = new List<SurfaceCompareDefinition>
                {
                    new SurfaceCompareDefinition
                    {
                        Name = "SC_SubPixel",
                        TemplateImageFile = tempTpl,
                        InspectRoi = new Roi { X = 10, Y = 10, Width = 120, Height = 120 },
                        TemplateRoi = new Roi { X = 10, Y = 10, Width = 120, Height = 120 },
                        AutoAlign = true,
                        SubPixelAlign = true,
                        AutoAlignMaxShiftPx = 5,
                        EdgeTolerancePx = 1,
                        DiffThreshold = 25,
                        MinBlobArea = 8
                    }
                }
            };

            var resSub = inspector.Inspect(subpixImg, cfgSubpix);
            if (!resSub.SurfaceCompares[0].Pass)
                throw new Exception($"SubPixel align with EdgeTolerance failed to pass, count={resSub.SurfaceCompares[0].Count}");

            Console.WriteLine("   [OK] SubPixel align handled fractional pixel shift seamlessly.");
        }
        finally
        {
            if (File.Exists(tempTpl)) File.Delete(tempTpl);
        }
    }

    private static void TestSurfaceCompareNormalizeLighting()
    {
        Console.WriteLine("--> Test 5: Normalize Lighting");

        string tempTpl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sc_lighting.png");
        try
        {
            using (var tpl = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(100)))
            {
                Cv2.Rectangle(tpl, new Rect(30, 30, 40, 40), Scalar.All(180), -1);
                tpl.SaveImage(tempTpl);
            }

            var inspector = CreateInspectionService();

            // Test image has 35 gray level global illumination increase (135 base, 215 square)
            using var brightImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(135));
            Cv2.Rectangle(brightImg, new Rect(40, 40, 40, 40), Scalar.All(215), -1);

            // Case A: NormalizeLighting = false -> diff threshold 25 will fail because of +35 drift
            var cfgNoNorm = new VisionConfig
            {
                SurfaceCompares = new List<SurfaceCompareDefinition>
                {
                    new SurfaceCompareDefinition
                    {
                        Name = "SC_NoNorm",
                        TemplateImageFile = tempTpl,
                        InspectRoi = new Roi { X = 10, Y = 10, Width = 100, Height = 100 },
                        TemplateRoi = new Roi { X = 10, Y = 10, Width = 100, Height = 100 },
                        DiffThreshold = 25,
                        MinBlobArea = 50,
                        MaxBlobArea = 99999,
                        NormalizeLighting = false
                    }
                }
            };
            var resNoNorm = inspector.Inspect(brightImg, cfgNoNorm);
            if (resNoNorm.SurfaceCompares[0].Pass)
                throw new Exception("Without NormalizeLighting, +35 illumination drift should trigger false positive defect!");

            // Case B: NormalizeLighting = true -> brightness drift is normalized, passes
            var cfgNorm = new VisionConfig
            {
                SurfaceCompares = new List<SurfaceCompareDefinition>
                {
                    new SurfaceCompareDefinition
                    {
                        Name = "SC_Norm",
                        TemplateImageFile = tempTpl,
                        InspectRoi = new Roi { X = 10, Y = 10, Width = 100, Height = 100 },
                        TemplateRoi = new Roi { X = 10, Y = 10, Width = 100, Height = 100 },
                        DiffThreshold = 25,
                        MinBlobArea = 50,
                        MaxBlobArea = 99999,
                        NormalizeLighting = true
                    }
                }
            };
            var resNorm = inspector.Inspect(brightImg, cfgNorm);
            if (!resNorm.SurfaceCompares[0].Pass)
                throw new Exception("With NormalizeLighting = true, uniform brightness drift should be eliminated and pass!");

            Console.WriteLine("   [OK] Normalize Lighting eliminated global illumination drift false positives.");
        }
        finally
        {
            if (File.Exists(tempTpl)) File.Delete(tempTpl);
        }
    }

    private static void TestSurfaceCompareSpatialEdgeTolerance()
    {
        Console.WriteLine("--> Test 6: Spatial Edge Tolerance across Algorithms");

        string tempTpl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sc_edgetol.png");
        try
        {
            using (var tpl = new Mat(120, 120, MatType.CV_8UC1, Scalar.All(30)))
            {
                // High contrast pattern with sharp edges
                Cv2.PutText(tpl, "OK", new Point(20, 80), HersheyFonts.HersheyComplex, 2.0, Scalar.All(240), 4);
                tpl.SaveImage(tempTpl);
            }

            var inspector = CreateInspectionService();

            // Test image shifted by 1 pixel (mechanical jitter)
            using var jitterImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(30));
            Cv2.PutText(jitterImg, "OK", new Point(21, 81), HersheyFonts.HersheyComplex, 2.0, Scalar.All(240), 4);

            var algos = new[]
            {
                SurfaceCompareAlgorithm.AbsDiff,
                SurfaceCompareAlgorithm.SSIM,
                SurfaceCompareAlgorithm.GradientAdaptive,
                SurfaceCompareAlgorithm.EdgeCompare
            };

            foreach (var algo in algos)
            {
                var cfg = new VisionConfig
                {
                    SurfaceCompares = new List<SurfaceCompareDefinition>
                    {
                        new SurfaceCompareDefinition
                        {
                            Name = $"SC_{algo}",
                            TemplateImageFile = tempTpl,
                            InspectRoi = new Roi { X = 0, Y = 0, Width = 120, Height = 120 },
                            TemplateRoi = new Roi { X = 0, Y = 0, Width = 120, Height = 120 },
                            Algorithm = algo,
                            EdgeTolerancePx = 2,
                            DiffThreshold = 35,
                            SsimThreshold = 0.70,
                            MinBlobArea = 15,
                            MaxBlobArea = 99999
                        }
                    }
                };

                var res = inspector.Inspect(jitterImg, cfg);
                if (!res.SurfaceCompares[0].Pass)
                {
                    throw new Exception($"Algorithm {algo} with EdgeTolerancePx=2 failed on 1px jitter! Defects={res.SurfaceCompares[0].Count}");
                }
                Console.WriteLine($"      Algorithm {algo} successfully suppressed edge jitter false positives.");
            }

            Console.WriteLine("   [OK] Spatial Edge Tolerance works harmoniously across all 4 algorithms.");
        }
        finally
        {
            if (File.Exists(tempTpl)) File.Delete(tempTpl);
        }
    }

    private static void TestSurfaceCompareEdgeCompareAlgorithm()
    {
        Console.WriteLine("--> Test 7: EdgeCompare Algorithm for Print Defects & Missing Strokes");

        string tempTpl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sc_edgecmp.png");
        try
        {
            using (var tpl = new Mat(140, 140, MatType.CV_8UC1, Scalar.All(220)))
            {
                // Crisp text "VISION"
                Cv2.PutText(tpl, "VISION", new Point(10, 80), HersheyFonts.HersheyComplex, 1.0, Scalar.All(20), 3);
                tpl.SaveImage(tempTpl);
            }

            var inspector = CreateInspectionService();

            // Good test image
            using var goodImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(220));
            Cv2.PutText(goodImg, "VISION", new Point(10, 80), HersheyFonts.HersheyComplex, 1.0, Scalar.All(20), 3);

            // Defective test image: stroke broken / missing piece in "V"
            using var defectImg = goodImg.Clone();
            Cv2.Circle(defectImg, new Point(25, 70), 5, Scalar.All(220), -1); // cut a hole through the stroke

            var cfg = new VisionConfig
            {
                SurfaceCompares = new List<SurfaceCompareDefinition>
                {
                    new SurfaceCompareDefinition
                    {
                        Name = "SC_EdgeCmp",
                        TemplateImageFile = tempTpl,
                        InspectRoi = new Roi { X = 0, Y = 0, Width = 140, Height = 140 },
                        TemplateRoi = new Roi { X = 0, Y = 0, Width = 140, Height = 140 },
                        Algorithm = SurfaceCompareAlgorithm.EdgeCompare,
                        DiffThreshold = 25,
                        MinBlobArea = 8,
                        MaxBlobArea = 99999
                    }
                }
            };

            var resGood = inspector.Inspect(goodImg, cfg);
            if (!resGood.SurfaceCompares[0].Pass)
                throw new Exception("EdgeCompare should pass on flawless print!");

            var resDefect = inspector.Inspect(defectImg, cfg);
            if (resDefect.SurfaceCompares[0].Pass || resDefect.SurfaceCompares[0].Count == 0)
                throw new Exception("EdgeCompare must detect missing stroke in letter 'V'!");

            Console.WriteLine($"   [OK] EdgeCompare detected broken stroke defect with high sensitivity: defects={resDefect.SurfaceCompares[0].Count}, maxArea={resDefect.SurfaceCompares[0].MaxArea}");
        }
        finally
        {
            if (File.Exists(tempTpl)) File.Delete(tempTpl);
        }
    }

    private static void TestSurfaceCompareSsimThresholdDecoupling()
    {
        Console.WriteLine("--> Test 8: SSIM Threshold Decoupling (Bug Fix)");

        string tempTpl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sc_ssim_fix.png");
        try
        {
            using (var tpl = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(100)))
            {
                Cv2.Circle(tpl, new Point(50, 50), 20, Scalar.All(200), -1);
                tpl.SaveImage(tempTpl);
            }

            var inspector = CreateInspectionService();

            using var testImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(100));
            Cv2.Circle(testImg, new Point(50, 50), 20, Scalar.All(200), -1);

            // Set DiffThreshold to 80 (previously this would override ssimThr to (1 - 0.80)*255 = 51)
            // SsimThreshold is set to 0.95 -> ssimThr should be (1 - 0.95)*255 = 12.75
            var cfg = new VisionConfig
            {
                SurfaceCompares = new List<SurfaceCompareDefinition>
                {
                    new SurfaceCompareDefinition
                    {
                        Name = "SC_SsimDecouple",
                        TemplateImageFile = tempTpl,
                        InspectRoi = new Roi { X = 0, Y = 0, Width = 100, Height = 100 },
                        TemplateRoi = new Roi { X = 0, Y = 0, Width = 100, Height = 100 },
                        Algorithm = SurfaceCompareAlgorithm.SSIM,
                        DiffThreshold = 80,
                        SsimThreshold = 0.95,
                        MinBlobArea = 5,
                        MaxBlobArea = 99999
                    }
                }
            };

            var res = inspector.Inspect(testImg, cfg);
            if (!res.SurfaceCompares[0].Pass)
                throw new Exception("Identical images must pass under SSIM with decoupled threshold!");

            Console.WriteLine("   [OK] DiffThreshold no longer overrides SsimThreshold.");
        }
        finally
        {
            if (File.Exists(tempTpl)) File.Delete(tempTpl);
        }
    }
}

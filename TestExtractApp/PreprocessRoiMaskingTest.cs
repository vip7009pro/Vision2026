using System;
using System.Collections.Generic;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class PreprocessRoiMaskingTest
{
    public static void RunTests()
    {
        Console.WriteLine("\n==========================================");
        Console.WriteLine(">>> RUNNING PREPROCESS ROI MASKING TESTS <<<");
        Console.WriteLine("==========================================");

        TestRectangleStaticMask();
        TestRectangleExcludeMask();
        TestCircleAndPolygonMask();
        TestFollowOriginPoseRotation();

        Console.WriteLine(">>> ALL PREPROCESS ROI MASKING TESTS PASSED SUCCESSFULLY! <<<\n");
    }

    private static void TestRectangleStaticMask()
    {
        var pre = new ImagePreprocessor();
        using var src = new Mat(400, 400, MatType.CV_8UC1, new Scalar(255));
        var settings = new PreprocessSettings();
        var rois = new List<PreprocessRoiDefinition>
        {
            new PreprocessRoiDefinition
            {
                Shape = PreprocessRoiShape.Rectangle,
                Mode = PreprocessRoiMode.Include,
                FollowOrigin = false,
                X = 100,
                Y = 100,
                Width = 200,
                Height = 200
            }
        };

        using var dst = pre.Run(src, settings, rois);

        // Check inside ROI: (200, 200) should be 255
        byte insideVal = dst.At<byte>(200, 200);
        if (insideVal != 255)
            throw new Exception($"TestRectangleStaticMask Failed: Inside pixel expected 255 but got {insideVal}");

        // Check outside ROI: (50, 50) should be 0
        byte outsideVal = dst.At<byte>(50, 50);
        if (outsideVal != 0)
            throw new Exception($"TestRectangleStaticMask Failed: Outside pixel expected 0 but got {outsideVal}");

        Console.WriteLine("[PASS] TestRectangleStaticMask: Include ROI preserved inside and masked outside.");
    }

    private static void TestRectangleExcludeMask()
    {
        var pre = new ImagePreprocessor();
        using var src = new Mat(400, 400, MatType.CV_8UC1, new Scalar(255));
        var settings = new PreprocessSettings();
        var rois = new List<PreprocessRoiDefinition>
        {
            new PreprocessRoiDefinition
            {
                Shape = PreprocessRoiShape.Rectangle,
                Mode = PreprocessRoiMode.Exclude,
                FollowOrigin = false,
                X = 150,
                Y = 150,
                Width = 100,
                Height = 100
            }
        };

        using var dst = pre.Run(src, settings, rois);

        // Check inside excluded ROI: (200, 200) should be 0 (masked out)
        byte insideVal = dst.At<byte>(200, 200);
        if (insideVal != 0)
            throw new Exception($"TestRectangleExcludeMask Failed: Excluded pixel expected 0 but got {insideVal}");

        // Check outside excluded ROI: (50, 50) should remain 255
        byte outsideVal = dst.At<byte>(50, 50);
        if (outsideVal != 255)
            throw new Exception($"TestRectangleExcludeMask Failed: Unaffected pixel expected 255 but got {outsideVal}");

        Console.WriteLine("[PASS] TestRectangleExcludeMask: Exclude ROI properly blanked inner area.");
    }

    private static void TestCircleAndPolygonMask()
    {
        var pre = new ImagePreprocessor();
        using var src = new Mat(400, 400, MatType.CV_8UC1, new Scalar(255));
        var settings = new PreprocessSettings();
        var rois = new List<PreprocessRoiDefinition>
        {
            new PreprocessRoiDefinition
            {
                Shape = PreprocessRoiShape.Circle,
                Mode = PreprocessRoiMode.Include,
                FollowOrigin = false,
                CircleCenterX = 100,
                CircleCenterY = 100,
                CircleRadius = 40
            },
            new PreprocessRoiDefinition
            {
                Shape = PreprocessRoiShape.Polygon,
                Mode = PreprocessRoiMode.Include,
                FollowOrigin = false,
                PolygonPoints = new List<Point2dModel>
                {
                    new Point2dModel { X = 250, Y = 250 },
                    new Point2dModel { X = 350, Y = 250 },
                    new Point2dModel { X = 350, Y = 350 },
                    new Point2dModel { X = 250, Y = 350 }
                }
            }
        };

        using var dst = pre.Run(src, settings, rois);

        // Circle center (100, 100) -> 255
        byte circleCenterVal = dst.At<byte>(100, 100);
        if (circleCenterVal != 255)
            throw new Exception($"TestCircleAndPolygonMask Failed: Circle center expected 255 but got {circleCenterVal}");

        // Polygon center (300, 300) -> 255
        byte polyCenterVal = dst.At<byte>(300, 300);
        if (polyCenterVal != 255)
            throw new Exception($"TestCircleAndPolygonMask Failed: Polygon center expected 255 but got {polyCenterVal}");

        // Background (200, 200) -> 0
        byte bgVal = dst.At<byte>(200, 200);
        if (bgVal != 0)
            throw new Exception($"TestCircleAndPolygonMask Failed: Background expected 0 but got {bgVal}");

        Console.WriteLine("[PASS] TestCircleAndPolygonMask: Circle and Polygon masks correctly composed.");
    }

    private static void TestFollowOriginPoseRotation()
    {
        var pre = new ImagePreprocessor();
        using var src = new Mat(400, 400, MatType.CV_8UC1, new Scalar(255));
        var settings = new PreprocessSettings();

        // Origin teach at (200, 200).
        // Con hàng xoay 90 độ theo chiều kim đồng hồ quanh (200, 200) -> Origin found at (200, 200), Angle = 90°.
        var originTeach = new Point2d(200, 200);
        var originFound = new Point2d(200, 200);
        double originAngleDeg = 90.0;

        // ROI Rectangle đặt tại teach: tâm tại (200, 100), size 40x40.
        // Tọa độ teach: X = 180, Y = 80, W = 40, H = 40.
        // Khi xoay 90 độ quanh (200, 200), tâm mới chuyển thành (300, 200).
        var roiFollow = new PreprocessRoiDefinition
        {
            Shape = PreprocessRoiShape.Rectangle,
            Mode = PreprocessRoiMode.Include,
            FollowOrigin = true,
            X = 180,
            Y = 80,
            Width = 40,
            Height = 40
        };

        // Test khi FollowOrigin = true
        using var dstFollow = pre.Run(src, settings, new List<PreprocessRoiDefinition> { roiFollow }, originTeach, originFound, originAngleDeg);

        byte rotatedInsideVal = dstFollow.At<byte>(200, 300); // (Y=200, X=300)
        byte unrotatedOldPosVal = dstFollow.At<byte>(100, 200); // (Y=100, X=200)

        if (rotatedInsideVal != 255)
            throw new Exception($"TestFollowOriginPoseRotation Failed: Expected rotated position (X=300, Y=200) to be 255 but got {rotatedInsideVal}");

        if (unrotatedOldPosVal != 0)
            throw new Exception($"TestFollowOriginPoseRotation Failed: Expected old teach position (X=200, Y=100) to be 0 (masked out) but got {unrotatedOldPosVal}");

        Console.WriteLine("[PASS] TestFollowOriginPoseRotation (FollowOrigin=true): Masking ROI correctly rotated 90° following the part origin.");

        // Test khi FollowOrigin = false (giữ tĩnh)
        var roiStatic = new PreprocessRoiDefinition
        {
            Shape = PreprocessRoiShape.Rectangle,
            Mode = PreprocessRoiMode.Include,
            FollowOrigin = false,
            X = 180,
            Y = 80,
            Width = 40,
            Height = 40
        };

        using var dstStatic = pre.Run(src, settings, new List<PreprocessRoiDefinition> { roiStatic }, originTeach, originFound, originAngleDeg);

        byte staticInsideVal = dstStatic.At<byte>(100, 200); // (Y=100, X=200)
        byte staticOutsideRotatedVal = dstStatic.At<byte>(200, 300); // (Y=200, X=300)

        if (staticInsideVal != 255)
            throw new Exception($"TestFollowOriginPoseRotation (FollowOrigin=false) Failed: Expected static teach position (X=200, Y=100) to be 255 but got {staticInsideVal}");

        if (staticOutsideRotatedVal != 0)
            throw new Exception($"TestFollowOriginPoseRotation (FollowOrigin=false) Failed: Expected rotated position (X=300, Y=200) to be 0 but got {staticOutsideRotatedVal}");

        Console.WriteLine("[PASS] TestFollowOriginPoseRotation (FollowOrigin=false): Masking ROI correctly remained static at original teach coordinates.");
    }
}

import re

filepath = r'g:\NODEJS\Vision2026\VisionInspectionApp.VisionEngine\Class1.cs'
with open(filepath, 'r', encoding='utf-8') as f:
    text = f.read()

feature_based_code = '''
    private MatchResult MatchByFeatureBased(Mat roiGray, Mat templateGray, PointDefinition definition, double angleDeg, PreprocessSettings? preprocess, Rect roiRect)
    {
        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);
        
        using var orb = ORB.Create(500);
        using var des1 = new Mat();
        using var des2 = new Mat();
        
        orb.DetectAndCompute(templPrep, null, out KeyPoint[] keypoints1, des1);
        orb.DetectAndCompute(roiGray, null, out KeyPoint[] keypoints2, des2);
        
        if (des1.Empty() || des2.Empty())
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }
        
        using var bf = new BFMatcher(NormTypes.Hamming, crossCheck: true);
        var matches = bf.Match(des1, des2);
        
        var goodMatches = matches.OrderBy(m => m.Distance).Take(50).ToArray();
        
        if (goodMatches.Length < 4)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }
        
        var pts1 = goodMatches.Select(m => new Point2d(keypoints1[m.QueryIdx].Pt.X, keypoints1[m.QueryIdx].Pt.Y)).ToArray();
        var pts2 = goodMatches.Select(m => new Point2d(keypoints2[m.TrainIdx].Pt.X, keypoints2[m.TrainIdx].Pt.Y)).ToArray();
        
        using var H = Cv2.FindHomography(InputArray.Create(pts1), InputArray.Create(pts2), HomographyMethods.Ransac, 3.0);
        
        if (H.Empty())
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }
        
        var objCenter = new Point2d[] { new Point2d(templPrep.Width / 2.0, templPrep.Height / 2.0) };
        var sceneCenter = Cv2.PerspectiveTransform(objCenter, H);
        
        var centerInRoi = sceneCenter[0];
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        
        var objCorners = new Point2d[] {
            new Point2d(0, 0),
            new Point2d(templPrep.Width, 0),
            new Point2d(templPrep.Width, templPrep.Height),
            new Point2d(0, templPrep.Height)
        };
        var sceneCorners = Cv2.PerspectiveTransform(objCorners, H);
        var minX = sceneCorners.Min(p => p.X);
        var maxX = sceneCorners.Max(p => p.X);
        var minY = sceneCorners.Min(p => p.Y);
        var maxY = sceneCorners.Max(p => p.Y);
        
        var matchRect = new Rect((int)(roiRect.X + minX), (int)(roiRect.Y + minY), (int)(maxX - minX), (int)(maxY - minY));
        
        return new MatchResult(global, 1.0, angleDeg, matchRect);
    }
'''

# Add MatchByFeatureBased to PatternMatcher
text = re.sub(r'(private static MatchResult MatchByShapeModel)', feature_based_code + r'\n    \1', text)


target_match = '''        using var roiGray = EnsureGrayBorrowed(roi);
        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);'''
patch_match = '''        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.OriginAlgorithm == OriginAlgorithm.FeatureBased)
        {
            return MatchByFeatureBased(roiGray.Mat, templateGray, definition, 0.0, preprocess, roiRect);
        }
        
        if (definition.OriginAlgorithm == OriginAlgorithm.TemplateMatch)
        {
            using var tPrep = PreprocessTemplateForMatch(templateGray, preprocess);
            var (maxV, maxL) = MatchTemplatePyramid(roiGray.Mat, tPrep, TemplateMatchModes.CCoeffNormed);
            var cInRoi = new Point2d(maxL.X + tPrep.Width / 2.0, maxL.Y + tPrep.Height / 2.0);
            var g = new Point2d(cInRoi.X + roiRect.X, cInRoi.Y + roiRect.Y);
            var mRect = new Rect(roiRect.X + maxL.X, roiRect.Y + maxL.Y, tPrep.Width, tPrep.Height);
            return new MatchResult(g, maxV, 0.0, mRect);
        }

        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);'''
text = text.replace(target_match, patch_match)


target_fixed = '''        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.ShapeModel is not null
            && definition.ShapeModel.TemplateWidth > 0'''
patch_fixed = '''        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.OriginAlgorithm == OriginAlgorithm.FeatureBased)
        {
            return MatchByFeatureBased(roiGray.Mat, templateGray, definition, angleDeg, preprocess, roiRect);
        }

        if (definition.OriginAlgorithm == OriginAlgorithm.TemplateMatch)
        {
            // Fall through to TemplateMatch Pyramid logic below by bypassing ShapeModel
        }
        else if (definition.ShapeModel is not null
            && definition.ShapeModel.TemplateWidth > 0'''
text = text.replace(target_fixed, patch_fixed)


target_rot = '''        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.ShapeModel is not null)'''
patch_rot = '''        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.OriginAlgorithm == OriginAlgorithm.FeatureBased)
        {
            return MatchByFeatureBased(roiGray.Mat, templateGray, definition, 0.0, preprocess, roiRect); // Rotation is implicit in ORB homography
        }

        if (definition.OriginAlgorithm == OriginAlgorithm.TemplateMatch)
        {
            // Fall through to MatchWithRotation using Template Matching Pyramids
        }
        else if (definition.ShapeModel is not null)'''
text = text.replace(target_rot, patch_rot)

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(text)
print("Updated Class1.cs properly")

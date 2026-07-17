import re
import os

path_te_vm = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs'
with open(path_te_vm, 'r', encoding='utf-8') as f:
    text_te = f.read()

target_te = '''            var mr = run.Origin.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;

                dst.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
                dst.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
            }'''

patch_te = '''            var mr = run.Origin.MatchRect;
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
            }'''

text_te = text_te.replace(target_te, patch_te)

# Also format angle
target_te_angle = '''Label = $"Origin ({angle:0.0}°)"'''
patch_te_angle = '''Label = $"Origin ({angle:0.00}°)"'''
text_te = text_te.replace(target_te_angle, patch_te_angle)

with open(path_te_vm, 'w', encoding='utf-8') as f:
    f.write(text_te)

path_insp_vm = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\InspectionViewModel.cs'
with open(path_insp_vm, 'r', encoding='utf-8') as f:
    text_insp = f.read()

target_insp = '''            var mr = LastResult.Origin.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;
                OverlayItems.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = LastResult.Origin.Pass ? Brushes.Lime : Brushes.Red });
                OverlayItems.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = LastResult.Origin.Pass ? Brushes.Lime : Brushes.Red });

                originPos = new Point2d(cx, cy);
            }'''

patch_insp = '''            var mr = LastResult.Origin.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;
                originPos = new Point2d(cx, cy);

                var a = angle * Math.PI / 180.0;
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

                OverlayItems.Add(new OverlayLineItem { X1 = cp1.X, Y1 = cp1.Y, X2 = cp2.X, Y2 = cp2.Y, Stroke = LastResult.Origin.Pass ? Brushes.Lime : Brushes.Red });
                OverlayItems.Add(new OverlayLineItem { X1 = cp3.X, Y1 = cp3.Y, X2 = cp4.X, Y2 = cp4.Y, Stroke = LastResult.Origin.Pass ? Brushes.Lime : Brushes.Red });
            }'''

text_insp = text_insp.replace(target_insp, patch_insp)

# Format angle
target_insp_angle1 = '''Label = $"Origin ({angle:0.0}°)"'''
patch_insp_angle1 = '''Label = $"Origin ({angle:0.00}°)"'''
text_insp = text_insp.replace(target_insp_angle1, patch_insp_angle1)

# In case it has weird characters from previous edits
text_insp = re.sub(r'Label = \$"Origin \(\{angle:0\.0\}[^\"]*\"\)', r'Label = $"Origin ({angle:0.00}°)"', text_insp)

with open(path_insp_vm, 'w', encoding='utf-8') as f:
    f.write(text_insp)

print("ViewModels crosshair fixed")

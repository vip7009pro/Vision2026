import re
import os

path_te_vm = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs'
with open(path_te_vm, 'r', encoding='utf-8') as f:
    text_te = f.read()

target2 = '''    public OriginAlgorithm Origin_Algorithm
    {
        get => _config?.Origin?.OriginAlgorithm ?? OriginAlgorithm.ShapeBased;
        set
        {
            if (_config?.Origin != null)
            {
                _config.Origin.OriginAlgorithm = value;
                OnPropertyChanged();
            }
        }
    }'''

patch2 = '''    public OriginAlgorithm Origin_Algorithm
    {
        get => _config?.Origin?.OriginAlgorithm ?? OriginAlgorithm.ShapeBased;
        set
        {
            if (_config?.Origin != null)
            {
                _config.Origin.OriginAlgorithm = value;
                OnPropertyChanged();
            }
        }
    }

    public double Origin_MinAngle
    {
        get => _config?.Origin?.MinAngle ?? -20.0;
        set
        {
            if (_config?.Origin != null)
            {
                _config.Origin.MinAngle = value;
                OnPropertyChanged();
            }
        }
    }

    public double Origin_MaxAngle
    {
        get => _config?.Origin?.MaxAngle ?? 20.0;
        set
        {
            if (_config?.Origin != null)
            {
                _config.Origin.MaxAngle = value;
                OnPropertyChanged();
            }
        }
    }'''

if 'public double Origin_MinAngle' not in text_te:
    text_te = text_te.replace(target2, patch2)

target_draw = '''        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = stroke, Label = label });
        OverlayItems.Add(new OverlayLineItem { X1 = p2.X, Y1 = p2.Y, X2 = p3.X, Y2 = p3.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p3.X, Y1 = p3.Y, X2 = p4.X, Y2 = p4.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p4.X, Y1 = p4.Y, X2 = p1.X, Y2 = p1.Y, Stroke = stroke });
    }'''

patch_draw = '''        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = stroke, Label = label });
        OverlayItems.Add(new OverlayLineItem { X1 = p2.X, Y1 = p2.Y, X2 = p3.X, Y2 = p3.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p3.X, Y1 = p3.Y, X2 = p4.X, Y2 = p4.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p4.X, Y1 = p4.Y, X2 = p1.X, Y2 = p1.Y, Stroke = stroke });

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);
        var hx = new Point2d(hw * cos, hw * sin);
        var hy = new Point2d(-hh * sin, hh * cos);
        var cp1 = new Point2d(center.X - hx.X, center.Y - hx.Y);
        var cp2 = new Point2d(center.X + hx.X, center.Y + hx.Y);
        var cp3 = new Point2d(center.X - hy.X, center.Y - hy.Y);
        var cp4 = new Point2d(center.X + hy.X, center.Y + hy.Y);
        OverlayItems.Add(new OverlayLineItem { X1 = cp1.X, Y1 = cp1.Y, X2 = cp2.X, Y2 = cp2.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = cp3.X, Y1 = cp3.Y, X2 = cp4.X, Y2 = cp4.Y, Stroke = stroke });
    }'''

def replace_in_template_func(content):
    # Find the function body of AddRotatedTemplateAtPoint and do replacement only inside it
    start_idx = content.find('private void AddRotatedTemplateAtPoint')
    if start_idx == -1: return content
    end_idx = content.find('    }', start_idx) + 5
    body = content[start_idx:end_idx]
    if 'var hx = new Point2d' not in body:
        new_body = body.replace(target_draw, patch_draw)
        content = content[:start_idx] + new_body + content[end_idx:]
    return content

text_te = replace_in_template_func(text_te)
with open(path_te_vm, 'w', encoding='utf-8') as f:
    f.write(text_te)

path_insp_vm = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\InspectionViewModel.cs'
with open(path_insp_vm, 'r', encoding='utf-8') as f:
    text_insp = f.read()

text_insp = replace_in_template_func(text_insp)
with open(path_insp_vm, 'w', encoding='utf-8') as f:
    f.write(text_insp)

print("ViewModels fixed")

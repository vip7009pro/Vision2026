using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.HMI;

public static class HmiVectorAssets
{
    private static readonly ConcurrentDictionary<(HmiControlType, bool, HmiColorTheme), DrawingImage> _drawingCache = new();

    public static DrawingImage GetAssetDrawing(HmiControlType type, bool isOn, HmiColorTheme theme = HmiColorTheme.Green)
    {
        return _drawingCache.GetOrAdd((type, isOn, theme), key =>
        {
            var img = CreateUncachedAssetDrawing(key.Item1, key.Item2, key.Item3);
            if (img.CanFreeze)
            {
                img.Freeze();
            }
            return img;
        });
    }

    private static DrawingImage CreateUncachedAssetDrawing(HmiControlType type, bool isOn, HmiColorTheme theme)
    {
        return type switch
        {
            HmiControlType.Lamp => CreateLampDrawing(isOn, theme),
            HmiControlType.Button => CreateButtonDrawing(isOn, theme),
            HmiControlType.Switch => CreateSwitchDrawing(isOn),
            HmiControlType.ValueDisplay => CreateValueDisplayDrawing(isOn, theme),
            HmiControlType.Label => CreateLabelDrawing(theme),
            HmiControlType.NumericDisplay => CreateValueDisplayDrawing(isOn, theme),
            HmiControlType.Conveyor => CreateConveyorDrawing(isOn),
            HmiControlType.Cylinder => CreateCylinderDrawing(isOn),
            _ => CreateDefaultBoxDrawing(isOn)
        };
    }

    private static DrawingImage CreateValueDisplayDrawing(bool isOn, HmiColorTheme theme)
    {
        var group = new DrawingGroup();

        var bgBrush = new SolidColorBrush(Color.FromRgb(18, 22, 28));
        var borderBrush = new LinearGradientBrush(
            Color.FromRgb(80, 90, 100),
            Color.FromRgb(30, 35, 42),
            new Point(0, 0), new Point(0, 1));

        group.Children.Add(new GeometryDrawing(
            bgBrush,
            new Pen(borderBrush, 2),
            new RectangleGeometry(new Rect(0, 0, 120, 50), 6, 6)));

        Color activeColor = GetThemeColorBright(theme);
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(activeColor), 1),
            new RectangleGeometry(new Rect(3, 3, 114, 44), 4, 4)));

        return new DrawingImage(group);
    }

    private static DrawingImage CreateLabelDrawing(HmiColorTheme theme)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1),
            new RectangleGeometry(new Rect(0, 0, 120, 40), 4, 4)));

        return new DrawingImage(group);
    }

    private static Color GetThemeColor(HmiColorTheme theme)
    {
        return theme switch
        {
            HmiColorTheme.Green => Color.FromRgb(46, 125, 50),
            HmiColorTheme.Red => Color.FromRgb(198, 40, 40),
            HmiColorTheme.Blue => Color.FromRgb(21, 101, 192),
            HmiColorTheme.Amber => Color.FromRgb(245, 124, 0),
            HmiColorTheme.Yellow => Color.FromRgb(251, 192, 45),
            HmiColorTheme.Cyan => Color.FromRgb(0, 151, 167),
            HmiColorTheme.Purple => Color.FromRgb(123, 31, 162),
            HmiColorTheme.Orange => Color.FromRgb(230, 81, 0),
            HmiColorTheme.Magenta => Color.FromRgb(194, 24, 91),
            HmiColorTheme.White => Color.FromRgb(189, 189, 189),
            HmiColorTheme.IndustrialGray => Color.FromRgb(117, 117, 117),
            _ => Color.FromRgb(46, 125, 50)
        };
    }

    private static Color GetThemeColorBright(HmiColorTheme theme)
    {
        return theme switch
        {
            HmiColorTheme.Green => Color.FromRgb(76, 175, 80),
            HmiColorTheme.Red => Color.FromRgb(239, 83, 80),
            HmiColorTheme.Blue => Color.FromRgb(66, 165, 245),
            HmiColorTheme.Amber => Color.FromRgb(255, 183, 77),
            HmiColorTheme.Yellow => Color.FromRgb(255, 238, 88),
            HmiColorTheme.Cyan => Color.FromRgb(38, 198, 218),
            HmiColorTheme.Purple => Color.FromRgb(171, 71, 188),
            HmiColorTheme.Orange => Color.FromRgb(255, 152, 0),
            HmiColorTheme.Magenta => Color.FromRgb(236, 64, 122),
            HmiColorTheme.White => Color.FromRgb(255, 255, 255),
            HmiColorTheme.IndustrialGray => Color.FromRgb(224, 224, 224),
            _ => Color.FromRgb(76, 175, 80)
        };
    }

    // ─── 1. Pilot Lamp Drawing ───
    private static DrawingImage CreateLampDrawing(bool isOn, HmiColorTheme theme)
    {
        var group = new DrawingGroup();

        // Metallic Outer Bezel
        var bezelBrush = new LinearGradientBrush(
            Color.FromRgb(180, 180, 180),
            Color.FromRgb(60, 60, 60),
            new Point(0, 0), new Point(1, 1));
        group.Children.Add(new GeometryDrawing(bezelBrush, new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 2),
            new EllipseGeometry(new Point(50, 50), 45, 45)));

        // Inner Bezel Shadow
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 40, 40)), null,
            new EllipseGeometry(new Point(50, 50), 38, 38)));

        // Lens Surface
        Brush lensBrush;
        if (isOn)
        {
            Color mainCol = GetThemeColorBright(theme);
            Color centerCol = Color.FromRgb(255, 255, 255);
            var rad = new RadialGradientBrush(centerCol, mainCol)
            {
                Center = new Point(0.4, 0.4),
                GradientOrigin = new Point(0.4, 0.4),
                RadiusX = 0.6,
                RadiusY = 0.6
            };
            lensBrush = rad;
        }
        else
        {
            Color darkCol = Color.FromRgb(40, 40, 40);
            Color dimCol = GetThemeColor(theme);
            // Blend dim color with dark gray for off state
            Color offCol = Color.FromRgb((byte)(dimCol.R / 3), (byte)(dimCol.G / 3), (byte)(dimCol.B / 3));
            lensBrush = new LinearGradientBrush(offCol, darkCol, new Point(0.2, 0.2), new Point(0.8, 0.8));
        }

        group.Children.Add(new GeometryDrawing(lensBrush, new Pen(new SolidColorBrush(Color.FromRgb(20, 20, 20)), 1),
            new EllipseGeometry(new Point(50, 50), 35, 35)));

        // Gloss Highlight Curve
        var glassPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 3);
        group.Children.Add(new GeometryDrawing(null, glassPen,
            new EllipseGeometry(new Point(45, 38), 20, 12)));

        return new DrawingImage(group);
    }

    // ─── 2. Industrial Push Button ───
    private static DrawingImage CreateButtonDrawing(bool isOn, HmiColorTheme theme)
    {
        var group = new DrawingGroup();

        // Base Housing Frame
        var baseBrush = new LinearGradientBrush(
            Color.FromRgb(80, 80, 80),
            Color.FromRgb(40, 40, 40),
            90);
        group.Children.Add(new GeometryDrawing(baseBrush, new Pen(new SolidColorBrush(Color.FromRgb(20, 20, 20)), 2),
            new RectangleGeometry(new Rect(5, 5, 90, 90), 8, 8)));

        // Button Cap (Depressed if ON, Raised if OFF)
        Rect capRect = isOn ? new Rect(14, 14, 72, 72) : new Rect(10, 10, 80, 80);
        Color baseCol = GetThemeColor(theme);
        Color brightCol = GetThemeColorBright(theme);

        Brush capBrush;
        if (isOn)
        {
            capBrush = new RadialGradientBrush(Color.FromRgb(255, 255, 255), brightCol)
            {
                Center = new Point(0.5, 0.5),
                RadiusX = 0.7,
                RadiusY = 0.7
            };
        }
        else
        {
            capBrush = new LinearGradientBrush(brightCol, baseCol, 45);
        }

        group.Children.Add(new GeometryDrawing(capBrush, new Pen(new SolidColorBrush(Color.FromRgb(10, 10, 10)), 2),
            new RectangleGeometry(capRect, 6, 6)));

        // Center LED Indicator Strip
        Color ledColor = isOn ? Color.FromRgb(0, 255, 120) : Color.FromRgb(60, 60, 60);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(ledColor), null,
            new RectangleGeometry(new Rect(25, 45, 50, 10), 3, 3)));

        return new DrawingImage(group);
    }

    // ─── 3. Rotary Switch ───
    private static DrawingImage CreateSwitchDrawing(bool isOn)
    {
        var group = new DrawingGroup();

        // Switch Base Plate
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 2),
            new EllipseGeometry(new Point(50, 50), 42, 42)));

        // Position Indicators (OFF / ON text labels)
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(180, 180, 180)), null,
            new EllipseGeometry(new Point(25, 25), 4, 4))); // OFF dot
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(76, 175, 80)), null,
            new EllipseGeometry(new Point(75, 25), 4, 4))); // ON dot

        // Rotary Knob
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 150)), 1.5),
            new EllipseGeometry(new Point(50, 50), 28, 28)));

        // Pointer Lever (Angled Left 45° if OFF, Angled Right 45° if ON)
        Point pStart = new Point(50, 50);
        Point pEnd = isOn ? new Point(72, 28) : new Point(28, 28);
        Color pointerColor = isOn ? Color.FromRgb(76, 175, 80) : Color.FromRgb(220, 220, 220);

        var lineGeo = new LineGeometry(pStart, pEnd);
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(pointerColor), 6) { EndLineCap = PenLineCap.Round }, lineGeo));

        return new DrawingImage(group);
    }

    // ─── 4. Conveyor Belt ───
    private static DrawingImage CreateConveyorDrawing(bool isOn)
    {
        var group = new DrawingGroup();

        // Main Frame
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 2),
            new RectangleGeometry(new Rect(5, 20, 190, 40), 10, 10)));

        // Rollers (Left & Right)
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 120, 120)), null,
            new EllipseGeometry(new Point(25, 40), 15, 15)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 120, 120)), null,
            new EllipseGeometry(new Point(175, 40), 15, 15)));

        // Belt Track
        Color beltCol = isOn ? Color.FromRgb(0, 150, 136) : Color.FromRgb(80, 80, 80);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(beltCol), null,
            new RectangleGeometry(new Rect(25, 25, 150, 30), 4, 4)));

        // Direction Arrows / Motion Slats
        Color arrowCol = isOn ? Color.FromRgb(255, 255, 255) : Color.FromRgb(120, 120, 120);
        for (int x = 40; x <= 160; x += 30)
        {
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(arrowCol), null,
                new RectangleGeometry(new Rect(x, 35, 12, 10), 2, 2)));
        }

        // Status Indicator Lamp on Conveyor
        Color statusCol = isOn ? Color.FromRgb(76, 175, 80) : Color.FromRgb(158, 158, 158);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(statusCol), null,
            new EllipseGeometry(new Point(100, 12), 6, 6)));

        return new DrawingImage(group);
    }

    // ─── 5. Pneumatic Cylinder ───
    private static DrawingImage CreateCylinderDrawing(bool isOn)
    {
        var group = new DrawingGroup();

        // Cylinder Body Barrel
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            new Pen(new SolidColorBrush(Color.FromRgb(140, 140, 140)), 2),
            new RectangleGeometry(new Rect(10, 20, 90, 40), 4, 4)));

        // Front End Cap
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 100, 100)), null,
            new RectangleGeometry(new Rect(100, 15, 15, 50), 2, 2)));

        // Piston Rod (Retracted = 30px extended, Extended = 70px extended)
        double rodLength = isOn ? 70 : 25;
        var rodBrush = new LinearGradientBrush(Color.FromRgb(230, 230, 230), Color.FromRgb(130, 130, 130), 90);
        group.Children.Add(new GeometryDrawing(rodBrush, new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 1),
            new RectangleGeometry(new Rect(115, 33, rodLength, 14), 2, 2)));

        // Piston Tip
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 50, 50)), null,
            new RectangleGeometry(new Rect(115 + rodLength, 28, 12, 24), 2, 2)));

        // Position Sensors (Retracted vs Extended)
        Color retSensorCol = isOn ? Color.FromRgb(100, 100, 100) : Color.FromRgb(255, 152, 0);
        Color extSensorCol = isOn ? Color.FromRgb(76, 175, 80) : Color.FromRgb(100, 100, 100);

        group.Children.Add(new GeometryDrawing(new SolidColorBrush(retSensorCol), null,
            new RectangleGeometry(new Rect(20, 8, 12, 10), 2, 2)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(extSensorCol), null,
            new RectangleGeometry(new Rect(80, 8, 12, 10), 2, 2)));

        return new DrawingImage(group);
    }

    private static DrawingImage CreateDefaultBoxDrawing(bool isOn)
    {
        var group = new DrawingGroup();
        Color col = isOn ? Color.FromRgb(76, 175, 80) : Color.FromRgb(100, 100, 100);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(col), new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 2),
            new RectangleGeometry(new Rect(5, 5, 90, 90), 8, 8)));
        return new DrawingImage(group);
    }
}

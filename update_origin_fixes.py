import re
import os

# 1. Update Models/Class1.cs
path_models = r'g:\NODEJS\Vision2026\VisionInspectionApp.Models\Class1.cs'
with open(path_models, 'r', encoding='utf-8') as f:
    text = f.read()

target1 = '''    public OriginAlgorithm OriginAlgorithm { get; set; } = OriginAlgorithm.ShapeBased;'''
patch1 = '''    public OriginAlgorithm OriginAlgorithm { get; set; } = OriginAlgorithm.ShapeBased;

    public double MinAngle { get; set; } = -20.0;
    public double MaxAngle { get; set; } = 20.0;'''

if 'public double MinAngle' not in text:
    text = text.replace(target1, patch1)
    with open(path_models, 'w', encoding='utf-8') as f:
        f.write(text)

# 2. Update ToolEditorViewModel.cs
path_te_vm = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs'
with open(path_te_vm, 'r', encoding='utf-8') as f:
    text = f.read()

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

if 'public double Origin_MinAngle' not in text:
    text = text.replace(target2, patch2)

target2_draw = '''        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = stroke, Label = label });
        OverlayItems.Add(new OverlayLineItem { X1 = p2.X, Y1 = p2.Y, X2 = p3.X, Y2 = p3.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p3.X, Y1 = p3.Y, X2 = p4.X, Y2 = p4.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p4.X, Y1 = p4.Y, X2 = p1.X, Y2 = p1.Y, Stroke = stroke });
    }'''

patch2_draw = '''        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = stroke, Label = label });
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

text = text.replace(target2_draw, patch2_draw)
with open(path_te_vm, 'w', encoding='utf-8') as f:
    f.write(text)

# 3. Update InspectionViewModel.cs
path_insp_vm = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\InspectionViewModel.cs'
with open(path_insp_vm, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace(target2_draw, patch2_draw)
with open(path_insp_vm, 'w', encoding='utf-8') as f:
    f.write(text)

# 4. Update ToolEditorView.xaml
path_te_xaml = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml'
with open(path_te_xaml, 'r', encoding='utf-8') as f:
    text = f.read()

target4 = '''                                        <Border Grid.Column="1" Background="{DynamicResource PanelBackgroundBrush}">
                                            <ComboBox ItemsSource="{Binding AvailableOriginAlgorithms}" SelectedItem="{Binding Origin_Algorithm, UpdateSourceTrigger=PropertyChanged}" Margin="4" />
                                        </Border>
                                    </Grid>
                                </Border>
                            </StackPanel>'''

patch4 = '''                                        <Border Grid.Column="1" Background="{DynamicResource PanelBackgroundBrush}">
                                            <ComboBox ItemsSource="{Binding AvailableOriginAlgorithms}" SelectedItem="{Binding Origin_Algorithm, UpdateSourceTrigger=PropertyChanged}" Margin="4" />
                                        </Border>
                                    </Grid>
                                </Border>
                                <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="120" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <Border Background="{DynamicResource WindowBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,1,0">
                                            <TextBlock Text="Min Angle (deg)" VerticalAlignment="Center" Margin="8,4" Foreground="{DynamicResource TextBrush}"/>
                                        </Border>
                                        <Border Grid.Column="1" Background="{DynamicResource PanelBackgroundBrush}">
                                            <TextBox Text="{Binding Origin_MinAngle, UpdateSourceTrigger=PropertyChanged, Delay=250}" Margin="4" />
                                        </Border>
                                    </Grid>
                                </Border>
                                <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="120" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <Border Background="{DynamicResource WindowBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,1,0">
                                            <TextBlock Text="Max Angle (deg)" VerticalAlignment="Center" Margin="8,4" Foreground="{DynamicResource TextBrush}"/>
                                        </Border>
                                        <Border Grid.Column="1" Background="{DynamicResource PanelBackgroundBrush}">
                                            <TextBox Text="{Binding Origin_MaxAngle, UpdateSourceTrigger=PropertyChanged, Delay=250}" Margin="4" />
                                        </Border>
                                    </Grid>
                                </Border>
                            </StackPanel>'''

if 'Origin_MinAngle' not in text:
    text = text.replace(target4, patch4)
    with open(path_te_xaml, 'w', encoding='utf-8') as f:
        f.write(text)

# 5. Update VisionEngine\Class1.cs
path_vision = r'g:\NODEJS\Vision2026\VisionInspectionApp.VisionEngine\Class1.cs'
with open(path_vision, 'r', encoding='utf-8') as f:
    text = f.read()

target5 = '''        var sceneCorners = Cv2.PerspectiveTransform(objCorners, H);
        var minX = sceneCorners.Min(p => p.X);
        var maxX = sceneCorners.Max(p => p.X);
        var minY = sceneCorners.Min(p => p.Y);
        var maxY = sceneCorners.Max(p => p.Y);
        
        var matchRect = new Rect((int)(roiRect.X + minX), (int)(roiRect.Y + minY), (int)(maxX - minX), (int)(maxY - minY));
        
        return new MatchResult(global, 1.0, angleDeg, matchRect);'''

patch5 = '''        var sceneCorners = Cv2.PerspectiveTransform(objCorners, H);
        var minX = sceneCorners.Min(p => p.X);
        var maxX = sceneCorners.Max(p => p.X);
        var minY = sceneCorners.Min(p => p.Y);
        var maxY = sceneCorners.Max(p => p.Y);
        
        var matchRect = new Rect((int)(roiRect.X + minX), (int)(roiRect.Y + minY), (int)(maxX - minX), (int)(maxY - minY));
        
        var h11 = H.At<double>(0, 0);
        var h21 = H.At<double>(1, 0);
        var actualAngleDeg = Math.Atan2(h21, h11) * 180.0 / Math.PI;
        
        return new MatchResult(global, 1.0, actualAngleDeg, matchRect);'''

if 'Math.Atan2(h21, h11)' not in text:
    text = text.replace(target5, patch5)
    with open(path_vision, 'w', encoding='utf-8') as f:
        f.write(text)

# 6. Update Application\Class1.cs
path_app = r'g:\NODEJS\Vision2026\VisionInspectionApp.Application\Class1.cs'
with open(path_app, 'r', encoding='utf-8') as f:
    text = f.read()

target6a = '''var originMatch = _matcher.MatchWithRotation(originMat, originDef, originTempl, originPre, -60.0, 60.0, 2.0);'''
patch6a = '''var originMatch = _matcher.MatchWithRotation(originMat, originDef, originTempl, originPre, originDef.MinAngle, originDef.MaxAngle, 2.0);'''

target6b = '''var retry = _matcher.MatchWithRotation(originMat, originDefBase, originTempl, originPre, -60.0, 60.0, 2.0);'''
patch6b = '''var retry = _matcher.MatchWithRotation(originMat, originDefBase, originTempl, originPre, originDefBase.MinAngle, originDefBase.MaxAngle, 2.0);'''

text = text.replace(target6a, patch6a)
text = text.replace(target6b, patch6b)

with open(path_app, 'w', encoding='utf-8') as f:
    f.write(text)

print("Done")

import re
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Replace ToolboxList to add icons and new mouse down event
old_listbox = r'<ListBox Grid\.Row="1" x:Name="ToolboxList".*?</ListBox>'
new_listbox = '''<ListBox Grid.Row="1" x:Name="ToolboxList" ItemsSource="{Binding ToolboxItems}" 
                                 PreviewMouseLeftButtonDown="ToolboxList_PreviewMouseLeftButtonDown" 
                                 PreviewMouseMove="ToolboxList_PreviewMouseMove" 
                                 BorderThickness="0" Background="Transparent">
                            <ListBox.ItemTemplate>
                                <DataTemplate>
                                    <StackPanel Orientation="Horizontal" Margin="2,6">
                                        <TextBlock x:Name="IconTxt" Text="&#x1F527;" Margin="0,0,8,0" FontFamily="Segoe UI Emoji" FontSize="14" VerticalAlignment="Center" />
                                        <TextBlock Text="{Binding}" FontWeight="Medium" Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" />
                                    </StackPanel>
                                    <DataTemplate.Triggers>
                                        <DataTrigger Binding="{Binding}" Value="Preprocess">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x2699;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Origin">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F3AF;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Point">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F4CD;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Line">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F4CF;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Caliper">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F4D0;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Distance">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x2194;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="LineLineDistance">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x23F8;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="PointLineDistance">
                                            <Setter TargetName="IconTxt" Property=\"Text\" Value=\"&#x23EF;\" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Angle">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x2220;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Diameter">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x2B55;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="EdgePairDetect">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x23F8;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="LinePairDetection">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x23F8;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="BlobDetection">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F9A0;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="CodeDetection">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F532;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="CircleFinder">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F518;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Condition">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x2753;" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding}" Value="Text">
                                            <Setter TargetName="IconTxt" Property="Text" Value="&#x1F524;" />
                                        </DataTrigger>
                                    </DataTemplate.Triggers>
                                </DataTemplate>
                            </ListBox.ItemTemplate>
                        </ListBox>'''

text = re.sub(old_listbox, new_listbox, text, flags=re.DOTALL)

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml', 'w', encoding='utf-8') as f:
    f.write(text)

# Now update code-behind to fix DragDrop logic
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml.cs', 'r', encoding='utf-8') as f:
    cs_text = f.read()

old_cs = '''    private void ToolboxList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ScrollBar) return;
            d = VisualTreeHelper.GetParent(d);
        }

        if (sender is not ListBox lb)
        {
            return;
        }

        if (lb.SelectedItem is not string type || string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        DragDrop.DoDragDrop(lb, new DataObject(DataFormats.StringFormat, type), DragDropEffects.Copy);
    }'''

new_cs = '''    private System.Windows.Point _dragStartPoint;

    private void ToolboxList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void ToolboxList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var diff = _dragStartPoint - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ScrollBar) return;
            d = VisualTreeHelper.GetParent(d);
        }

        if (sender is not ListBox lb)
        {
            return;
        }

        if (lb.SelectedItem is not string type || string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        DragDrop.DoDragDrop(lb, new DataObject(DataFormats.StringFormat, type), DragDropEffects.Copy);
    }'''

cs_text = cs_text.replace(old_cs, new_cs)

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs_text)

print('Toolbox updated')

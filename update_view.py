import re

filepath = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml'
with open(filepath, 'r', encoding='utf-8') as f:
    text = f.read()

origin_panel = '''
                            <StackPanel Visibility="{Binding IsOriginNode, Converter={StaticResource BoolToVis}}">
                                <Border Background="{DynamicResource BorderBrush}" Padding="4,2" Margin="0,8,0,0">
                                    <TextBlock Text="Origin Params" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" />
                                </Border>

                                <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="120" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <Border Background="{DynamicResource WindowBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,1,0">
                                            <TextBlock Text="Algorithm" VerticalAlignment="Center" Margin="8,4" Foreground="{DynamicResource TextBrush}"/>
                                        </Border>
                                        <Border Grid.Column="1" Background="{DynamicResource PanelBackgroundBrush}">
                                            <ComboBox ItemsSource="{Binding AvailableOriginAlgorithms}" SelectedItem="{Binding Origin_Algorithm, UpdateSourceTrigger=PropertyChanged}" Margin="4" />
                                        </Border>
                                    </Grid>
                                </Border>
                            </StackPanel>
'''

text = re.sub(r'(<StackPanel Visibility="\{Binding IsPointNode)', origin_panel + r'\1', text, count=1)

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(text)
print("Updated ToolEditorView.xaml")

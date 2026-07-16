import re
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<TextBlock x:Name="IconTxt" Text="&#x1F527;" Margin="0,0,8,0" FontFamily="Segoe UI Emoji" FontSize="14" VerticalAlignment="Center" />',
                    '<TextBlock x:Name="IconTxt" Text="&#x1F527;" Margin="0,0,8,0" FontFamily="Segoe UI Emoji" FontSize="14" VerticalAlignment="Center" Foreground="{DynamicResource TextBrush}" />')

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml', 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed icon foreground')

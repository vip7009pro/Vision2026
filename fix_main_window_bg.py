import re
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\MainWindow.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = re.sub(r'<Style TargetType="TextBox">([\s\S]*?)<Setter Property="Background" Value="White" />',
              r'<Style TargetType="TextBox">\g<1><Setter Property="Background" Value="{DynamicResource InputBackgroundBrush}" />', text)

text = re.sub(r'<Style TargetType="ComboBox">([\s\S]*?)<Setter Property="Background" Value="White" />',
              r'<Style TargetType="ComboBox">\g<1><Setter Property="Background" Value="{DynamicResource InputBackgroundBrush}" />', text)

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(text)
print('Styles updated in MainWindow')

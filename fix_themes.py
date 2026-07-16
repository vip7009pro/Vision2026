import re
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\MainWindow.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Fix TextBox in MainWindow
text = re.sub(r'<Style TargetType="TextBox">\s*<Setter Property="BorderBrush" Value="\{DynamicResource BorderBrush\}" />\s*<Setter Property="Background" Value="White" />\s*</Style>',
              '<Style TargetType="TextBox">\n            <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />\n            <Setter Property="Background" Value="{DynamicResource InputBackgroundBrush}" />\n            <Setter Property="Foreground" Value="{DynamicResource InputTextBrush}" />\n        </Style>', text)

# Fix ComboBox in MainWindow
text = re.sub(r'<Style TargetType="ComboBox">\s*<Setter Property="BorderBrush" Value="\{DynamicResource BorderBrush\}" />\s*<Setter Property="Background" Value="White" />\s*</Style>',
              '<Style TargetType="ComboBox">\n            <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />\n            <Setter Property="Background" Value="{DynamicResource InputBackgroundBrush}" />\n            <Setter Property="Foreground" Value="{DynamicResource InputTextBrush}" />\n        </Style>', text)

# Fix Button in MainWindow - remove hardcoded Foreground="White"
text = text.replace('<Setter Property="Foreground" Value="White" />', '<Setter Property="Foreground" Value="{DynamicResource TextBrush}" />')

# Let's also ensure TabItem uses dynamic resources properly
# The TabItem ControlTemplate has Background="{TemplateBinding Background}"
# Let's just make sure TabControl uses dynamic resources
text = text.replace('<Style TargetType="TabItem">', '<Style TargetType="TabItem">\n            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />\n            <Setter Property="Background" Value="{DynamicResource PanelAltBackgroundBrush}" />')

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(text)

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\App.xaml', 'r', encoding='utf-8') as f:
    app_text = f.read()

if '<Style TargetType="CheckBox">' not in app_text:
    app_text = app_text.replace('</ResourceDictionary>', '    <Style TargetType="CheckBox">\n                <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>\n            </Style>\n        </ResourceDictionary>')
    with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\App.xaml', 'w', encoding='utf-8') as f:
        f.write(app_text)

print('Themes fixed')

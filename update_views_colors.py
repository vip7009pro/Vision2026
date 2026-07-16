import os
import re

directory = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views'

replacements = {
    r'Background="#F0F0F0"': 'Background="{DynamicResource WindowBackgroundBrush}"',
    r'Background="#F5F5F5"': 'Background="{DynamicResource PanelAltBackgroundBrush}"',
    r'Background="#F9F9F9"': 'Background="{DynamicResource PanelBackgroundBrush}"',
    r'Background="#FAFAFA"': 'Background="{DynamicResource PanelBackgroundBrush}"',
    r'Background="#E0E0E0"': 'Background="{DynamicResource BorderBrush}"',
    r'BorderBrush="#CCC"': 'BorderBrush="{DynamicResource BorderBrush}"',
    r'BorderBrush="#DDD"': 'BorderBrush="{DynamicResource BorderBrush}"',
    r'Foreground="#333"': 'Foreground="{DynamicResource TextBrush}"',
    r'Foreground="Black"': 'Foreground="{DynamicResource TextBrush}"',
    r'Background="White"': 'Background="{DynamicResource WindowBackgroundBrush}"',
    # Keep button custom background colors like #4CAF50 but change Foreground from "White" to "{DynamicResource TextBrush}" if needed?
    # Actually, green button with white text is fine in dark mode!
}

for filename in os.listdir(directory):
    if filename.endswith(".xaml"):
        filepath = os.path.join(directory, filename)
        with open(filepath, 'r', encoding='utf-8') as f:
            text = f.read()
            
        original_text = text
        for old, new in replacements.items():
            text = text.replace(old, new)
            
        if text != original_text:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(text)
            print(f"Updated {filename}")

print("All views updated.")

import re

filepath = r'g:\NODEJS\Vision2026\CONTEXT.md'
with open(filepath, 'r', encoding='utf-8') as f:
    text = f.read()

new_section = '''### 6. C# UTF-8 BOM Encoding Fix
- Converted all `.cs` and `.xaml` files across the codebase to use **UTF-8 with BOM** instead of plain UTF-8.
- This prevents the Roslyn compiler from falling back to local Windows code pages (like Windows-1258/ANSI) and resolves UI bugs where Vietnamese strings (e.g. in `InspectionViewModel.cs` Message Boxes, or `Text` tool placeholders) displayed as corrupted characters at runtime.

'''

text = text.replace('## Remaining Roadmap', new_section + '## Remaining Roadmap')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(text)
print("Updated CONTEXT.md")

import re

filepath = r'g:\NODEJS\Vision2026\CONTEXT.md'
with open(filepath, 'r', encoding='utf-8') as f:
    text = f.read()

new_section = '''### 5. Enhancements to Origin Tool
- **Angle Constraints**: Added `MinAngle` and `MaxAngle` bounds to `PointDefinition`. This allows the user to limit the search space strictly to a customized angular range (e.g., -20° to 20° or 0° to 360°) via the Tool Editor properties panel.
- **Rotation Fixes**: Extracted exact orientation angles from the Homography matrix (`Cv2.FindHomography`) when using `FeatureBased` matching. Enhanced UI rendering in `ToolEditorViewModel` and `InspectionViewModel` to properly draw rotated bounding box center lines (crosshairs) that rotate alongside the matched template correctly.

'''

text = text.replace('## Remaining Roadmap', new_section + '## Remaining Roadmap')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(text)
print("Updated CONTEXT.md")

import re

filepath = r'g:\NODEJS\Vision2026\CONTEXT.md'
with open(filepath, 'r', encoding='utf-8') as f:
    text = f.read()

new_section = '''### 4. Advanced Vision Engine Algorithms (Origin Tool)
- **Origin Tool Tech Expansion**: Upgraded the `Origin` tool to match industrial vision platforms (Cognex, Halcon, Hikvision). Users can now select the core matching algorithm directly in the Properties Panel.
- **Algorithms Supported**:
  - `ShapeBased` (GPM - Default): Edge-based generalized hough transform for finding shape outlines, highly robust to lighting variation.
  - `TemplateMatch` (NCC): Fast normalized cross-correlation for pixel-perfect texture matching.
  - `FeatureBased` (ORB/RANSAC): New algorithm implemented to extract scale & rotation invariant keypoints. Uses `BFMatcher` and Homography (`RANSAC`) to find complex, textured, or heavily warped objects reliably.

'''

text = text.replace('## Remaining Roadmap', new_section + '## Remaining Roadmap')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(text)
print("Updated CONTEXT.md")

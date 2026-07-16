import re
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

replacements = {
    '"InputP1"': '"P1"',
    '"InputP2"': '"P2"',
    '"InputL1"': '"L1"',
    '"InputL2"': '"L2"',
    '"InputP"': '"P1"',
    '"InputL"': '"L1"',
    '"InputC"': '"C1"',
    '"InputE1"': '"E1"',
    '"InputE2"': '"E2"',
    '"InImg"': '"Image"',
    '"Pre"': '"Preprocess"',
    '"OutLLD"': '"Distance"',
    '"OutPLD"': '"Distance"',
    '"OutEP"': '"EdgePair"',
    '"OutLP"': '"LinePair"',
    '"OutDist"': '"Distance"',
    '"OutCond"': '"Result"',
    '"OutPre"': '"Image"',
    '"OutBlob"': '"Blobs"',
    '"OutCode"': '"Code"',
    '"OutCircle"': '"Circle"',
    '"OutLine"': '"Line"',
    '"OutPoint"': '"Point"',
    '"OutCaliper"': '"Width"'
}

for old, new in replacements.items():
    text = text.replace(old, new)

with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
print('Replacements done.')

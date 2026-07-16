import re

filepath = r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs'
with open(filepath, 'r', encoding='utf-8') as f:
    text = f.read()

# Add AvailableOriginAlgorithms
available_prop = '''
    public ObservableCollection<OriginAlgorithm> AvailableOriginAlgorithms { get; }
        = new ObservableCollection<OriginAlgorithm>((OriginAlgorithm[])Enum.GetValues(typeof(OriginAlgorithm)));
'''
text = re.sub(r'(\s*public ObservableCollection<PointFindAlgorithm> AvailablePointFindAlgorithms)', 
              available_prop + r'\1', text, count=1)

# Add IsOriginNode property
is_origin_node_prop = '''
    public bool IsOriginNode => SelectedNode != null && string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase);
'''
text = re.sub(r'(\s*public bool IsPointNode)', is_origin_node_prop + r'\1', text, count=1)

# Add Origin_Algorithm property
origin_alg_prop = '''
    public OriginAlgorithm Origin_Algorithm
    {
        get => _config?.Origin?.OriginAlgorithm ?? OriginAlgorithm.ShapeBased;
        set
        {
            if (_config?.Origin != null)
            {
                _config.Origin.OriginAlgorithm = value;
                OnPropertyChanged();
            }
        }
    }
'''
text = re.sub(r'(\s*public PointFindAlgorithm Point_Algorithm)', origin_alg_prop + r'\1', text, count=1)

# Add OnPropertyChanged in RefreshPropertiesPanel
refresh_props = '''
        OnPropertyChanged(nameof(IsOriginNode));
        OnPropertyChanged(nameof(AvailableOriginAlgorithms));
        OnPropertyChanged(nameof(Origin_Algorithm));
'''
text = re.sub(r'(\s*OnPropertyChanged\(nameof\(IsPointNode\)\);)', refresh_props + r'\1', text, count=1)

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(text)
print("Updated ToolEditorViewModel.cs")

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        public bool IsImageOutputNode => string.Equals(SelectedNode?.Type, "ImageOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode?.Type, "OutputImage", StringComparison.OrdinalIgnoreCase);

        public IEnumerable<ImageOutputFormat> AvailableImageOutputFormats => Enum.GetValues<ImageOutputFormat>();
        public IEnumerable<ImageOutputCondition> AvailableImageOutputConditions => Enum.GetValues<ImageOutputCondition>();

        public List<string> AvailableImageNodes
        {
            get
            {
                var list = new List<string> { "Default (Current Image)" };
                if (Nodes != null)
                {
                    list.AddRange(Nodes.Where(n => n != SelectedNode && !string.IsNullOrWhiteSpace(n.RefName)).Select(n => n.RefName));
                }
                return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private ImageOutputDefinition? SelectedImageOutputDef()
        {
            if (_config is null || SelectedNode is null || !IsImageOutputNode)
                return null;
            _config.ImageOutputs ??= new List<ImageOutputDefinition>();
            var def = _config.ImageOutputs.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is null)
            {
                def = new ImageOutputDefinition { Name = SelectedNode.RefName };
                _config.ImageOutputs.Add(def);
            }
            return def;
        }

        public string ImageOutput_InputNodeChoice
        {
            get
            {
                var defName = SelectedImageOutputDef()?.InputNodeName;
                return string.IsNullOrWhiteSpace(defName) ? "Default (Current Image)" : defName;
            }
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null) return;
                var val = string.Equals(value, "Default (Current Image)", StringComparison.OrdinalIgnoreCase) ? string.Empty : (value ?? string.Empty);
                if (def.InputNodeName == val) return;
                def.InputNodeName = val;
                OnPropertyChanged();

                if (SelectedNode is not null)
                {
                    for (var i = Edges.Count - 1; i >= 0; i--)
                    {
                        var e = Edges[i];
                        if (string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            Edges.RemoveAt(i);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        var fromNode = Nodes.FirstOrDefault(n => string.Equals(n.RefName, val, StringComparison.OrdinalIgnoreCase));
                        if (fromNode is not null)
                        {
                            fromNode.EnsurePortsInitialized();
                            SelectedNode.EnsurePortsInitialized();
                            var fromPort = fromNode.OutPorts.FirstOrDefault()?.Name ?? "Out";
                            var toPort = SelectedNode.InPorts.FirstOrDefault()?.Name ?? "In";
                            Edges.Add(new ToolGraphEdgeViewModel(fromNode, SelectedNode, fromPort, toPort));
                        }
                    }

                    SyncEdgesToConfig();
                    RefreshPreviews();
                }

                IsDirty = true;
                RequestAutoSave();
            }
        }

        public string ImageOutput_SaveFolderPath
        {
            get => SelectedImageOutputDef()?.SaveFolderPath ?? @"C:\VisionOutput";
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.SaveFolderPath == value) return;
                def.SaveFolderPath = value ?? @"C:\VisionOutput";
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public string ImageOutput_FileNameFormat
        {
            get => SelectedImageOutputDef()?.FileNameFormat ?? "IMG_{ProductName}_{YYYY}{MM}{DD}_{HH}{mm}{ss}_{Count}";
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.FileNameFormat == value) return;
                def.FileNameFormat = value ?? "IMG_{ProductName}_{YYYY}{MM}{DD}_{HH}{mm}{ss}_{Count}";
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public ImageOutputFormat ImageOutput_Format
        {
            get => SelectedImageOutputDef()?.Format ?? ImageOutputFormat.JPG;
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.Format == value) return;
                def.Format = value;
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public bool ImageOutput_IncludeOverlay
        {
            get => SelectedImageOutputDef()?.IncludeOverlay ?? true;
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.IncludeOverlay == value) return;
                def.IncludeOverlay = value;
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public bool ImageOutput_ShowRoi
        {
            get => SelectedImageOutputDef()?.ShowRoi ?? true;
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.ShowRoi == value) return;
                def.ShowRoi = value;
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public bool ImageOutput_EnableOutput
        {
            get => SelectedImageOutputDef()?.EnableOutput ?? true;
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.EnableOutput == value) return;
                def.EnableOutput = value;
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public int ImageOutput_TextFontSize
        {
            get => SelectedImageOutputDef()?.TextFontSize ?? 18;
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.TextFontSize == value) return;
                def.TextFontSize = Math.Clamp(value, 8, 96);
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public double ImageOutput_OverlayScale
        {
            get => SelectedImageOutputDef()?.OverlayScale ?? 1.0;
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || Math.Abs(def.OverlayScale - value) < 0.001) return;
                def.OverlayScale = Math.Clamp(value, 0.2, 5.0);
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        public ImageOutputCondition ImageOutput_SaveCondition
        {
            get => SelectedImageOutputDef()?.SaveCondition ?? ImageOutputCondition.Always;
            set
            {
                var def = SelectedImageOutputDef();
                if (def is null || def.SaveCondition == value) return;
                def.SaveCondition = value;
                OnPropertyChanged();
                IsDirty = true;
                RequestAutoSave();
            }
        }

        private ICommand? _browseImageOutputFolderCommand;
        public ICommand BrowseImageOutputFolderCommand => _browseImageOutputFolderCommand ??= new RelayCommand(BrowseImageOutputFolder);

        private void BrowseImageOutputFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Image Output Folder",
                InitialDirectory = Directory.Exists(ImageOutput_SaveFolderPath) ? ImageOutput_SaveFolderPath : @"C:\"
            };

            if (dialog.ShowDialog() == true)
            {
                ImageOutput_SaveFolderPath = dialog.FolderName;
            }
        }
    }
}

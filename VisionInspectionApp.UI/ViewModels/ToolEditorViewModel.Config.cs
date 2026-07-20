using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.VisionEngine;
namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        private void RequestAutoSave()
        {
            _autoSavePending = true;
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    
        private void AutoSaveNow()
        {
            _autoSaveTimer.Stop();
            if (!_autoSavePending)
            {
                return;
            }
    
            _autoSavePending = false;
            if (_config is null)
            {
                return;
            }
    
            if (string.IsNullOrWhiteSpace(ProductCode))
            {
                return;
            }
    
            _config.ProductCode = ProductCode;
            _configService.SaveConfig(_config);
            EnsureTemplatePathsAbsolute(_config);
        }
    
        public ObservableCollection<string> AvailableConfigs { get; }
    
        [ObservableProperty]
        private string? _selectedConfig;
        partial void OnSelectedConfigChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ProductCode = value;
                LoadConfig(); // Auto-load when config is selected
            }
            else
            {
                ClearActiveGraph();
            }
    
            RefreshPreviews();
        }
    
        public ICommand RefreshConfigsCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand SaveConfigCommand { get; }
    
        private void SyncEdgesToConfig()
        {
            if (_config?.ToolGraph is null)
            {
                return;
            }
    
            _config.ToolGraph.Edges = Edges.Select(e => new ToolGraphEdge { FromNodeId = e.FromNodeId, ToNodeId = e.ToNodeId, FromPort = e.FromPort, ToPort = e.ToPort }).ToList();
        }
    
        private void RefreshConfigs()
        {
            AvailableConfigs.Clear();
            try
            {
                var configRoot = _storeOptions.ConfigRootDirectory;
                if (Directory.Exists(configRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(configRoot, "*.json"))
                    {
                        var productCode = Path.GetFileNameWithoutExtension(file);
                        if (!string.IsNullOrEmpty(productCode))
                        {
                            AvailableConfigs.Add(productCode);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lß╗ùi load config: {ex.Message}");
            }
    
            // ?m b?o ban d?u khng ch?n b?t k? c?u hnh no (t?t c? r?ng)
            SelectedConfig = null;
            ProductCode = "";
            ClearActiveGraph();
        }
    
        private void LoadConfig()
        {
            var code = SelectedConfig ?? ProductCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                ClearActiveGraph();
                return;
            }
    
            // D?n s?ch c?u hnh cu kh?i b? nh? tru?c khi n?p c?u hnh m?i
            ClearActiveGraph();
            try
            {
                _config = _configService.LoadConfig(code);
                ProductCode = _config.ProductCode;
                Nodes.Clear();
                Edges.Clear();
                foreach (var n in _config.ToolGraph.Nodes)
                {
                    var vm = new ToolGraphNodeViewModel
                    {
                        Id = n.Id,
                        Type = n.Type,
                        RefName = n.RefName,
                        X = n.X,
                        Y = n.Y,
                        InputCount = n.InputCount
                    };
                    vm.PropertyChanged += Node_PropertyChanged;
                    Nodes.Add(vm);
                }
    
                foreach (var e in _config.ToolGraph.Edges)
                {
                    var from = Nodes.FirstOrDefault(x => string.Equals(x.Id, e.FromNodeId, StringComparison.OrdinalIgnoreCase));
                    var to = Nodes.FirstOrDefault(x => string.Equals(x.Id, e.ToNodeId, StringComparison.OrdinalIgnoreCase));
                    if (from is null || to is null)
                    {
                        continue;
                    }
    
                    Edges.Add(new ToolGraphEdgeViewModel(from, to, e.FromPort, e.ToPort));
                }
    
                SelectedNode = Nodes.Count > 0 ? Nodes[0] : null;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lß╗ùi load config: {ex.Message}");
                ClearActiveGraph();
            }
        }
    
        private void SaveConfig()
        {
            var code = SelectedConfig ?? ProductCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }
    
            // Brand-new products may not have a json yet.
            _config ??= new VisionConfig
            {
                ProductCode = code
            };
            _config.ProductCode = ProductCode;
            SyncToolGraphToConfig();
            _configService.SaveConfig(_config);
            RefreshPreviews();
        }
    
        private void SyncToolGraphToConfig()
        {
            if (_config?.ToolGraph is null)
            {
                return;
            }
    
            // D?n d?p cc config m? ci (khng cn node tuong ?ng trong graph)
            var validRefNames = new HashSet<string>(Nodes.Select(n => n.RefName).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            _config.PreprocessNodes.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Points.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Lines.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Calipers.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Distances.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.LineToLineDistances.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.PointToLineDistances.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Angles.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Conditions.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.TextNodes.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ImageSources.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.BlobDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.SurfaceCompares.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.LinePairDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.EdgePairs.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.EdgePairDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CircleFinders.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Diameters.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CodeDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ToolGraph.Nodes.Clear();
            foreach (var n in Nodes)
            {
                _config.ToolGraph.Nodes.Add(new ToolGraphNode { Id = n.Id, Type = n.Type, RefName = n.RefName, X = n.X, Y = n.Y, InputCount = n.InputCount });
            }
    
            _config.ToolGraph.Edges.Clear();
            foreach (var e in Edges)
            {
                _config.ToolGraph.Edges.Add(new ToolGraphEdge { FromNodeId = e.FromNodeId, ToNodeId = e.ToNodeId, FromPort = e.FromPort, ToPort = e.ToPort });
            }
        }
    }
}

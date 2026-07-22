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
    
            if (!string.IsNullOrWhiteSpace(CurrentJobFilePath) && !string.IsNullOrWhiteSpace(CurrentTempWorkingDir))
            {
                SyncToolGraphToConfig();
                _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
            }
        }
    
        [ObservableProperty]
        private string? _currentJobFilePath;

        [ObservableProperty]
        private string? _currentTempWorkingDir;

        public ICommand OpenJobCommand { get; }
        public ICommand SaveJobCommand { get; }
        public ICommand SaveJobAsCommand { get; }
    
        public void CloseJob()
        {
            Nodes.Clear();
            Edges.Clear();
            _config = null;
            CurrentJobFilePath = null;
            CurrentTempWorkingDir = null;
            ProductCode = string.Empty;
            _lastRun = null;
            _lastRunError = null;
            SelectedNode = null;
            FinalOverlayItems = new List<OverlayItem>();
            SelectedNodeOverlayItems = new List<OverlayItem>();
            SelectedNodePreviewImage = null;
            FinalPreviewImage = null;
            _cachedFinalPreviewImage = null;
            LinePreviewImage = null;
            PointEdgePreviewImage = null;
            BlobThresholdPreviewImage = null;
            Origin_TemplatePreviewImage = null;

            foreach (var kv in _imageSourcePreviewCache.Values)
            {
                kv.Image?.Dispose();
            }
            _imageSourcePreviewCache.Clear();

            _sharedImage.SetImage(null);

            LastResult = null;
            SpecResults?.Clear();
            ToolTimings?.Clear();
            CodeDetectionResults?.Clear();
            SurfaceCompareDebugItems?.Clear();
            DebugTemplate = null;
            DebugCurrent = null;
            DebugBinary = null;
            DebugDiff = null;

            RefreshPreviews();
            IsDirty = false;
        }

        partial void OnIsDirtyChanged(bool value)
        {
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                var title = System.Windows.Application.Current.MainWindow.Title;
                if (value && !title.EndsWith("*"))
                {
                    System.Windows.Application.Current.MainWindow.Title = title + "*";
                }
                else if (!value && title.EndsWith("*"))
                {
                    System.Windows.Application.Current.MainWindow.Title = title.TrimEnd('*');
                }
            }
        }
    
        private void SyncEdgesToConfig()
        {
            if (_config?.ToolGraph is null)
            {
                return;
            }
    
            _config.ToolGraph.Edges = Edges.Select(e => new ToolGraphEdge { FromNodeId = e.FromNodeId, ToNodeId = e.ToNodeId, FromPort = e.FromPort, ToPort = e.ToPort }).ToList();
        }
    
        private void OpenJob()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Job Files (*.job)|*.job|All Files (*.*)|*.*",
                Title = "Open Vision Job"
            };

            if (dialog.ShowDialog() == true)
            {
                ClearActiveGraph();
                try
                {
                    _config = _jobService.LoadJob(dialog.FileName, out var tempDir);
                    CurrentJobFilePath = dialog.FileName;
                    CurrentTempWorkingDir = tempDir;
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
                    IsDirty = false;
                    if (System.Windows.Application.Current?.MainWindow != null)
                    {
                        System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - " + Path.GetFileName(CurrentJobFilePath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load job: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ClearActiveGraph();
                }
            }
        }
    
        private void SaveJob()
        {
            if (string.IsNullOrWhiteSpace(CurrentJobFilePath))
            {
                SaveJobAs();
                return;
            }

            if (_config is null)
            {
                _config = new VisionConfig { ProductCode = ProductCode };
            }

            if (string.IsNullOrWhiteSpace(CurrentTempWorkingDir))
            {
                CurrentTempWorkingDir = Path.Combine(Path.GetTempPath(), "Vision2026", "Jobs", Guid.NewGuid().ToString());
                Directory.CreateDirectory(CurrentTempWorkingDir);
            }

            _config.ProductCode = ProductCode;
            SyncToolGraphToConfig();
            
            try
            {
                _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
                IsDirty = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save job: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RefreshPreviews();
        }

        private void SaveJobAs()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Job Files (*.job)|*.job|All Files (*.*)|*.*",
                Title = "Save Vision Job As"
            };

            if (dialog.ShowDialog() == true)
            {
                CurrentJobFilePath = dialog.FileName;
                System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - " + Path.GetFileName(CurrentJobFilePath);
                if (string.IsNullOrWhiteSpace(CurrentTempWorkingDir))
                {
                    CurrentTempWorkingDir = Path.Combine(Path.GetTempPath(), "Vision2026", "Jobs", Guid.NewGuid().ToString());
                    Directory.CreateDirectory(CurrentTempWorkingDir);
                }
                SaveJob();
            }
        }
    
        private void SyncToolGraphToConfig()
        {
            if (_config?.ToolGraph is null)
            {
                return;
            }
    
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

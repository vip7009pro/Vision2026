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
    public sealed partial class ToolGraphNodeViewModel : ObservableObject
    {
        [ObservableProperty]
        private int? _executionTimeMs;
        // Keep this aligned with ToolEditorView.xaml node template:
        // Border Padding=8, header StackPanel with 3 lines + Margin bottom=6.
        private const double NodeHeaderHeight = 60.0;
        private const double PortItemHeight = 20.0;
        private const double NodeBottomPadding = 16.0;
        [ObservableProperty]
        private string _id = string.Empty;
        [ObservableProperty]
        private string _type = string.Empty;
        partial void OnTypeChanged(string value)
        {
            RebuildPorts();
            OnPropertyChanged(nameof(NodeHeight));
        }
    
        [ObservableProperty]
        private string _refName = string.Empty;
        [ObservableProperty]
        private double _x;
        [ObservableProperty]
        private double _y;
        [ObservableProperty]
        private int _inputCount = 1;
        [ObservableProperty]
        private bool _isSelected;
        partial void OnInputCountChanged(int value)
        {
            RebuildPorts();
            OnPropertyChanged(nameof(NodeHeight));
        }
    
        public ObservableCollection<NodePortViewModel> InPorts { get; } = new();
        public ObservableCollection<NodePortViewModel> OutPorts { get; } = new();
    
        public double NodeHeight
        {
            get
            {
                var count = Math.Max(1, InPorts.Count);
                // Header + ports (top-aligned) + bottom padding.
                return Math.Max(100, NodeHeaderHeight + count * PortItemHeight + NodeBottomPadding);
            }
        }
    
        public int GetInPortIndex(string portName)
        {
            for (var i = 0; i < InPorts.Count; i++)
            {
                if (string.Equals(InPorts[i].Name, portName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
    
            return 0;
        }
    
        public int GetOutPortIndex(string portName)
        {
            for (var i = 0; i < OutPorts.Count; i++)
            {
                if (string.Equals(OutPorts[i].Name, portName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
    
            return 0;
        }
    
        public double GetInPortCenterY(string portName)
        {
            EnsurePortsInitialized();
            var idx = GetInPortIndex(portName);
            return NodeHeaderHeight + idx * PortItemHeight + PortItemHeight / 2.0;
        }
    
        public double GetOutPortCenterY(string portName)
        {
            EnsurePortsInitialized();
            var idx = GetOutPortIndex(portName);
            return NodeHeaderHeight + idx * PortItemHeight + PortItemHeight / 2.0;
        }
    
        public void EnsurePortsInitialized()
        {
            if (InPorts.Count == 0 && OutPorts.Count == 0)
            {
                RebuildPorts();
            }
        }
    
        private void RebuildPorts()
        {
            InPorts.Clear();
            OutPorts.Clear();
            // Output port based on node type
            string outName = $"Out";
            if (string.Equals(Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
                outName = "Distance";
            else if (string.Equals(Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
                outName = "Distance";
            else if (string.Equals(Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
                outName = "EdgePair";
            else if (string.Equals(Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
                outName = "LinePair";
            else if (string.Equals(Type, "Distance", StringComparison.OrdinalIgnoreCase))
                outName = "Distance";
            else if (string.Equals(Type, "Condition", StringComparison.OrdinalIgnoreCase))
                outName = "Result";
            else if (string.Equals(Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                outName = "Image";
            else if (string.Equals(Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                outName = "Image";
            else if (string.Equals(Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
                outName = "Blobs";
            else if (string.Equals(Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
                outName = "Code";
            else if (string.Equals(Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
                outName = "Circle";
            else
                outName = $"Out{Type}";
            OutPorts.Add(new NodePortViewModel(this, outName, isInput: false));
            if (string.Equals(Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
            {
            // ImageSource has no input ports - it's a source
            }
            else if (string.Equals(Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "Image", isInput: true));
            }
            else if (string.Equals(Type, "ResultView", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "Result", isInput: true));
            }
            else if (string.Equals(Type, "Origin", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "Point", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "Caliper", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "BlobDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))

            {
                InPorts.Add(new NodePortViewModel(this, "Image", isInput: true));
            }
            else if (string.Equals(Type, "Diameter", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "C1", isInput: true));
            }
            else if (string.Equals(Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "P1", isInput: true));
                InPorts.Add(new NodePortViewModel(this, "P2", isInput: true));
            }
            else if (string.Equals(Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "L1", isInput: true));
                InPorts.Add(new NodePortViewModel(this, "L2", isInput: true));
            }
            else if (string.Equals(Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "L1", isInput: true));
                InPorts.Add(new NodePortViewModel(this, "L2", isInput: true));
            }
            else if (string.Equals(Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "E1", isInput: true));
                InPorts.Add(new NodePortViewModel(this, "E2", isInput: true));
                InPorts.Add(new NodePortViewModel(this, "Preprocess", isInput: true));
            }
            else if (string.Equals(Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                InPorts.Add(new NodePortViewModel(this, "P1", isInput: true));
                InPorts.Add(new NodePortViewModel(this, "L1", isInput: true));
            }
            else if (string.Equals(Type, "Condition", StringComparison.OrdinalIgnoreCase))
            {
                var n = Math.Clamp(InputCount, 1, 16);
                for (var i = 1; i <= n; i++)
                {
                    InPorts.Add(new NodePortViewModel(this, $"Input{i}", isInput: true));
                }
            }
            else
            {
                InPorts.Add(new NodePortViewModel(this, "Image", isInput: true));
            }
        }
    }
}

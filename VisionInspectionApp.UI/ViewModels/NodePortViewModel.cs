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
    public sealed class NodePortViewModel
    {
        public NodePortViewModel(ToolGraphNodeViewModel node, string name, bool isInput)
        {
            Node = node;
            Name = name;
            IsInput = isInput;
        }
    
        public ToolGraphNodeViewModel Node { get; }
        public string Name { get; }
        public bool IsInput { get; }
        public string Tag => IsInput ? $"InPort:{Name}" : $"OutPort:{Name}";
    }
}

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
    public sealed partial class TextColorConditionRow : ObservableObject
    {
        private readonly Action _onChanged;
        public TextColorConditionDefinition Model { get; }
    
        [ObservableProperty]
        private string _expression;
        [ObservableProperty]
        private string _color;
        public TextColorConditionRow(TextColorConditionDefinition model, Action onChanged)
        {
            Model = model;
            _onChanged = onChanged;
            _expression = model.Expression ?? string.Empty;
            _color = model.Color ?? "#FF00FF00";
        }
    
        partial void OnExpressionChanged(string value)
        {
            Model.Expression = value ?? string.Empty;
            _onChanged();
        }
    
        partial void OnColorChanged(string value)
        {
            Model.Color = value ?? "#FF00FF00";
            _onChanged();
        }
    }
}

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
        public ObservableCollection<TextColorConditionRow> TextNode_ConditionRows { get; }
        public ICommand TextNode_AddConditionCommand { get; }
        public ICommand TextNode_RemoveConditionCommand { get; }
        public ICommand TextNode_PickDefaultColorCommand { get; }
        public ICommand TextNode_PickConditionColorCommand { get; }
    
        private void TextNode_AddCondition()
        {
            var def = SelectedTextNodeDef();
            if (def is null)
            {
                return;
            }
    
            def.Conditions ??= new();
            var c = new TextColorConditionDefinition
            {
                Expression = string.Empty,
                Color = "#FF00FF00"
            };
            def.Conditions.Add(c);
            TextNode_ConditionRows.Add(new TextColorConditionRow(c, OnTextNodeConditionEdited));
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    
        private void TextNode_RemoveCondition(TextColorConditionRow? row)
        {
            if (row is null)
            {
                return;
            }
    
            var def = SelectedTextNodeDef();
            if (def is null || def.Conditions is null)
            {
                return;
            }
    
            def.Conditions.Remove(row.Model);
            TextNode_ConditionRows.Remove(row);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    
        private TextNodeDefinition? SelectedTextNodeDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Text", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.TextNodes.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public string TextNode_Text
        {
            get => SelectedTextNodeDef()?.Text ?? string.Empty;
            set
            {
                var def = SelectedTextNodeDef();
                if (def is null)
                    return;
                value ??= string.Empty;
                if (string.Equals(def.Text, value, StringComparison.Ordinal))
                    return;
                def.Text = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public int TextNode_X
        {
            get => SelectedTextNodeDef()?.X ?? 0;
            set
            {
                var def = SelectedTextNodeDef();
                if (def is null)
                    return;
                if (def.X == value)
                    return;
                def.X = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public int TextNode_Y
        {
            get => SelectedTextNodeDef()?.Y ?? 0;
            set
            {
                var def = SelectedTextNodeDef();
                if (def is null)
                    return;
                if (def.Y == value)
                    return;
                def.Y = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string TextNode_DefaultColor
        {
            get => SelectedTextNodeDef()?.DefaultColor ?? "#FFFFFFFF";
            set
            {
                var def = SelectedTextNodeDef();
                if (def is null)
                    return;
                value ??= "#FFFFFFFF";
                if (string.Equals(def.DefaultColor, value, StringComparison.Ordinal))
                    return;
                def.DefaultColor = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        private void TextNode_PickDefaultColor()
        {
            using var dlg = new System.Windows.Forms.ColorDialog();
            if (TryParseHexBrush(TextNode_DefaultColor)is System.Windows.Media.SolidColorBrush scb)
            {
                dlg.Color = System.Drawing.Color.FromArgb(scb.Color.A, scb.Color.R, scb.Color.G, scb.Color.B);
            }
            else
            {
                dlg.Color = System.Drawing.Color.White;
            }
    
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TextNode_DefaultColor = $"#{dlg.Color.A:X2}{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            }
        }
    
        private void TextNode_PickConditionColor(TextColorConditionRow? row)
        {
            if (row is null)
                return;
            using var dlg = new System.Windows.Forms.ColorDialog();
            if (TryParseHexBrush(row.Color)is System.Windows.Media.SolidColorBrush scb)
            {
                dlg.Color = System.Drawing.Color.FromArgb(scb.Color.A, scb.Color.R, scb.Color.G, scb.Color.B);
            }
            else
            {
                dlg.Color = System.Drawing.Color.White;
            }
    
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                row.Color = $"#{dlg.Color.A:X2}{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    }
}

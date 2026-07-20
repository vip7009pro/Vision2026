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
        public string? Distance_PointA
        {
            get => SelectedDistanceDef()?.PointA;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedDistanceDef();
                if (def is null)
                    return;
                if (string.Equals(def.PointA ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.PointA = value ?? string.Empty;
                SyncInputEdgeForDistancePort("P1", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string? Distance_PointB
        {
            get => SelectedDistanceDef()?.PointB;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedDistanceDef();
                if (def is null)
                    return;
                if (string.Equals(def.PointB ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.PointB = value ?? string.Empty;
                SyncInputEdgeForDistancePort("P2", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string? LineLineDistance_LineA
        {
            get => SelectedLineLineDistanceDef()?.LineA;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedLineLineDistanceDef();
                if (def is null)
                    return;
                if (string.Equals(def.LineA ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.LineA = value ?? string.Empty;
                SyncInputEdgeForLineLineDistancePort("L1", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string? LineLineDistance_LineB
        {
            get => SelectedLineLineDistanceDef()?.LineB;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedLineLineDistanceDef();
                if (def is null)
                    return;
                if (string.Equals(def.LineB ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.LineB = value ?? string.Empty;
                SyncInputEdgeForLineLineDistancePort("L2", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string? PointLineDistance_Point
        {
            get => SelectedPointLineDistanceDef()?.Point;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedPointLineDistanceDef();
                if (def is null)
                    return;
                if (string.Equals(def.Point ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.Point = value ?? string.Empty;
                SyncInputEdgeForPointLineDistancePort("P1", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string? PointLineDistance_Line
        {
            get => SelectedPointLineDistanceDef()?.Line;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedPointLineDistanceDef();
                if (def is null)
                    return;
                if (string.Equals(def.Line ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.Line = value ?? string.Empty;
                SyncInputEdgeForPointLineDistancePort("L1", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public LineLineDistanceMode LineLineDistance_Mode
        {
            get => SelectedLineLineDistanceDef()?.Mode ?? LineLineDistanceMode.ClosestPointsOnSegments;
            set
            {
                var def = SelectedLineLineDistanceDef();
                if (def is null)
                    return;
                if (def.Mode == value)
                    return;
                def.Mode = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public PointLineDistanceMode PointLineDistance_Mode
        {
            get => SelectedPointLineDistanceDef()?.Mode ?? PointLineDistanceMode.PointToSegment;
            set
            {
                var def = SelectedPointLineDistanceDef();
                if (def is null)
                    return;
                if (def.Mode == value)
                    return;
                def.Mode = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        private LineDistance? SelectedDistanceDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.Distances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        private LineToLineDistance? SelectedLineLineDistanceDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        private PointToLineDistance? SelectedPointLineDistanceDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public double Distance_Nominal
        {
            get
            {
                if (SelectedDistanceDef()is { } d)
                    return d.Nominal;
                if (SelectedLineLineDistanceDef()is { } ll)
                    return ll.Nominal;
                if (SelectedPointLineDistanceDef()is { } pl)
                    return pl.Nominal;
                if (SelectedAngleDef()is { } a)
                    return a.Nominal;
                if (SelectedLinePairDef()is { } lpd)
                    return lpd.Nominal;
                if (SelectedEdgePairDef()is { } ep)
                    return ep.Nominal;
                if (SelectedEdgePairDetectDef()is { } epd)
                    return epd.Nominal;
                if (SelectedDiameterDef()is { } dia)
                    return dia.Nominal;
                return 0.0;
            }
    
            set
            {
                if (SelectedDistanceDef()is { } d)
                {
                    if (Math.Abs(d.Nominal - value) < 0.0000001)
                        return;
                    d.Nominal = value;
                }
                else if (SelectedLineLineDistanceDef()is { } ll)
                {
                    if (Math.Abs(ll.Nominal - value) < 0.0000001)
                        return;
                    ll.Nominal = value;
                }
                else if (SelectedPointLineDistanceDef()is { } pl)
                {
                    if (Math.Abs(pl.Nominal - value) < 0.0000001)
                        return;
                    pl.Nominal = value;
                }
                else if (SelectedAngleDef()is { } a)
                {
                    if (Math.Abs(a.Nominal - value) < 0.0000001)
                        return;
                    a.Nominal = value;
                }
                else if (SelectedLinePairDef()is { } lpd)
                {
                    if (Math.Abs(lpd.Nominal - value) < 0.0000001)
                        return;
                    lpd.Nominal = value;
                }
                else if (SelectedEdgePairDef()is { } ep)
                {
                    if (Math.Abs(ep.Nominal - value) < 0.0000001)
                        return;
                    ep.Nominal = value;
                }
                else if (SelectedEdgePairDetectDef()is { } epd)
                {
                    if (Math.Abs(epd.Nominal - value) < 0.0000001)
                        return;
                    epd.Nominal = value;
                }
                else if (SelectedDiameterDef()is { } dia)
                {
                    if (Math.Abs(dia.Nominal - value) < 0.0000001)
                        return;
                    dia.Nominal = value;
                }
                else
                {
                    return;
                }
    
                RequestSpecEditPreviewRefresh();
                OnPropertyChanged();
            }
        }
    
        public double Distance_TolPlus
        {
            get
            {
                if (SelectedDistanceDef()is { } d)
                    return d.TolerancePlus;
                if (SelectedLineLineDistanceDef()is { } ll)
                    return ll.TolerancePlus;
                if (SelectedPointLineDistanceDef()is { } pl)
                    return pl.TolerancePlus;
                if (SelectedAngleDef()is { } a)
                    return a.TolerancePlus;
                if (SelectedLinePairDef()is { } lpd)
                    return lpd.TolerancePlus;
                if (SelectedEdgePairDef()is { } ep)
                    return ep.TolerancePlus;
                if (SelectedEdgePairDetectDef()is { } epd)
                    return epd.TolerancePlus;
                if (SelectedDiameterDef()is { } dia)
                    return dia.TolerancePlus;
                return 0.0;
            }
    
            set
            {
                if (SelectedDistanceDef()is { } d)
                {
                    if (Math.Abs(d.TolerancePlus - value) < 0.0000001)
                        return;
                    d.TolerancePlus = value;
                }
                else if (SelectedLineLineDistanceDef()is { } ll)
                {
                    if (Math.Abs(ll.TolerancePlus - value) < 0.0000001)
                        return;
                    ll.TolerancePlus = value;
                }
                else if (SelectedPointLineDistanceDef()is { } pl)
                {
                    if (Math.Abs(pl.TolerancePlus - value) < 0.0000001)
                        return;
                    pl.TolerancePlus = value;
                }
                else if (SelectedAngleDef()is { } a)
                {
                    if (Math.Abs(a.TolerancePlus - value) < 0.0000001)
                        return;
                    a.TolerancePlus = value;
                }
                else if (SelectedLinePairDef()is { } lpd)
                {
                    if (Math.Abs(lpd.TolerancePlus - value) < 0.0000001)
                        return;
                    lpd.TolerancePlus = value;
                }
                else if (SelectedEdgePairDef()is { } ep)
                {
                    if (Math.Abs(ep.TolerancePlus - value) < 0.0000001)
                        return;
                    ep.TolerancePlus = value;
                }
                else if (SelectedEdgePairDetectDef()is { } epd)
                {
                    if (Math.Abs(epd.TolerancePlus - value) < 0.0000001)
                        return;
                    epd.TolerancePlus = value;
                }
                else if (SelectedDiameterDef()is { } dia)
                {
                    if (Math.Abs(dia.TolerancePlus - value) < 0.0000001)
                        return;
                    dia.TolerancePlus = value;
                }
                else
                {
                    return;
                }
    
                RequestSpecEditPreviewRefresh();
                OnPropertyChanged();
            }
        }
    
        public double Distance_TolMinus
        {
            get
            {
                if (SelectedDistanceDef()is { } d)
                    return d.ToleranceMinus;
                if (SelectedLineLineDistanceDef()is { } ll)
                    return ll.ToleranceMinus;
                if (SelectedPointLineDistanceDef()is { } pl)
                    return pl.ToleranceMinus;
                if (SelectedAngleDef()is { } a)
                    return a.ToleranceMinus;
                if (SelectedLinePairDef()is { } lpd)
                    return lpd.ToleranceMinus;
                if (SelectedEdgePairDef()is { } ep)
                    return ep.ToleranceMinus;
                if (SelectedEdgePairDetectDef()is { } epd)
                    return epd.ToleranceMinus;
                if (SelectedDiameterDef()is { } dia)
                    return dia.ToleranceMinus;
                return 0.0;
            }
    
            set
            {
                if (SelectedDistanceDef()is { } d)
                {
                    if (Math.Abs(d.ToleranceMinus - value) < 0.0000001)
                        return;
                    d.ToleranceMinus = value;
                }
                else if (SelectedLineLineDistanceDef()is { } ll)
                {
                    if (Math.Abs(ll.ToleranceMinus - value) < 0.0000001)
                        return;
                    ll.ToleranceMinus = value;
                }
                else if (SelectedPointLineDistanceDef()is { } pl)
                {
                    if (Math.Abs(pl.ToleranceMinus - value) < 0.0000001)
                        return;
                    pl.ToleranceMinus = value;
                }
                else if (SelectedAngleDef()is { } a)
                {
                    if (Math.Abs(a.ToleranceMinus - value) < 0.0000001)
                        return;
                    a.ToleranceMinus = value;
                }
                else if (SelectedLinePairDef()is { } lpd)
                {
                    if (Math.Abs(lpd.ToleranceMinus - value) < 0.0000001)
                        return;
                    lpd.ToleranceMinus = value;
                }
                else if (SelectedEdgePairDef()is { } ep)
                {
                    if (Math.Abs(ep.ToleranceMinus - value) < 0.0000001)
                        return;
                    ep.ToleranceMinus = value;
                }
                else if (SelectedEdgePairDetectDef()is { } epd)
                {
                    if (Math.Abs(epd.ToleranceMinus - value) < 0.0000001)
                        return;
                    epd.ToleranceMinus = value;
                }
                else if (SelectedDiameterDef()is { } dia)
                {
                    if (Math.Abs(dia.ToleranceMinus - value) < 0.0000001)
                        return;
                    dia.ToleranceMinus = value;
                }
                else
                {
                    return;
                }
    
                RequestSpecEditPreviewRefresh();
                OnPropertyChanged();
            }
        }
    }
}

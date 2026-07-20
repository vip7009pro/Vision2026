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
    public sealed class ToolGraphEdgeViewModel : ObservableObject
    {
        private const double NodeWidth = 160.0;
        private const double PortOverhang = 6.0;
        private const double RouteClearance = 32.0;
        private readonly ToolGraphNodeViewModel _from;
        private readonly ToolGraphNodeViewModel _to;
        public ToolGraphEdgeViewModel(ToolGraphNodeViewModel from, ToolGraphNodeViewModel to, string fromPort, string toPort)
        {
            _from = from;
            _to = to;
            FromPort = fromPort;
            ToPort = toPort;
        }
    
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
        public string FromNodeId => _from.Id;
        public string ToNodeId => _to.Id;
        public string FromPort { get; }
        public string ToPort { get; }
    
        public Geometry PathData
        {
            get
            {
                var p1 = GetFromPortPosition();
                var p2 = GetToPortPosition();
                var fig = new PathFigure
                {
                    StartPoint = p1,
                    IsClosed = false,
                    IsFilled = false
                };
                if (p2.X >= p1.X)
                {
                    // Normal left-to-right connection.
                    var midX = (p1.X + p2.X) * 0.5;
                    fig.Segments.Add(new LineSegment(new System.Windows.Point(midX, p1.Y), true));
                    fig.Segments.Add(new LineSegment(new System.Windows.Point(midX, p2.Y), true));
                    fig.Segments.Add(new LineSegment(p2, true));
                }
                else
                {
                    // A destination to the left used to force the wire back through the
                    // source node.  Exit to the right, then use the outer top lane
                    // before approaching the target from its left side.
                    var exit = new System.Windows.Point(p1.X + RouteClearance, p1.Y);
                    var approach = new System.Windows.Point(p2.X - RouteClearance, p2.Y);
                    // Keep reverse edges in the top lane.  A bottom lane adds an
                    // unnecessary U-turn and makes these connections hard to follow.
                    var routeY = Math.Min(_from.Y, _to.Y) - RouteClearance;
                    fig.Segments.Add(new LineSegment(exit, true));
                    fig.Segments.Add(new LineSegment(new System.Windows.Point(exit.X, routeY), true));
                    fig.Segments.Add(new LineSegment(new System.Windows.Point(approach.X, routeY), true));
                    fig.Segments.Add(new LineSegment(approach, true));
                    fig.Segments.Add(new LineSegment(p2, true));
                }
    
                return new PathGeometry(new[] { fig });
            }
        }
    
        public void NotifyGeometryChanged()
        {
            OnPropertyChanged(nameof(PathData));
        }
    
        private System.Windows.Point GetFromPortPosition()
        {
            _from.EnsurePortsInitialized();
            var cy = _from.GetOutPortCenterY(FromPort);
            // Out port ellipse is pushed outwards by Margin="0,0,-6,0" in XAML.
            return new System.Windows.Point(_from.X + NodeWidth + PortOverhang, _from.Y + cy);
        }
    
        private System.Windows.Point GetToPortPosition()
        {
            _to.EnsurePortsInitialized();
            var cy = _to.GetInPortCenterY(ToPort);
            return new System.Windows.Point(_to.X, _to.Y + cy);
        }
    }
}

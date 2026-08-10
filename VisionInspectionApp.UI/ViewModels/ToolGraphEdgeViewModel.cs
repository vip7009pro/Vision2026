using System;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed class ToolGraphEdgeViewModel : ObservableObject
    {
        private const double NodeWidth = 160.0;
        private const double PortOverhang = 6.0;
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

        private enum AnchorDir { Left, Right, Top, Bottom }

        public Geometry PathData
        {
            get
            {
                var (p1, dir1) = GetFromAnchor();
                var (p2, dir2) = GetToAnchor();

                var fig = new PathFigure
                {
                    StartPoint = p1,
                    IsClosed = false,
                    IsFilled = false
                };

                double dx = Math.Abs(p2.X - p1.X);
                double dy = Math.Abs(p2.Y - p1.Y);
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double smoothness = Math.Max(25.0, Math.Min(dist * 0.4, 140.0));

                Point cp1 = dir1 switch
                {
                    AnchorDir.Right => new Point(p1.X + smoothness, p1.Y),
                    AnchorDir.Left => new Point(p1.X - smoothness, p1.Y),
                    AnchorDir.Bottom => new Point(p1.X, p1.Y + smoothness),
                    AnchorDir.Top => new Point(p1.X, p1.Y - smoothness),
                    _ => new Point(p1.X + smoothness, p1.Y)
                };

                Point cp2 = dir2 switch
                {
                    AnchorDir.Right => new Point(p2.X + smoothness, p2.Y),
                    AnchorDir.Left => new Point(p2.X - smoothness, p2.Y),
                    AnchorDir.Bottom => new Point(p2.X, p2.Y + smoothness),
                    AnchorDir.Top => new Point(p2.X, p2.Y - smoothness),
                    _ => new Point(p2.X - smoothness, p2.Y)
                };

                fig.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
                return new PathGeometry(new[] { fig });
            }
        }

        public void NotifyGeometryChanged()
        {
            OnPropertyChanged(nameof(PathData));
        }

        private (Point Point, AnchorDir Dir) GetFromAnchor()
        {
            _from.EnsurePortsInitialized();
            double wA = NodeWidth;
            double hA = _from.NodeHeight > 0 ? _from.NodeHeight : 60.0;

            _to.EnsurePortsInitialized();
            double wB = NodeWidth;
            double hB = _to.NodeHeight > 0 ? _to.NodeHeight : 60.0;

            double cxA = _from.X + wA / 2.0;
            double cyA = _from.Y + hA / 2.0;
            double cxB = _to.X + wB / 2.0;
            double cyB = _to.Y + hB / 2.0;

            double dx = cxB - cxA;
            double dy = cyB - cyA;

            if (dy > Math.Abs(dx) * 0.7) // Target is Below Source
            {
                return (new Point(_from.X + wA / 2.0, _from.Y + hA), AnchorDir.Bottom);
            }
            else if (-dy > Math.Abs(dx) * 0.7) // Target is Above Source
            {
                return (new Point(_from.X + wA / 2.0, _from.Y), AnchorDir.Top);
            }
            else if (dx < -Math.Abs(dy) * 0.7) // Target is to the Left of Source
            {
                var outY = _from.GetOutPortCenterY(FromPort);
                return (new Point(_from.X - PortOverhang, _from.Y + outY), AnchorDir.Left);
            }
            else // Target is to the Right of Source (Default)
            {
                var outY = _from.GetOutPortCenterY(FromPort);
                return (new Point(_from.X + wA + PortOverhang, _from.Y + outY), AnchorDir.Right);
            }
        }

        private (Point Point, AnchorDir Dir) GetToAnchor()
        {
            _from.EnsurePortsInitialized();
            double wA = NodeWidth;
            double hA = _from.NodeHeight > 0 ? _from.NodeHeight : 60.0;

            _to.EnsurePortsInitialized();
            double wB = NodeWidth;
            double hB = _to.NodeHeight > 0 ? _to.NodeHeight : 60.0;

            double cxA = _from.X + wA / 2.0;
            double cyA = _from.Y + hA / 2.0;
            double cxB = _to.X + wB / 2.0;
            double cyB = _to.Y + hB / 2.0;

            double dx = cxB - cxA;
            double dy = cyB - cyA;

            if (dy > Math.Abs(dx) * 0.7) // Target is Below Source
            {
                return (new Point(_to.X + wB / 2.0, _to.Y), AnchorDir.Top);
            }
            else if (-dy > Math.Abs(dx) * 0.7) // Target is Above Source
            {
                return (new Point(_to.X + wB / 2.0, _to.Y + hB), AnchorDir.Bottom);
            }
            else if (dx < -Math.Abs(dy) * 0.7) // Target is to the Left of Source
            {
                var inY = _to.GetInPortCenterY(ToPort);
                return (new Point(_to.X + wB + PortOverhang, _to.Y + inY), AnchorDir.Right);
            }
            else // Target is to the Right of Source (Default)
            {
                var inY = _to.GetInPortCenterY(ToPort);
                return (new Point(_to.X - PortOverhang, _to.Y + inY), AnchorDir.Left);
            }
        }
    }
}

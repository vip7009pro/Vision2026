using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class ToolEditorView : UserControl
{
    private bool _isWiring;
    private ToolGraphNodeViewModel? _wireFrom;
    private string _wireFromPort = "Out";
    private Point _wireCurrent;

    public ToolEditorView()
    {
        InitializeComponent();

        AddHandler(Thumb.MouseLeftButtonDownEvent, new MouseButtonEventHandler(AnyNode_MouseLeftButtonDown), true);
    }

    private void AnyNode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        var d = e.OriginalSource as DependencyObject;
        while (d is not null)
        {
            if (d is Thumb t && t.DataContext is ToolGraphNodeViewModel n)
            {
                vm.SelectedNode = n;
                return;
            }

            d = VisualTreeHelper.GetParent(d);
        }
    }

    private void ToolboxList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is not ListBox lb)
        {
            return;
        }

        if (lb.SelectedItem is not string type || string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        DragDrop.DoDragDrop(lb, new DataObject(DataFormats.StringFormat, type), DragDropEffects.Copy);
    }

    private void EditorCanvas_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.StringFormat) is not string type || string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        var pos = e.GetPosition(EditorCanvas);
        vm.AddNode(type, pos);
    }

    private void NodeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb t)
        {
            return;
        }

        if (t.DataContext is not ToolGraphNodeViewModel n)
        {
            return;
        }

        n.X += e.HorizontalChange;
        n.Y += e.VerticalChange;
    }

    private void NodeThumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        if (sender is not Thumb t)
        {
            return;
        }

        if (t.DataContext is ToolGraphNodeViewModel n)
        {
            vm.SelectedNode = n;
            e.Handled = true;
        }
    }

    private void OutPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        if (sender is not FrameworkElement fe)
        {
            return;
        }

        // The ellipse DataContext is NodePortViewModel (from ItemsControl).
        if (fe.DataContext is not NodePortViewModel port)
        {
            return;
        }

        var n = port.Node;

        vm.SelectedNode = n;

        _isWiring = true;
        _wireFrom = n;
        _wireFromPort = port.Name;
        _wireCurrent = e.GetPosition(EditorCanvas);

        WirePreviewPath.Visibility = Visibility.Visible;
        UpdateWirePreview(GetOutPortPosition(n, _wireFromPort), _wireCurrent);

        EditorCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void EditorCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isWiring || _wireFrom is null)
        {
            return;
        }

        _wireCurrent = e.GetPosition(EditorCanvas);
        UpdateWirePreview(GetOutPortPosition(_wireFrom, _wireFromPort), _wireCurrent);
    }

    private void EditorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isWiring)
        {
            return;
        }

        if (DataContext is not ToolEditorViewModel vm)
        {
            CancelWiring();
            return;
        }

        var hit = FindHitInPort(e.GetPosition(EditorCanvas));
        if (hit is not null && _wireFrom is not null)
        {
            var (node, portName) = hit.Value;
            vm.CreateEdge(_wireFrom, node, fromPort: _wireFromPort, toPort: portName);
        }

        CancelWiring();
        e.Handled = true;
    }

    private void CancelWiring()
    {
        _isWiring = false;
        _wireFrom = null;
        WirePreviewPath.Data = null;
        WirePreviewPath.Visibility = Visibility.Collapsed;
        EditorCanvas.ReleaseMouseCapture();
    }

    private (ToolGraphNodeViewModel Node, string PortName)? FindHitInPort(Point canvasPoint)
    {
        // When mouse is captured by canvas, the port control won't receive MouseUp.
        // We solve it by hit-testing the visual tree under the cursor.
        var result = VisualTreeHelper.HitTest(EditorCanvas, canvasPoint);
        var d = result?.VisualHit as DependencyObject;

        while (d is not null)
        {
            if (d is FrameworkElement fe)
            {
                if (fe.Tag is string tag && tag.StartsWith("InPort:", StringComparison.OrdinalIgnoreCase))
                {
                    var portName = tag.Substring("InPort:".Length);
                    if (fe.DataContext is NodePortViewModel p)
                    {
                        return (p.Node, portName);
                    }
                }
            }

            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private static Point GetOutPortPosition(ToolGraphNodeViewModel node, string portName)
    {
        node.EnsurePortsInitialized();
        var cy = node.GetOutPortCenterY(portName);
        return new Point(node.X + 160, node.Y + cy);
    }

    private void UpdateWirePreview(Point p1, Point p2)
    {
        var dx = Math.Abs(p2.X - p1.X);
        var c = Math.Max(40.0, dx * 0.5);

        var c1 = new Point(p1.X + c, p1.Y);
        var c2 = new Point(p2.X - c, p2.Y);

        var fig = new PathFigure { StartPoint = p1, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new BezierSegment(c1, c2, p2, true));
        WirePreviewPath.Data = new PathGeometry(new[] { fig });
    }
}

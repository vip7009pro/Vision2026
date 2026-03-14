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

    private bool _isCanvasPanning;
    private Point _canvasPanStart;
    private double _canvasPanStartH;
    private double _canvasPanStartV;

    private Dictionary<ToolGraphNodeViewModel, Point>? _multiDragStart;

    private bool _isRangeSelecting;
    private Point _rangeSelectStart;

    public ToolEditorView()
    {
        InitializeComponent();

        AddHandler(Thumb.MouseLeftButtonDownEvent, new MouseButtonEventHandler(AnyNode_MouseLeftButtonDown), true);

        EditorCanvas.PreviewMouseDown += EditorCanvas_PreviewMouseDown;
        EditorCanvas.PreviewMouseMove += EditorCanvas_PreviewMouseMove;
        EditorCanvas.PreviewMouseUp += EditorCanvas_PreviewMouseUp;

        EditorCanvas.PreviewMouseLeftButtonDown += EditorCanvas_PreviewMouseLeftButtonDown;
        EditorCanvas.PreviewMouseLeftButtonUp += EditorCanvas_PreviewMouseLeftButtonUp;
    }

    private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        if (sender is not FrameworkElement fe)
        {
            return;
        }

        if (fe.DataContext is not ToolGraphEdgeViewModel edge)
        {
            return;
        }

        vm.SelectEdge(edge);
        EditorCanvas.Focus();
        e.Handled = true;
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
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    vm.ToggleNodeSelection(n);
                }
                else
                {
                    vm.SelectedNode = n;
                }

                EditorCanvas.Focus();
                e.Handled = true;
                return;
            }

            d = VisualTreeHelper.GetParent(d);
        }
    }

    private void EditorCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isWiring || _isCanvasPanning)
        {
            return;
        }

        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        // Only start range selection when clicking on empty canvas (not on node/edge/port).
        if (IsMouseOverInteractiveElement(e.GetPosition(EditorCanvas)))
        {
            return;
        }

        _isRangeSelecting = true;
        _rangeSelectStart = GetCanvasLogicalPosition(e.GetPosition(EditorCanvas));

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            vm.ClearNodeSelection();
            vm.SelectedNode = null;
        }

        UpdateSelectionRect(_rangeSelectStart, _rangeSelectStart);
        SelectionRect.Visibility = Visibility.Visible;
        EditorCanvas.CaptureMouse();
        EditorCanvas.Focus();
        e.Handled = true;
    }

    private void EditorCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRangeSelecting)
        {
            return;
        }

        if (DataContext is not ToolEditorViewModel vm)
        {
            _isRangeSelecting = false;
            SelectionRect.Visibility = Visibility.Collapsed;
            EditorCanvas.ReleaseMouseCapture();
            return;
        }

        var end = GetCanvasLogicalPosition(e.GetPosition(EditorCanvas));
        var rect = MakeRect(_rangeSelectStart, end);

        foreach (var n in vm.Nodes)
        {
            var nb = new Rect(n.X, n.Y, 160, n.NodeHeight);
            if (rect.IntersectsWith(nb))
            {
                if (!n.IsSelected)
                {
                    vm.ToggleNodeSelection(n);
                }
            }
        }

        _isRangeSelecting = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        EditorCanvas.ReleaseMouseCapture();
        e.Handled = true;
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

        if (DataContext is not ToolEditorViewModel vm)
        {
            n.X += e.HorizontalChange;
            n.Y += e.VerticalChange;
            return;
        }

        if (!n.IsSelected)
        {
            vm.SelectedNode = n;
        }

        if (vm.SelectedNodes.Count <= 1)
        {
            n.X += e.HorizontalChange;
            n.Y += e.VerticalChange;
            return;
        }

        if (_multiDragStart is null)
        {
            // Fallback if DragStarted didn't run.
            _multiDragStart = vm.SelectedNodes.ToDictionary(x => x, x => new Point(x.X, x.Y));
        }

        foreach (var sn in vm.SelectedNodes)
        {
            if (_multiDragStart.TryGetValue(sn, out var p0))
            {
                sn.X = p0.X + e.HorizontalChange;
                sn.Y = p0.Y + e.VerticalChange;
            }
        }
    }

    private void NodeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb t || t.DataContext is not ToolGraphNodeViewModel n)
        {
            return;
        }

        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        if (!n.IsSelected)
        {
            vm.SelectedNode = n;
        }

        _multiDragStart = vm.SelectedNodes.ToDictionary(x => x, x => new Point(x.X, x.Y));
    }

    private void NodeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _multiDragStart = null;
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
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                vm.ToggleNodeSelection(n);
            }
            else
            {
                vm.SelectedNode = n;
            }
            e.Handled = true;
        }
    }

    private void EditorCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        if (CanvasScrollViewer is null)
        {
            return;
        }

        _isCanvasPanning = true;
        _canvasPanStart = e.GetPosition(this);
        _canvasPanStartH = CanvasScrollViewer.HorizontalOffset;
        _canvasPanStartV = CanvasScrollViewer.VerticalOffset;
        EditorCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void EditorCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isCanvasPanning || CanvasScrollViewer is null)
        {
            if (_isRangeSelecting)
            {
                var cur = GetCanvasLogicalPosition(e.GetPosition(EditorCanvas));
                UpdateSelectionRect(_rangeSelectStart, cur);
                e.Handled = true;
            }
            return;
        }

        var p = e.GetPosition(this);
        var dx = p.X - _canvasPanStart.X;
        var dy = p.Y - _canvasPanStart.Y;
        CanvasScrollViewer.ScrollToHorizontalOffset(_canvasPanStartH - dx);
        CanvasScrollViewer.ScrollToVerticalOffset(_canvasPanStartV - dy);
        e.Handled = true;
    }

    private void EditorCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isCanvasPanning || e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _isCanvasPanning = false;
        EditorCanvas.ReleaseMouseCapture();
        e.Handled = true;
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

        _wireCurrent = GetCanvasLogicalPosition(e.GetPosition(EditorCanvas));
        UpdateWirePreview(GetOutPortPosition(_wireFrom, _wireFromPort), _wireCurrent);
    }

    private void EditorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isWiring)
        {
            if (DataContext is ToolEditorViewModel vm0)
            {
                // Click on empty canvas clears edge selection.
                vm0.SelectedEdge = null;
            }
            EditorCanvas.Focus();
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

    private void EditorCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is ToolEditorViewModel vmEsc)
            {
                vmEsc.ClearNodeSelection();
                vmEsc.SelectedNode = null;
                vmEsc.SelectedEdge = null;
            }
            SelectionRect.Visibility = Visibility.Collapsed;
            _isRangeSelecting = false;
            EditorCanvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete)
        {
            return;
        }

        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        if (vm.SelectedEdge is null)
        {
            return;
        }

        if (vm.DeleteSelectedEdgeCommand.CanExecute(null))
        {
            vm.DeleteSelectedEdgeCommand.Execute(null);
            e.Handled = true;
        }
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

    private void EditorCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (DataContext is not ToolEditorViewModel vm)
        {
            return;
        }

        var delta = e.Delta > 0 ? 0.1 : -0.1;
        vm.CanvasZoom = Math.Clamp(vm.CanvasZoom + delta, 0.2, 3.0);
        e.Handled = true;
    }

    private Point GetCanvasLogicalPosition(Point p)
    {
        // EditorCanvas uses LayoutTransform for zoom.
        // Mouse positions reported relative to EditorCanvas already match the canvas logical space,
        // so dividing by CanvasZoom would cause a visible offset (especially when zoom < 1).
        return p;
    }

    private bool IsMouseOverInteractiveElement(Point canvasPoint)
    {
        var result = VisualTreeHelper.HitTest(EditorCanvas, canvasPoint);
        var d = result?.VisualHit as DependencyObject;

        while (d is not null)
        {
            if (d is Thumb)
            {
                return true;
            }

            if (d is System.Windows.Shapes.Path)
            {
                return true;
            }

            if (d is System.Windows.Shapes.Ellipse)
            {
                return true;
            }

            d = VisualTreeHelper.GetParent(d);
        }

        return false;
    }

    private static Rect MakeRect(Point a, Point b)
    {
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.X, b.X);
        var y2 = Math.Max(a.Y, b.Y);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private void UpdateSelectionRect(Point a, Point b)
    {
        var r = MakeRect(a, b);
        Canvas.SetLeft(SelectionRect, r.X);
        Canvas.SetTop(SelectionRect, r.Y);
        SelectionRect.Width = r.Width;
        SelectionRect.Height = r.Height;
    }

    private static Point GetOutPortPosition(ToolGraphNodeViewModel node, string portName)
    {
        node.EnsurePortsInitialized();
        var cy = node.GetOutPortCenterY(portName);
        return new Point(node.X + 166, node.Y + cy);
    }

    private void UpdateWirePreview(Point p1, Point p2)
    {
        var midX = (p1.X + p2.X) * 0.5;
        if (midX < p1.X + 30) midX = p1.X + 30;
        if (midX > p2.X - 30) midX = p2.X - 30;

        var fig = new PathFigure { StartPoint = p1, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new LineSegment(new Point(midX, p1.Y), true));
        fig.Segments.Add(new LineSegment(new Point(midX, p2.Y), true));
        fig.Segments.Add(new LineSegment(p2, true));
        WirePreviewPath.Data = new PathGeometry(new[] { fig });
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class ToolEditorView : UserControl
{
    private bool _isWiring;
    private ToolGraphNodeViewModel? _wireFrom;
    private string _wireFromPort = "Out";
    private Point _wireCurrent;

    private bool _isCanvasPanning;
    private Point _panStartMouse;
    private double _panStartH;
    private double _panStartV;
    private ToolEditorViewModel? _subscribedVm;

    private Dictionary<ToolGraphNodeViewModel, Point>? _multiDragStart;

    private bool _isRangeSelecting;
    private Point _rangeSelectStart;

    public ToolEditorView()
    {
        InitializeComponent();

        AddHandler(Thumb.MouseLeftButtonDownEvent, new MouseButtonEventHandler(AnyNode_MouseLeftButtonDown), true);

        CanvasScrollViewer.PreviewMouseDown += CanvasScrollViewer_PreviewMouseDown;
        CanvasScrollViewer.PreviewMouseMove += CanvasScrollViewer_PreviewMouseMove;
        CanvasScrollViewer.PreviewMouseUp += CanvasScrollViewer_PreviewMouseUp;
        CanvasScrollViewer.LostMouseCapture += CanvasScrollViewer_LostMouseCapture;

        EditorCanvas.PreviewMouseLeftButtonDown += EditorCanvas_PreviewMouseLeftButtonDown;
        EditorCanvas.PreviewMouseLeftButtonUp += EditorCanvas_PreviewMouseLeftButtonUp;

        DataContextChanged += ToolEditorView_DataContextChanged;
        Loaded += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, AutoFitAndCenterGraph);
    }

    private void ToolEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.RequestAutoFitGraph -= OnRequestAutoFitGraph;
        }

        if (e.NewValue is ToolEditorViewModel vm)
        {
            _subscribedVm = vm;
            _subscribedVm.RequestAutoFitGraph += OnRequestAutoFitGraph;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, AutoFitAndCenterGraph);
        }
    }

    private void OnRequestAutoFitGraph()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, AutoFitAndCenterGraph);
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

        // If holding Ctrl or Shift on empty canvas, start box range selection
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
        {
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

    private System.Windows.Point _dragStartPoint;

    private void ToolboxList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void ToolboxList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var diff = _dragStartPoint - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ScrollBar) return;
            d = VisualTreeHelper.GetParent(d);
        }

        if (sender is not ListBox lb)
        {
            return;
        }

        string? type = lb.SelectedItem switch
        {
            string s => s,
            ToolboxItemModel item => item.Name,
            _ => lb.SelectedItem?.ToString()
        };

        if (string.IsNullOrWhiteSpace(type))
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

        var pos = GetCanvasLogicalPosition(e.GetPosition(EditorCanvas));
        vm.AddNode(type, pos);
        vm.IsDirty = true;
    }

    private void NodeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb t || t.DataContext is not ToolGraphNodeViewModel n)
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

        // LayoutTransform on EditorCanvas means e.HorizontalChange & e.VerticalChange are ALREADY exact logical units!
        var dx = e.HorizontalChange;
        var dy = e.VerticalChange;
        if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001)
        {
            return;
        }

        if (vm.SelectedNodes.Count <= 1)
        {
            n.X += dx;
            n.Y += dy;
            return;
        }

        foreach (var sn in vm.SelectedNodes)
        {
            sn.X += dx;
            sn.Y += dy;
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
        if (DataContext is ToolEditorViewModel vm)
        {
            vm.IsDirty = true;
            if (_multiDragStart is not null && _multiDragStart.Count > 0)
            {
                var startPositions = _multiDragStart;
                var endPositions = startPositions.Keys.ToDictionary(x => x, x => new Point(x.X, x.Y));
                bool hasMoved = startPositions.Any(kv => Math.Abs(kv.Value.X - endPositions[kv.Key].X) > 0.1 || Math.Abs(kv.Value.Y - endPositions[kv.Key].Y) > 0.1);
                if (hasMoved)
                {
                    vm.UndoManager.Execute(new UndoRedoManager.DelegateAction(
                        doAction: () =>
                        {
                            foreach (var (node, pos) in endPositions)
                            {
                                node.X = pos.X;
                                node.Y = pos.Y;
                            }
                            vm.IsDirty = true;
                        },
                        undoAction: () =>
                        {
                            foreach (var (node, pos) in startPositions)
                            {
                                node.X = pos.X;
                                node.Y = pos.Y;
                            }
                            vm.IsDirty = true;
                        }
                    ));
                }
            }
        }
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

    private void CanvasScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ToolEditorViewModel vm || CanvasScrollViewer is null)
        {
            return;
        }

        var isMiddle = e.ChangedButton == MouseButton.Middle;
        var isLeftOnBg = e.ChangedButton == MouseButton.Left &&
                         !IsMouseOverInteractiveElement(e.GetPosition(EditorCanvas)) &&
                         (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0;

        if (isMiddle || isLeftOnBg)
        {
            _isCanvasPanning = true;
            _panStartMouse = e.GetPosition(this);
            _panStartH = CanvasScrollViewer.HorizontalOffset;
            _panStartV = CanvasScrollViewer.VerticalOffset;
            CanvasScrollViewer.CaptureMouse();
            e.Handled = true;
        }
    }

    private void CanvasScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
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

        var curMouse = e.GetPosition(this);
        var dx = curMouse.X - _panStartMouse.X;
        var dy = curMouse.Y - _panStartMouse.Y;

        CanvasScrollViewer.ScrollToHorizontalOffset(_panStartH - dx);
        CanvasScrollViewer.ScrollToVerticalOffset(_panStartV - dy);
        e.Handled = true;
    }

    private void CanvasScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isCanvasPanning)
        {
            StopCanvasPan();
            e.Handled = true;
        }
    }

    private void CanvasScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
    {
        StopCanvasPan();
    }

    private void StopCanvasPan()
    {
        _isCanvasPanning = false;
        if (CanvasScrollViewer != null && CanvasScrollViewer.IsMouseCaptured)
        {
            CanvasScrollViewer.ReleaseMouseCapture();
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

        if (e.ClickCount == 2)
        {
            vm.ShowPortValueDialog(n, port.Name);
            e.Handled = true;
            return;
        }

        vm.SelectedNode = n;

        _isWiring = true;
        _wireFrom = n;
        _wireFromPort = port.Name;
        _wireCurrent = GetCanvasLogicalPosition(e.GetPosition(EditorCanvas));

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

        if (vm.SelectedEdge is not null)
        {
            if (vm.DeleteSelectedEdgeCommand.CanExecute(null))
            {
                vm.DeleteSelectedEdgeCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (vm.SelectedNode is not null)
        {
            if (vm.DeleteSelectedNodeCommand.CanExecute(null))
            {
                vm.DeleteSelectedNodeCommand.Execute(null);
                e.Handled = true;
            }
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
        if (DataContext is not ToolEditorViewModel vm || CanvasScrollViewer is null)
        {
            return;
        }

        var oldZoom = vm.CanvasZoom;
        var zoomFactor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var newZoom = Math.Clamp(oldZoom * zoomFactor, 0.1, 4.0);
        if (Math.Abs(newZoom - oldZoom) < 0.0000001)
        {
            return;
        }

        var mouseInViewport = e.GetPosition(CanvasScrollViewer);

        var contentX = (CanvasScrollViewer.HorizontalOffset + mouseInViewport.X) / oldZoom;
        var contentY = (CanvasScrollViewer.VerticalOffset + mouseInViewport.Y) / oldZoom;

        vm.CanvasZoom = newZoom;
        EditorCanvas.UpdateLayout();
        CanvasScrollViewer.UpdateLayout();

        CanvasScrollViewer.ScrollToHorizontalOffset(contentX * newZoom - mouseInViewport.X);
        CanvasScrollViewer.ScrollToVerticalOffset(contentY * newZoom - mouseInViewport.Y);

        e.Handled = true;
    }

    private Point GetCanvasLogicalPosition(Point pEditorCanvas)
    {
        return new Point(pEditorCanvas.X - 3000.0, pEditorCanvas.Y - 3000.0);
    }

    private bool IsMouseOverInteractiveElement(Point canvasPoint)
    {
        if (EditorCanvas == null) return false;
        var result = VisualTreeHelper.HitTest(EditorCanvas, canvasPoint);
        var d = result?.VisualHit as DependencyObject;

        while (d is not null && d != EditorCanvas)
        {
            if (d is Thumb || d is System.Windows.Shapes.Path || d is System.Windows.Shapes.Ellipse)
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
        var fig = new PathFigure { StartPoint = p1, IsClosed = false, IsFilled = false };
        double dx = Math.Abs(p2.X - p1.X);
        double dy = Math.Abs(p2.Y - p1.Y);
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double smoothness = Math.Max(20.0, Math.Min(dist * 0.4, 120.0));

        Point cp1, cp2;
        if (Math.Abs(p2.Y - p1.Y) > Math.Abs(p2.X - p1.X) * 0.8)
        {
            double sign = p2.Y >= p1.Y ? 1.0 : -1.0;
            cp1 = new Point(p1.X, p1.Y + smoothness * sign);
            cp2 = new Point(p2.X, p2.Y - smoothness * sign);
        }
        else
        {
            double sign = p2.X >= p1.X ? 1.0 : -1.0;
            cp1 = new Point(p1.X + smoothness * sign, p1.Y);
            cp2 = new Point(p2.X - smoothness * sign, p2.Y);
        }

        fig.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
        WirePreviewPath.Data = new PathGeometry(new[] { fig });
    }

    public void AutoFitAndCenterGraph()
    {
        StopCanvasPan();

        if (DataContext is not ToolEditorViewModel vm || vm.Nodes.Count == 0 || CanvasScrollViewer == null)
        {
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var n in vm.Nodes)
        {
            if (n.X < minX) minX = n.X;
            if (n.Y < minY) minY = n.Y;
            if (n.X + 160.0 > maxX) maxX = n.X + 160.0;
            double nh = n.NodeHeight > 0 ? n.NodeHeight : 80.0;
            if (n.Y + nh > maxY) maxY = n.Y + nh;
        }

        if (minX >= maxX || minY >= maxY)
        {
            return;
        }

        const double padding = 80.0;
        const double baseOffset = 3000.0;

        double contentW = (maxX - minX) + padding * 2.0;
        double contentH = (maxY - minY) + padding * 2.0;

        double viewW = CanvasScrollViewer.ActualWidth > 0 ? CanvasScrollViewer.ActualWidth : 800.0;
        double viewH = CanvasScrollViewer.ActualHeight > 0 ? CanvasScrollViewer.ActualHeight : 600.0;

        double scaleX = viewW / contentW;
        double scaleY = viewH / contentH;
        double fitZoom = Math.Min(scaleX, scaleY);
        fitZoom = Math.Clamp(fitZoom, 0.3, 1.5);

        vm.CanvasZoom = fitZoom;
        EditorCanvas.UpdateLayout();
        CanvasScrollViewer.UpdateLayout();

        double graphCenterX = (baseOffset + (minX + maxX) / 2.0) * fitZoom;
        double graphCenterY = (baseOffset + (minY + maxY) / 2.0) * fitZoom;

        CanvasScrollViewer.ScrollToHorizontalOffset(Math.Max(0, graphCenterX - viewW / 2.0));
        CanvasScrollViewer.ScrollToVerticalOffset(Math.Max(0, graphCenterY - viewH / 2.0));
    }

    private void BtnFitView_Click(object sender, RoutedEventArgs e)
    {
        AutoFitAndCenterGraph();
    }

    private void BtnGlobalPreprocess_Click(object sender, RoutedEventArgs e)
    {
        var window = new GlobalPreprocessWindow
        {
            Owner = Window.GetWindow(this),
            DataContext = this.DataContext
        };
        window.Show();
    }

    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var globalSettings = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<VisionInspectionApp.UI.Services.GlobalAppSettingsService>((System.Windows.Application.Current as App).ServiceProvider);
        var themeService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<VisionInspectionApp.UI.Services.ThemeService>((System.Windows.Application.Current as App).ServiceProvider);

        globalSettings.Settings.IsDarkMode = !globalSettings.Settings.IsDarkMode;
        themeService.ApplyTheme(globalSettings.Settings.IsDarkMode);
    }
}

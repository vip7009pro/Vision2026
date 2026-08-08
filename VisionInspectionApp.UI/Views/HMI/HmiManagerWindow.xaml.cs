using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using VisionInspectionApp.UI.ViewModels.HMI;

namespace VisionInspectionApp.UI.Views.HMI;

public partial class HmiManagerWindow : Window
{
    private bool _isDraggingItem = false;
    private bool _isRubberbanding = false;

    private Point _dragStartPoint;
    private Dictionary<HmiControlViewModel, (double OriginalX, double OriginalY)> _draggedItemsStartPos = new();
    private HmiControlViewModel? _primaryDraggedItem;

    public HmiManagerWindow(HmiManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not HmiManagerViewModel vm || vm.IsRunMode) return;

        bool isCtrlOrShiftPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ||
                                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        Point mousePos = e.GetPosition(sender as IInputElement);

        if (e.OriginalSource is DependencyObject dep)
        {
            var presenter = FindParent<ContentPresenter>(dep);
            if (presenter?.DataContext is HmiControlViewModel itemVm)
            {
                vm.SelectControl(itemVm, toggleSelection: isCtrlOrShiftPressed, multiSelect: isCtrlOrShiftPressed);

                _primaryDraggedItem = itemVm;
                _isDraggingItem = true;
                _dragStartPoint = mousePos;

                _draggedItemsStartPos.Clear();
                foreach (var ctrl in vm.SelectedControls)
                {
                    _draggedItemsStartPos[ctrl] = (ctrl.Model.X, ctrl.Model.Y);
                }

                if (sender is UIElement elem)
                {
                    elem.CaptureMouse();
                }
                e.Handled = true;
                return;
            }
        }

        // Clicked on empty canvas area
        if (!isCtrlOrShiftPressed)
        {
            vm.ClearSelection();
        }

        // Start Rubberband Drag Selection Box
        _isRubberbanding = true;
        _dragStartPoint = mousePos;
        Canvas.SetLeft(RubberbandRect, mousePos.X);
        Canvas.SetTop(RubberbandRect, mousePos.Y);
        RubberbandRect.Width = 0;
        RubberbandRect.Height = 0;
        RubberbandRect.Visibility = Visibility.Visible;

        if (sender is UIElement canvasElem)
        {
            canvasElem.CaptureMouse();
        }
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not HmiManagerViewModel vm || vm.IsRunMode) return;

        Point currentPoint = e.GetPosition(sender as IInputElement);

        // Case A: Moving Dragged Controls
        if (_isDraggingItem && _primaryDraggedItem != null)
        {
            double deltaX = currentPoint.X - _dragStartPoint.X;
            double deltaY = currentPoint.Y - _dragStartPoint.Y;

            foreach (var kvp in _draggedItemsStartPos)
            {
                var itemVm = kvp.Key;
                double newX = kvp.Value.OriginalX + deltaX;
                double newY = kvp.Value.OriginalY + deltaY;

                // Snap to Grid (10px) if enabled
                if (vm.ScreenConfig.ShowGrid && vm.ScreenConfig.GridSize > 1)
                {
                    double grid = vm.ScreenConfig.GridSize;
                    newX = Math.Round(newX / grid) * grid;
                    newY = Math.Round(newY / grid) * grid;
                }

                // Clamp to screen bounds
                newX = Math.Max(0, Math.Min(vm.ScreenConfig.Width - itemVm.Model.Width, newX));
                newY = Math.Max(0, Math.Min(vm.ScreenConfig.Height - itemVm.Model.Height, newY));

                itemVm.Model.X = newX;
                itemVm.Model.Y = newY;
                itemVm.NotifyModelChanged();
            }

            vm.IsDirty = true;
            return;
        }

        // Case B: Drawing Rubberband Box
        if (_isRubberbanding)
        {
            double x = Math.Min(_dragStartPoint.X, currentPoint.X);
            double y = Math.Min(_dragStartPoint.Y, currentPoint.Y);
            double w = Math.Abs(currentPoint.X - _dragStartPoint.X);
            double h = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

            Canvas.SetLeft(RubberbandRect, x);
            Canvas.SetTop(RubberbandRect, y);
            RubberbandRect.Width = w;
            RubberbandRect.Height = h;
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not HmiManagerViewModel vm) return;

        if (_isDraggingItem)
        {
            _isDraggingItem = false;
            _primaryDraggedItem = null;
            _draggedItemsStartPos.Clear();
            if (sender is UIElement elem)
            {
                elem.ReleaseMouseCapture();
            }
            return;
        }

        if (_isRubberbanding)
        {
            _isRubberbanding = false;
            RubberbandRect.Visibility = Visibility.Collapsed;
            if (sender is UIElement elem)
            {
                elem.ReleaseMouseCapture();
            }

            // Select all controls intersecting rubberband rect
            double rx = Canvas.GetLeft(RubberbandRect);
            double ry = Canvas.GetTop(RubberbandRect);
            double rw = RubberbandRect.Width;
            double rh = RubberbandRect.Height;

            if (rw > 5 && rh > 5)
            {
                Rect selectionRect = new Rect(rx, ry, rw, rh);
                bool isCtrlOrShiftPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ||
                                             (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

                if (!isCtrlOrShiftPressed)
                {
                    vm.ClearSelection();
                }

                foreach (var ctrl in vm.Controls)
                {
                    Rect ctrlRect = new Rect(ctrl.Model.X, ctrl.Model.Y, ctrl.Model.Width, ctrl.Model.Height);
                    if (selectionRect.IntersectsWith(ctrlRect))
                    {
                        vm.SelectControl(ctrl, multiSelect: true);
                    }
                }
            }
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;

        if (parentObject is T parent)
            return parent;

        return FindParent<T>(parentObject);
    }
}

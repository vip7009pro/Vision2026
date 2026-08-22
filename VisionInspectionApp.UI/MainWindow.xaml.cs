using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var rcWorkArea = monitorInfo.rcWork;
                var rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void MainWindow_StateChanged(object? sender, System.EventArgs e)
    {
        if (BtnMaximizeRestoreText != null)
        {
            BtnMaximizeRestoreText.Text = WindowState == WindowState.Maximized ? "🗗" : "🗖";
        }
        if (BtnMaximizeRestore != null)
        {
            BtnMaximizeRestore.ToolTip = WindowState == WindowState.Maximized ? "Thu nhỏ cửa sổ (Restore)" : "Phóng to tối đa (Maximize)";
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && System.Windows.Application.Current is App app && app.ServiceProvider != null)
        {
            var themeService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<VisionInspectionApp.UI.Services.ThemeService>(app.ServiceProvider);
            var currentThemeId = themeService.CurrentThemeId;

            var cm = new ContextMenu
            {
                PlacementTarget = btn,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            string? lastGroup = null;
            foreach (var theme in VisionInspectionApp.UI.Services.ThemeService.AvailableThemes)
            {
                if (theme.Group != lastGroup)
                {
                    if (lastGroup != null)
                    {
                        cm.Items.Add(new Separator());
                    }
                    var groupHeader = new MenuItem
                    {
                        Header = theme.Group,
                        IsEnabled = false,
                        FontWeight = FontWeights.Bold,
                        FontSize = 11.5,
                        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("TextMutedBrush")
                    };
                    cm.Items.Add(groupHeader);
                    lastGroup = theme.Group;
                }

                var isCurrent = string.Equals(theme.Id, currentThemeId, StringComparison.OrdinalIgnoreCase);

                // Create color preview swatch icon
                var swatchBorder = new System.Windows.Controls.Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(1),
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    Background = new System.Windows.Media.LinearGradientBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.PrimaryColorHex),
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.AccentColorHex),
                        45.0),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                var mi = new MenuItem
                {
                    Header = $"{theme.Name}  -  {theme.Description}",
                    Icon = swatchBorder,
                    IsChecked = isCurrent,
                    IsCheckable = true
                };

                var targetThemeId = theme.Id;
                mi.Click += (_, _) =>
                {
                    themeService.ApplyTheme(targetThemeId);
                };

                cm.Items.Add(mi);
            }

            cm.IsOpen = true;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            bool isDirty = vm.ToolEditor.IsDirty || vm.Calibration.IsDirty;
            if (isDirty)
            {
                var result = MessageBox.Show("There are unsaved changes. Do you want to save them before closing?", 
                                             "Unsaved Changes", 
                                             MessageBoxButton.YesNoCancel, 
                                             MessageBoxImage.Warning);
                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
                else if (result == MessageBoxResult.Yes)
                {
                    if (vm.ToolEditor.IsDirty && vm.ToolEditor.SaveJobCommand.CanExecute(null))
                        vm.ToolEditor.SaveJobCommand.Execute(null);

                    if (vm.Calibration.IsDirty && vm.Calibration.SaveJobCommand.CanExecute(null))
                        vm.Calibration.SaveJobCommand.Execute(null);
                }
            }
        }
    }
}
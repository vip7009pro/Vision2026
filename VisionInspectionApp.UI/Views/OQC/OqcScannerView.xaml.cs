using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcScannerView : UserControl
{
    private bool _hasInitialAutoFit = false;

    public OqcScannerView()
    {
        InitializeComponent();
        Loaded += OqcScannerView_Loaded;
        DataContextChanged += OqcScannerView_DataContextChanged;
        IsVisibleChanged += OqcScannerView_IsVisibleChanged;
        PreviewKeyDown += OqcScannerView_PreviewKeyDown;
        PreviewTextInput += OqcScannerView_PreviewTextInput;
    }

    private void OqcScannerView_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Khi đầu đọc barcode bắn chuỗi ký tự vào, tự động focus vào ScanInputTextBox
        // để không bị mất ký tự nếu công nhân vừa click chuột vào ảnh hay bảng kết quả
        if (ScanInputTextBox != null && !ScanInputTextBox.IsKeyboardFocusWithin)
        {
            ScanInputTextBox.Focus();
            ScanInputTextBox.SelectAll();
        }
    }

    private void OqcScannerView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewModels.OqcScannerViewModel oldVm)
        {
            oldVm.RequestFocusAndSelectInput -= OnRequestFocusAndSelectInput;
        }
        if (e.NewValue is ViewModels.OqcScannerViewModel newVm)
        {
            newVm.RequestFocusAndSelectInput += OnRequestFocusAndSelectInput;
        }
    }

    private void OqcScannerView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                FocusAndSelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void OnRequestFocusAndSelectInput()
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            FocusAndSelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void FocusAndSelectAll()
    {
        if (ScanInputTextBox == null) return;
        if (!ScanInputTextBox.IsKeyboardFocusWithin)
        {
            ScanInputTextBox.Focus();
        }
        ScanInputTextBox.SelectAll();
    }

    private void ScanInputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ScanInputTextBox?.SelectAll();
    }

    private void ScanInputTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ScanInputTextBox != null && !ScanInputTextBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            ScanInputTextBox.Focus();
            ScanInputTextBox.SelectAll();
        }
    }

    private void OqcScannerView_Loaded(object sender, RoutedEventArgs e)
    {
        FocusAndSelectAll();
        if (!_hasInitialAutoFit)
        {
            _hasInitialAutoFit = true;
            ScheduleAutoFit();
        }
    }

    private void ScheduleAutoFit()
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            OqcImageViewer?.ResetView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BtnFitImagePreview_Click(object sender, RoutedEventArgs e)
    {
        OqcImageViewer?.ResetView();
    }

    private void OqcScannerView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.OqcScannerViewModel vm) return;

        bool isCtrlF8 = (e.Key == Key.F8 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control);
        bool isSpace = (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None);

        if (isSpace || isCtrlF8)
        {
            if (vm.TriggerInspectOrLiveCommand.CanExecute(null))
            {
                vm.TriggerInspectOrLiveCommand.Execute(null);
                e.Handled = true;
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FocusAndSelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.EscapeCloseJobAndClearTextCommand.CanExecute(null))
            {
                vm.EscapeCloseJobAndClearTextCommand.Execute(null);
                e.Handled = true;
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FocusAndSelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (vm.EnableLiveCameraCommand.CanExecute(null))
            {
                vm.EnableLiveCameraCommand.Execute(null);
                e.Handled = true;
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FocusAndSelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
        else if (e.Key == Key.Enter)
        {
            // Chế độ Cú đấm thép: Xử lý sự kiện Enter khi quét mã từ đầu đọc hoặc bàn phím
            // Quét mã mới hoặc quét lại mã phiên (IsSameAsCurrentSessionCode) đều kích hoạt ScanCommand để nạp Job và reset counting mẫu
            string currentInput = ScanInputTextBox?.Text?.Trim() ?? "";
            bool isSameCode = vm.IsSameAsCurrentSessionCode(currentInput);

            if (vm.ScanCommand.CanExecute(null))
            {
                vm.ScanCommand.Execute(null);
                e.Handled = true;
            }

            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                FocusAndSelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}

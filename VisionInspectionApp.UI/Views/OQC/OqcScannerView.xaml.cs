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
        PreviewKeyDown += OqcScannerView_PreviewKeyDown;
    }

    private void OqcScannerView_Loaded(object sender, RoutedEventArgs e)
    {
        ScanInputTextBox.Focus();
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
            }
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (vm.EnableLiveCameraCommand.CanExecute(null))
            {
                vm.EnableLiveCameraCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter)
        {
            // Chế độ Cú đấm thép: Khi Job đã được mở trong phiên này:
            // - Nếu Enter với cùng mã phiên hiện tại: vô hiệu hóa hoàn toàn phím Enter (coi như không làm gì khi Enter)
            // - Nếu scan mã tiếp theo (mã khác): cho phép sự kiện Enter thực thi để nạp Job tiếp tương ứng và tạo phiên mới!
            if (vm.SteelPunchMode && vm.HasLoadedJob)
            {
                string currentInput = ScanInputTextBox.Text?.Trim() ?? "";
                if (vm.IsSameAsCurrentSessionCode(currentInput))
                {
                    e.Handled = true;
                }
            }
        }
    }
}

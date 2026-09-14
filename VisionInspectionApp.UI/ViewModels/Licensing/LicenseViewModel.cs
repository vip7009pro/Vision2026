using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Application.Licensing;

namespace VisionInspectionApp.UI.ViewModels.Licensing;

public partial class LicenseViewModel : ObservableObject
{
    private readonly ILicenseService _licenseService;

    [ObservableProperty]
    private string _formattedMachineCode = string.Empty;

    [ObservableProperty]
    private string _machineFingerprint = string.Empty;

    [ObservableProperty]
    private string _statusText = "Chưa kích hoạt";

    [ObservableProperty]
    private Brush _statusBrush = Brushes.Gray;

    [ObservableProperty]
    private string _customerName = "Chưa đăng ký";

    [ObservableProperty]
    private string _editionText = "None";

    [ObservableProperty]
    private string _expirationText = "Không có";

    [ObservableProperty]
    private string _licenseKeyInput = string.Empty;

    [ObservableProperty]
    private string _serverUrlInput = "http://localhost:4000";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSuccessMessage;

    [ObservableProperty]
    private bool _isLicensed;

    [ObservableProperty]
    private bool _isPendingApproval;

    [ObservableProperty]
    private string _pendingApprovalMessage = string.Empty;

    private System.Windows.Threading.DispatcherTimer? _autoPollTimer;

    public LicenseViewModel(ILicenseService licenseService)
    {
        _licenseService = licenseService;
        _licenseService.StatusChanged += OnLicenseStatusChanged;

        FormattedMachineCode = _licenseService.FormattedMachineCode;
        MachineFingerprint = _licenseService.MachineFingerprint;

        RefreshLicenseInfo();

        if (!IsLicensed)
        {
            StartPolling();
            // Kích hoạt kiểm tra / auto-register ngầm lần đầu
            _ = CheckPendingApprovalAsync();
        }
    }

    private void StartPolling()
    {
        if (_autoPollTimer != null) return;

        _autoPollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _autoPollTimer.Tick += async (_, _) =>
        {
            if (!IsLicensed && !IsBusy)
            {
                await CheckPendingApprovalAsync();
            }
        };
        _autoPollTimer.Start();
    }

    public void StopPolling()
    {
        if (_autoPollTimer != null)
        {
            _autoPollTimer.Stop();
            _autoPollTimer = null;
        }
    }

    public void Cleanup()
    {
        StopPolling();
        _licenseService.StatusChanged -= OnLicenseStatusChanged;
    }

    private void OnLicenseStatusChanged(object? sender, LicenseStatus status)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(RefreshLicenseInfo);
    }

    public void RefreshLicenseInfo()
    {
        var lic = _licenseService.CurrentLicense;
        IsLicensed = _licenseService.Status == LicenseStatus.Active || _licenseService.Status == LicenseStatus.GracePeriod;

        switch (_licenseService.Status)
        {
            case LicenseStatus.Active:
                StatusText = "🟢 Đã Kích Hoạt (Hoạt Động Bình Thường)";
                StatusBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                break;
            case LicenseStatus.GracePeriod:
                StatusText = "🟡 Thời Gian Ân Hạn Mất Mạng (Grace Period)";
                StatusBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                break;
            case LicenseStatus.Expired:
                StatusText = "🔴 Đã Hết Hạn Bản Quyền";
                StatusBrush = new SolidColorBrush(Color.FromRgb(244, 63, 94));
                break;
            case LicenseStatus.Revoked:
                StatusText = "⛔ Bản Quyền Đã Bị Thu Hồi Từ Xa";
                StatusBrush = new SolidColorBrush(Color.FromRgb(244, 63, 94));
                break;
            case LicenseStatus.Suspended:
                StatusText = "⏸ Bản Quyền Đang Bị Tạm Khóa";
                StatusBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                break;
            case LicenseStatus.ClockTampered:
                StatusText = "⚠️ Phát Hiện Gian Lận Đồng Hồ Hệ Thống";
                StatusBrush = new SolidColorBrush(Color.FromRgb(244, 63, 94));
                break;
            default:
                StatusText = "⚪ Chưa Kích Hoạt Bản Quyền";
                StatusBrush = Brushes.Gray;
                break;
        }

        if (lic != null)
        {
            CustomerName = lic.CustomerName;
            EditionText = lic.Edition;
            LicenseKeyInput = lic.LicenseKey;

            if (string.IsNullOrWhiteSpace(lic.ExpirationDateUtc))
            {
                ExpirationText = "Vĩnh viễn (Perpetual)";
            }
            else if (DateTime.TryParse(lic.ExpirationDateUtc, out var exp))
            {
                int rem = _licenseService.RemainingDays;
                ExpirationText = $"{exp.ToLocalTime():dd/MM/yyyy HH:mm} (Còn {rem} ngày)";
            }
        }
        else
        {
            CustomerName = "Chưa đăng ký";
            EditionText = "None";
            ExpirationText = "Chưa có";
        }

        IsPendingApproval = _licenseService.IsPendingApproval;
        PendingApprovalMessage = _licenseService.PendingApprovalMessage ?? string.Empty;

        if (IsLicensed)
        {
            StopPolling();
        }
    }

    [RelayCommand]
    public async Task CheckPendingApprovalAsync()
    {
        IsBusy = true;
        ShowMessage("Đang kiểm tra trạng thái phê duyệt từ máy chủ...", true);

        try
        {
            var result = await _licenseService.AutoRegisterOrCheckApprovalAsync(ServerUrlInput?.Trim());
            RefreshLicenseInfo();

            if (result.Success)
            {
                ShowMessage(result.Message, true);
                StopPolling();
            }
            else
            {
                ShowMessage(result.Message, false);
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Lỗi kiểm tra phê duyệt: {ex.Message}", false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ActivateOnlineAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseKeyInput))
        {
            ShowMessage("Vui lòng nhập mã License Key!", false);
            return;
        }

        IsBusy = true;
        ShowMessage("Đang kết nối đến License Server để kích hoạt...", true);

        try
        {
            var result = await _licenseService.ActivateOnlineAsync(LicenseKeyInput.Trim(), ServerUrlInput?.Trim());
            if (result.Success)
            {
                ShowMessage(result.Message, true);
                RefreshLicenseInfo();
            }
            else
            {
                ShowMessage(result.Message, false);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportRequestFileAsync()
    {
        try
        {
            var sfd = new SaveFileDialog
            {
                Title = "Lưu File Yêu Cầu Cấp Phép Bản Quyền (.req)",
                Filter = "License Request File (*.req)|*.req|All Files (*.*)|*.*",
                FileName = $"MachineRequest_{Environment.MachineName}_{FormattedMachineCode}.req"
            };

            if (sfd.ShowDialog() == true)
            {
                var reqCode = await _licenseService.GenerateOfflineRequestCodeAsync();
                await File.WriteAllTextAsync(sfd.FileName, reqCode);
                ShowMessage($"Đã xuất file yêu cầu thành công:\n{Path.GetFileName(sfd.FileName)}\nHãy gửi file này cho Quản trị viên để lấy file bản quyền (.lic).", true);
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Lỗi xuất file yêu cầu: {ex.Message}", false);
        }
    }

    [RelayCommand]
    private async Task ImportLicenseFileAsync()
    {
        try
        {
            var ofd = new OpenFileDialog
            {
                Title = "Chọn File Bản Quyền (.lic) Do Quản Trị Viên Cung Cấp",
                Filter = "License File (*.lic)|*.lic|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                IsBusy = true;
                var content = await File.ReadAllTextAsync(ofd.FileName);
                var result = await _licenseService.ActivateOfflineAsync(content);

                if (result.Success)
                {
                    ShowMessage(result.Message, true);
                    RefreshLicenseInfo();
                }
                else
                {
                    ShowMessage(result.Message, false);
                }
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Lỗi nạp file bản quyền: {ex.Message}", false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyMachineCode()
    {
        try
        {
            Clipboard.SetText(FormattedMachineCode);
            ShowMessage($"Đã sao chép mã máy: {FormattedMachineCode}", true);
        }
        catch { }
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        var confirm = MessageBox.Show(
            "Bạn có chắc chắn muốn hủy kích hoạt bản quyền trên máy tính này?",
            "Xác nhận hủy bản quyền",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            await _licenseService.DeactivateLocalAsync();
            RefreshLicenseInfo();
            ShowMessage("Đã hủy bản quyền trên máy tính này.", true);
        }
    }

    [ObservableProperty]
    private bool _hasStatusMessage;

    private void ShowMessage(string msg, bool isSuccess)
    {
        StatusMessage = msg;
        IsSuccessMessage = isSuccess;
        HasStatusMessage = !string.IsNullOrWhiteSpace(msg);
    }
}

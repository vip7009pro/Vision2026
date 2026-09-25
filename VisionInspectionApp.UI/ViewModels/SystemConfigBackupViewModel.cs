using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public partial class SystemConfigBackupViewModel : ObservableObject
{
    private readonly ISystemConfigBackupService _backupService;
    private readonly IDbManagerService _dbManager;
    private readonly IPlcManagerService _plcManager;
    private readonly IOqcScannerService _oqcScanner;
    private readonly GlobalAppSettingsService _appSettingsService;

    // ─── Tùy chọn Xuất Cấu Hình (Export) ───
    [ObservableProperty]
    private bool _includeAppSettings = true;

    [ObservableProperty]
    private bool _includePlcConfig = true;

    [ObservableProperty]
    private bool _includeDatabaseConfig = true;

    [ObservableProperty]
    private bool _includeOqcConfig = true;

    [ObservableProperty]
    private bool _includeCalibrationConfig = true;

    [ObservableProperty]
    private bool _includeCameraConfig = true;

    // Thông tin trạng thái hệ thống hiện tại trên máy
    [ObservableProperty]
    private string _currentMachineName = Environment.MachineName;

    [ObservableProperty]
    private int _currentDbCount;

    [ObservableProperty]
    private int _currentPlcCount;

    [ObservableProperty]
    private int _currentTagCount;

    [ObservableProperty]
    private string _currentSystemSummary = "";

    // ─── Tùy chọn Nạp Cấu Hình (Import) ───
    [ObservableProperty]
    private string _selectedImportFilePath = "";

    [ObservableProperty]
    private SystemConfigPackageInspection? _inspectedPackage;

    [ObservableProperty]
    private bool _isPackageLoaded;

    [ObservableProperty]
    private bool _restoreAppSettings = true;

    [ObservableProperty]
    private bool _restorePlcConfig = true;

    [ObservableProperty]
    private bool _restoreDatabaseConfig = true;

    [ObservableProperty]
    private bool _restoreOqcConfig = true;

    [ObservableProperty]
    private bool _restoreCalibrationConfig = true;

    [ObservableProperty]
    private bool _restoreCameraConfig = true;

    // ─── Trạng thái thực thi & Nhật ký (Logs) ───
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng thực hiện sao lưu hoặc phục hồi cấu hình.";

    [ObservableProperty]
    private Brush _statusBrush = Brushes.Gray;

    public ObservableCollection<string> OperationLogs { get; } = new();

    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand BrowseImportFileCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public IRelayCommand ClearLogsCommand { get; }

    public SystemConfigBackupViewModel(
        ISystemConfigBackupService backupService,
        IDbManagerService dbManager,
        IPlcManagerService plcManager,
        IOqcScannerService oqcScanner,
        GlobalAppSettingsService appSettingsService)
    {
        _backupService = backupService;
        _dbManager = dbManager;
        _plcManager = plcManager;
        _oqcScanner = oqcScanner;
        _appSettingsService = appSettingsService;

        ExportCommand = new AsyncRelayCommand(ExecuteExportAsync);
        BrowseImportFileCommand = new AsyncRelayCommand(ExecuteBrowseImportFileAsync);
        RestoreCommand = new AsyncRelayCommand(ExecuteRestoreAsync);
        ClearLogsCommand = new RelayCommand(() => OperationLogs.Clear());

        RefreshCurrentSystemStats();
    }

    public void RefreshCurrentSystemStats()
    {
        CurrentDbCount = _dbManager.Databases.Count;
        CurrentPlcCount = _plcManager.Plcs.Count;
        CurrentTagCount = _plcManager.Tags.Count;

        CurrentSystemSummary = $"🖥️ Máy: {CurrentMachineName} | 🗄️ CSDL: {CurrentDbCount} kết nối | 🔌 PLC: {CurrentPlcCount} trạm ({CurrentTagCount} tags) | 📷 OQC: Sẵn sàng";
    }

    private void AddLog(string msg)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        OperationLogs.Add($"[{timestamp}] {msg}");
    }

    private async Task ExecuteExportAsync()
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Xuất Toàn Bộ Cấu Hình Hệ Thống",
                Filter = "Gói Cấu Hình Vision (*.json)|*.json|Tất cả tệp (*.*)|*.*",
                FileName = $"CMS_VINA_Config_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (sfd.ShowDialog() != true) return;

            IsBusy = true;
            StatusMessage = "⏳ Đang trích xuất và đóng gói toàn bộ cấu hình...";
            StatusBrush = Brushes.DodgerBlue;
            AddLog($"Bắt đầu đóng gói cấu hình ra tệp: {sfd.FileName}");

            var options = new SystemConfigBackupOptions
            {
                IncludeAppSettings = IncludeAppSettings,
                IncludePlcConfig = IncludePlcConfig,
                IncludeDatabaseConfig = IncludeDatabaseConfig,
                IncludeOqcConfig = IncludeOqcConfig,
                IncludeCalibrationConfig = IncludeCalibrationConfig,
                IncludeCameraConfig = IncludeCameraConfig
            };

            var (success, path, error) = await _backupService.ExportBackupPackageToFileAsync(sfd.FileName, options);

            if (success)
            {
                StatusMessage = "✅ Xuất gói cấu hình hệ thống thành công!";
                StatusBrush = Brushes.Green;
                AddLog($"✅ Ghi tệp thành công ({new FileInfo(path).Length / 1024.0:F1} KB).");
                AddLog("💡 Bạn có thể copy tệp này sang máy khác và nạp lại để sử dụng ngay.");

                System.Windows.MessageBox.Show(
                    $"✅ Xuất toàn bộ cấu hình hệ thống thành công!\n\nĐường dẫn:\n{path}\n\nĐể chuyển sang máy khác, chỉ cần sao chép tệp này và dùng chức năng 'Nạp Cấu Hình'.",
                    "Xuất Cấu Hình Thành Công",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"❌ Xuất cấu hình thất bại: {error}";
                StatusBrush = Brushes.Red;
                AddLog($"❌ Lỗi: {error}");

                System.Windows.MessageBox.Show(
                    $"❌ Không thể xuất cấu hình: {error}",
                    "Lỗi Xuất Cấu Hình",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi: {ex.Message}";
            StatusBrush = Brushes.Red;
            AddLog($"❌ Lỗi ngoại lệ: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteBrowseImportFileAsync()
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn Tệp Gói Cấu Hình Vision Để Nạp",
                Filter = "Gói Cấu Hình Vision (*.json)|*.json|Tất cả tệp (*.*)|*.*"
            };

            if (ofd.ShowDialog() != true) return;

            SelectedImportFilePath = ofd.FileName;
            AddLog($"Đang kiểm tra gói cấu hình: {Path.GetFileName(SelectedImportFilePath)}...");

            var inspection = _backupService.InspectBackupPackage(SelectedImportFilePath);
            InspectedPackage = inspection;
            IsPackageLoaded = inspection.IsValid;

            if (inspection.IsValid)
            {
                StatusMessage = $"🔍 Đã nhận dạng gói sao lưu từ máy '{inspection.ExportedFromMachine}' ({inspection.ExportedAt:dd/MM/yyyy HH:mm}).";
                StatusBrush = Brushes.Green;
                AddLog($"Đã kiểm tra hợp lệ gói cấu hình:");
                AddLog($"• Máy xuất: {inspection.ExportedFromMachine}");
                AddLog($"• Ngày xuất: {inspection.ExportedAt:dd/MM/yyyy HH:mm:ss}");
                AddLog($"• Database: {inspection.DatabaseCount} kết nối ({string.Join(", ", inspection.DatabaseNames)})");
                AddLog($"• PLC: {inspection.PlcCount} trạm ({inspection.TagCount} biến Tags)");
                AddLog($"• Cấu hình OQC: {(inspection.HasOqcConfig ? "Có (Đầy đủ câu lệnh SQL)" : "Không")}");
                AddLog($"• Cài đặt App: {(inspection.HasAppSettings ? "Có (Theme, Đèn, OTA)" : "Không")}");
                AddLog($"• Hiệu chuẩn: {(inspection.HasCalibrationConfig ? "Có (Ma trận Chessboard)" : "Không")}");
            }
            else
            {
                StatusMessage = $"❌ Tệp không hợp lệ: {inspection.ErrorMessage}";
                StatusBrush = Brushes.Red;
                AddLog($"❌ Tệp không hợp lệ: {inspection.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi đọc tệp: {ex.Message}";
            StatusBrush = Brushes.Red;
            AddLog($"❌ Lỗi đọc tệp: {ex.Message}");
        }
    }

    private async Task ExecuteRestoreAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedImportFilePath) || !File.Exists(SelectedImportFilePath))
        {
            System.Windows.MessageBox.Show("Vui lòng chọn tệp sao lưu hợp lệ trước khi thực hiện nạp.", "Thông Báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "Bạn có chắc chắn muốn nạp toàn bộ cấu hình từ tệp này vào máy tính không?\n\nCác cấu hình tương ứng trên máy sẽ được cập nhật đồng bộ để máy chạy được ngay.",
            "Xác Nhận Nạp Cấu Hình",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsBusy = true;
            StatusMessage = "⏳ Đang nạp và đồng bộ cấu hình hệ thống...";
            StatusBrush = Brushes.DodgerBlue;
            AddLog($"Bắt đầu phục hồi cấu hình từ: {Path.GetFileName(SelectedImportFilePath)}");

            var options = new SystemConfigRestoreOptions
            {
                RestoreAppSettings = RestoreAppSettings,
                RestorePlcConfig = RestorePlcConfig,
                RestoreDatabaseConfig = RestoreDatabaseConfig,
                RestoreOqcConfig = RestoreOqcConfig,
                RestoreCalibrationConfig = RestoreCalibrationConfig,
                RestoreCameraConfig = RestoreCameraConfig
            };

            var res = await _backupService.RestoreBackupPackageFromFileAsync(SelectedImportFilePath, options);

            foreach (var log in res.Logs)
            {
                AddLog(log);
            }

            if (res.Success)
            {
                // Reload runtime hot
                _appSettingsService.Reload();
                RefreshCurrentSystemStats();

                StatusMessage = "🎉 Phục hồi toàn bộ cấu hình thành công! Hệ thống sẵn sàng hoạt động.";
                StatusBrush = Brushes.Green;

                System.Windows.MessageBox.Show(
                    "🎉 NẠP CẤU HÌNH THÀNH CÔNG!\n\n" +
                    $"• Cơ sở dữ liệu: Đã nạp {res.DatabasesRestored} kết nối\n" +
                    $"• PLC: Đã nạp {res.PlcsRestored} trạm PLC ({res.TagsRestored} tags)\n" +
                    $"• OQC Scanner: Đã nạp toàn bộ câu lệnh & tự động khớp Database ComboBox\n" +
                    $"• Cài đặt App: {(res.AppSettingsRestored ? "Đã đồng bộ" : "Giữ nguyên")}\n" +
                    $"• Hiệu chuẩn: {(res.CalibrationRestored ? "Đã nạp" : "Giữ nguyên")}\n\n" +
                    "Toàn bộ cấu hình đã được áp dụng tức thì vào bộ nhớ.",
                    "Thành Công",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"❌ Lỗi nạp cấu hình: {res.Message}";
                StatusBrush = Brushes.Red;

                System.Windows.MessageBox.Show(
                    $"❌ Nạp cấu hình thất bại:\n{res.Message}",
                    "Lỗi",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi: {ex.Message}";
            StatusBrush = Brushes.Red;
            AddLog($"❌ Ngoại lệ: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex = 1; // Default to OQC Scanner tab (Index 1)

    [ObservableProperty]
    private string _globalStatusMessage = "Hệ thống sẵn sàng.";

    [ObservableProperty]
    private string _globalStatusSeverity = "Info"; // Info, Warning, Error, Success

    [ObservableProperty]
    private bool _hasUpdateAvailable = false;

    [ObservableProperty]
    private string _updateBadgeText = "";

    private readonly IRecentJobsService? _recentJobsService;
    private readonly LightingControllerService? _lightingService;
    private readonly IOtaUpdateService? _otaService;
    private readonly GlobalAppSettingsService? _settingsService;
    private readonly IServiceProvider? _serviceProvider;

    public ObservableCollection<string> RecentJobs { get; } = new();

    public MainWindowViewModel(
        ToolEditorViewModel toolEditor,
        CalibrationViewModel calibration,
        ManualInspectionViewModel manualInspection,
        InspectionViewModel inspection,
        OqcScannerViewModel oqcScanner,
        CameraSettingsViewModel cameraSettings,
        IRecentJobsService? recentJobsService = null,
        LightingControllerService? lightingService = null,
        IOtaUpdateService? otaService = null,
        GlobalAppSettingsService? settingsService = null,
        IServiceProvider? serviceProvider = null)
    {
        ToolEditor = toolEditor;
        Calibration = calibration;
        ManualInspection = manualInspection;
        Inspection = inspection;
        OqcScanner = oqcScanner;
        OqcScanner.RequestSwitchTab = idx => SelectedTabIndex = idx;
        ToolEditor.CheckShouldAutoRunOnJobLoad = () => SelectedTabIndex == 1 && OqcScanner.AutoRunJob;
        CameraSettings = cameraSettings;
        _recentJobsService = recentJobsService;
        _lightingService = lightingService;
        _otaService = otaService;
        _settingsService = settingsService;
        _serviceProvider = serviceProvider;

        if (_lightingService != null)
        {
            _lightingService.OnError += (s, errMsg) =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    SetGlobalStatus($"⚠️ [Đèn Chiếu Sáng] {errMsg}", "Warning");
                });
            };

            _lightingService.OnConnectionStateChanged += (s, state) =>
            {
                if (state == LightingConnectionState.Connected)
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        SetGlobalStatus("💡 Đèn Chiếu Sáng: Đã kết nối thành công.", "Success");
                    });
                }
            };
        }

        if (_recentJobsService != null)
        {
            _recentJobsService.RecentJobsChanged += ReloadRecentJobs;
            ReloadRecentJobs();
        }

        ToolEditor.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(ToolEditor.CurrentJobFilePath) or nameof(ToolEditor.ProductCode) or nameof(ToolEditor.IsDirty))
            {
                OnPropertyChanged(nameof(HeaderJobTitle));
                OnPropertyChanged(nameof(HeaderProductCodeTitle));
            }
        };

        CloseJobCommand = new RelayCommand(CloseJob);
        SwitchTabCommand = new RelayCommand<object>(ExecuteSwitchTab);
        ExitCommand = new RelayCommand(ExecuteExit);
        AboutCommand = new RelayCommand(ExecuteAbout);
        OpenRecentJobCommand = new RelayCommand<string>(ExecuteOpenRecentJob);
        ClearRecentJobsCommand = new RelayCommand(ExecuteClearRecentJobs);
        OpenOtaUpdateDialogCommand = new RelayCommand(ExecuteOpenOtaUpdateDialog);
        OpenDocumentationCommand = new RelayCommand<string>(ExecuteOpenDocumentation);
        OpenDocsFolderCommand = new RelayCommand(ExecuteOpenDocsFolder);

        if (_selectedTabIndex == 3)
        {
            CameraSettings.OnViewActivated();
        }

        if (_settingsService?.Settings.Ota.AutoCheckOnStartup == true && _otaService != null)
        {
            _ = CheckUpdateInBackgroundAsync();
        }
    }

    public void SetGlobalStatus(string message, string severity = "Info")
    {
        GlobalStatusMessage = message;
        GlobalStatusSeverity = severity;
        if (ToolEditor != null)
        {
            ToolEditor.StatusBarText = message;
        }
    }

    public ICommand OpenOtaUpdateDialogCommand { get; }
    public ICommand OpenDocumentationCommand { get; }
    public ICommand OpenDocsFolderCommand { get; }

    private void ExecuteOpenDocumentation(string? docId)
    {
        try
        {
            var win = new Views.Documentation.DocumentationViewerWindow(docId)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở cửa sổ tài liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteOpenDocsFolder()
    {
        try
        {
            var docsDir = DocumentationService.ResolveDocsDirectory();
            if (Directory.Exists(docsDir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", docsDir) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show($"Thư mục tài liệu chưa tồn tại tại: {docsDir}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteOpenOtaUpdateDialog()
    {
        if (_otaService == null || _settingsService == null) return;

        var vm = new OtaUpdateViewModel(_otaService, _settingsService);
        var dialog = new Views.OTA.OtaUpdateDialog(vm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dialog.ShowDialog();

        if (!vm.HasUpdate)
        {
            HasUpdateAvailable = false;
        }
    }

    private async Task CheckUpdateInBackgroundAsync()
    {
        try
        {
            await Task.Delay(2500).ConfigureAwait(false); // Chờ 2.5s sau khi mở app để nhường CPU khởi tạo
            if (_otaService == null || _settingsService == null) return;

            var otaCfg = _settingsService.Settings.Ota;
            var result = await _otaService.CheckForUpdateAsync(otaCfg.UpdateServerUrl, otaCfg.UpdateSourceType).ConfigureAwait(false);

            if (result.HasUpdate && result.Manifest != null)
            {
                if (!string.Equals(result.Manifest.Version, otaCfg.IgnoredVersion, StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        HasUpdateAvailable = true;
                        UpdateBadgeText = $"🚀 Có Bản Mới: v{result.Manifest.Version}";
                        SetGlobalStatus($"🎉 Phát hiện bản cập nhật mới (v{result.Manifest.Version}). Nhấn menu Trợ Giúp để cập nhật.", "Info");
                    });
                }
            }
        }
        catch { }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 3)
        {
            CameraSettings.OnViewActivated();
        }
        else
        {
            CameraSettings.OnViewDeactivated();
        }

        Task.Run(() =>
        {
            try
            {
                GC.Collect(1, GCCollectionMode.Optimized);
            }
            catch { }
        });
    }

    public string HeaderJobTitle
    {
        get
        {
            var jobName = string.IsNullOrWhiteSpace(ToolEditor.CurrentJobFilePath)
                ? "[Chưa lưu]"
                : Path.GetFileName(ToolEditor.CurrentJobFilePath);
            var dirtyMark = ToolEditor.IsDirty ? " *" : "";
            return $"📁 Job: {jobName}{dirtyMark}";
        }
    }

    public string HeaderProductCodeTitle
    {
        get
        {
            var prodCode = string.IsNullOrWhiteSpace(ToolEditor.ProductCode)
                ? "--"
                : ToolEditor.ProductCode;
            var dirtyMark = ToolEditor.IsDirty ? " *" : "";
            return $"🏷️ SP: {prodCode}{dirtyMark}";
        }
    }

    public ICommand CloseJobCommand { get; }
    public ICommand SwitchTabCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand OpenRecentJobCommand { get; }
    public ICommand ClearRecentJobsCommand { get; }

    private void ReloadRecentJobs()
    {
        RecentJobs.Clear();
        if (_recentJobsService != null)
        {
            foreach (var j in _recentJobsService.GetRecentJobs())
            {
                RecentJobs.Add(j);
            }
        }
    }

    private void ExecuteOpenRecentJob(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                _recentJobsService?.RemoveRecentJob(filePath);
                MessageBox.Show($"Tệp Job không tồn tại hoặc đã bị di chuyển:\n{filePath}", "Không tìm thấy tệp", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        ToolEditor.LoadJobFromFile(filePath);
        SelectedTabIndex = 0; // Chuyển sang màn hình Tool Editor
    }

    private void ExecuteClearRecentJobs()
    {
        _recentJobsService?.ClearRecentJobs();
    }

    private void ExecuteSwitchTab(object? parameter)
    {
        if (parameter is null) return;
        if (int.TryParse(parameter.ToString(), out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    private void ExecuteExit()
    {
        System.Windows.Application.Current?.MainWindow?.Close();
    }

    private void ExecuteAbout()
    {
        var currentVer = _otaService?.CurrentVersion?.ToString() ?? "1.0.0.0";
        MessageBox.Show(
            $"CMS VINA VISION SYSTEM — Enterprise Industrial Vision Platform\n" +
            $"Version {currentVer} (64-bit Edition)\n\n" +
            "© 2026 CMS VINA Co., Ltd. All rights reserved.\n" +
            "Industrial Machine Vision, Multi-camera Inspection, OQC & Automation Integration.\n\n" +
            "────────────────────────────────────────\n" +
            "Tác giả: Nguyễn Văn Hùng\n" +
            "Phone: +84971092454\n" +
            "Email: pagehungnguyen.com\n" +
            "Web: hungnguyenpage.com",
            "About CMS VINA Vision System",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CloseJob()
    {
        ToolEditor.CloseJob();
        Calibration.CloseJob();
        Inspection.CloseJob();
        
        if (System.Windows.Application.Current?.MainWindow != null)
        {
            System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM";
        }
    }

    public ToolEditorViewModel ToolEditor { get; }

    public CalibrationViewModel Calibration { get; }

    public ManualInspectionViewModel ManualInspection { get; }

    public InspectionViewModel Inspection { get; }

    public OqcScannerViewModel OqcScanner { get; }

    public CameraSettingsViewModel CameraSettings { get; }
}

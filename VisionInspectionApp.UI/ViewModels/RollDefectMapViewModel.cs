using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels;

public partial class RollDefectMapViewModel : ObservableObject, IDisposable
{
    private readonly RollDefectManager _defectManager;
    private readonly PlcMotionSyncService _motionSyncService;
    private readonly ShiftRegisterTracker _shiftRegisterTracker;
    private readonly DispatcherTimer _refreshTimer;
    private bool _disposed;

    [ObservableProperty]
    private RollSession? _session;

    [ObservableProperty]
    private double _currentWebPositionMm;

    [ObservableProperty]
    private int _totalDefectsCount;

    [ObservableProperty]
    private int _rejectCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private double _yieldRate = 100.0;

    [ObservableProperty]
    private int _pendingRejectCount;

    [ObservableProperty]
    private long _totalRejectsTriggered;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng giám sát bản đồ cuộn thời gian thực.";

    public RollDefectMapViewModel(RollDefectManager defectManager, PlcMotionSyncService motionSyncService, ShiftRegisterTracker shiftRegisterTracker)
    {
        _defectManager = defectManager ?? throw new ArgumentNullException(nameof(defectManager));
        _motionSyncService = motionSyncService ?? throw new ArgumentNullException(nameof(motionSyncService));
        _shiftRegisterTracker = shiftRegisterTracker ?? throw new ArgumentNullException(nameof(shiftRegisterTracker));

        Session = _defectManager.CurrentSession;
        UpdateMetrics();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _refreshTimer.Tick += (_, _) => UpdateMetrics();
        _refreshTimer.Start();
    }

    private void UpdateMetrics()
    {
        if (_disposed) return;

        Session = _defectManager.CurrentSession;
        CurrentWebPositionMm = _motionSyncService.CurrentWebPositionMm;

        if (Session != null)
        {
            TotalDefectsCount = Session.TotalDefects;
            RejectCount = Session.RejectCount;
            WarningCount = Session.WarningCount;

            double goodMeters = Math.Max(0, Session.TotalLengthMeters - (RejectCount * 0.5));
            YieldRate = Session.TotalLengthMeters > 0
                ? Math.Clamp((goodMeters / Session.TotalLengthMeters) * 100.0, 0.0, 100.0)
                : 100.0;
        }

        PendingRejectCount = _shiftRegisterTracker.PendingCount;
        TotalRejectsTriggered = _shiftRegisterTracker.TotalRejectsTriggered;
    }

    [RelayCommand]
    private void ExportJson()
    {
        if (Session == null || Session.Defects.Count == 0)
        {
            MessageBox.Show("Phiên cuộn hiện tại chưa có dữ liệu vết lỗi để xuất!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Xuất Dữ Liệu Cuộn Sang JSON",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = $"Roll_{Session.RollId}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                RollReportExporter.ExportToJson(Session, dlg.FileName);
                StatusMessage = $"Đã xuất dữ liệu JSON thành công: {Path.GetFileName(dlg.FileName)}";
                MessageBox.Show($"Đã xuất dữ liệu JSON thành công!\nĐường dẫn: {dlg.FileName}", "Xuất Báo Cáo Cuộn", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xuất JSON: {ex.Message}", "Lỗi Xuất File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (Session == null || Session.Defects.Count == 0)
        {
            MessageBox.Show("Phiên cuộn hiện tại chưa có dữ liệu vết lỗi để xuất Cut List CSV!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Xuất Danh Sách Cắt (Cut List) Sang CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = $"CutList_{Session.RollId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                RollReportExporter.ExportToCsv(Session, dlg.FileName);
                StatusMessage = $"Đã xuất bảng Cut List CSV thành công: {Path.GetFileName(dlg.FileName)}";
                MessageBox.Show($"Đã xuất bảng Cut List CSV thành công!\nĐường dẫn: {dlg.FileName}", "Xuất Báo Cáo Cuộn", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xuất CSV: {ex.Message}", "Lỗi Xuất File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ExportHtml()
    {
        if (Session == null)
        {
            MessageBox.Show("Phiên cuộn chưa được khởi tạo!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Xuất Chứng Chỉ Chất Lượng Cuộn Sang HTML",
            Filter = "HTML Files (*.html)|*.html|All Files (*.*)|*.*",
            FileName = $"RollCertificate_{Session.RollId}_{DateTime.Now:yyyyMMdd_HHmmss}.html"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                RollReportExporter.ExportToHtmlCertificate(Session, dlg.FileName);
                StatusMessage = $"Đã xuất chứng chỉ HTML thành công: {Path.GetFileName(dlg.FileName)}";

                var res = MessageBox.Show($"Đã xuất chứng chỉ HTML thành công!\nBạn có muốn mở ngay trên trình duyệt web không?", "Xuất Chứng Chỉ Chất Lượng", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dlg.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xuất HTML: {ex.Message}", "Lỗi Xuất File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ResetSession()
    {
        var res = MessageBox.Show("Bạn có chắc chắn muốn bắt đầu một Phiên Cuộn Mới không?\nDữ liệu cuộn cũ sẽ được lưu trữ và bộ đếm mét dài sẽ reset về 0m.", "Khởi Tạo Phiên Cuộn Mới", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            _defectManager.StartNewSession($"ROLL_{DateTime.Now:yyyyMMdd_HHmmss}", _defectManager.CurrentSession.RollWidthMm, 0.0);
            UpdateMetrics();
            StatusMessage = "Đã khởi tạo Phiên Cuộn Mới thành công.";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Stop();
    }
}

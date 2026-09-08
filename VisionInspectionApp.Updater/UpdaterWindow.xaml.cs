using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows;

namespace VisionInspectionApp.Updater;

public partial class UpdaterWindow : Window
{
    private int _targetPid = -1;
    private string _packageZipPath = "";
    private string _targetAppDir = "";
    private string _restartExePath = "";

    public UpdaterWindow()
    {
        InitializeComponent();
        ParseCommandLineArgs();
    }

    private void ParseCommandLineArgs()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out _targetPid);
            }
            else if (string.Equals(args[i], "--package", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _packageZipPath = args[++i].Trim('"', ' ');
            }
            else if (string.Equals(args[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string targetVal = args[++i].Trim('"', ' ');
                // Phòng ngừa lỗi Windows escape khi đường dẫn thư mục kết thúc bằng \"
                int restartIdx = targetVal.IndexOf("--restart", StringComparison.OrdinalIgnoreCase);
                if (restartIdx >= 0)
                {
                    string extractedTarget = targetVal.Substring(0, restartIdx).Trim('"', ' ', '\\', '/');
                    string extractedRestart = targetVal.Substring(restartIdx + "--restart".Length).Trim('"', ' ');
                    _targetAppDir = extractedTarget;
                    if (string.IsNullOrWhiteSpace(_restartExePath))
                    {
                        _restartExePath = extractedRestart;
                    }
                }
                else
                {
                    _targetAppDir = targetVal.TrimEnd('\\', '/');
                }
            }
            else if (string.Equals(args[i], "--restart", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _restartExePath = args[++i].Trim('"', ' ');
            }
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Chờ giao diện hiển thị xong rồi mới bắt đầu tiến trình cập nhật
        await Task.Delay(300);
        await RunUpdatePipelineAsync();
    }

    private async Task RunUpdatePipelineAsync()
    {
        string backupDir = "";

        try
        {
            // 1. Kiểm tra tham số đầu vào
            if (string.IsNullOrWhiteSpace(_packageZipPath) || !File.Exists(_packageZipPath))
            {
                throw new FileNotFoundException($"Không tìm thấy tệp gói cập nhật: '{_packageZipPath}'");
            }

            if (string.IsNullOrWhiteSpace(_targetAppDir) || !Directory.Exists(_targetAppDir))
            {
                _targetAppDir = AppDomain.CurrentDomain.BaseDirectory;
            }

            // 2. Chờ ứng dụng chính kết thúc để nhả file locks
            SetStatus("Đang chờ đóng ứng dụng chính...", "Đang giải phóng các tiến trình và khóa tệp...", 10);
            if (_targetPid > 0)
            {
                await WaitForProcessExitAsync(_targetPid, TimeSpan.FromSeconds(15));
            }
            await Task.Delay(600); // Đệm để Windows nhả toàn bộ file handle

            // 3. Sao lưu phiên bản hiện tại (Backup)
            SetStatus("Đang sao lưu phiên bản hiện tại...", "Tạo bản sao lưu đề phòng lỗi...", 25);
            backupDir = Path.Combine(_targetAppDir, "backup", $"backup_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(backupDir);
            await BackupTargetFilesAsync(_targetAppDir, backupDir);

            // 4. Giải nén gói cập nhật mới
            SetStatus("Đang giải nén gói cập nhật mới...", "Ghi đè các thư viện và tệp thực thi mới...", 45);
            await ExtractUpdatePackageAsync(_packageZipPath, _targetAppDir);

            // 5. Hoàn tất & Khởi động lại ứng dụng chính
            SetStatus("Cập nhật thành công!", "Đang khởi động lại ứng dụng Vision System...", 100);
            await Task.Delay(800);

            bool restarted = RestartMainApplication("--updated");
            if (!restarted)
            {
                MessageBox.Show(
                    this,
                    "Bản cập nhật đã được cài đặt thành công 100%!\n\nTuy nhiên hệ thống không thể tự động khởi chạy lại ứng dụng.\nVui lòng mở lại ứng dụng từ màn hình Desktop hoặc thư mục cài đặt.",
                    "Cập Nhật Thành Công - Vision Updater",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                // Cho phép tiến trình mới kịp khởi động trước khi Updater tắt hoàn toàn
                await Task.Delay(600);
            }

            // Xóa file zip tạm sau khi thành công
            try { File.Delete(_packageZipPath); } catch { }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            SetStatus("Cập nhật gặp lỗi! Đang khôi phục bản cũ...", ex.Message, 100);

            // Rollback từ thư mục backup nếu có
            if (!string.IsNullOrWhiteSpace(backupDir) && Directory.Exists(backupDir))
            {
                try
                {
                    await RestoreFromBackupAsync(backupDir, _targetAppDir);
                }
                catch { }
            }

            // Ghi nhật ký lỗi chi tiết ra file updater_error.log
            string errorLogPath = Path.Combine(_targetAppDir, "updater_error.log");
            string errorLogContent = $@"================================================================================
CMS VINA VISION UPDATER - BÁO CÁO LỖI CẬP NHẬT
================================================================================
Thời gian: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Thư mục đích: {_targetAppDir}
Gói cập nhật: {_packageZipPath}
PID tiến trình chính: {_targetPid}
Tệp khởi động lại: {_restartExePath}
--------------------------------------------------------------------------------
THÔNG BÁO LỖI:
{ex.Message}

CHI TIẾT NGOẠI LỆ (STACK TRACE):
{ex}
================================================================================
";
            try
            {
                File.WriteAllText(errorLogPath, errorLogContent, System.Text.Encoding.UTF8);
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(backupDir) && Directory.Exists(backupDir))
            {
                try
                {
                    File.WriteAllText(Path.Combine(backupDir, "updater_error.log"), errorLogContent, System.Text.Encoding.UTF8);
                }
                catch { }
            }

            // Hiển thị hộp thoại thông báo lỗi chi tiết cho người dùng biết rõ nguyên nhân
            try
            {
                MessageBox.Show(
                    this,
                    $"Quá trình cài đặt bản cập nhật gặp sự cố và hệ thống đã tự động khôi phục (Restore) lại phiên bản cũ an toàn.\n\n" +
                    $"NGUYÊN NHÂN LỖI CỤ THỂ:\n{ex.Message}\n\n" +
                    $"Nhật ký lỗi chi tiết đã được lưu tại:\n{errorLogPath}",
                    "Lỗi Cài Đặt Bản Cập Nhật OTA - Vision Updater",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
                // Fallback nếu không có window context
                MessageBox.Show(
                    $"Quá trình cài đặt bản cập nhật gặp sự cố và hệ thống đã tự động khôi phục (Restore) lại phiên bản cũ an toàn.\n\n" +
                    $"NGUYÊN NHÂN LỖI CỤ THỂ:\n{ex.Message}\n\n" +
                    $"Nhật ký lỗi chi tiết đã được lưu tại:\n{errorLogPath}",
                    "Lỗi Cài Đặt Bản Cập Nhật OTA - Vision Updater",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Khởi động lại bản cũ
            RestartMainApplication("--update-failed");

            Environment.Exit(1);
        }
    }

    private string ResolveRestartExecutablePath()
    {
        if (string.IsNullOrWhiteSpace(_targetAppDir) || !Directory.Exists(_targetAppDir))
        {
            _targetAppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        }

        // 1. Kiểm tra nếu _restartExePath đã được truyền và tồn tại
        if (!string.IsNullOrWhiteSpace(_restartExePath))
        {
            string clean = _restartExePath.Trim('"', ' ');
            if (File.Exists(clean)) return Path.GetFullPath(clean);

            string combined = Path.Combine(_targetAppDir, clean);
            if (File.Exists(combined)) return Path.GetFullPath(combined);
        }

        // 2. Tìm file VisionInspectionApp.UI.exe trong thư mục target
        string mainAppPath = Path.Combine(_targetAppDir, "VisionInspectionApp.UI.exe");
        if (File.Exists(mainAppPath))
        {
            return Path.GetFullPath(mainAppPath);
        }

        // 3. Quét các file .exe khác trong _targetAppDir, ưu tiên file có chữ Vision hoặc Inspection
        try
        {
            var exes = Directory.GetFiles(_targetAppDir, "*.exe", SearchOption.TopDirectoryOnly);
            foreach (var exe in exes)
            {
                string name = Path.GetFileName(exe);
                if (name.StartsWith("VisionUpdater", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("Vision", StringComparison.OrdinalIgnoreCase) || name.Contains("Inspection", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(exe);
                }
            }

            foreach (var exe in exes)
            {
                string name = Path.GetFileName(exe);
                if (!name.StartsWith("VisionUpdater", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(exe);
                }
            }
        }
        catch { }

        return "";
    }

    private bool RestartMainApplication(string arguments)
    {
        string exeToStart = ResolveRestartExecutablePath();
        if (string.IsNullOrWhiteSpace(exeToStart) || !File.Exists(exeToStart))
        {
            return false;
        }

        string workDir = !string.IsNullOrWhiteSpace(_targetAppDir) && Directory.Exists(_targetAppDir)
            ? _targetAppDir
            : (Path.GetDirectoryName(exeToStart) ?? AppDomain.CurrentDomain.BaseDirectory);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exeToStart,
                WorkingDirectory = workDir,
                Arguments = arguments,
                UseShellExecute = true
            };
            var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            try
            {
                var psiFallback = new ProcessStartInfo
                {
                    FileName = exeToStart,
                    WorkingDirectory = workDir,
                    Arguments = arguments,
                    UseShellExecute = false
                };
                var proc = Process.Start(psiFallback);
                return proc != null;
            }
            catch
            {
                return false;
            }
        }
    }

    private static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            var stopwatch = Stopwatch.StartNew();

            while (!proc.HasExited && stopwatch.Elapsed < timeout)
            {
                await Task.Delay(250);
                proc.Refresh();
            }

            if (!proc.HasExited)
            {
                // Nếu quá timeout vẫn chưa thoát, buộc dừng process để tránh treo
                proc.Kill();
                await Task.Delay(500);
            }
        }
        catch (ArgumentException)
        {
            // Process đã thoát từ trước
        }
        catch { }

        // Đảm bảo không còn bất kỳ tiến trình VisionInspectionApp hoặc LightingServer nào khác đang chạy ngầm
        try
        {
            foreach (var p in Process.GetProcessesByName("VisionInspectionApp.UI"))
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }
            foreach (var p in Process.GetProcessesByName("VisionInspectionApp.LightingServer"))
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static Task BackupTargetFilesAsync(string sourceDir, string destDir)
    {
        return Task.Run(() =>
        {
            // Sao lưu các tệp .exe, .dll, .json, .config ở thư mục gốc
            var extensions = new[] { "*.exe", "*.dll", "*.json", "*.config" };
            foreach (var ext in extensions)
            {
                foreach (var file in Directory.GetFiles(sourceDir, ext, SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(file);
                    // Bỏ qua chính file updater
                    if (fileName.StartsWith("VisionUpdater", StringComparison.OrdinalIgnoreCase)) continue;

                    string destFile = Path.Combine(destDir, fileName);
                    try { File.Copy(file, destFile, true); } catch { }
                }
            }
        });
    }

    private Task ExtractUpdatePackageAsync(string zipPath, string targetDir)
    {
        return Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            int total = archive.Entries.Count;
            int current = 0;

            foreach (var entry in archive.Entries)
            {
                current++;
                if (string.IsNullOrEmpty(entry.Name)) // Thư mục
                {
                    string dirPath = Path.Combine(targetDir, entry.FullName);
                    Directory.CreateDirectory(dirPath);
                    continue;
                }

                // Không ghi đè các tệp của chính VisionUpdater đang chạy (.exe, .dll, .runtimeconfig.json, v.v.)
                if (entry.Name.StartsWith("VisionUpdater", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                if (!destinationPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                {
                    // Chống Zip Slip vulnerability
                    continue;
                }

                string? parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // Thử ghi đè với retry và fallback đổi tên nếu file bị khóa
                bool success = false;
                Exception? lastEx = null;
                for (int retry = 0; retry < 6; retry++)
                {
                    try
                    {
                        entry.ExtractToFile(destinationPath, overwrite: true);
                        success = true;
                        break;
                    }
                    catch (IOException ioEx)
                    {
                        lastEx = ioEx;
                        // Thử đổi tên tệp cũ nếu đang bị khóa (Windows NTFS cho phép đổi tên file đang mở)
                        try
                        {
                            string tempOld = destinationPath + ".old_" + Guid.NewGuid().ToString("N")[..6];
                            if (File.Exists(tempOld)) File.Delete(tempOld);
                            File.Move(destinationPath, tempOld);
                            entry.ExtractToFile(destinationPath, overwrite: true);
                            success = true;
                            break;
                        }
                        catch { }
                        System.Threading.Thread.Sleep(300);
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        System.Threading.Thread.Sleep(300);
                    }
                }

                if (!success)
                {
                    throw new IOException($"Không thể ghi đè tệp '{destinationPath}'. Tệp có thể đang bị khóa bởi tiến trình khác. Chi tiết: {lastEx?.Message}", lastEx);
                }

                if (current % 5 == 0 || current == total)
                {
                    double pct = 45.0 + ((double)current / total * 50.0);
                    Dispatcher.Invoke(() =>
                    {
                        UpdateProgressBar.Value = pct;
                        DetailTextBlock.Text = $"Đang giải nén: {entry.Name} ({current}/{total})";
                    });
                }
            }
        });
    }

    private static Task RestoreFromBackupAsync(string backupDir, string targetDir)
    {
        return Task.Run(() =>
        {
            foreach (var file in Directory.GetFiles(backupDir, "*.*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(backupDir, file);
                string destFile = Path.Combine(targetDir, relPath);
                try
                {
                    string? p = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(p)) Directory.CreateDirectory(p);
                    File.Copy(file, destFile, true);
                }
                catch { }
            }
        });
    }

    private void SetStatus(string status, string detail, double progress)
    {
        Dispatcher.Invoke(() =>
        {
            StatusTextBlock.Text = status;
            DetailTextBlock.Text = detail;
            UpdateProgressBar.Value = progress;
        });
    }
}

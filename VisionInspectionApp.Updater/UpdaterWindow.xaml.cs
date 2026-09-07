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
                _packageZipPath = args[++i];
            }
            else if (string.Equals(args[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _targetAppDir = args[++i];
            }
            else if (string.Equals(args[i], "--restart", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _restartExePath = args[++i];
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

            if (!string.IsNullOrWhiteSpace(_restartExePath) && File.Exists(_restartExePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _restartExePath,
                    UseShellExecute = true,
                    Arguments = "--updated"
                });
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

            await Task.Delay(2500);

            // Khởi động lại bản cũ
            if (!string.IsNullOrWhiteSpace(_restartExePath) && File.Exists(_restartExePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _restartExePath,
                        UseShellExecute = true
                    });
                }
                catch { }
            }

            Environment.Exit(1);
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

            string myExeName = Process.GetCurrentProcess().MainModule?.ModuleName ?? "VisionUpdater.exe";

            foreach (var entry in archive.Entries)
            {
                current++;
                if (string.IsNullOrEmpty(entry.Name)) // Thư mục
                {
                    string dirPath = Path.Combine(targetDir, entry.FullName);
                    Directory.CreateDirectory(dirPath);
                    continue;
                }

                // Không ghi đè chính VisionUpdater đang chạy
                if (string.Equals(entry.Name, myExeName, StringComparison.OrdinalIgnoreCase))
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

                // Thử ghi đè với retry
                bool success = false;
                for (int retry = 0; retry < 5; retry++)
                {
                    try
                    {
                        entry.ExtractToFile(destinationPath, overwrite: true);
                        success = true;
                        break;
                    }
                    catch (IOException)
                    {
                        System.Threading.Thread.Sleep(200);
                    }
                }

                if (!success)
                {
                    throw new IOException($"Không thể ghi đè tệp '{destinationPath}'. Tệp có thể đang bị khóa bởi tiến trình khác.");
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

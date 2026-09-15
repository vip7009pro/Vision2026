using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.Services;

public class DotnetRuntimeService : IDotnetRuntimeService, IDisposable
{
    private static IDotnetRuntimeService? _instance;
    public static IDotnetRuntimeService Instance => _instance ??= new DotnetRuntimeService();

    public const string MicrosoftX86DirectDownloadUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe";
    public const string MicrosoftOfficialDownloadPageUrl = "https://dotnet.microsoft.com/download/dotnet/8.0";

    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    public DotnetRuntimeService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _disposeClient = false;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            _disposeClient = true;
        }
    }

    /// <summary>
    /// Phương thức tĩnh hỗ trợ kiểm tra nhanh .NET x86 runtime mà không cần khởi tạo DI container.
    /// </summary>
    public static bool IsX86Installed(int minMajorVersion = 8)
    {
        return Instance.IsX86RuntimeInstalled(minMajorVersion);
    }

    public bool IsX86RuntimeInstalled(int minMajorVersion = 8)
    {
        // 1. Kiểm tra trường hợp PlcBridge là self-contained (có sẵn coreclr.dll 32-bit cục bộ)
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(baseDir, "coreclr.dll")) && File.Exists(Path.Combine(baseDir, "VisionInspectionApp.PlcBridge.dll")))
            {
                return true;
            }

            string bridgeSubDir = Path.Combine(baseDir, "VisionInspectionApp.PlcBridge");
            if (File.Exists(Path.Combine(bridgeSubDir, "coreclr.dll")))
            {
                return true;
            }
        }
        catch { }

        // 2. Kiểm tra thư mục hệ thống Program Files (x86)\dotnet\shared
        try
        {
            string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(pfX86))
            {
                string sharedDesktop = Path.Combine(pfX86, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (Directory.Exists(sharedDesktop))
                {
                    foreach (var dir in Directory.GetDirectories(sharedDesktop))
                    {
                        string verName = Path.GetFileName(dir);
                        if (IsVersionAtLeast(verName, minMajorVersion))
                        {
                            return true;
                        }
                    }
                }

                string sharedCore = Path.Combine(pfX86, "dotnet", "shared", "Microsoft.NETCore.App");
                if (Directory.Exists(sharedCore))
                {
                    foreach (var dir in Directory.GetDirectories(sharedCore))
                    {
                        string verName = Path.GetFileName(dir);
                        if (IsVersionAtLeast(verName, minMajorVersion))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Kiểm tra qua Registry Windows (WOW6432Node)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var keyDesktop = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App")
                                    ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App");
                if (keyDesktop != null)
                {
                    foreach (var valName in keyDesktop.GetValueNames())
                    {
                        if (IsVersionAtLeast(valName, minMajorVersion)) return true;
                    }
                    foreach (var subKeyName in keyDesktop.GetSubKeyNames())
                    {
                        if (IsVersionAtLeast(subKeyName, minMajorVersion)) return true;
                    }
                }

                using var keyCore = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.NETCore.App")
                                 ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.NETCore.App");
                if (keyCore != null)
                {
                    foreach (var valName in keyCore.GetValueNames())
                    {
                        if (IsVersionAtLeast(valName, minMajorVersion)) return true;
                    }
                    foreach (var subKeyName in keyCore.GetSubKeyNames())
                    {
                        if (IsVersionAtLeast(subKeyName, minMajorVersion)) return true;
                    }
                }
            }
            catch { }
        }

        // 4. Kiểm tra qua lệnh dotnet.exe x86 nếu có
        try
        {
            string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string x86DotnetExe = Path.Combine(pfX86, "dotnet", "dotnet.exe");
            if (File.Exists(x86DotnetExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = x86DotnetExe,
                    Arguments = "--list-runtimes",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1500);

                    using var reader = new StringReader(output);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Contains("Microsoft.WindowsDesktop.App") || line.Contains("Microsoft.NETCore.App"))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && IsVersionAtLeast(parts[1], minMajorVersion))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return false;
    }

    public bool IsX64RuntimeInstalled(int minMajorVersion = 8)
    {
        try
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string sharedDesktop = Path.Combine(pf, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            if (Directory.Exists(sharedDesktop))
            {
                foreach (var dir in Directory.GetDirectories(sharedDesktop))
                {
                    if (IsVersionAtLeast(Path.GetFileName(dir), minMajorVersion)) return true;
                }
            }

            string sharedCore = Path.Combine(pf, "dotnet", "shared", "Microsoft.NETCore.App");
            if (Directory.Exists(sharedCore))
            {
                foreach (var dir in Directory.GetDirectories(sharedCore))
                {
                    if (IsVersionAtLeast(Path.GetFileName(dir), minMajorVersion)) return true;
                }
            }
        }
        catch { }

        return Environment.Version.Major >= minMajorVersion;
    }

    public string GetX86RuntimeDownloadUrl() => MicrosoftX86DirectDownloadUrl;
    public string GetOfficialDownloadPageUrl() => MicrosoftOfficialDownloadPageUrl;

    public async Task<(bool Success, string InstallerPath, string ErrorMessage)> DownloadX86InstallerAsync(
        IProgress<FileDownloadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string targetFile = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-8.0-win-x86.exe");

        try
        {
            if (File.Exists(targetFile))
            {
                try { File.Delete(targetFile); } catch { }
            }

            using var response = await _httpClient.GetAsync(
                MicrosoftX86DirectDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (false, "", $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            long? totalBytes = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);

            byte[] buffer = new byte[65536];
            int bytesRead;
            long totalDownloaded = 0;
            var sw = Stopwatch.StartNew();
            long lastReportTime = 0;
            long lastReportBytes = 0;

            progress?.Report(new FileDownloadProgressInfo
            {
                BytesDownloaded = 0,
                TotalBytes = totalBytes,
                Percentage = 0,
                SpeedBytesPerSec = 0,
                StatusText = "Bắt đầu tải bộ cài đặt .NET x86 từ Microsoft..."
            });

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalDownloaded += bytesRead;

                long elapsed = sw.ElapsedMilliseconds;
                if (elapsed - lastReportTime >= 100 || (totalBytes.HasValue && totalDownloaded >= totalBytes.Value))
                {
                    double deltaSec = (elapsed - lastReportTime) / 1000.0;
                    double speed = deltaSec > 0 ? (totalDownloaded - lastReportBytes) / deltaSec : 0;
                    double pct = totalBytes.HasValue && totalBytes.Value > 0
                        ? Math.Min(100.0, (double)totalDownloaded / totalBytes.Value * 100.0)
                        : 0;

                    progress?.Report(new FileDownloadProgressInfo
                    {
                        BytesDownloaded = totalDownloaded,
                        TotalBytes = totalBytes,
                        Percentage = pct,
                        SpeedBytesPerSec = speed,
                        StatusText = totalBytes.HasValue && totalBytes.Value > 0
                            ? $"{pct:F0}% ({totalDownloaded / (1024.0 * 1024.0):F1} MB / {totalBytes.Value / (1024.0 * 1024.0):F1} MB)"
                            : $"Đã tải: {totalDownloaded / (1024.0 * 1024.0):F1} MB"
                    });

                    lastReportTime = elapsed;
                    lastReportBytes = totalDownloaded;
                }
            }

            progress?.Report(new FileDownloadProgressInfo
            {
                BytesDownloaded = totalDownloaded,
                TotalBytes = totalBytes ?? totalDownloaded,
                Percentage = 100.0,
                SpeedBytesPerSec = 0,
                StatusText = "Đã tải xong bộ cài đặt .NET x86."
            });

            return (true, targetFile, "");
        }
        catch (OperationCanceledException)
        {
            return (false, "", "Quá trình tải bộ cài đặt đã bị hủy.");
        }
        catch (Exception ex)
        {
            return (false, "", $"Lỗi tải bộ cài đặt .NET x86: {ex.Message}");
        }
    }

    public async Task<(bool Success, string ErrorMessage)> RunInstallerAsync(string installerPath, bool passive = true)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return (false, $"Không tìm thấy tệp cài đặt: {installerPath}");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = passive ? "/install /passive /norestart" : "/install /norestart",
                UseShellExecute = true,
                Verb = "runas" // Chạy với quyền Quản trị viên (UAC prompt)
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return (false, "Không thể khởi chạy tiến trình cài đặt.");
            }

            await proc.WaitForExitAsync().ConfigureAwait(false);

            if (proc.ExitCode == 0 || proc.ExitCode == 3010) // 0 = Success, 3010 = Success (Reboot required)
            {
                return (true, "");
            }

            return (false, $"Bộ cài đặt kết thúc với mã thoát: {proc.ExitCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi thực thi bộ cài đặt: {ex.Message}");
        }
    }

    public void OpenDownloadPageInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MicrosoftX86DirectDownloadUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = MicrosoftOfficialDownloadPageUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    public static bool IsVersionAtLeast(string versionString, int minMajor)
    {
        if (string.IsNullOrWhiteSpace(versionString)) return false;

        string clean = versionString.TrimStart('v', 'V').Trim();
        var parts = clean.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[0], out int major))
        {
            return major >= minMajor;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }
}

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.Licensing;

public sealed class LicenseService : ILicenseService, IDisposable
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _storageDir;
    private readonly string _licenseVaultPath;
    private readonly string _clockVaultPath;

    private LicenseStatus _status = LicenseStatus.Unlicensed;
    private LicensePayload? _currentLicense;
    private SignedLicensePackage? _currentPackage;
    private Timer? _heartbeatTimer;
    private string _serverUrl = "http://localhost:4000";

    public event EventHandler<LicenseStatus>? StatusChanged;

    public LicenseStatus Status => _status;
    public LicensePayload? CurrentLicense => _currentLicense;
    public string FormattedMachineCode => HardwareFingerprintService.GetFormattedMachineCode();
    public string MachineFingerprint => HardwareFingerprintService.GetMachineFingerprint();
    public string ServerUrl
    {
        get => _serverUrl;
        set => _serverUrl = value?.TrimEnd('/') ?? "http://localhost:4000";
    }

    public int RemainingDays
    {
        get
        {
            if (_currentLicense == null || string.IsNullOrWhiteSpace(_currentLicense.ExpirationDateUtc))
                return -1; // Vĩnh viễn

            if (DateTime.TryParse(_currentLicense.ExpirationDateUtc, out var exp))
            {
                var diff = (exp - DateTime.UtcNow).TotalDays;
                return diff > 0 ? (int)Math.Ceiling(diff) : 0;
            }
            return -1;
        }
    }

    public LicenseService(string? customStorageDir = null, string? initialServerUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(initialServerUrl))
        {
            _serverUrl = initialServerUrl.TrimEnd('/');
        }
        else
        {
            var envUrl = Environment.GetEnvironmentVariable("VISION_LICENSE_SERVER_URL");
            if (!string.IsNullOrWhiteSpace(envUrl))
            {
                _serverUrl = envUrl.TrimEnd('/');
            }
        }

        if (!string.IsNullOrWhiteSpace(customStorageDir))
        {
            _storageDir = customStorageDir;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _storageDir = Path.Combine(appData, "VisionInspectionApp", "Security");
        }

        if (!Directory.Exists(_storageDir))
        {
            try { Directory.CreateDirectory(_storageDir); } catch { }
        }

        _licenseVaultPath = Path.Combine(_storageDir, "license.vault");
        _clockVaultPath = Path.Combine(_storageDir, "clock.vault");
    }

    public async Task<LicenseValidationResult> ValidateLicenseAsync()
    {
        // 1. Kiểm tra tồn tại file license.vault
        if (!File.Exists(_licenseVaultPath))
        {
            SetStatus(LicenseStatus.Unlicensed, null, null);
            return new LicenseValidationResult(false, LicenseStatus.Unlicensed, "Phần mềm chưa được kích hoạt bản quyền.", null, 0);
        }

        // 2. Kiểm tra tính toàn vẹn thời gian hệ thống (Anti-Clock Tampering)
        var (isClockValid, _) = LicenseCryptoService.CheckAndRecordSystemClock(_clockVaultPath);
        if (!isClockValid)
        {
            SetStatus(LicenseStatus.ClockTampered, _currentLicense, _currentPackage);
            return new LicenseValidationResult(false, LicenseStatus.ClockTampered, "Phát hiện gian lận: Thời gian hệ thống Windows đã bị chỉnh lùi.", _currentLicense, 0);
        }

        try
        {
            // 3. Đọc và giải mã dữ liệu DPAPI
            var cipherBytes = await File.ReadAllBytesAsync(_licenseVaultPath);
            var plainBytes = LicenseCryptoService.UnprotectData(cipherBytes);
            var json = Encoding.UTF8.GetString(plainBytes);

            var package = JsonSerializer.Deserialize<SignedLicensePackage>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (package == null || package.Payload == null || string.IsNullOrWhiteSpace(package.Signature))
            {
                SetStatus(LicenseStatus.Unlicensed, null, null);
                return new LicenseValidationResult(false, LicenseStatus.Unlicensed, "Dữ liệu bản quyền cục bộ bị hư hại.", null, 0);
            }

            // 4. Xác thực chữ ký số RSA-2048
            bool isSignatureValid = LicenseCryptoService.VerifySignature(package.Payload, package.Signature);
            if (!isSignatureValid)
            {
                SetStatus(LicenseStatus.Unlicensed, null, null);
                return new LicenseValidationResult(false, LicenseStatus.Unlicensed, "Chữ ký số bản quyền không hợp lệ hoặc file đã bị can thiệp trái phép.", null, 0);
            }

            // 5. Kiểm tra ràng buộc phần cứng (Hardware Fingerprint match)
            string currentFp = HardwareFingerprintService.GetMachineFingerprint();
            if (!HardwareFingerprintService.IsFingerprintMatch(package.Payload.MachineFingerprint, currentFp))
            {
                SetStatus(LicenseStatus.Unlicensed, null, null);
                return new LicenseValidationResult(false, LicenseStatus.Unlicensed, "Bản quyền này được cấp cho máy tính khác (Sai lệch mã phần cứng Hardware ID).", null, 0);
            }

            // 6. Kiểm tra hạn dùng
            if (!string.IsNullOrWhiteSpace(package.Payload.ExpirationDateUtc))
            {
                if (DateTime.TryParse(package.Payload.ExpirationDateUtc, out var expDate))
                {
                    if (expDate < DateTime.UtcNow)
                    {
                        SetStatus(LicenseStatus.Expired, package.Payload, package);
                        return new LicenseValidationResult(false, LicenseStatus.Expired, $"Bản quyền đã hết hạn vào ngày {expDate.ToLocalTime():dd/MM/yyyy HH:mm}.", package.Payload, 0);
                    }
                }
            }

            // Hợp lệ!
            SetStatus(LicenseStatus.Active, package.Payload, package);
            StartHeartbeatTimerIfNeeded();

            int remDays = RemainingDays;
            string remMsg = remDays < 0 ? "Vĩnh viễn" : $"{remDays} ngày";
            return new LicenseValidationResult(true, LicenseStatus.Active, $"Bản quyền {package.Payload.Edition} hợp lệ ({remMsg}).", package.Payload, remDays);
        }
        catch (Exception ex)
        {
            SetStatus(LicenseStatus.Unlicensed, null, null);
            return new LicenseValidationResult(false, LicenseStatus.Unlicensed, $"Lỗi xác thực bản quyền: {ex.Message}", null, 0);
        }
    }

    public async Task<LicenseActivationResult> ActivateOnlineAsync(string licenseKey, string? customServerUrl = null)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return new LicenseActivationResult(false, "Vui lòng nhập License Key.", null);
        }

        string targetUrl = (!string.IsNullOrWhiteSpace(customServerUrl) ? customServerUrl.TrimEnd('/') : _serverUrl);
        var endpoint = $"{targetUrl}/api/v1/license/activate";

        try
        {
            var p = HardwareFingerprintService.GetHardwareProfile();
            var payload = new
            {
                licenseKey = licenseKey.Trim(),
                machineFingerprint = HardwareFingerprintService.GetMachineFingerprint(),
                machineName = p.MachineName,
                osVersion = p.OsVersion,
                appVersion = "2.1.0",
                localIp = "127.0.0.1"
            };

            var response = await HttpClient.PostAsJsonAsync(endpoint, payload);
            var content = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

            if (!success || !response.IsSuccessStatusCode)
            {
                return new LicenseActivationResult(false, !string.IsNullOrEmpty(message) ? message : $"Kích hoạt thất bại (Mã lỗi HTTP {(int)response.StatusCode}).", null);
            }

            if (!root.TryGetProperty("license", out var licenseEl))
            {
                return new LicenseActivationResult(false, "Phản hồi từ Server thiếu dữ liệu bản quyền.", null);
            }

            var package = JsonSerializer.Deserialize<SignedLicensePackage>(licenseEl.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (package == null || package.Payload == null || string.IsNullOrWhiteSpace(package.Signature))
            {
                return new LicenseActivationResult(false, "Dữ liệu bản quyền trả về không đúng định dạng.", null);
            }

            // Xác thực chữ ký số bằng Public Key tại Client
            if (!LicenseCryptoService.VerifySignature(package.Payload, package.Signature))
            {
                return new LicenseActivationResult(false, "Xác thực chữ ký số máy chủ thất bại! Dữ liệu có thể bị giả mạo.", null);
            }

            // Lưu trữ an toàn cục bộ bằng Windows DPAPI
            await SavePackageToVaultAsync(package);

            SetStatus(LicenseStatus.Active, package.Payload, package);
            StartHeartbeatTimerIfNeeded();

            return new LicenseActivationResult(true, "Kích hoạt bản quyền trực tuyến thành công!", package.Payload);
        }
        catch (Exception ex)
        {
            return new LicenseActivationResult(false, $"Không thể kết nối đến License Server ({targetUrl}): {ex.Message}", null);
        }
    }

    public Task<string> GenerateOfflineRequestCodeAsync()
    {
        var p = HardwareFingerprintService.GetHardwareProfile();
        var req = new MachineRequestData
        {
            MachineFingerprint = HardwareFingerprintService.GetMachineFingerprint(),
            MachineName = p.MachineName,
            OsVersion = p.OsVersion,
            AppVersion = "2.1.0",
            RequestTimestampUtc = DateTime.UtcNow.ToString("o"),
            RequestChecksum = HardwareFingerprintService.GetFormattedMachineCode()
        };

        var json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = true });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return Task.FromResult(base64);
    }

    public async Task<LicenseActivationResult> ActivateOfflineAsync(string licenseFileContent)
    {
        if (string.IsNullOrWhiteSpace(licenseFileContent))
        {
            return new LicenseActivationResult(false, "Nội dung file bản quyền (.lic) rỗng.", null);
        }

        try
        {
            // Thử giải mã Base64, nếu không được thì coi là JSON raw
            string json;
            try
            {
                var bytes = Convert.FromBase64String(licenseFileContent.Trim());
                json = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                json = licenseFileContent.Trim();
            }

            var package = JsonSerializer.Deserialize<SignedLicensePackage>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (package == null || package.Payload == null || string.IsNullOrWhiteSpace(package.Signature))
            {
                return new LicenseActivationResult(false, "File bản quyền không hợp lệ hoặc thiếu chữ ký số.", null);
            }

            // 1. Xác thực chữ ký số RSA
            if (!LicenseCryptoService.VerifySignature(package.Payload, package.Signature))
            {
                return new LicenseActivationResult(false, "Chữ ký số không hợp lệ! File bản quyền đã bị chỉnh sửa hoặc giả mạo.", null);
            }

            // 2. So khớp phần cứng
            string currentFp = HardwareFingerprintService.GetMachineFingerprint();
            if (!HardwareFingerprintService.IsFingerprintMatch(package.Payload.MachineFingerprint, currentFp))
            {
                return new LicenseActivationResult(false, "Mã phần cứng (Hardware ID) trong file bản quyền không khớp với máy tính này!", null);
            }

            // 3. Kiểm tra hạn dùng
            if (!string.IsNullOrWhiteSpace(package.Payload.ExpirationDateUtc))
            {
                if (DateTime.TryParse(package.Payload.ExpirationDateUtc, out var expDate) && expDate < DateTime.UtcNow)
                {
                    return new LicenseActivationResult(false, $"File bản quyền này đã hết hạn từ ngày {expDate.ToLocalTime():dd/MM/yyyy}.", null);
                }
            }

            // Lưu vào vault
            await SavePackageToVaultAsync(package);

            SetStatus(LicenseStatus.Active, package.Payload, package);

            return new LicenseActivationResult(true, "Kích hoạt bản quyền ngoại tuyến thành công!", package.Payload);
        }
        catch (Exception ex)
        {
            return new LicenseActivationResult(false, $"Lỗi nạp file bản quyền: {ex.Message}", null);
        }
    }

    public Task<bool> DeactivateLocalAsync()
    {
        try
        {
            if (File.Exists(_licenseVaultPath))
            {
                File.Delete(_licenseVaultPath);
            }

            SetStatus(LicenseStatus.Unlicensed, null, null);
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public bool IsFeatureAllowed(string featureName)
    {
        if (_status != LicenseStatus.Active && _status != LicenseStatus.GracePeriod)
            return false;

        if (_currentLicense == null) return false;

        // Gói Enterprise cho phép toàn bộ tính năng
        if (_currentLicense.Edition.Equals("Enterprise", StringComparison.OrdinalIgnoreCase))
            return true;

        if (_currentLicense.AllowedFeatures.Contains(featureName))
            return true;

        return false;
    }

    public void AssertCanExecuteInspection()
    {
        if (_status == LicenseStatus.Active || _status == LicenseStatus.GracePeriod)
        {
            return;
        }

        string msg = _status switch
        {
            LicenseStatus.Unlicensed => "Phần mềm chưa kích hoạt bản quyền! Vui lòng vào Menu 'Trợ Giúp' -> 'Quản lý Bản quyền' để kích hoạt.",
            LicenseStatus.Expired => "Bản quyền phần mềm đã hết hạn! Vui lòng gia hạn để tiếp tục kiểm tra.",
            LicenseStatus.Revoked => "Bản quyền đã bị thu hồi từ xa bởi Quản trị viên!",
            LicenseStatus.Suspended => "Bản quyền đang bị tạm khóa. Vui lòng liên hệ nhà cung cấp.",
            LicenseStatus.ClockTampered => "Phát hiện gian lận thời gian hệ thống! Khóa chức năng kiểm tra.",
            _ => "Trạng thái bản quyền không hợp lệ để thực thi phân tích."
        };

        throw new InvalidOperationException($"[LICENSE ERROR] {msg}");
    }

    private async Task SavePackageToVaultAsync(SignedLicensePackage package)
    {
        var json = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var cipherBytes = LicenseCryptoService.ProtectData(plainBytes);

        var dir = Path.GetDirectoryName(_licenseVaultPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(_licenseVaultPath, cipherBytes);
    }

    private void SetStatus(LicenseStatus newStatus, LicensePayload? payload, SignedLicensePackage? package)
    {
        _status = newStatus;
        _currentLicense = payload;
        _currentPackage = package;

        try
        {
            StatusChanged?.Invoke(this, newStatus);
        }
        catch { }
    }

    private void StartHeartbeatTimerIfNeeded()
    {
        if (_heartbeatTimer != null) return;

        // Chỉ gửi heartbeat nếu có server url và gói có HeartbeatIntervalHours > 0
        int intervalHours = _currentLicense?.HeartbeatIntervalHours ?? 12;
        if (intervalHours <= 0) return; // Chế độ offline thuần túy

        var interval = TimeSpan.FromHours(intervalHours);
        _heartbeatTimer = new Timer(async _ => await SendHeartbeatAsync(), null, TimeSpan.FromMinutes(1), interval);
    }

    private async Task SendHeartbeatAsync()
    {
        if (_currentLicense == null || string.IsNullOrWhiteSpace(_currentLicense.LicenseKey))
            return;

        try
        {
            var endpoint = $"{_serverUrl}/api/v1/license/heartbeat";
            var payload = new
            {
                licenseKey = _currentLicense.LicenseKey,
                machineFingerprint = HardwareFingerprintService.GetMachineFingerprint(),
                appVersion = "2.1.0"
            };

            var response = await HttpClient.PostAsJsonAsync(endpoint, payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var st))
                {
                    string statusStr = st.GetString() ?? "";
                    if (statusStr.Equals("Revoked", StringComparison.OrdinalIgnoreCase))
                    {
                        // Bị thu hồi từ xa!
                        await DeactivateLocalAsync();
                        SetStatus(LicenseStatus.Revoked, _currentLicense, _currentPackage);
                    }
                    else if (statusStr.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStatus(LicenseStatus.Suspended, _currentLicense, _currentPackage);
                    }
                }
            }
        }
        catch
        {
            // Mất kết nối internet -> Chuyển sang GracePeriod nếu đang Active
            if (_status == LicenseStatus.Active)
            {
                SetStatus(LicenseStatus.GracePeriod, _currentLicense, _currentPackage);
            }
        }
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }
}

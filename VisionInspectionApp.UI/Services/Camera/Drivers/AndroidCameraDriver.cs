using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Services.Camera.Drivers;

/// <summary>
/// Trình điều khiển Camera Android chuyên dụng (tối ưu hóa cho Samsung Galaxy S24 Ultra).
/// Hỗ trợ kết nối qua USB (ADB Forwarding/Tethering), Wi-Fi, Ethernet có dây.
/// Hỗ trợ điều khiển Pro Mode (Phơi sáng, Bù trừ EV, Lấy nét cự ly Focus, ISO, Flash Torch, Cân bằng trắng, Chọn ống kính).
/// Hỗ trợ song song: Luồng Live View 30-60 FPS và Chụp ảnh nguyên bản cảm biến Full-Resolution (12MP/50MP/200MP).
/// </summary>
public sealed class AndroidCameraDriver : CameraDriverBase
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private VideoCapture? _cap;
    private string _baseUrl = "http://127.0.0.1:8080";
    private string _streamUrl = "http://127.0.0.1:8080/video";
    private string _photoSnapshotUrl = "http://127.0.0.1:8080/photo.jpg";
    private string _previewShotUrl = "http://127.0.0.1:8080/shot.jpg";
    private string _statusUrl = "http://127.0.0.1:8080/status.json";

    public static List<CameraDeviceInfo> ScanDevices()
    {
        var list = new List<CameraDeviceInfo>
        {
            new CameraDeviceInfo
            {
                Vendor = CameraVendor.AndroidCamera,
                InterfaceType = CameraInterfaceType.Android,
                Index = -3,
                ModelName = "Samsung Galaxy S24 Ultra (Cáp USB ADB)",
                IpAddress = "127.0.0.1",
                AndroidHost = "127.0.0.1",
                AndroidPort = 8080,
                AndroidConnectionMode = AndroidConnectionMode.Usb,
                SerialNumber = "S24U-USB"
            },
            new CameraDeviceInfo
            {
                Vendor = CameraVendor.AndroidCamera,
                InterfaceType = CameraInterfaceType.Android,
                Index = -4,
                ModelName = "Samsung Galaxy S24 Ultra (Wi-Fi / Ethernet LAN)",
                IpAddress = "192.168.1.150",
                AndroidHost = "192.168.1.150",
                AndroidPort = 8080,
                AndroidConnectionMode = AndroidConnectionMode.WiFi,
                SerialNumber = "S24U-LAN"
            }
        };

        return list;
    }

    public override async Task<bool> OpenAsync(CameraDeviceInfo device)
    {
        _deviceInfo = device;

        string host = !string.IsNullOrWhiteSpace(device.AndroidHost) ? device.AndroidHost.Trim() : (!string.IsNullOrWhiteSpace(device.IpAddress) ? device.IpAddress.Trim() : "127.0.0.1");
        int port = device.AndroidPort > 0 ? device.AndroidPort : 8080;

        _baseUrl = $"http://{host}:{port}";
        _streamUrl = $"{_baseUrl}/video";
        _photoSnapshotUrl = $"{_baseUrl}/photo.jpg";
        _previewShotUrl = $"{_baseUrl}/shot.jpg";
        _statusUrl = $"{_baseUrl}/status.json";

        // 1. Nếu kết nối qua USB: Tự động chạy lệnh ADB forward port nếu có thể
        if (device.AndroidConnectionMode == AndroidConnectionMode.Usb || host == "127.0.0.1" || host == "localhost")
        {
            _ = Task.Run(() => EnsureAdbPortForward(port));
        }

        // 2. Kiểm tra kết nối HTTP tới Server Camera trên điện thoại
        bool isReachable = await TestConnectionAsync(host, port);
        if (!isReachable)
        {
            RaiseErrorOccurred($"Không thể kết nối đến Camera Android tại {_baseUrl}. Vui lòng mở ứng dụng Camera Server (IP Webcam) trên điện thoại.");
            _isOpened = false;
            return false;
        }

        _isOpened = true;

        // 3. Đọc thông số ban đầu và áp dụng thông số Pro Mode hiện tại
        try
        {
            await ApplyParametersAsync(_parameters);
        }
        catch { }

        return true;
    }

    public override async Task CloseAsync()
    {
        await StopGrabbingAsync();

        if (_cap != null)
        {
            try { _cap.Dispose(); } catch { }
            _cap = null;
        }

        _isOpened = false;
    }

    public override async Task<bool> StartGrabbingAsync()
    {
        if (!_isOpened) return false;
        if (_isGrabbing) return true;

        _isGrabbing = true;
        _cts = new CancellationTokenSource();

        _grabThread = new Thread(() => GrabLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "AndroidCameraGrabThread"
        };
        _grabThread.Start();

        await Task.Delay(50);
        return true;
    }

    public override async Task StopGrabbingAsync()
    {
        if (!_isGrabbing) return;

        _isGrabbing = false;
        _cts?.Cancel();

        if (_grabThread != null)
        {
            _grabThread.Join(500);
            _grabThread = null;
        }

        if (_cap != null)
        {
            try { _cap.Dispose(); } catch { }
            _cap = null;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Chụp ảnh tĩnh. Nếu cấu hình AndroidHighResSnapshot = true, tải trực tiếp ảnh nguyên bản cảm biến Galaxy S24 Ultra qua /photo.jpg.
    /// </summary>
    public override async Task<Mat?> GrabFrameAsync(int timeoutMs = 2500)
    {
        if (!_isOpened) return null;

        // 1. Ưu tiên chụp Full-Res Sensor Snapshot (12MP/50MP/200MP) qua HTTP REST API
        if (_parameters.AndroidHighResSnapshot)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                var response = await _httpClient.GetAsync(_photoSnapshotUrl, HttpCompletionOption.ResponseContentRead, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var fullResMat = Cv2.ImDecode(bytes, ImreadModes.Color);
                        if (fullResMat != null && !fullResMat.Empty())
                        {
                            return ApplySoftwarePostProcessing(fullResMat, _parameters);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidCameraDriver] HighRes Snapshot thất bại, chuyển fallback stream: {ex.Message}");
            }
        }

        // 2. Fallback: Lấy frame từ luồng VideoCapture đang chạy hoặc chụp frame nhanh từ /shot.jpg
        try
        {
            if (_cap != null && _cap.IsOpened())
            {
                using var rawFrame = new Mat();
                if (_cap.Read(rawFrame) && !rawFrame.Empty())
                {
                    return ApplySoftwarePostProcessing(rawFrame, _parameters);
                }
            }

            // Fallback tải preview shot
            using var cts = new CancellationTokenSource(timeoutMs);
            var response = await _httpClient.GetAsync(_previewShotUrl, HttpCompletionOption.ResponseContentRead, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                if (bytes != null && bytes.Length > 0)
                {
                    var shotMat = Cv2.ImDecode(bytes, ImreadModes.Color);
                    if (shotMat != null && !shotMat.Empty())
                    {
                        return ApplySoftwarePostProcessing(shotMat, _parameters);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidCameraDriver] Fallback snapshot lỗi: {ex.Message}");
        }

        return null;
    }

    public override async Task<bool> ExecuteSoftwareTriggerAsync()
    {
        if (!_isOpened) return false;

        try
        {
            var mat = await GrabFrameAsync(2000);
            if (mat != null && !mat.Empty())
            {
                try
                {
                    RaiseFrameCaptured(mat);
                    return true;
                }
                finally
                {
                    mat.Dispose();
                }
            }
        }
        catch { }

        return false;
    }

    public override async Task ApplyParametersAsync(CameraParameters parameters)
    {
        _parameters = parameters.Clone();

        if (!_isOpened) return;

        await Task.Run(async () =>
        {
            try
            {
                // 1. Điều khiển Bù trừ sáng EV (-2.0 đến +2.0)
                int evInt = (int)Math.Round(_parameters.AndroidEv * 2.0); // một số app dùng bước 0.5 hoặc -4..+4
                await SendControlCommandAsync($"/settings/exposure_compensation?set={_parameters.AndroidEv:F1}");

                // 2. Điều khiển Phơi sáng (Exposure Time / Shutter Speed)
                if (_parameters.AutoExposure)
                {
                    await SendControlCommandAsync("/settings/exposure_mode?set=auto");
                }
                else
                {
                    // Chuyển micro-giây sang nano-giây hoặc ms tùy app hỗ trợ
                    long exposureNs = (long)(_parameters.ExposureTimeUs * 1000L);
                    await SendControlCommandAsync($"/settings/exposure_time?set={exposureNs}");
                }

                // 3. Điều khiển Lấy nét Focus (Auto, Macro, Manual)
                switch (_parameters.AndroidFocusMode)
                {
                    case AndroidFocusMode.Auto:
                        await SendControlCommandAsync("/settings/focus_mode?set=continuous-picture");
                        break;
                    case AndroidFocusMode.Macro:
                        await SendControlCommandAsync("/settings/focus_mode?set=macro");
                        break;
                    case AndroidFocusMode.Manual:
                        await SendControlCommandAsync("/settings/focus_mode?set=manual");
                        float dist = Math.Clamp(_parameters.AndroidFocusDistance, 0.0f, 1.0f);
                        await SendControlCommandAsync($"/settings/focus_distance?set={dist:F2}");
                        break;
                }

                // 4. Điều khiển ISO
                if (!string.IsNullOrWhiteSpace(_parameters.AndroidIso) && !_parameters.AndroidIso.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    await SendControlCommandAsync($"/settings/iso?set={_parameters.AndroidIso.Trim()}");
                }
                else
                {
                    await SendControlCommandAsync("/settings/iso?set=auto");
                }

                // 5. Điều khiển Cân bằng trắng (White Balance)
                string wb = _parameters.AndroidWhiteBalance?.ToLowerInvariant() ?? "auto";
                await SendControlCommandAsync($"/settings/whitebalance?set={wb}");

                // 6. Điều khiển Đèn Flash Torch
                if (_parameters.AndroidEnableTorch)
                {
                    await SendControlCommandAsync("/enabletorch");
                }
                else
                {
                    await SendControlCommandAsync("/disabletorch");
                }

                // 7. Lựa chọn Ống kính Galaxy S24 Ultra (Primary 1x, Tele 3x, Tele 5x, UltraWide 0.6x)
                int lensId = (int)_parameters.AndroidLens;
                await SendControlCommandAsync($"/settings/camera?set={lensId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidCameraDriver] ApplyParametersAsync Exception: {ex.Message}");
            }
        });
    }

    public override async Task<CameraParameters> ReadParametersAsync()
    {
        if (_isOpened)
        {
            try
            {
                var response = await _httpClient.GetAsync(_statusUrl);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("curvals", out var curvals))
                    {
                        if (curvals.TryGetProperty("exposure_compensation", out var evProp) && evProp.TryGetSingle(out float ev))
                        {
                            _parameters.AndroidEv = ev;
                        }
                        if (curvals.TryGetProperty("focus_distance", out var fdProp) && fdProp.TryGetSingle(out float fd))
                        {
                            _parameters.AndroidFocusDistance = fd;
                        }
                    }
                }
            }
            catch { }
        }

        return await Task.FromResult(_parameters.Clone());
    }

    private void GrabLoop(CancellationToken token)
    {
        try
        {
            _cap?.Dispose();
            _cap = new VideoCapture(_streamUrl, VideoCaptureAPIs.ANY);
            _cap.Set(VideoCaptureProperties.BufferSize, 1);

            var frame = new Mat();

            while (!token.IsCancellationRequested && _isGrabbing)
            {
                if (_cap.IsOpened() && _cap.Read(frame) && !frame.Empty())
                {
                    var processed = ApplySoftwarePostProcessing(frame, _parameters);
                    try
                    {
                        RaiseFrameCaptured(processed);
                    }
                    finally
                    {
                        if (!ReferenceEquals(processed, frame))
                        {
                            processed.Dispose();
                        }
                    }
                }
                else
                {
                    // Fallback polling shot.jpg nếu VideoCapture luồng MJPEG bị đứt đoạn
                    try
                    {
                        var task = _httpClient.GetByteArrayAsync(_previewShotUrl, token);
                        task.Wait(token);
                        byte[] bytes = task.Result;
                        if (bytes != null && bytes.Length > 0)
                        {
                            using var shotMat = Cv2.ImDecode(bytes, ImreadModes.Color);
                            if (shotMat != null && !shotMat.Empty())
                            {
                                var processed = ApplySoftwarePostProcessing(shotMat, _parameters);
                                try
                                {
                                    RaiseFrameCaptured(processed);
                                }
                                finally
                                {
                                    if (!ReferenceEquals(processed, shotMat))
                                    {
                                        processed.Dispose();
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    Thread.Sleep(33); // ~30 FPS
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            RaiseErrorOccurred($"Lỗi trong luồng nhận ảnh Android Camera: {ex.Message}");
        }
        finally
        {
            _isGrabbing = false;
        }
    }

    private async Task<bool> TestConnectionAsync(string host, int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(2000);
            var res = await _httpClient.GetAsync($"{_baseUrl}/status.json", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            // Thử kiểm tra endpoint shot.jpg
            try
            {
                using var cts = new CancellationTokenSource(1500);
                var res = await _httpClient.GetAsync($"{_baseUrl}/shot.jpg", cts.Token);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    private async Task SendControlCommandAsync(string relativeUrl)
    {
        try
        {
            string url = $"{_baseUrl}{relativeUrl}";
            using var cts = new CancellationTokenSource(1000);
            await _httpClient.GetAsync(url, cts.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidCameraDriver] Control command '{relativeUrl}' warning: {ex.Message}");
        }
    }

    /// <summary>
    /// Tự động thiết lập ADB Port Forwarding (adb forward tcp:{port} tcp:{port}) khi kết nối qua cáp USB Type-C.
    /// </summary>
    public static bool EnsureAdbPortForward(int port = 8080)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = $"forward tcp:{port} tcp:{port}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(1500);
                return proc.ExitCode == 0;
            }
        }
        catch
        {
            // Máy chưa cài hoặc chưa add ADB vào PATH -> Người dùng có thể dùng USB Tethering hoặc Wi-Fi/Ethernet
        }
        return false;
    }
}

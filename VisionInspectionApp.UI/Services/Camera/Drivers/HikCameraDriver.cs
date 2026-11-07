using MvCamCtrl.NET;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera.Drivers;

/// <summary>
/// Driver tích hợp camera công nghiệp HIKRobot MVS SDK chuẩn qua package MvCameraControl.Net (MvCamCtrl.NET).
/// Hỗ trợ cả 2 chuẩn giao tiếp GigE Vision & USB3 Vision.
/// Tự động cách ly an toàn nếu chưa cài đặt MVS SDK runtime trên máy tính.
/// </summary>
public sealed class HikCameraDriver : CameraDriverBase
{
    private MyCamera? _camera;
    private static bool? _isMvSdkAvailable;
    private MyCamera.cbExceptiondelegate? _exceptionCallback;
    private readonly NativeMatPool _matPool = new(4);
    private IntPtr _pConvertBuffer = IntPtr.Zero;
    private uint _convertBufferSize = 0;
    private uint _lastFrameNum = 0;
    private long _hardwareDroppedFrames = 0;
    private bool _isFirstFrame = true;
    private readonly object _reconnectGate = new();
    private bool _isReconnecting = false;
    private CameraFrameMetadata? _latestMetadata;

    public long HardwareDroppedFrames => Interlocked.Read(ref _hardwareDroppedFrames);
    public CameraFrameMetadata? LatestMetadata => _latestMetadata;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private void OnExceptionCallback(uint nMsgType, IntPtr pUser)
    {
        System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Exception callback: 0x{nMsgType:X8}");
        RaiseErrorOccurred($"Camera Hardware Exception: 0x{nMsgType:X8}");
        StartAutoReconnectWatchdog();
    }

    private void StartAutoReconnectWatchdog()
    {
        lock (_reconnectGate)
        {
            if (_isReconnecting || !_isOpened) return;
            _isReconnecting = true;
        }

        Task.Run(async () =>
        {
            System.Diagnostics.Debug.WriteLine("[HikCameraDriver] Watchdog: Bắt đầu tiến trình tự động khôi phục kết nối Camera...");
            while (_isOpened && _isReconnecting)
            {
                try
                {
                    await Task.Delay(1000);
                    if (_camera != null)
                    {
                        try { _camera.MV_CC_StopGrabbing_NET(); } catch { }
                        try { _camera.MV_CC_CloseDevice_NET(); } catch { }

                        int openRet = _camera.MV_CC_OpenDevice_NET(MyCamera.MV_ACCESS_Exclusive, 0);
                        if (openRet == MyCamera.MV_OK)
                        {
                            int grabRet = _camera.MV_CC_StartGrabbing_NET();
                            if (grabRet == MyCamera.MV_OK)
                            {
                                System.Diagnostics.Debug.WriteLine("[HikCameraDriver] Watchdog: Phục hồi kết nối Camera thành công!");
                                lock (_reconnectGate) { _isReconnecting = false; }
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Tìm đường dẫn file đĩa MvCameraControl.dll hoặc MvCameraControl.Net.dll thực tế trên Windows
    /// </summary>
    public static string? FindHikMvsDllPath()
    {
        try
        {
            // 1. Kiểm tra biến môi trường hệ thống của Hikrobot MVS Installer
            string? envPath = Environment.GetEnvironmentVariable("MVCAM_COMMON_RUNENV");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                string p64 = System.IO.Path.Combine(envPath, "Win64", "MvCameraControl.dll");
                if (System.IO.File.Exists(p64)) return p64;
                string p64net = System.IO.Path.Combine(envPath, "Win64", "MvCameraControl.Net.dll");
                if (System.IO.File.Exists(p64net)) return p64net;
                string p32 = System.IO.Path.Combine(envPath, "Win32", "MvCameraControl.dll");
                if (System.IO.File.Exists(p32)) return p32;
            }

            string? mvsPath = Environment.GetEnvironmentVariable("MVS_SDK_PATH");
            if (!string.IsNullOrWhiteSpace(mvsPath))
            {
                string p = System.IO.Path.Combine(mvsPath, "MvCameraControl.dll");
                if (System.IO.File.Exists(p)) return p;
                string pnet = System.IO.Path.Combine(mvsPath, "MvCameraControl.Net.dll");
                if (System.IO.File.Exists(pnet)) return pnet;
            }

            // 2. Kiểm tra các thư mục cài đặt tiêu chuẩn của Hikrobot MVS trên Windows
            string[] candidatePaths = new string[]
            {
                @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64\MvCameraControl.dll",
                @"C:\Program Files\Common Files\MVS\Runtime\Win64\MvCameraControl.dll",
                @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64\MvCameraControl.Net.dll",
                @"C:\Program Files\Common Files\MVS\Runtime\Win64\MvCameraControl.Net.dll",
                @"C:\Program Files (x86)\MVS\Development\VB.Net\MvCameraControl.dll",
                @"C:\Program Files (x86)\MVS\Development\VB.Net\MvCameraControl.Net.dll",
                @"C:\Program Files\MVS\Development\VB.Net\MvCameraControl.dll",
                @"C:\Program Files\MVS\Development\VB.Net\MvCameraControl.Net.dll",
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MvCameraControl.Net.dll"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MvCameraControl.dll"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "win-x64", "native", "MvCameraControl.dll")
            };

            foreach (var path in candidatePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Kiểm tra an toàn xem Hikrobot MVS SDK đã được cài đặt trên đĩa cứng Windows hay chưa
    /// </summary>
    public static bool IsMvSdkAvailable()
    {
        if (!_isMvSdkAvailable.HasValue)
        {
            string? dllPath = FindHikMvsDllPath();
            if (string.IsNullOrEmpty(dllPath))
            {
                // File đĩa DLL không tồn tại -> Chưa cài MVS SDK. Dừng lại ngay lập tức.
                _isMvSdkAvailable = false;
            }
            else
            {
                try
                {
                    string? dir = System.IO.Path.GetDirectoryName(dllPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        SetDllDirectory(dir);
                    }

                    // Gọi kiểm tra phiên bản SDK từ MvCamCtrl.NET.MyCamera
                    uint ver = MyCamera.MV_CC_GetSDKVersion_NET();
                    _isMvSdkAvailable = true;
                }
                catch
                {
                    _isMvSdkAvailable = false;
                }
            }
        }
        return _isMvSdkAvailable.Value;
    }

    /// <summary>
    /// Tìm kiếm tất cả các camera Hikrobot (GigE & USB3) đang cắm vào PC
    /// </summary>
    public static List<CameraDeviceInfo> ScanDevices()
    {
        var resultList = new List<CameraDeviceInfo>();

        if (!IsMvSdkAvailable())
        {
            System.Diagnostics.Debug.WriteLine("[HikCameraDriver] MvCameraControl SDK chưa sẵn sàng trên hệ thống.");
            return resultList;
        }

        try
        {
            var deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int ret = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref deviceList);

            if (ret == MyCamera.MV_OK && deviceList.nDeviceNum > 0)
            {
                for (int i = 0; i < deviceList.nDeviceNum; i++)
                {
                    if (deviceList.pDeviceInfo[i] == IntPtr.Zero) continue;

                    var devInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(deviceList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO))!;

                    var info = new CameraDeviceInfo
                    {
                        Vendor = CameraVendor.Hikrobot,
                        Index = i
                    };

                    if (devInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                    {
                        var gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(devInfo.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                        info.InterfaceType = CameraInterfaceType.GigE;
                        info.ModelName = string.IsNullOrWhiteSpace(gigeInfo.chModelName) ? "Hikrobot GigE Camera" : gigeInfo.chModelName;
                        info.SerialNumber = gigeInfo.chSerialNumber ?? "";
                        info.UserDefinedName = gigeInfo.chUserDefinedName ?? "";

                        uint ip = gigeInfo.nCurrentIp;
                        info.IpAddress = $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
                    }
                    else if (devInfo.nTLayerType == MyCamera.MV_USB_DEVICE)
                    {
                        var usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(devInfo.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                        info.InterfaceType = CameraInterfaceType.USB3;
                        info.ModelName = string.IsNullOrWhiteSpace(usbInfo.chModelName) ? "Hikrobot USB3 Camera" : usbInfo.chModelName;
                        info.SerialNumber = usbInfo.chSerialNumber ?? "";
                        info.UserDefinedName = usbInfo.chUserDefinedName ?? "";
                    }

                    resultList.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Lỗi EnumDevices: {ex.Message}");
        }

        return resultList;
    }

    public override async Task<bool> OpenAsync(CameraDeviceInfo device)
    {
        _deviceInfo = device;

        if (!IsMvSdkAvailable())
        {
            RaiseErrorOccurred("Chưa cài đặt Hikrobot MVS SDK (MvCameraControl.Net) trên máy tính này.");
            _isOpened = false;
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                var deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
                int ret = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref deviceList);

                if (ret != MyCamera.MV_OK || deviceList.nDeviceNum == 0 || device.Index >= deviceList.nDeviceNum || deviceList.pDeviceInfo[device.Index] == IntPtr.Zero)
                {
                    RaiseErrorOccurred($"Không tìm thấy camera Hikrobot chỉ số {device.Index}. Vui lòng kiểm tra dây cáp và nguồn camera.");
                    return false;
                }

                var devInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(deviceList.pDeviceInfo[device.Index], typeof(MyCamera.MV_CC_DEVICE_INFO))!;

                _camera = new MyCamera();
                ret = _camera.MV_CC_CreateDevice_NET(ref devInfo);
                if (ret != MyCamera.MV_OK)
                {
                    RaiseErrorOccurred("Lỗi tạo kết nối camera Hikrobot.");
                    _camera = null;
                    return false;
                }

                ret = _camera.MV_CC_OpenDevice_NET(MyCamera.MV_ACCESS_Exclusive, 0);
                if (ret != MyCamera.MV_OK)
                {
                    _camera.MV_CC_DestroyDevice_NET();
                    _camera = null;
                    RaiseErrorOccurred("Lỗi Mở thiết bị camera Hikrobot. Có thể thiết bị đang bị phần mềm khác chiếm dụng.");
                    return false;
                }

                _isOpened = true;

                // 1. Cấu hình Ring Buffer phần cứng của Hikrobot SDK lên 20 nodes (đảm bảo buffer ~500ms cho 41 FPS @ 25MP)
                // Tuyệt đối không để 3 nodes vì sẽ bị tràn buffer và rớt 90% frame ngay lập tức.
                try
                {
                    _camera.MV_CC_SetImageNodeNum_NET(20);
                }
                catch { }

                // 2. Tự động thương lượng kích thước gói tin tối ưu cho card mạng 10GbE (MV-GT1002) / GigE Vision
                if (device.InterfaceType == CameraInterfaceType.GigE)
                {
                    try
                    {
                        int optimalPkt = _camera.MV_CC_GetOptimalPacketSize_NET();
                        if (optimalPkt > 0)
                        {
                            _camera.MV_GIGE_SetGevSCPSPacketSize_NET((uint)optimalPkt);
                            _camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", (uint)optimalPkt);
                            _parameters.PacketSize = optimalPkt;
                            System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Auto-negotiated 10GbE Optimal Packet Size: {optimalPkt} Bytes");
                        }
                        else
                        {
                            _camera.MV_GIGE_SetGevSCPSPacketSize_NET(9000);
                            _camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", 9000);
                            _parameters.PacketSize = 9000;
                        }

                        // Đặt Packet Delay về 0 cho kết nối trực tiếp băng thông cao
                        _camera.MV_CC_SetIntValueEx_NET("GevSCPD", 0);
                        _parameters.PacketDelay = 0;
                    }
                    catch { }
                }

                // 3. Thiết lập chiến lược nhận frame: LatestImagesOnly cho Live View không bị giật trễ
                try
                {
                    _camera.MV_CC_SetGrabStrategy_NET(MyCamera.MV_GRAB_STRATEGY.MV_GrabStrategy_LatestImagesOnly);
                }
                catch { }

                // 4. Bật chất lượng giải mã Bayer tốc độ cao (0 = Fast) cho luồng trực tiếp 40+ FPS
                try
                {
                    _camera.MV_CC_SetBayerCvtQuality_NET(0);
                }
                catch { }

                // Đăng ký Exception Callback từ Hikrobot SDK
                try
                {
                    _exceptionCallback = OnExceptionCallback;
                    _camera.MV_CC_RegisterExceptionCallBack_NET(_exceptionCallback, IntPtr.Zero);
                }
                catch { }

                // Đồng bộ hóa trạng thái lật phần cứng ban đầu từ camera
                try
                {
                    bool curHwX = false;
                    if (_camera.MV_CC_GetBoolValue_NET("ReverseX", ref curHwX) == MyCamera.MV_OK)
                    {
                        lock (_lock)
                        {
                            _hardwareReverseXApplied = curHwX;
                            _parameters.ReverseX = curHwX;
                        }
                    }
                    bool curHwY = false;
                    if (_camera.MV_CC_GetBoolValue_NET("ReverseY", ref curHwY) == MyCamera.MV_OK)
                    {
                        lock (_lock)
                        {
                            _hardwareReverseYApplied = curHwY;
                            _parameters.ReverseY = curHwY;
                        }
                    }
                }
                catch { }

                return true;
            }
            catch (Exception ex)
            {
                RaiseErrorOccurred($"Lỗi khởi động Hikrobot SDK Driver: {ex.Message}");
                _isOpened = false;
                return false;
            }
        });
    }

    public override async Task CloseAsync()
    {
        await StopGrabbingAsync();

        if (_pConvertBuffer != IntPtr.Zero)
        {
            try { Marshal.FreeHGlobal(_pConvertBuffer); } catch { }
            _pConvertBuffer = IntPtr.Zero;
            _convertBufferSize = 0;
        }

        _matPool.Dispose();

        if (_camera != null && IsMvSdkAvailable())
        {
            try
            {
                _camera.MV_CC_CloseDevice_NET();
                _camera.MV_CC_DestroyDevice_NET();
            }
            catch { }
            _camera = null;
        }

        _isOpened = false;
        _hardwareReverseXApplied = false;
        _hardwareReverseYApplied = false;
    }

    public override async Task<bool> StartGrabbingAsync()
    {
        if (!_isOpened || _camera == null || !IsMvSdkAvailable()) return false;
        if (_isGrabbing) return true;

        return await Task.Run(() =>
        {
            try
            {
                int ret = _camera.MV_CC_StartGrabbing_NET();
                if (ret != MyCamera.MV_OK)
                {
                    RaiseErrorOccurred("Không thể bắt đầu luồng hình ảnh (StartGrabbing) trên Camera Hikrobot.");
                    return false;
                }

                _isGrabbing = true;
                _cts = new CancellationTokenSource();
                _isFirstFrame = true;

                _convertBufferSize = GetPayloadSize();
                if (_pConvertBuffer == IntPtr.Zero && _convertBufferSize > 0)
                {
                    _pConvertBuffer = Marshal.AllocHGlobal((int)_convertBufferSize);
                }

                _grabThread = new Thread(() => ContinuousGrabLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = "HikCameraGrabThread"
                };
                _grabThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                RaiseErrorOccurred($"Lỗi StartGrabbing: {ex.Message}");
                _isGrabbing = false;
                return false;
            }
        });
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

        if (_pConvertBuffer != IntPtr.Zero)
        {
            try { Marshal.FreeHGlobal(_pConvertBuffer); } catch { }
            _pConvertBuffer = IntPtr.Zero;
            _convertBufferSize = 0;
        }

        if (_camera != null && IsMvSdkAvailable())
        {
            try { _camera.MV_CC_StopGrabbing_NET(); } catch { }
        }

        await Task.CompletedTask;
    }

    private uint GetPayloadSize()
    {
        if (_camera != null && IsMvSdkAvailable())
        {
            try
            {
                var stValue = new MyCamera.MVCC_INTVALUE_EX();
                int ret = _camera.MV_CC_GetIntValueEx_NET("PayloadSize", ref stValue);
                if (ret == MyCamera.MV_OK && stValue.nCurValue > 0)
                {
                    return (uint)Math.Max(stValue.nCurValue, 5120 * 3840 * 4);
                }
            }
            catch { }
        }
        // Fallback an toàn tối thiểu 80MB cho camera 20MP (5120x3840x4)
        return 5120 * 3840 * 4;
    }

    private readonly SemaphoreSlim _driverGate = new(1, 1);
    private Mat? _latestContinuousFrame;
    private readonly object _latestContinuousFrameLock = new();
    private int _discardFramesCount;
    private int _grabFrameWaiters = 0;

    public override async Task<Mat?> GrabFrameAsync(int timeoutMs = 3000)
    {
        if (!_isOpened || _camera == null || !IsMvSdkAvailable()) return null;

        // 1. Nếu camera đang ở chế độ Live View (Grabbing liên tục)
        if (_isGrabbing)
        {
            Interlocked.Increment(ref _grabFrameWaiters);
            try
            {
                bool isSoftwareTrigger = _parameters.TriggerMode == CameraTriggerMode.On
                                      && _parameters.TriggerSource == CameraTriggerSource.Software;

                // Reset frame cũ để đảm bảo 100% nhận frame mới tinh từ phần cứng
                lock (_latestContinuousFrameLock)
                {
                    _latestContinuousFrame?.Dispose();
                    _latestContinuousFrame = null;

                    // ĐẶC BIỆT QUAN TRỌNG: Ở SoftTrigger mode, mỗi xung TriggerSoftware
                    // chỉ tạo đúng 1 frame duy nhất. Nếu _discardFramesCount > 0,
                    // GrabLoop sẽ discard frame đó → _latestContinuousFrame mãi mãi null → timeout.
                    // Phải reset discard counter để frame từ trigger được chấp nhận.
                    if (isSoftwareTrigger)
                    {
                        _discardFramesCount = 0;
                    }
                }

                // Nếu ở TriggerMode Software, gửi lệnh TriggerSoftware để phần cứng camera chụp frame mới
                if (isSoftwareTrigger && _camera != null)
                {
                    try
                    {
                        _camera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Software trigger error: {ex.Message}");
                    }
                }

                // Chờ ContinuousGrabLoop nhận được frame mới từ camera
                int maxLoops = Math.Max(20, timeoutMs / 20);
                for (int i = 0; i < maxLoops; i++)
                {
                    lock (_latestContinuousFrameLock)
                    {
                        if (_latestContinuousFrame != null && !_latestContinuousFrame.IsDisposed && !_latestContinuousFrame.Empty())
                        {
                            var freshMat = _latestContinuousFrame;
                            _latestContinuousFrame = null;

                            CameraParameters p;
                            bool hwX, hwY;
                            lock (_lock)
                            {
                                p = _parameters;
                                hwX = _hardwareReverseXApplied;
                                hwY = _hardwareReverseYApplied;
                            }
                            var processed = ApplySoftwarePostProcessing(freshMat, p, hwX, hwY);
                            if (!ReferenceEquals(processed, freshMat))
                            {
                                freshMat.Dispose();
                            }
                            return processed;
                        }
                    }
                    await Task.Delay(20);
                }
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref _grabFrameWaiters);
            }
        }

        // 2. Nếu camera ở chế độ Standby (0 Mbps) hoặc cần chụp frame độc lập
        await _driverGate.WaitAsync();
        return await Task.Run(() =>
        {
            bool needStopGrabbingAfterwards = false;
            IntPtr pData = IntPtr.Zero;
            try
            {
                if (_camera == null) return null;

                if (!_isGrabbing)
                {
                    int retStart = _camera.MV_CC_StartGrabbing_NET();
                    if (retStart == MyCamera.MV_OK)
                    {
                        needStopGrabbingAfterwards = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] MV_CC_StartGrabbing_NET failed with code: 0x{retStart:X8}");
                    }
                }

                try
                {
                    _camera.MV_CC_ClearImageBuffer_NET();
                }
                catch { }

                // Gửi lệnh TriggerSoftware nếu ở Trigger Mode Software
                if (_parameters.TriggerMode == CameraTriggerMode.On && _parameters.TriggerSource == CameraTriggerSource.Software)
                {
                    _camera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                }

                uint bufferSize = GetPayloadSize();
                pData = Marshal.AllocHGlobal((int)bufferSize);
                var frameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();

                int ret = _camera.MV_CC_GetOneFrameTimeout_NET(pData, bufferSize, ref frameInfo, timeoutMs);
                if (ret == MyCamera.MV_OK && frameInfo.nWidth > 0 && frameInfo.nHeight > 0)
                {
                    var rawMat = ConvertHikFrameToMat(pData, frameInfo);
                    if (rawMat != null && !rawMat.IsDisposed && !rawMat.Empty())
                    {
                        CameraParameters p;
                        bool hwX, hwY;
                        lock (_lock)
                        {
                            p = _parameters;
                            hwX = _hardwareReverseXApplied;
                            hwY = _hardwareReverseYApplied;
                        }
                        var processed = ApplySoftwarePostProcessing(rawMat, p, hwX, hwY);
                        // QUAN TRỌNG: rawMat được quản lý bởi _matPool (Ring Buffer nội bộ của Driver).
                        // Caller của GrabFrameAsync (Snapshot / ToolEditor / ManualInspection) cần sở hữu Mat riêng biệt
                        // để có thể Dispose tự do mà không phá hủy ô nhớ của Ring Buffer.
                        if (ReferenceEquals(processed, rawMat))
                        {
                            return rawMat.Clone();
                        }
                        return processed;
                    }
                    return null;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] MV_CC_GetOneFrameTimeout_NET returned 0x{ret:X8}, w={frameInfo.nWidth}, h={frameInfo.nHeight}");
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] GrabFrameAsync exception: {ex.Message}");
                return null;
            }
            finally
            {
                if (pData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pData);
                }

                if (needStopGrabbingAfterwards && _camera != null)
                {
                    try
                    {
                        _camera.MV_CC_StopGrabbing_NET();
                    }
                    catch { }
                }

                _driverGate.Release();
            }
        });
    }

    public override async Task<bool> ExecuteSoftwareTriggerAsync()
    {
        if (!_isOpened || _camera == null || !IsMvSdkAvailable()) return false;

        return await Task.Run(() =>
        {
            try
            {
                lock (_latestContinuousFrameLock)
                {
                    _discardFramesCount = 0;
                }
                int ret = _camera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                return ret == MyCamera.MV_OK;
            }
            catch
            {
                return false;
            }
        });
    }

    public override async Task ApplyParametersAsync(CameraParameters parameters)
    {
        await base.ApplyParametersAsync(parameters);
        if (!_isOpened || _camera == null || !IsMvSdkAvailable()) return;

        await _driverGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                try
                {
                // Invalidate cached continuous frame from old parameters and discard 2 in-flight hardware FIFO frames
                lock (_latestContinuousFrameLock)
                {
                    _latestContinuousFrame?.Dispose();
                    _latestContinuousFrame = null;
                    _discardFramesCount = 2;
                }

                try
                {
                    _camera.MV_CC_ClearImageBuffer_NET();
                }
                catch { }

                // 0. Bật chất lượng chuyển đổi Bayer tốc độ cao (0 = Fast) cho luồng trực tiếp 40+ FPS
                try
                {
                    _camera.MV_CC_SetBayerCvtQuality_NET(0);
                }
                catch { }

                // 1. Exposure Time & Auto Exposure
                if (parameters.AutoExposure)
                {
                    _camera.MV_CC_SetEnumValueByString_NET("ExposureAuto", "Continuous");
                }
                else
                {
                    _camera.MV_CC_SetEnumValueByString_NET("ExposureAuto", "Off");
                    _camera.MV_CC_SetFloatValue_NET("ExposureTime", parameters.ExposureTimeUs);
                }

                // 2. Gain & Auto Gain
                if (parameters.AutoGain)
                {
                    _camera.MV_CC_SetEnumValueByString_NET("GainAuto", "Continuous");
                }
                else
                {
                    _camera.MV_CC_SetEnumValueByString_NET("GainAuto", "Off");
                    _camera.MV_CC_SetFloatValue_NET("Gain", parameters.GainDb);
                }

                // 3. Cân Bằng Trắng (White Balance)
                try
                {
                    if (parameters.AutoWhiteBalanceOnce)
                    {
                        _camera.MV_CC_SetEnumValueByString_NET("BalanceWhiteAuto", "Once");
                    }
                    else if (parameters.AutoWhiteBalance)
                    {
                        _camera.MV_CC_SetEnumValueByString_NET("BalanceWhiteAuto", "Continuous");
                    }
                    else
                    {
                        _camera.MV_CC_SetEnumValueByString_NET("BalanceWhiteAuto", "Off");
                        if (parameters.RedGain > 0)
                        {
                            _camera.MV_CC_SetEnumValueByString_NET("BalanceRatioSelector", "Red");
                            int redInt = (int)Math.Round(parameters.RedGain * 1024);
                            if (_camera.MV_CC_SetIntValueEx_NET("BalanceRatio", redInt) != MyCamera.MV_OK)
                            {
                                _camera.MV_CC_SetFloatValue_NET("BalanceRatio", parameters.RedGain);
                            }
                        }
                        if (parameters.BlueGain > 0)
                        {
                            _camera.MV_CC_SetEnumValueByString_NET("BalanceRatioSelector", "Blue");
                            int blueInt = (int)Math.Round(parameters.BlueGain * 1024);
                            if (_camera.MV_CC_SetIntValueEx_NET("BalanceRatio", blueInt) != MyCamera.MV_OK)
                            {
                                _camera.MV_CC_SetFloatValue_NET("BalanceRatio", parameters.BlueGain);
                            }
                        }
                    }
                }
                catch { }

                // 4. Gamma
                try
                {
                    _camera.MV_CC_SetBoolValue_NET("GammaEnable", true);
                    _camera.MV_CC_SetFloatValue_NET("Gamma", parameters.Gamma);
                }
                catch { }

                // 5. Trigger Mode & Trigger Source
                if (parameters.TriggerMode == CameraTriggerMode.On)
                {
                    _camera.MV_CC_SetEnumValueByString_NET("TriggerMode", "On");
                    string srcStr = parameters.TriggerSource switch
                    {
                        CameraTriggerSource.Line0 => "Line0",
                        CameraTriggerSource.Line1 => "Line1",
                        CameraTriggerSource.Line2 => "Line2",
                        _ => "Software"
                    };
                    _camera.MV_CC_SetEnumValueByString_NET("TriggerSource", srcStr);
                    try { _camera.MV_CC_SetGrabStrategy_NET(MyCamera.MV_GRAB_STRATEGY.MV_GrabStrategy_OneByOne); } catch { }
                }
                else
                {
                    _camera.MV_CC_SetEnumValueByString_NET("TriggerMode", "Off");
                    try { _camera.MV_CC_SetGrabStrategy_NET(MyCamera.MV_GRAB_STRATEGY.MV_GrabStrategy_LatestImagesOnly); } catch { }
                }

                // 6. Hardware Reverse X / Y (Flip)
                bool hwX = false;
                bool hwY = false;
                try
                {
                    _camera.MV_CC_SetBoolValue_NET("ReverseX", parameters.ReverseX);
                    bool actualValX = false;
                    int getRetX = _camera.MV_CC_GetBoolValue_NET("ReverseX", ref actualValX);
                    hwX = (getRetX == MyCamera.MV_OK) ? actualValX : parameters.ReverseX;
                }
                catch { hwX = false; }

                try
                {
                    _camera.MV_CC_SetBoolValue_NET("ReverseY", parameters.ReverseY);
                    bool actualValY = false;
                    int getRetY = _camera.MV_CC_GetBoolValue_NET("ReverseY", ref actualValY);
                    hwY = (getRetY == MyCamera.MV_OK) ? actualValY : parameters.ReverseY;
                }
                catch { hwY = false; }

                lock (_lock)
                {
                    _hardwareReverseXApplied = hwX;
                    _hardwareReverseYApplied = hwY;
                }

                // 7. Packet Size & Packet Delay (GigE Vision)
                if (_deviceInfo.InterfaceType == CameraInterfaceType.GigE)
                {
                    try
                    {
                        int targetPacketSize = parameters.PacketSize;
                        if (targetPacketSize <= 1500)
                        {
                            int optimalPkt = _camera.MV_CC_GetOptimalPacketSize_NET();
                            if (optimalPkt > 0) targetPacketSize = optimalPkt;
                        }

                        if (targetPacketSize > 0)
                        {
                            _camera.MV_GIGE_SetGevSCPSPacketSize_NET((uint)targetPacketSize);
                            _camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", targetPacketSize);
                        }
                        _camera.MV_CC_SetIntValueEx_NET("GevSCPD", parameters.PacketDelay);
                    }
                    catch { }
                }

                // 8. Định dạng điểm ảnh Pixel Format & Hardware Camera ROI
                bool wasGrabbing = _isGrabbing;
                if (wasGrabbing)
                {
                    try { _camera.MV_CC_StopGrabbing_NET(); } catch { }
                }

                try
                {
                    // 8.1. Áp dụng Pixel Format chuẩn MVS
                    if (!string.IsNullOrWhiteSpace(parameters.PixelFormat))
                    {
                        try
                        {
                            uint pixelUint = MapPixelFormatToUint(parameters.PixelFormat);
                            int ret = _camera.MV_CC_SetPixelFormat_NET(pixelUint);
                            if (ret != MyCamera.MV_OK)
                            {
                                string genicamStr = MapPixelFormatToGenICam(parameters.PixelFormat);
                                _camera.MV_CC_SetEnumValueByString_NET("PixelFormat", genicamStr);
                            }
                        }
                        catch { }
                    }

                    // 8.2. Áp dụng Hardware Camera ROI (Cắt từ phần cứng)
                    if (parameters.EnableHardwareRoi && parameters.RoiWidth > 0 && parameters.RoiHeight > 0)
                    {
                        var stWidthMax = new MyCamera.MVCC_INTVALUE_EX();
                        var stHeightMax = new MyCamera.MVCC_INTVALUE_EX();
                        _camera.MV_CC_GetIntValueEx_NET("WidthMax", ref stWidthMax);
                        _camera.MV_CC_GetIntValueEx_NET("HeightMax", ref stHeightMax);

                        long maxW = stWidthMax.nCurValue > 0 ? stWidthMax.nCurValue : 5472;
                        long maxH = stHeightMax.nCurValue > 0 ? stHeightMax.nCurValue : 3648;

                        int w = Math.Clamp((parameters.RoiWidth / 4) * 4, 32, (int)maxW);
                        int h = Math.Clamp((parameters.RoiHeight / 2) * 2, 32, (int)maxH);
                        int ox = Math.Clamp((parameters.RoiOffsetX / 4) * 4, 0, (int)(maxW - w));
                        int oy = Math.Clamp((parameters.RoiOffsetY / 2) * 2, 0, (int)(maxH - h));

                        _camera.MV_CC_SetIntValueEx_NET("OffsetX", 0);
                        _camera.MV_CC_SetIntValueEx_NET("OffsetY", 0);
                        _camera.MV_CC_SetIntValueEx_NET("Width", w);
                        _camera.MV_CC_SetIntValueEx_NET("Height", h);
                        _camera.MV_CC_SetIntValueEx_NET("OffsetX", ox);
                        _camera.MV_CC_SetIntValueEx_NET("OffsetY", oy);
                    }
                    else if (!parameters.EnableHardwareRoi)
                    {
                        var stWidthMax = new MyCamera.MVCC_INTVALUE_EX();
                        var stHeightMax = new MyCamera.MVCC_INTVALUE_EX();
                        _camera.MV_CC_GetIntValueEx_NET("WidthMax", ref stWidthMax);
                        _camera.MV_CC_GetIntValueEx_NET("HeightMax", ref stHeightMax);

                        long maxW = stWidthMax.nCurValue > 0 ? stWidthMax.nCurValue : 5472;
                        long maxH = stHeightMax.nCurValue > 0 ? stHeightMax.nCurValue : 3648;

                        _camera.MV_CC_SetIntValueEx_NET("OffsetX", 0);
                        _camera.MV_CC_SetIntValueEx_NET("OffsetY", 0);
                        _camera.MV_CC_SetIntValueEx_NET("Width", maxW);
                        _camera.MV_CC_SetIntValueEx_NET("Height", maxH);
                    }
                }
                finally
                {
                    if (wasGrabbing)
                    {
                        try { _camera.MV_CC_StartGrabbing_NET(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Lỗi ApplyParameters: {ex.Message}");
            }
        });
    }
    finally
    {
        _driverGate.Release();
    }
}

public override async Task<CameraParameters> ReadParametersAsync()
{
    if (!_isOpened || _camera == null || !IsMvSdkAvailable())
    {
        return await base.ReadParametersAsync();
    }

    return await Task.Run(() =>
    {
        var p = new CameraParameters();
        lock (_lock)
        {
            p = _parameters.Clone();
        }

        try
        {
            // 1. Exposure Time & Auto
            var stExpAuto = new MyCamera.MVCC_ENUMVALUE();
            if (_camera.MV_CC_GetEnumValue_NET("ExposureAuto", ref stExpAuto) == MyCamera.MV_OK)
            {
                p.AutoExposure = (stExpAuto.nCurValue != 0);
            }
            var stExp = new MyCamera.MVCC_FLOATVALUE();
            if (_camera.MV_CC_GetFloatValue_NET("ExposureTime", ref stExp) == MyCamera.MV_OK)
            {
                p.ExposureTimeUs = stExp.fCurValue;
            }

            // 2. Gain & Auto Gain
            var stGainAuto = new MyCamera.MVCC_ENUMVALUE();
            if (_camera.MV_CC_GetEnumValue_NET("GainAuto", ref stGainAuto) == MyCamera.MV_OK)
            {
                p.AutoGain = (stGainAuto.nCurValue != 0);
            }
            var stGain = new MyCamera.MVCC_FLOATVALUE();
            if (_camera.MV_CC_GetFloatValue_NET("Gain", ref stGain) == MyCamera.MV_OK)
            {
                p.GainDb = stGain.fCurValue;
            }

            // 3. Gamma
            var stGamma = new MyCamera.MVCC_FLOATVALUE();
            if (_camera.MV_CC_GetFloatValue_NET("Gamma", ref stGamma) == MyCamera.MV_OK)
            {
                p.Gamma = stGamma.fCurValue;
            }

            // 4. White Balance & BalanceRatio (Red / Blue)
            var stWb = new MyCamera.MVCC_ENUMVALUE();
            if (_camera.MV_CC_GetEnumValue_NET("BalanceWhiteAuto", ref stWb) == MyCamera.MV_OK)
            {
                p.AutoWhiteBalance = (stWb.nCurValue != 0);
            }
            try
            {
                _camera.MV_CC_SetEnumValueByString_NET("BalanceRatioSelector", "Red");
                var stRed = new MyCamera.MVCC_INTVALUE_EX();
                if (_camera.MV_CC_GetIntValueEx_NET("BalanceRatio", ref stRed) == MyCamera.MV_OK && stRed.nCurValue > 0)
                {
                    p.RedGain = (float)stRed.nCurValue / 1024.0f;
                }
                else
                {
                    var stRedF = new MyCamera.MVCC_FLOATVALUE();
                    if (_camera.MV_CC_GetFloatValue_NET("BalanceRatio", ref stRedF) == MyCamera.MV_OK)
                        p.RedGain = stRedF.fCurValue;
                }

                _camera.MV_CC_SetEnumValueByString_NET("BalanceRatioSelector", "Blue");
                var stBlue = new MyCamera.MVCC_INTVALUE_EX();
                if (_camera.MV_CC_GetIntValueEx_NET("BalanceRatio", ref stBlue) == MyCamera.MV_OK && stBlue.nCurValue > 0)
                {
                    p.BlueGain = (float)stBlue.nCurValue / 1024.0f;
                }
                else
                {
                    var stBlueF = new MyCamera.MVCC_FLOATVALUE();
                    if (_camera.MV_CC_GetFloatValue_NET("BalanceRatio", ref stBlueF) == MyCamera.MV_OK)
                        p.BlueGain = stBlueF.fCurValue;
                }
            }
            catch { }

            // 4.1. Đọc Pixel Format hiện thời của Camera từ phần cứng
            try
            {
                var stPix = new MyCamera.MVCC_ENUMVALUE();
                if (_camera.MV_CC_GetEnumValue_NET("PixelFormat", ref stPix) == MyCamera.MV_OK)
                {
                    string fmt = MapUintToPixelFormat((uint)stPix.nCurValue);
                    if (!string.IsNullOrWhiteSpace(fmt))
                    {
                        p.PixelFormat = fmt;
                    }
                }
            }
            catch { }

            // 5. Trigger Mode & Source
            var stTrig = new MyCamera.MVCC_ENUMVALUE();
            if (_camera.MV_CC_GetEnumValue_NET("TriggerMode", ref stTrig) == MyCamera.MV_OK)
            {
                p.TriggerMode = (stTrig.nCurValue == 1) ? CameraTriggerMode.On : CameraTriggerMode.Off;
            }
            var stTrigSrc = new MyCamera.MVCC_ENUMVALUE();
            if (_camera.MV_CC_GetEnumValue_NET("TriggerSource", ref stTrigSrc) == MyCamera.MV_OK)
            {
                p.TriggerSource = stTrigSrc.nCurValue switch
                {
                    0 => CameraTriggerSource.Line0,
                    1 => CameraTriggerSource.Line1,
                    2 => CameraTriggerSource.Line2,
                    7 => CameraTriggerSource.Software,
                    _ => CameraTriggerSource.Software
                };
            }

            // 6. Reverse X / Y
            bool hwX = false;
            if (_camera.MV_CC_GetBoolValue_NET("ReverseX", ref hwX) == MyCamera.MV_OK)
            {
                p.ReverseX = hwX;
            }
            bool hwY = false;
            if (_camera.MV_CC_GetBoolValue_NET("ReverseY", ref hwY) == MyCamera.MV_OK)
            {
                p.ReverseY = hwY;
            }

            // 7. GigE Packet Size & Delay
            if (_deviceInfo.InterfaceType == CameraInterfaceType.GigE)
            {
                var stPktSize = new MyCamera.MVCC_INTVALUE_EX();
                if (_camera.MV_CC_GetIntValueEx_NET("GevSCPSPacketSize", ref stPktSize) == MyCamera.MV_OK)
                {
                    p.PacketSize = (int)stPktSize.nCurValue;
                }
                var stPktDelay = new MyCamera.MVCC_INTVALUE_EX();
                if (_camera.MV_CC_GetIntValueEx_NET("GevSCPD", ref stPktDelay) == MyCamera.MV_OK)
                {
                    p.PacketDelay = (int)stPktDelay.nCurValue;
                }
            }

            // 8. ROI (Width, Height, OffsetX, OffsetY)
            var stW = new MyCamera.MVCC_INTVALUE_EX();
            var stH = new MyCamera.MVCC_INTVALUE_EX();
            var stOx = new MyCamera.MVCC_INTVALUE_EX();
            var stOy = new MyCamera.MVCC_INTVALUE_EX();
            var stWmax = new MyCamera.MVCC_INTVALUE_EX();
            var stHmax = new MyCamera.MVCC_INTVALUE_EX();
            _camera.MV_CC_GetIntValueEx_NET("Width", ref stW);
            _camera.MV_CC_GetIntValueEx_NET("Height", ref stH);
            _camera.MV_CC_GetIntValueEx_NET("OffsetX", ref stOx);
            _camera.MV_CC_GetIntValueEx_NET("OffsetY", ref stOy);
            _camera.MV_CC_GetIntValueEx_NET("WidthMax", ref stWmax);
            _camera.MV_CC_GetIntValueEx_NET("HeightMax", ref stHmax);

            if (stWmax.nCurValue > 0 && stHmax.nCurValue > 0)
            {
                bool isCropped = (stW.nCurValue < stWmax.nCurValue || stH.nCurValue < stHmax.nCurValue || stOx.nCurValue > 0 || stOy.nCurValue > 0);
                p.EnableHardwareRoi = isCropped;
                p.RoiWidth = (int)stW.nCurValue;
                p.RoiHeight = (int)stH.nCurValue;
                p.RoiOffsetX = (int)stOx.nCurValue;
                p.RoiOffsetY = (int)stOy.nCurValue;
            }

            lock (_lock)
            {
                _hardwareReverseXApplied = p.ReverseX;
                _hardwareReverseYApplied = p.ReverseY;
                _parameters = p.Clone();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] ReadParameters exception: {ex.Message}");
        }

        return p;
    });
}

    private static string MapPixelFormatToGenICam(string format)
    {
        return format switch
        {
            "Mono 8" => "Mono8",
            "Mono 10" => "Mono10",
            "Mono 12" => "Mono12",
            "RGB 8" => "RGB8Packed",
            "BGR 8" => "BGR8Packed",
            "YUV 422 (YUYV) Packed" => "YUV422_YUYV_Packed",
            "YUV 422 Packed" => "YUV422Packed",
            "Bayer BG 8" => "BayerBG8",
            "Bayer RG 8" => "BayerRG8",
            "Bayer GB 8" => "BayerGB8",
            "Bayer GR 8" => "BayerGR8",
            "Bayer BG 10" => "BayerBG10",
            "Bayer RG 10" => "BayerRG10",
            "Bayer GB 10" => "BayerGB10",
            "Bayer GR 10" => "BayerGR10",
            "Bayer BG 10 Packed" => "BayerBG10Packed",
            "Bayer RG 10 Packed" => "BayerRG10Packed",
            "Bayer GB 10 Packed" => "BayerGB10Packed",
            "Bayer GR 10 Packed" => "BayerGR10Packed",
            "Bayer BG 12" => "BayerBG12",
            "Bayer RG 12" => "BayerRG12",
            "Bayer GB 12" => "BayerGB12",
            "Bayer GR 12" => "BayerGR12",
            "Bayer BG 12 Packed" => "BayerBG12Packed",
            "Bayer RG 12 Packed" => "BayerRG12Packed",
            "Bayer GB 12 Packed" => "BayerGB12Packed",
            "Bayer GR 12 Packed" => "BayerGR12Packed",
            _ => "BayerBG8"
        };
    }

    private static uint MapPixelFormatToUint(string format)
    {
        return format switch
        {
            "Mono 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8,
            "Mono 10" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10,
            "Mono 12" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12,
            "RGB 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed,
            "BGR 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
            "YUV 422 (YUYV) Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_YUYV_Packed,
            "YUV 422 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_Packed,
            "Bayer BG 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8,
            "Bayer RG 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8,
            "Bayer GB 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8,
            "Bayer GR 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8,
            "Bayer BG 10" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10,
            "Bayer RG 10" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10,
            "Bayer GB 10" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10,
            "Bayer GR 10" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10,
            "Bayer BG 10 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10_Packed,
            "Bayer RG 10 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10_Packed,
            "Bayer GB 10 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10_Packed,
            "Bayer GR 10 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10_Packed,
            "Bayer BG 12" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12,
            "Bayer RG 12" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12,
            "Bayer GB 12" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12,
            "Bayer GR 12" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12,
            "Bayer BG 12 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12_Packed,
            "Bayer RG 12 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12_Packed,
            "Bayer GB 12 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12_Packed,
            "Bayer GR 12 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12_Packed,
            _ => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8
        };
    }

    private static string MapUintToPixelFormat(uint pixelType)
    {
        return (MyCamera.MvGvspPixelType)pixelType switch
        {
            MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8 => "Mono 8",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10 => "Mono 10",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12 => "Mono 12",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed => "RGB 8",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed => "BGR 8",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_YUYV_Packed => "YUV 422 (YUYV) Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_Packed => "YUV 422 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8 => "Bayer BG 8",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8 => "Bayer RG 8",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8 => "Bayer GB 8",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8 => "Bayer GR 8",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10 => "Bayer BG 10",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10 => "Bayer RG 10",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10 => "Bayer GB 10",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10 => "Bayer GR 10",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10_Packed => "Bayer BG 10 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10_Packed => "Bayer RG 10 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10_Packed => "Bayer GB 10 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10_Packed => "Bayer GR 10 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12 => "Bayer BG 12",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12 => "Bayer RG 12",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12 => "Bayer GB 12",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12 => "Bayer GR 12",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12_Packed => "Bayer BG 12 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12_Packed => "Bayer RG 12 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12_Packed => "Bayer GB 12 Packed",
            MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12_Packed => "Bayer GR 12 Packed",
            _ => "Bayer BG 8"
        };
    }

    private void ContinuousGrabLoop(CancellationToken token)
    {
        var frameOut = new MyCamera.MV_FRAME_OUT();
        uint fallbackBufLen = 0;
        IntPtr pFallbackData = IntPtr.Zero;

        try
        {
            while (!token.IsCancellationRequested && _isGrabbing)
            {
                try
                {
                    bool gotFrame = false;
                    IntPtr pData = IntPtr.Zero;
                    MyCamera.MV_FRAME_OUT_INFO_EX frameInfo = default;
                    bool isZeroCopyBuffer = false;

                    // 1. Ưu tiên cơ chế Zero-Copy DMA Driver Buffer (MV_CC_GetImageBuffer_NET) - Đạt chuẩn 41 FPS cho 25MP 10GbE
                    if (_camera != null)
                    {
                        int retBuf = _camera.MV_CC_GetImageBuffer_NET(ref frameOut, 100);
                        if (retBuf == MyCamera.MV_OK && frameOut.pBufAddr != IntPtr.Zero && frameOut.stFrameInfo.nWidth > 0 && frameOut.stFrameInfo.nHeight > 0)
                        {
                            gotFrame = true;
                            isZeroCopyBuffer = true;
                            pData = frameOut.pBufAddr;
                            frameInfo = frameOut.stFrameInfo;
                        }
                        else
                        {
                            // Fallback cơ chế GetOneFrameTimeout nếu driver không hỗ trợ GetImageBuffer
                            if (pFallbackData == IntPtr.Zero)
                            {
                                fallbackBufLen = GetPayloadSize();
                                pFallbackData = Marshal.AllocHGlobal((int)fallbackBufLen);
                            }

                            int retOne = _camera.MV_CC_GetOneFrameTimeout_NET(pFallbackData, fallbackBufLen, ref frameInfo, 100);
                            if (retOne == MyCamera.MV_OK && frameInfo.nWidth > 0 && frameInfo.nHeight > 0)
                            {
                                gotFrame = true;
                                isZeroCopyBuffer = false;
                                pData = pFallbackData;
                            }
                        }
                    }

                    if (gotFrame && pData != IntPtr.Zero)
                    {
                        try
                        {
                            // 2. Hardware Frame Drop & Sequence Counter Tracking
                            if (frameInfo.nFrameNum > 0)
                            {
                                if (!_isFirstFrame && frameInfo.nFrameNum > _lastFrameNum + 1)
                                {
                                    uint dropped = frameInfo.nFrameNum - _lastFrameNum - 1;
                                    Interlocked.Add(ref _hardwareDroppedFrames, dropped);
                                    System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] ⚠️ Phát hiện rớt frame phần cứng: {dropped} frame(s), Tổng rơi: {_hardwareDroppedFrames}");
                                }
                                _lastFrameNum = frameInfo.nFrameNum;
                                _isFirstFrame = false;
                            }

                            ulong devTimestampNs = ((ulong)frameInfo.nDevTimeStampHigh << 32) | (ulong)frameInfo.nDevTimeStampLow;

                            _latestMetadata = new CameraFrameMetadata
                            {
                                FrameNum = frameInfo.nFrameNum,
                                DeviceTimestampNs = devTimestampNs,
                                HostTimestamp = DateTime.UtcNow,
                                HardwareDroppedFrames = Interlocked.Read(ref _hardwareDroppedFrames),
                                Width = frameInfo.nWidth,
                                Height = frameInfo.nHeight,
                                PixelFormat = frameInfo.enPixelType.ToString()
                            };

                            bool shouldDiscard = false;
                            lock (_latestContinuousFrameLock)
                            {
                                if (_discardFramesCount > 0)
                                {
                                    _discardFramesCount--;
                                    shouldDiscard = true;
                                }
                            }

                            if (shouldDiscard)
                            {
                                continue;
                            }

                            // 3. Giải mã màu Zero-Copy trực tiếp vào Ring Buffer Mat (13.7ms cho 25MP ~ 73 FPS)
                            var rawMat = ConvertHikFrameToMat(pData, frameInfo);
                            if (!rawMat.Empty())
                            {
                                // Nếu có bên đang chờ GrabFrameAsync (ví dụ Snapshot), clone 1 bản riêng cho họ
                                if (Volatile.Read(ref _grabFrameWaiters) > 0)
                                {
                                    lock (_latestContinuousFrameLock)
                                    {
                                        _latestContinuousFrame?.Dispose();
                                        _latestContinuousFrame = rawMat.Clone();
                                    }
                                }

                                // 4. Phát sự kiện frame đã nhận (RaiseFrameCaptured tự kiểm tra và không clone thừa)
                                RaiseFrameCaptured(rawMat);
                            }
                        }
                        finally
                        {
                            if (isZeroCopyBuffer && _camera != null)
                            {
                                _camera.MV_CC_FreeImageBuffer_NET(ref frameOut);
                            }
                        }
                    }
                    else
                    {
                        // Khi chờ Hardware Trigger hoặc nhường nhịp CPU
                        Thread.Sleep(2);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Grab loop error: {ex.Message}");
                }
            }
        }
        finally
        {
            if (pFallbackData != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(pFallbackData); } catch { }
                pFallbackData = IntPtr.Zero;
            }
        }
    }

    private Mat ConvertHikFrameToMat(IntPtr pData, MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
    {
        int w = frameInfo.nWidth;
        int h = frameInfo.nHeight;
        if (w <= 0 || h <= 0 || pData == IntPtr.Zero) return new Mat();

        // 1. Khởi tạo Ring Buffer 4 Mat trong NativeMatPool (Zero GC allocation)
        _matPool.Initialize(w, h, MatType.CV_8UC3);
        var targetMat = _matPool.GetNextRingBuffer();
        if (targetMat == null || targetMat.IsDisposed)
        {
            targetMat = new Mat(h, w, MatType.CV_8UC3);
        }

        // 2. Chuyển đổi định dạng điểm ảnh
        // 2.1. Đã là BGR8_Packed chuẩn
        if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed)
        {
            using var bgrDirect = Mat.FromPixelData(h, w, MatType.CV_8UC3, pData);
            bgrDirect.CopyTo(targetMat);
            return targetMat;
        }

        // 2.2. Là RGB8_Packed
        if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed)
        {
            using var rgbMat = Mat.FromPixelData(h, w, MatType.CV_8UC3, pData);
            Cv2.CvtColor(rgbMat, targetMat, ColorConversionCodes.RGB2BGR);
            return targetMat;
        }

        // 2.3. Là Mono8
        if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
        {
            using var mono = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
            Cv2.CvtColor(mono, targetMat, ColorConversionCodes.GRAY2BGR);
            return targetMat;
        }

        // 2.4. Đối với tất cả định dạng màu Bayer (BayerBG8, BayerRG8, BayerGB8, BayerGR8, Bayer10, Bayer12...)
        // Ưu tiên số 1: Sử dụng bộ xử lý màu chính hãng Hikrobot SDK (MV_CC_ConvertPixelTypeEx_NET)
        // Ghi TRỰC TIẾP vào targetMat.Data (Zero-Copy DMA vào Ring Buffer, 0 cấp phát trung gian).
        // Bộ xử lý SDK chính hãng tự động áp dụng đầy đủ:
        // - Đúng 100% Bayer pattern, triệt tiêu lỗi hoán đổi Red/Blue
        // - Cân bằng trắng (White Balance / BalanceRatio)
        // - Ma trận hiệu chỉnh màu sắc gốc của Camera (CCM - Color Correction Matrix)
        // - Đường cong Gamma tối ưu (giúp màu sắc sống động, chân thực chuẩn xác như phần mềm MVS)
        if (_camera != null && IsMvSdkAvailable())
        {
            try
            {
                var cvtParam = new MyCamera.MV_CC_PIXEL_CONVERT_PARAM_EX
                {
                    nWidth = (uint)w,
                    nHeight = (uint)h,
                    enSrcPixelType = frameInfo.enPixelType,
                    pSrcData = pData,
                    nSrcDataLen = frameInfo.nFrameLen > 0 ? frameInfo.nFrameLen : (uint)(w * h * 4),
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
                    pDstBuffer = targetMat.Data,
                    nDstBufferSize = (uint)(w * h * 3)
                };

                int ret = _camera.MV_CC_ConvertPixelTypeEx_NET(ref cvtParam);
                if (ret == MyCamera.MV_OK && cvtParam.nDstLen > 0)
                {
                    return targetMat;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] SDK ConvertPixelTypeEx exception: {ex.Message}");
            }
        }

        // 2.5. Fallback OpenCV siêu tốc với ÁNH XẠ BAYER CHUẨN XÁC 100%
        // (Lưu ý: Quy ước Bayer của OpenCV đặt tên theo hàng 2, nên cần đảo chéo để khớp chuẩn GenICam/Hikrobot):
        // - GenICam BayerBG8 (B G / G R) -> OpenCV BayerRG2BGR (cho ra B ở (0,0) và R ở (1,1))
        // - GenICam BayerRG8 (R G / G B) -> OpenCV BayerBG2BGR (cho ra R ở (0,0) và B ở (1,1))
        // - GenICam BayerGB8 (G B / R G) -> OpenCV BayerGR2BGR (cho ra B ở (0,1) và R ở (1,0))
        // - GenICam BayerGR8 (G R / B G) -> OpenCV BayerGB2BGR (cho ra R ở (0,1) và B ở (1,0))
        switch (frameInfo.enPixelType)
        {
            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8:
            {
                using var bayer = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                Cv2.CvtColor(bayer, targetMat, ColorConversionCodes.BayerRG2BGR);
                return targetMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8:
            {
                using var bayer = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                Cv2.CvtColor(bayer, targetMat, ColorConversionCodes.BayerBG2BGR);
                return targetMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8:
            {
                using var bayer = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                Cv2.CvtColor(bayer, targetMat, ColorConversionCodes.BayerGR2BGR);
                return targetMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8:
            {
                using var bayer = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                Cv2.CvtColor(bayer, targetMat, ColorConversionCodes.BayerGB2BGR);
                return targetMat;
            }

            default:
            {
                using var rawMono = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                Cv2.CvtColor(rawMono, targetMat, ColorConversionCodes.GRAY2BGR);
                return targetMat;
            }
        }
    }
}

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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

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

    public override async Task<Mat?> GrabFrameAsync(int timeoutMs = 3000)
    {
        if (!_isOpened || _camera == null || !IsMvSdkAvailable()) return null;

        // 1. Nếu camera đang ở chế độ Live View (Grabbing liên tục)
        if (_isGrabbing)
        {
            // Tránh gọi MV_CC_GetOneFrameTimeout_NET xung đột với ContinuousGrabLoop
            for (int i = 0; i < 20; i++)
            {
                lock (_latestContinuousFrameLock)
                {
                    if (_latestContinuousFrame != null && !_latestContinuousFrame.IsDisposed && !_latestContinuousFrame.Empty())
                    {
                        return _latestContinuousFrame.Clone();
                    }
                }
                await Task.Delay(25);
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
                    return ConvertHikFrameToMat(pData, frameInfo);
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

                if (needStopGrabbingAfterwards)
                {
                    try { _camera.MV_CC_StopGrabbing_NET(); } catch { }
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
                // 0. Bật chất lượng chuyển đổi Bayer chất lượng cao của Hikrobot SDK
                try
                {
                    _camera.MV_CC_SetBayerCvtQuality_NET(1);
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
                }
                else
                {
                    _camera.MV_CC_SetEnumValueByString_NET("TriggerMode", "Off");
                }

                // 6. Hardware Reverse X / Y (Flip)
                try { _camera.MV_CC_SetBoolValue_NET("ReverseX", parameters.ReverseX); } catch { }
                try { _camera.MV_CC_SetBoolValue_NET("ReverseY", parameters.ReverseY); } catch { }

                // 7. Packet Size & Packet Delay (GigE Vision)
                if (_deviceInfo.InterfaceType == CameraInterfaceType.GigE && parameters.PacketSize > 0)
                {
                    try
                    {
                        _camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", parameters.PacketSize);
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
            "Bayer GB 8" => "BayerGB8",
            "Bayer GB 10" => "BayerGB10",
            "Bayer GB 10 Packed" => "BayerGB10Packed",
            "Bayer GB 12" => "BayerGB12",
            "Bayer GB 12 Packed" => "BayerGB12Packed",
            _ => "BayerGB8"
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
            "Bayer GB 8" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8,
            "Bayer GB 10" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10,
            "Bayer GB 10 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10_Packed,
            "Bayer GB 12" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12,
            "Bayer GB 12 Packed" => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12_Packed,
            _ => (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8
        };
    }

    private void ContinuousGrabLoop(CancellationToken token)
    {
        uint bufLen = GetPayloadSize();
        IntPtr pData = Marshal.AllocHGlobal((int)bufLen);
        var frameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();

        while (!token.IsCancellationRequested && _isGrabbing)
        {
            try
            {
                int ret = _camera != null ? _camera.MV_CC_GetOneFrameTimeout_NET(pData, bufLen, ref frameInfo, 100) : -1;
                if (ret == MyCamera.MV_OK && frameInfo.nWidth > 0 && frameInfo.nHeight > 0)
                {
                    using var rawMat = ConvertHikFrameToMat(pData, frameInfo);
                    if (!rawMat.Empty())
                    {
                        lock (_latestContinuousFrameLock)
                        {
                            _latestContinuousFrame?.Dispose();
                            _latestContinuousFrame = rawMat.Clone();
                        }
                        RaiseFrameCaptured(rawMat);
                    }
                }
                else
                {
                    // Khi chờ Hardware Trigger hoặc nhường nhịp CPU
                    Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Grab loop error: {ex.Message}");
            }
        }

        Marshal.FreeHGlobal(pData);
    }

    private Mat ConvertHikFrameToMat(IntPtr pData, MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
    {
        int w = frameInfo.nWidth;
        int h = frameInfo.nHeight;
        if (w <= 0 || h <= 0) return new Mat();

        // 1. Chuyển đổi màu chính hãng chuẩn MVS bằng bộ xử lý Hikrobot SDK (MV_CC_ConvertPixelTypeEx_NET)
        if (_camera != null && IsMvSdkAvailable())
        {
            uint dstBufferSize = (uint)(w * h * 3);
            IntPtr pDstData = IntPtr.Zero;
            try
            {
                pDstData = Marshal.AllocHGlobal((int)dstBufferSize);
                var cvtParam = new MyCamera.MV_CC_PIXEL_CONVERT_PARAM_EX
                {
                    nWidth = (uint)w,
                    nHeight = (uint)h,
                    enSrcPixelType = frameInfo.enPixelType,
                    pSrcData = pData,
                    nSrcDataLen = frameInfo.nFrameLen > 0 ? frameInfo.nFrameLen : (uint)(w * h * 4),
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
                    pDstBuffer = pDstData,
                    nDstBufferSize = dstBufferSize
                };

                int ret = _camera.MV_CC_ConvertPixelTypeEx_NET(ref cvtParam);
                if (ret == MyCamera.MV_OK && cvtParam.nDstLen > 0)
                {
                    using var bgrDirect = Mat.FromPixelData(h, w, MatType.CV_8UC3, pDstData);
                    return bgrDirect.Clone();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] SDK ConvertPixelTypeEx exception: {ex.Message}");
            }
            finally
            {
                if (pDstData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pDstData);
                }
            }
        }

        // 2. Fallback thủ công nếu SDK convert không thực hiện được
        switch (frameInfo.enPixelType)
        {
            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed:
            {
                using var bgrDirect = Mat.FromPixelData(h, w, MatType.CV_8UC3, pData);
                return bgrDirect.Clone();
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed:
            {
                using var rgbMat = Mat.FromPixelData(h, w, MatType.CV_8UC3, pData);
                var bgrMat = new Mat();
                Cv2.CvtColor(rgbMat, bgrMat, ColorConversionCodes.RGB2BGR);
                return bgrMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8:
            {
                using var rawMono = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                var colorMat = new Mat();
                Cv2.CvtColor(rawMono, colorMat, ColorConversionCodes.GRAY2BGR);
                return colorMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8:
            {
                using var bayerMat = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                var bgrMat = new Mat();
                Cv2.CvtColor(bayerMat, bgrMat, ColorConversionCodes.BayerRG2BGR);
                return bgrMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8:
            {
                using var bayerMat = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                var bgrMat = new Mat();
                Cv2.CvtColor(bayerMat, bgrMat, ColorConversionCodes.BayerGB2BGR);
                return bgrMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8:
            {
                using var bayerMat = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                var bgrMat = new Mat();
                Cv2.CvtColor(bayerMat, bgrMat, ColorConversionCodes.BayerBG2BGR);
                return bgrMat;
            }

            case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8:
            {
                using var bayerMat = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                var bgrMat = new Mat();
                Cv2.CvtColor(bayerMat, bgrMat, ColorConversionCodes.BayerGR2BGR);
                return bgrMat;
            }

            default:
            {
                // Mặc định xem như Mono8 và chuyển sang BGR
                using var rawMat = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);
                var colorMat = new Mat();
                Cv2.CvtColor(rawMat, colorMat, ColorConversionCodes.GRAY2BGR);
                return colorMat;
            }
        }
    }
}

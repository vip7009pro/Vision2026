using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera.Drivers;

/// <summary>
/// Driver tích hợp camera công nghiệp HIKRobot MVS SDK chuẩn qua P/Invoke MvCameraControl.dll.
/// Hỗ trợ cả 2 chuẩn giao tiếp GigE Vision & USB3 Vision.
/// Tự động cách ly an toàn nếu chưa cài đặt MVS SDK runtime trên máy tính.
/// </summary>
public sealed class HikCameraDriver : CameraDriverBase
{
    private IntPtr _handle = IntPtr.Zero;
    private static bool? _isMvSdkAvailable;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    /// <summary>
    /// Tìm đường dẫn file đĩa MvCameraControl.dll thực tế trên Windows thuần C# managed (không gọi unmanaged code)
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
                string p32 = System.IO.Path.Combine(envPath, "Win32", "MvCameraControl.dll");
                if (System.IO.File.Exists(p32)) return p32;
            }

            string? mvsPath = Environment.GetEnvironmentVariable("MVS_SDK_PATH");
            if (!string.IsNullOrWhiteSpace(mvsPath))
            {
                string p = System.IO.Path.Combine(mvsPath, "MvCameraControl.dll");
                if (System.IO.File.Exists(p)) return p;
            }

            // 2. Kiểm tra thư mục cài đặt tiêu chuẩn của Hikrobot MVS trên Windows
            string[] candidatePaths = new string[]
            {
                @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64\MvCameraControl.dll",
                @"C:\Program Files\Common Files\MVS\Runtime\Win64\MvCameraControl.dll",
                @"C:\Program Files (x86)\MVS\Development\VB.Net\MvCameraControl.dll",
                @"C:\Program Files\MVS\Development\VB.Net\MvCameraControl.dll",
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
                // File đĩa DLL không tồn tại -> 100% chưa cài MVS SDK. Dừng lại ngay lập tức, không gọi P/Invoke hay LoadLibrary!
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

                    bool loaded = NativeLibrary.TryLoad(dllPath, out var handle);
                    if (loaded && handle != IntPtr.Zero)
                    {
                        NativeLibrary.Free(handle);
                        _isMvSdkAvailable = true;
                    }
                    else
                    {
                        _isMvSdkAvailable = false;
                    }
                }
                catch
                {
                    _isMvSdkAvailable = false;
                }
            }
        }
        return _isMvSdkAvailable.Value;
    }

    #region MvCameraControl.dll P/Invoke Native Imports & Structs (Isolated in NativeMethods)

    private static class NativeMethods
    {
        private const string DllName = "MvCameraControl.dll";

        public const uint MV_GIGE_DEVICE = 0x00000001;
        public const uint MV_USB_DEVICE = 0x00000002;
        public const uint MV_ACCESS_Exclusive = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct MV_GIGE_DEVICE_INFO
        {
            public uint nIpCfgOption;
            public uint nIpCfgCurrent;
            public uint nCurrentIp;
            public uint nCurrentSubNetMask;
            public uint nDefultGateWay;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chManufacturerName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chModelName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chDeviceVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chManufacturerSpecificInfo;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string chSerialNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chUserDefinedName;
            public uint nNetExport;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MV_USB3_DEVICE_INFO
        {
            public System.Byte CblId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chManufacturerName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chModelName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chDeviceVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chManufacturerSpecificInfo;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string chSerialNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string chUserDefinedName;
            public uint nbDeviceAddress;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] Reserved;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct MV_CC_DEVICE_INFO
        {
            [FieldOffset(0)] public ushort nMajorVer;
            [FieldOffset(2)] public ushort nMinorVer;
            [FieldOffset(4)] public uint nMacAddrHigh;
            [FieldOffset(8)] public uint nMacAddrLow;
            [FieldOffset(12)] public uint nTLayerType;
            [FieldOffset(16)] public MV_GIGE_DEVICE_INFO stGigEInfo;
            [FieldOffset(16)] public MV_USB3_DEVICE_INFO stUsb3VInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MV_CC_DEVICE_INFO_LIST
        {
            public uint nDeviceNum;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public IntPtr[] pDeviceInfo;

            public static MV_CC_DEVICE_INFO_LIST Create()
            {
                return new MV_CC_DEVICE_INFO_LIST
                {
                    nDeviceNum = 0,
                    pDeviceInfo = new IntPtr[256]
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MV_FRAME_OUT_INFO_EX
        {
            public ushort nWidth;
            public ushort nHeight;
            public uint enPixelType;
            public uint nFrameNum;
            public uint nDevTimeStampHigh;
            public uint nDevTimeStampLow;
            public uint nReserved0;
            public uint nHostTimeStampHigh;
            public uint nHostTimeStampLow;
            public uint nFrameLen;
            public uint nSecondCount;
            public uint nCycleCount;
            public uint nCycleOffset;
            public float fGain;
            public float fExposureTime;
            public uint nAverageBrightness;
            public uint nRed;
            public uint nGreen;
            public uint nBlue;
            public uint nFrameCounter;
            public uint nTriggerIndex;
            public uint nInput;
            public uint nOutput;
            public ushort nOffsetX;
            public ushort nOffsetY;
            public ushort nChunkWidth;
            public ushort nChunkHeight;
            public uint nLostPacket;
            public uint nUnused;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public uint[] Reserved;
        }

        [DllImport(DllName, EntryPoint = "MV_CC_EnumDevices", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_EnumDevices(uint nTLayerType, ref MV_CC_DEVICE_INFO_LIST pstDevList);

        [DllImport(DllName, EntryPoint = "MV_CC_CreateHandle", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_CreateHandle(ref IntPtr handle, ref MV_CC_DEVICE_INFO pstDevInfo);

        [DllImport(DllName, EntryPoint = "MV_CC_DestroyHandle", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_DestroyHandle(IntPtr handle);

        [DllImport(DllName, EntryPoint = "MV_CC_OpenDevice", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_OpenDevice(IntPtr handle, uint nAccessMode, ushort nSwitchoverKey);

        [DllImport(DllName, EntryPoint = "MV_CC_CloseDevice", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_CloseDevice(IntPtr handle);

        [DllImport(DllName, EntryPoint = "MV_CC_StartGrabbing", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_StartGrabbing(IntPtr handle);

        [DllImport(DllName, EntryPoint = "MV_CC_StopGrabbing", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_StopGrabbing(IntPtr handle);

        [DllImport(DllName, EntryPoint = "MV_CC_GetOneFrameTimeout", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_GetOneFrameTimeout(IntPtr handle, IntPtr pData, uint nDataSize, ref MV_FRAME_OUT_INFO_EX pstFrameInfo, uint nMsec);

        [DllImport(DllName, EntryPoint = "MV_CC_SetFloatValue", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetFloatValue(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string strKey, float fValue);

        [DllImport(DllName, EntryPoint = "MV_CC_SetEnumValue", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetEnumValue(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string strKey, uint nValue);

        [DllImport(DllName, EntryPoint = "MV_CC_SetEnumValueByString", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetEnumValueByString(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string strKey, [MarshalAs(UnmanagedType.LPStr)] string sValue);

        [DllImport(DllName, EntryPoint = "MV_CC_SetBoolValue", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetBoolValue(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string strKey, bool bValue);

        [DllImport(DllName, EntryPoint = "MV_CC_SetIntValue", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetIntValue(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string strKey, uint nValue);

        [DllImport(DllName, EntryPoint = "MV_CC_SetCommandValue", CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetCommandValue(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string strKey);
    }

    #endregion

    /// <summary>
    /// Tìm kiếm tất cả các camera Hikrobot (GigE & USB3) đang cắm vào PC
    /// </summary>
    public static List<CameraDeviceInfo> ScanDevices()
    {
        var resultList = new List<CameraDeviceInfo>();

        // 1. Kiểm tra xem DLL MvCameraControl.dll có tồn tại trên máy hay không
        if (!IsMvSdkAvailable())
        {
            System.Diagnostics.Debug.WriteLine("[HikCameraDriver] MvCameraControl.dll chưa được cài đặt trên hệ thống.");
            return resultList;
        }

        // 2. Chỉ gọi NativeMethods khi MvCameraControl.dll chắc chắn khả thi
        try
        {
            var deviceList = NativeMethods.MV_CC_DEVICE_INFO_LIST.Create();
            int ret = NativeMethods.MV_CC_EnumDevices(NativeMethods.MV_GIGE_DEVICE | NativeMethods.MV_USB_DEVICE, ref deviceList);

            if (ret == 0 && deviceList.nDeviceNum > 0)
            {
                for (int i = 0; i < deviceList.nDeviceNum; i++)
                {
                    if (deviceList.pDeviceInfo[i] == IntPtr.Zero) continue;

                    var devInfo = (NativeMethods.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(deviceList.pDeviceInfo[i], typeof(NativeMethods.MV_CC_DEVICE_INFO))!;
                    
                    var info = new CameraDeviceInfo
                    {
                        Vendor = CameraVendor.Hikrobot,
                        Index = i
                    };

                    if (devInfo.nTLayerType == NativeMethods.MV_GIGE_DEVICE)
                    {
                        info.InterfaceType = CameraInterfaceType.GigE;
                        info.ModelName = string.IsNullOrWhiteSpace(devInfo.stGigEInfo.chModelName) ? "Hikrobot GigE Camera" : devInfo.stGigEInfo.chModelName;
                        info.SerialNumber = devInfo.stGigEInfo.chSerialNumber ?? "";
                        info.UserDefinedName = devInfo.stGigEInfo.chUserDefinedName ?? "";

                        uint ip = devInfo.stGigEInfo.nCurrentIp;
                        info.IpAddress = $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
                    }
                    else if (devInfo.nTLayerType == NativeMethods.MV_USB_DEVICE)
                    {
                        info.InterfaceType = CameraInterfaceType.USB3;
                        info.ModelName = string.IsNullOrWhiteSpace(devInfo.stUsb3VInfo.chModelName) ? "Hikrobot USB3 Camera" : devInfo.stUsb3VInfo.chModelName;
                        info.SerialNumber = devInfo.stUsb3VInfo.chSerialNumber ?? "";
                        info.UserDefinedName = devInfo.stUsb3VInfo.chUserDefinedName ?? "";
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
            RaiseErrorOccurred("Chưa cài đặt Hikrobot MVS SDK (MvCameraControl.dll) trên máy tính này.");
            _isOpened = false;
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                var deviceList = NativeMethods.MV_CC_DEVICE_INFO_LIST.Create();
                int ret = NativeMethods.MV_CC_EnumDevices(NativeMethods.MV_GIGE_DEVICE | NativeMethods.MV_USB_DEVICE, ref deviceList);

                if (ret != 0 || deviceList.nDeviceNum == 0 || device.Index >= deviceList.nDeviceNum || deviceList.pDeviceInfo[device.Index] == IntPtr.Zero)
                {
                    RaiseErrorOccurred($"Không tìm thấy camera Hikrobot chỉ số {device.Index}. Vui lòng kiểm tra dây cáp và nguồn camera.");
                    return false;
                }

                var devInfo = (NativeMethods.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(deviceList.pDeviceInfo[device.Index], typeof(NativeMethods.MV_CC_DEVICE_INFO))!;

                ret = NativeMethods.MV_CC_CreateHandle(ref _handle, ref devInfo);
                if (ret != 0 || _handle == IntPtr.Zero)
                {
                    RaiseErrorOccurred("Lỗi tạo Handle kết nối camera Hikrobot.");
                    return false;
                }

                ret = NativeMethods.MV_CC_OpenDevice(_handle, NativeMethods.MV_ACCESS_Exclusive, 0);
                if (ret != 0)
                {
                    NativeMethods.MV_CC_DestroyHandle(_handle);
                    _handle = IntPtr.Zero;
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

        if (_handle != IntPtr.Zero && IsMvSdkAvailable())
        {
            try
            {
                NativeMethods.MV_CC_CloseDevice(_handle);
                NativeMethods.MV_CC_DestroyHandle(_handle);
            }
            catch { }
            _handle = IntPtr.Zero;
        }

        _isOpened = false;
    }

    public override async Task<bool> StartGrabbingAsync()
    {
        if (!_isOpened || _handle == IntPtr.Zero || !IsMvSdkAvailable()) return false;
        if (_isGrabbing) return true;

        return await Task.Run(() =>
        {
            try
            {
                int ret = NativeMethods.MV_CC_StartGrabbing(_handle);
                if (ret != 0)
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

        if (_handle != IntPtr.Zero && IsMvSdkAvailable())
        {
            try { NativeMethods.MV_CC_StopGrabbing(_handle); } catch { }
        }

        await Task.CompletedTask;
    }

    public override async Task<Mat?> GrabFrameAsync(int timeoutMs = 1000)
    {
        if (!_isOpened || _handle == IntPtr.Zero || !IsMvSdkAvailable()) return null;

        return await Task.Run(() =>
        {
            try
            {
                uint bufferSize = 1920 * 1080 * 4;
                IntPtr pData = Marshal.AllocHGlobal((int)bufferSize);
                var frameInfo = new NativeMethods.MV_FRAME_OUT_INFO_EX();

                int ret = NativeMethods.MV_CC_GetOneFrameTimeout(_handle, pData, bufferSize, ref frameInfo, (uint)timeoutMs);
                if (ret == 0 && frameInfo.nWidth > 0 && frameInfo.nHeight > 0)
                {
                    var mat = ConvertHikFrameToMat(pData, frameInfo);
                    Marshal.FreeHGlobal(pData);
                    return mat;
                }

                Marshal.FreeHGlobal(pData);
                return null;
            }
            catch
            {
                return null;
            }
        });
    }

    public override async Task<bool> ExecuteSoftwareTriggerAsync()
    {
        if (!_isOpened || _handle == IntPtr.Zero || !IsMvSdkAvailable()) return false;

        return await Task.Run(() =>
        {
            try
            {
                int ret = NativeMethods.MV_CC_SetCommandValue(_handle, "TriggerSoftware");
                return ret == 0;
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
        if (!_isOpened || _handle == IntPtr.Zero || !IsMvSdkAvailable()) return;

        await Task.Run(() =>
        {
            try
            {
                // 1. Exposure Time & Auto Exposure
                if (parameters.AutoExposure)
                {
                    NativeMethods.MV_CC_SetEnumValueByString(_handle, "ExposureAuto", "Continuous");
                }
                else
                {
                    NativeMethods.MV_CC_SetEnumValueByString(_handle, "ExposureAuto", "Off");
                    NativeMethods.MV_CC_SetFloatValue(_handle, "ExposureTime", parameters.ExposureTimeUs);
                }

                // 2. Gain & Auto Gain
                if (parameters.AutoGain)
                {
                    NativeMethods.MV_CC_SetEnumValueByString(_handle, "GainAuto", "Continuous");
                }
                else
                {
                    NativeMethods.MV_CC_SetEnumValueByString(_handle, "GainAuto", "Off");
                    NativeMethods.MV_CC_SetFloatValue(_handle, "Gain", parameters.GainDb);
                }

                // 3. Gamma
                try
                {
                    NativeMethods.MV_CC_SetBoolValue(_handle, "GammaEnable", true);
                    NativeMethods.MV_CC_SetFloatValue(_handle, "Gamma", parameters.Gamma);
                }
                catch { }

                // 4. Trigger Mode & Trigger Source
                if (parameters.TriggerMode == CameraTriggerMode.On)
                {
                    NativeMethods.MV_CC_SetEnumValueByString(_handle, "TriggerMode", "On");
                    string srcStr = parameters.TriggerSource switch
                    {
                        CameraTriggerSource.Line0 => "Line0",
                        CameraTriggerSource.Line1 => "Line1",
                        CameraTriggerSource.Line2 => "Line2",
                        _ => "Software"
                    };
                    NativeMethods.MV_CC_SetEnumValueByString(_handle, "TriggerSource", srcStr);
                }
                else
                {
                    NativeMethods.MV_CC_SetEnumValueByString(_handle, "TriggerMode", "Off");
                }

                // 5. Hardware Reverse X / Y (Flip)
                try { NativeMethods.MV_CC_SetBoolValue(_handle, "ReverseX", parameters.ReverseX); } catch { }
                try { NativeMethods.MV_CC_SetBoolValue(_handle, "ReverseY", parameters.ReverseY); } catch { }

                // 6. Packet Size & Packet Delay (GigE Vision)
                if (_deviceInfo.InterfaceType == CameraInterfaceType.GigE && parameters.PacketSize > 0)
                {
                    try
                    {
                        NativeMethods.MV_CC_SetIntValue(_handle, "GevSCPSPacketSize", (uint)parameters.PacketSize);
                        NativeMethods.MV_CC_SetIntValue(_handle, "GevSCPD", (uint)parameters.PacketDelay);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Lỗi ApplyParameters: {ex.Message}");
            }
        });
    }

    private void ContinuousGrabLoop(CancellationToken token)
    {
        int bufLen = 1920 * 1080 * 4;
        IntPtr pData = Marshal.AllocHGlobal(bufLen);
        var frameInfo = new NativeMethods.MV_FRAME_OUT_INFO_EX();

        while (!token.IsCancellationRequested && _isGrabbing)
        {
            try
            {
                int ret = NativeMethods.MV_CC_GetOneFrameTimeout(_handle, pData, (uint)bufLen, ref frameInfo, 100);
                if (ret == 0 && frameInfo.nWidth > 0 && frameInfo.nHeight > 0)
                {
                    using var rawMat = ConvertHikFrameToMat(pData, frameInfo);
                    if (!rawMat.Empty())
                    {
                        RaiseFrameCaptured(rawMat);
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HikCameraDriver] Grab loop error: {ex.Message}");
            }
        }

        Marshal.FreeHGlobal(pData);
    }

    private static Mat ConvertHikFrameToMat(IntPtr pData, NativeMethods.MV_FRAME_OUT_INFO_EX frameInfo)
    {
        int w = frameInfo.nWidth;
        int h = frameInfo.nHeight;
        if (w <= 0 || h <= 0) return new Mat();

        using var rawMat = Mat.FromPixelData(h, w, MatType.CV_8UC1, pData);

        if (frameInfo.enPixelType == 0x02180015) // BGR8
        {
            using var bgrDirect = Mat.FromPixelData(h, w, MatType.CV_8UC3, pData);
            return bgrDirect.Clone();
        }
        else if (frameInfo.enPixelType == 0x02180014) // RGB8
        {
            using var rgbMat = Mat.FromPixelData(h, w, MatType.CV_8UC3, pData);
            var bgrMat = new Mat();
            Cv2.CvtColor(rgbMat, bgrMat, ColorConversionCodes.RGB2BGR);
            return bgrMat;
        }

        var colorMat = new Mat();
        Cv2.CvtColor(rawMat, colorMat, ColorConversionCodes.GRAY2BGR);
        return colorMat;
    }
}

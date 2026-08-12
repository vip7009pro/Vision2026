using System;
using System.Collections.Generic;
using VisionInspectionApp.UI.Services.Camera.Drivers;

namespace VisionInspectionApp.UI.Services.Camera;

public static class CameraDriverFactory
{
    public static ICameraDriver CreateDriver(CameraVendor vendor)
    {
        return vendor switch
        {
            CameraVendor.Hikrobot => new HikCameraDriver(),
            CameraVendor.Basler => new BaslerCameraDriver(),
            CameraVendor.Cognex => new CognexCameraDriver(),
            CameraVendor.WebcamDirectShow or CameraVendor.Rtsp => new OpenCvCameraDriver(),
            CameraVendor.Simulator => new SimulatorCameraDriver(),
            _ => new SimulatorCameraDriver()
        };
    }

    public static List<CameraDeviceInfo> ScanAllDevices()
    {
        var allDevices = new List<CameraDeviceInfo>();

        // 1. Quét Camera Giả Lập Simulator
        allDevices.Add(new CameraDeviceInfo
        {
            Vendor = CameraVendor.Simulator,
            InterfaceType = CameraInterfaceType.Virtual,
            Index = CameraService.SimulatorCameraIndex,
            ModelName = "🎮 Camera Giả Lập Công Nghiệp (Simulator)"
        });

        // 2. Quét Camera Hikrobot (GigE & USB3) - Chỉ quét nếu IsMvSdkAvailable() = true (kiểm tra file đĩa MvCameraControl.dll)
        if (HikCameraDriver.IsMvSdkAvailable())
        {
            try
            {
                var hikDevices = HikCameraDriver.ScanDevices();
                allDevices.AddRange(hikDevices);
            }
            catch { }
        }

        // 3. Quét Camera Basler (Placeholder)
        try
        {
            var baslerDevices = BaslerCameraDriver.ScanDevices();
            allDevices.AddRange(baslerDevices);
        }
        catch { }

        // 4. Quét Camera Cognex (Placeholder)
        try
        {
            var cognexDevices = CognexCameraDriver.ScanDevices();
            allDevices.AddRange(cognexDevices);
        }
        catch { }

        // 5. Quét USB Webcams & RTSP Streams (OpenCV DirectShow)
        try
        {
            var ocvDevices = OpenCvCameraDriver.ScanDevices();
            allDevices.AddRange(ocvDevices);
        }
        catch { }

        return allDevices;
    }
}

using System.Collections.Generic;
using DirectShowLib;

namespace VisionInspectionApp.UI.Services;

public static class DirectShowDeviceEnumerator
{
    public static List<string> GetDevices()
    {
        var devices = new List<string>();
        try
        {
            // Sử dụng DirectShowLib để quét tất cả thiết bị thuộc danh mục Video Input
            DsDevice[] dsDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            foreach (var dev in dsDevices)
            {
                if (dev != null && !string.IsNullOrEmpty(dev.Name))
                {
                    devices.Add(dev.Name);
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi và trả về danh sách trống để cơ chế fallback hoạt động
        }
        return devices;
    }
}

using System;
using DirectShowLib;
using OpenCvSharp;

namespace TestExtractApp;

public static class CameraTest
{
    public static void RunCameraTest()
    {
        Console.WriteLine("=== CAMERA PROBE TEST WITH DIRECTSHOW ENUMERATION ===");
        
        try
        {
            DsDevice[] dsDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            Console.WriteLine($"DirectShow Devices Count: {dsDevices.Length}");
            for (int i = 0; i < dsDevices.Length; i++)
            {
                Console.WriteLine($"  Device [{i}]: {dsDevices[i].Name} (Path: {dsDevices[i].DevicePath})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DirectShow enumeration exception: {ex.Message}");
        }

        for (int i = 0; i < 6; i++)
        {
            Console.WriteLine($"\n--- Testing Camera Index {i} ---");
            
            // Try DSHOW
            try
            {
                using var capDshow = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                Console.WriteLine($"  DSHOW IsOpened: {capDshow.IsOpened()}");
                if (capDshow.IsOpened())
                {
                    using var frame = new Mat();
                    for (int k = 0; k < 10; k++)
                    {
                        if (capDshow.Read(frame) && !frame.Empty())
                        {
                            Console.WriteLine($"  DSHOW Read OK: {frame.Width}x{frame.Height}");
                            break;
                        }
                        System.Threading.Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  DSHOW Exception: {ex.Message}");
            }

            // Try MSMF
            try
            {
                using var capMsmf = new VideoCapture(i, VideoCaptureAPIs.MSMF);
                Console.WriteLine($"  MSMF IsOpened: {capMsmf.IsOpened()}");
                if (capMsmf.IsOpened())
                {
                    using var frame = new Mat();
                    for (int k = 0; k < 10; k++)
                    {
                        if (capMsmf.Read(frame) && !frame.Empty())
                        {
                            Console.WriteLine($"  MSMF Read OK: {frame.Width}x{frame.Height}");
                            break;
                        }
                        System.Threading.Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  MSMF Exception: {ex.Message}");
            }

            // Try ANY
            try
            {
                using var capAny = new VideoCapture(i, VideoCaptureAPIs.ANY);
                Console.WriteLine($"  ANY IsOpened: {capAny.IsOpened()}");
                if (capAny.IsOpened())
                {
                    using var frame = new Mat();
                    for (int k = 0; k < 10; k++)
                    {
                        if (capAny.Read(frame) && !frame.Empty())
                        {
                            Console.WriteLine($"  ANY Read OK: {frame.Width}x{frame.Height}");
                            break;
                        }
                        System.Threading.Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ANY Exception: {ex.Message}");
            }
        }
    }
}

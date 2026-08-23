using System;
using System.Reflection;
using MvCamCtrl.NET;

namespace TestExtractApp;

public static class HikApiTest
{
    public static void PrintApi()
    {
        Console.WriteLine("=== HIKROBOT DELEGATES SIGNATURES ===");
        var tEx = typeof(MyCamera.cbExceptiondelegate);
        var mEx = tEx.GetMethod("Invoke");
        Console.WriteLine($"cbExceptiondelegate: {mEx.ReturnType.Name} ({string.Join(", ", Array.ConvertAll(mEx.GetParameters(), p => p.ParameterType.Name + " " + p.Name))})");

        var tOut = typeof(MyCamera.cbOutputExdelegate);
        var mOut = tOut.GetMethod("Invoke");
        Console.WriteLine($"cbOutputExdelegate: {mOut.ReturnType.Name} ({string.Join(", ", Array.ConvertAll(mOut.GetParameters(), p => p.ParameterType.Name + " " + p.Name))})");

        Console.WriteLine("\n--- MvGvspPixelType Enum Values ---");
        var pixelType = typeof(MyCamera.MvGvspPixelType);
        foreach (var name in Enum.GetNames(pixelType))
        {
            var val = (uint)(MyCamera.MvGvspPixelType)Enum.Parse(pixelType, name);
            Console.WriteLine($"PIXELTYPE: {name} = 0x{val:X8}");
        }
    }
}

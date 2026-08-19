using System;
using System.Reflection;
using MvCamCtrl.NET;

namespace TestExtractApp;

public static class HikApiTest
{
    public static void PrintApi()
    {
        Console.WriteLine("=== HIKROBOT MYCAMERA API INSPECTION ===");
        var t = typeof(MyCamera);
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (m.Name.Contains("Convert", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("Pixel", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("Bayer", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("Balance", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("Display", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"METHOD: {m.ReturnType.Name} {m.Name}({string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name + " " + p.Name))})");
            }
        }

        Console.WriteLine("\n--- Nested Types / Structs in MyCamera ---");
        foreach (var nt in t.GetNestedTypes())
        {
            if (nt.Name.Contains("CONVERT", StringComparison.OrdinalIgnoreCase) ||
                nt.Name.Contains("PIXEL", StringComparison.OrdinalIgnoreCase) ||
                nt.Name.Contains("FRAME", StringComparison.OrdinalIgnoreCase) ||
                nt.Name.Contains("IMAGE", StringComparison.OrdinalIgnoreCase) ||
                nt.Name.Contains("BAYER", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"STRUCT/TYPE: {nt.Name}");
                foreach (var f in nt.GetFields())
                {
                    Console.WriteLine($"    Field: {f.FieldType.Name} {f.Name}");
                }
            }
        }
    }
}

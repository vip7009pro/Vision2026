using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class PlcBridgeTest
{
    public static async Task RunTestsAsync()
    {
        Console.WriteLine("====================================================");
        Console.WriteLine("STARTING PLC BRIDGE & MX COMPONENT DRIVER TESTS");
        Console.WriteLine("====================================================");

        var plc0 = new PlcModel
        {
            Id = "P0",
            Name = "PLC_St0",
            DriverType = PlcDriverType.MitsubishiMxComponent,
            LogicalStationNumber = 0,
            Enabled = true
        };

        using var driver0 = new MitsubishiMxComponentDriver(plc0);

        Console.WriteLine($"[INFO] Connecting to PLC Station {plc0.LogicalStationNumber} via 32-bit Socket Bridge...");
        using var cts = new CancellationTokenSource(8000);
        bool connected0 = await driver0.ConnectAsync(cts.Token);

        if (connected0)
        {
            Console.WriteLine($"[PASS] Driver ConnectAsync Station 0 succeeded! State={plc0.State}, CpuName={plc0.CpuName}");

            var tagD0 = new PlcTag { PlcId = plc0.Id, Name = "D0_Test", Address = "D0", DataType = PlcDataType.Int16 };
            await driver0.WriteAsync(tagD0, (short)7788, cts.Token);
            var d0Val = await driver0.ReadAsync(tagD0, cts.Token);
            Console.WriteLine($"[TEST] Read Tag D0 after write 7788: Value={d0Val}");

            var tagD10 = new PlcTag { PlcId = plc0.Id, Name = "D10_Float", Address = "D10", DataType = PlcDataType.Float };
            await driver0.WriteAsync(tagD10, 123.456f, cts.Token);
            var d10Val = await driver0.ReadAsync(tagD10, cts.Token);
            Console.WriteLine($"[TEST] Read Tag D10 (Float): Value={d10Val}");

            await driver0.DisconnectAsync();
            Console.WriteLine($"[PASS] Driver DisconnectAsync completed. State={plc0.State}");
        }
        else
        {
            Console.WriteLine($"[INFO] Driver ConnectAsync Station 0 returned false: State={plc0.State}, Msg={plc0.CpuName}");
        }

        Console.WriteLine("====================================================");
    }
}

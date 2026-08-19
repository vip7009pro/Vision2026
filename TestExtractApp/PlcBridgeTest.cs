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

        var plc = new PlcModel
        {
            Id = "P1",
            Name = "PLC1",
            DriverType = PlcDriverType.MitsubishiMxComponent,
            LogicalStationNumber = 1,
            Enabled = true
        };

        using var driver = new MitsubishiMxComponentDriver(plc);

        Console.WriteLine($"[INFO] Connecting to PLC Station {plc.LogicalStationNumber} via 32-bit Socket Bridge...");
        using var cts = new CancellationTokenSource(8000);
        bool connected = await driver.ConnectAsync(cts.Token);

        if (connected)
        {
            Console.WriteLine($"[PASS] Driver ConnectAsync succeeded! State={plc.State}, CpuName={plc.CpuName}");
        }
        else
        {
            Console.WriteLine($"[FAIL] Driver ConnectAsync failed! State={plc.State}, Error={plc.CpuName}");
        }

        if (connected)
        {
            var tagX0 = new PlcTag { PlcId = plc.Id, Name = "Ready", Address = "X0", DataType = PlcDataType.Bool };
            var x0Val = await driver.ReadAsync(tagX0, cts.Token);
            Console.WriteLine($"[TEST] Read Tag X0: Value={x0Val}");

            var tagD200 = new PlcTag { PlcId = plc.Id, Name = "D200_PosX", Address = "D200", DataType = PlcDataType.Float };
            var d200Val = await driver.ReadAsync(tagD200, cts.Token);
            Console.WriteLine($"[TEST] Read Tag D200 (Float): Value={d200Val}");

            await driver.DisconnectAsync();
            Console.WriteLine($"[PASS] Driver DisconnectAsync completed. State={plc.State}");
        }

        Console.WriteLine("====================================================");
    }
}

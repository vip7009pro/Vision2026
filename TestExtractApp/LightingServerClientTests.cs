using System;
using System.Threading.Tasks;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class LightingServerClientTests
{
    private static int _passed;
    private static int _failed;

    public static void RunTests()
    {
        _passed = 0;
        _failed = 0;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════════");
        Console.WriteLine("   🌐 LIGHTING CONTROL SERVER & CLIENT — UNIT TESTS");
        Console.WriteLine("══════════════════════════════════════════════════════════\n");
        Console.ResetColor();

        RunAsyncTests().GetAwaiter().GetResult();

        Console.ForegroundColor = _failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"\n  RESULTS: {_passed} passed, {_failed} failed (Total: {_passed + _failed})\n");
        Console.ResetColor();

        if (_failed > 0)
        {
            throw new Exception($"LightingServerClientTests failed with {_failed} failures.");
        }
    }

    private static async Task RunAsyncTests()
    {
        await Test_LocalIPv4DetectionAsync();
        await Test_ServerStartupAndShutdownAsync();
        await Test_ClientConnectAndDisconnectAsync();
        await Test_ClientCommandsExecutionAsync();
        await Test_DirectServerOperationsAndSyncAsync();
        await Test_ServerReadHardwareStateAsync();
        await Test_ConcurrentClientsAsync();
    }

    private static Task Test_LocalIPv4DetectionAsync()
    {
        var ips = LightingControlServer.GetLocalIPv4Addresses();
        Assert(ips != null && ips.Count > 0, "GetLocalIPv4Addresses returns non-empty list");
        Assert(ips!.Contains("127.0.0.1") || ips.Count > 0, "Local IPv4 address resolved");
        return Task.CompletedTask;
    }

    private static async Task Test_ServerStartupAndShutdownAsync()
    {
        using var server = new LightingControlServer();
        int testPort = 15052;

        await server.StartServerAsync(testPort);
        Assert(server.IsRunning, "Server IsRunning after StartServerAsync");
        Assert(server.ListeningPort == testPort, "Server ListeningPort is configured port");

        await server.StopServerAsync();
        Assert(!server.IsRunning, "Server IsRunning is false after StopServerAsync");
    }

    private static async Task Test_ClientConnectAndDisconnectAsync()
    {
        using var server = new LightingControlServer();
        int testPort = 15053;
        await server.StartServerAsync(testPort);

        using var client = new LightingControlClientService();
        await client.ConnectAsync("127.0.0.1", testPort);
        Assert(client.IsConnected, "Client IsConnected is true after ConnectAsync");

        // Wait a small moment for server to register connection
        await Task.Delay(50);
        Assert(server.ConnectedClients.Count == 1, "Server ConnectedClients count is 1");

        await client.DisconnectAsync();
        Assert(!client.IsConnected, "Client IsConnected is false after DisconnectAsync");

        await Task.Delay(50);
        Assert(server.ConnectedClients.Count == 0, "Server ConnectedClients count is 0 after client disconnect");

        await server.StopServerAsync();
    }

    private static async Task Test_ClientCommandsExecutionAsync()
    {
        using var server = new LightingControlServer();
        int testPort = 15054;
        await server.StartServerAsync(testPort);

        using var client = new LightingControlClientService();
        await client.ConnectAsync("127.0.0.1", testPort);

        // 1. Set channel 0 power ON
        var pwrRes = await client.SetChannelPowerAsync(0, true);
        Assert(pwrRes.IsSuccess, "SetChannelPowerAsync(0, true) returns success");
        Assert(server.CurrentState.Channels[0].IsEnabled, "Server cached state Channel 0 is Enabled");

        // 2. Set channel 0 brightness to 180
        var brRes = await client.SetBrightnessAsync(0, 180);
        Assert(brRes.IsSuccess, "SetBrightnessAsync(0, 180) returns success");
        Assert(server.CurrentState.Channels[0].Brightness == 180, "Server cached state Channel 0 Brightness is 180");

        // 3. Set channel 0 lighting time to 75ms
        var timeRes = await client.SetLightingTimeAsync(0, 75);
        Assert(timeRes.IsSuccess, "SetLightingTimeAsync(0, 75) returns success");
        Assert(server.CurrentState.Channels[0].LightingTimeMs == 75, "Server cached state Channel 0 LightingTimeMs is 75");

        // 4. Read all parameters from client
        var readRes = await client.ReadAllAsync(4);
        Assert(readRes.IsSuccess, "ReadAllAsync(4) returns success");
        Assert(readRes.Data != null, "ReadAllAsync returns parsed Data");
        Assert(readRes.Data!.Channels[0].Brightness == 180, "ReadAllAsync Channel 0 Brightness is 180");
        Assert(readRes.Data!.Channels[0].IsEnabled, "ReadAllAsync Channel 0 IsEnabled is true");

        await client.DisconnectAsync();
        await server.StopServerAsync();
    }

    private static async Task Test_DirectServerOperationsAndSyncAsync()
    {
        using var server = new LightingControlServer();
        int testPort = 15055;
        await server.StartServerAsync(testPort);

        // Thao tác trực tiếp trên Server (mô phỏng thao tác tại máy OQC)
        await server.SetChannelPowerDirectAsync(2, true);
        await server.SetBrightnessDirectAsync(2, 225);
        await server.SetLightingTimeDirectAsync(2, 120);

        Assert(server.CurrentState.Channels[2].IsEnabled, "Server direct power is true");
        Assert(server.CurrentState.Channels[2].Brightness == 225, "Server direct brightness is 225");

        // Máy khách ở văn phòng kết nối vào và đọc trạng thái
        using var client = new LightingControlClientService();
        await client.ConnectAsync("127.0.0.1", testPort);

        var readRes = await client.ReadAllAsync(4);
        Assert(readRes.IsSuccess, "Client ReadAllAsync returns success");
        Assert(client.State.Channels[2].IsEnabled, "Client synced Channel 2 IsEnabled is true");
        Assert(client.State.Channels[2].Brightness == 225, "Client synced Channel 2 Brightness is 225");

        // Tắt toàn bộ kênh từ Server
        await server.TurnOffAllChannelsDirectAsync(4);
        Assert(!server.CurrentState.Channels[2].IsEnabled, "Server Channel 2 power is false after TurnOffAll");

        await client.DisconnectAsync();
        await server.StopServerAsync();
    }

    private static async Task Test_ConcurrentClientsAsync()
    {
        using var server = new LightingControlServer();
        int testPort = 15056;
        await server.StartServerAsync(testPort);

        using var client1 = new LightingControlClientService();
        using var client2 = new LightingControlClientService();
        using var client3 = new LightingControlClientService();

        await client1.ConnectAsync("127.0.0.1", testPort);
        await client2.ConnectAsync("127.0.0.1", testPort);
        await client3.ConnectAsync("127.0.0.1", testPort);

        await Task.Delay(50);
        Assert(server.ConnectedClients.Count == 3, "Server has 3 concurrent connected clients");

        // Gửi lệnh đồng thời từ cả 3 client
        var t1 = client1.SetBrightnessAsync(0, 111);
        var t2 = client2.SetBrightnessAsync(1, 222);
        var t3 = client3.SetBrightnessAsync(2, 250);

        await Task.WhenAll(t1, t2, t3);

        Assert(t1.Result.IsSuccess, "Concurrent Client 1 command succeeded");
        Assert(t2.Result.IsSuccess, "Concurrent Client 2 command succeeded");
        Assert(t3.Result.IsSuccess, "Concurrent Client 3 command succeeded");

        Assert(server.CurrentState.Channels[0].Brightness == 111, "Channel 0 brightness is 111");
        Assert(server.CurrentState.Channels[1].Brightness == 222, "Channel 1 brightness is 222");
        Assert(server.CurrentState.Channels[2].Brightness == 250, "Channel 2 brightness is 250");

        await client1.DisconnectAsync();
        await client2.DisconnectAsync();
        await client3.DisconnectAsync();
        await server.StopServerAsync();
    }

    private static async Task Test_ServerReadHardwareStateAsync()
    {
        using var server = new LightingControlServer();
        // Kiểm tra ReadStateFromHardwareAsync an toàn khi chưa cắm COM
        var res = await server.ReadStateFromHardwareAsync();
        Assert(res.IsSuccess, "ReadStateFromHardwareAsync returns success when not connected");

        // Khởi động server với port test
        int testPort = 15056;
        await server.StartServerAsync(testPort);
        Assert(server.IsRunning, "Server IsRunning after StartServerAsync with auto-read");

        // Kiểm tra CurrentState đã được khởi tạo
        Assert(server.CurrentState != null, "Server CurrentState is not null");
        Assert(server.CurrentState.Channels.Length >= 4, "Server CurrentState has at least 4 channels");

        await server.StopServerAsync();
    }

    private static void Assert(bool condition, string message)
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅ PASS: {message}");
            _passed++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {message}");
            _failed++;
        }
        Console.ResetColor();
    }
}

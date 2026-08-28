using System;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class LightingControllerTests
{
    private static int _passed;
    private static int _failed;

    public static void RunAllTests()
    {
        _passed = 0;
        _failed = 0;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════════");
        Console.WriteLine("   💡 LIGHTING CONTROLLER 8-CHANNEL ASCII — UNIT TESTS");
        Console.WriteLine("══════════════════════════════════════════════════════════\n");
        Console.ResetColor();

        // Command Builder Tests
        TestBuildSetChannelPower();
        TestBuildSetBrightness();
        TestBuildSetLightingTime();
        TestBuildSetTriggerMode();
        TestBuildReadAll();
        TestBuildSave();
        TestBuildFactoryReset();
        TestBuildSetLock();
        TestBuildMultiCommand();
        TestBuildMultiCommandRdLast();
        TestBuildChannelConfig();
        TestBuildNetworkConfig();

        // Response Parser Tests
        TestParseOk();
        TestParseE1();
        TestParseE2();
        TestParseE3();
        TestParseE4();
        TestParseE5();
        TestParseE6();
        TestParseE7();
        TestParseER();
        TestParseDataResponse();
        TestParseFullRd9999Response();

        // Validation Tests
        TestValidationBrightnessNegative();
        TestValidationBrightness256();
        TestValidationTime0();
        TestValidationTime1000();
        TestValidationChannelNegative();
        TestValidationChannel8();
        TestValidationTriggerInvalid();

        // Serial Transport & Interface Type Tests
        TestSerialTransportAndInterfaceType();

        // Echo & Newline Tolerance Tests
        TestEchoAndNewlineResponses();

        // Summary
        Console.ForegroundColor = _failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"\n  RESULTS: {_passed} passed, {_failed} failed (Total: {_passed + _failed})");
        Console.ResetColor();

        if (_failed > 0)
            throw new Exception($"Lighting Controller tests: {_failed} FAILED");
    }

    // =====================================================================
    // Command Builder Tests
    // =====================================================================

    private static void TestBuildSetChannelPower()
    {
        Assert("BuildSetChannelPower CH1 ON", LightingProtocol.BuildSetChannelPower(0, true), "$F0=1#");
        Assert("BuildSetChannelPower CH1 OFF", LightingProtocol.BuildSetChannelPower(0, false), "$F0=0#");
        Assert("BuildSetChannelPower CH8 ON", LightingProtocol.BuildSetChannelPower(7, true), "$F7=1#");
    }

    private static void TestBuildSetBrightness()
    {
        Assert("BuildSetBrightness CH1=200", LightingProtocol.BuildSetBrightness(0, 200), "$L0=200#");
        Assert("BuildSetBrightness CH1=0", LightingProtocol.BuildSetBrightness(0, 0), "$L0=0#");
        Assert("BuildSetBrightness CH1=255", LightingProtocol.BuildSetBrightness(0, 255), "$L0=255#");
        Assert("BuildSetBrightness CH2=180", LightingProtocol.BuildSetBrightness(1, 180), "$L1=180#");
    }

    private static void TestBuildSetLightingTime()
    {
        Assert("BuildSetLightingTime CH1=50", LightingProtocol.BuildSetLightingTime(0, 50), "$T0=50#");
        Assert("BuildSetLightingTime CH1=1", LightingProtocol.BuildSetLightingTime(0, 1), "$T0=1#");
        Assert("BuildSetLightingTime CH1=999", LightingProtocol.BuildSetLightingTime(0, 999), "$T0=999#");
    }

    private static void TestBuildSetTriggerMode()
    {
        Assert("BuildSetTriggerMode ExternalLow", LightingProtocol.BuildSetTriggerMode(LightingTriggerMode.ExternalLow), "$TR=0#");
        Assert("BuildSetTriggerMode ExternalHigh", LightingProtocol.BuildSetTriggerMode(LightingTriggerMode.ExternalHigh), "$TR=1#");
        Assert("BuildSetTriggerMode FallingEdge", LightingProtocol.BuildSetTriggerMode(LightingTriggerMode.FallingEdge), "$TR=2#");
        Assert("BuildSetTriggerMode RisingEdge", LightingProtocol.BuildSetTriggerMode(LightingTriggerMode.RisingEdge), "$TR=3#");
    }

    private static void TestBuildReadAll()
    {
        Assert("BuildReadAll", LightingProtocol.BuildReadAll(), "$RD=9999#");
    }

    private static void TestBuildSave()
    {
        Assert("BuildSave", LightingProtocol.BuildSave(), "$SA=1#");
    }

    private static void TestBuildFactoryReset()
    {
        Assert("BuildFactoryReset", LightingProtocol.BuildFactoryReset(), "$RS=1#");
    }

    private static void TestBuildSetLock()
    {
        Assert("BuildSetLock lock", LightingProtocol.BuildSetLock(true), "$LC=1#");
        Assert("BuildSetLock unlock", LightingProtocol.BuildSetLock(false), "$LC=0#");
    }

    private static void TestBuildMultiCommand()
    {
        var result = LightingProtocol.BuildMultiCommand(
            ("F0", "1"), ("L0", "200"), ("T0", "50"), ("TR", "3"));
        Assert("BuildMultiCommand batch", result, "$F0=1,L0=200,T0=50,TR=3#");
    }

    private static void TestBuildMultiCommandRdLast()
    {
        // RD must always be last, even if specified first
        var result = LightingProtocol.BuildMultiCommand(
            ("RD", "0"), ("L0", "10"), ("TR", "1"));
        Assert("BuildMultiCommand RD last", result, "$L0=10,TR=1,RD=0#");
    }

    private static void TestBuildChannelConfig()
    {
        var result = LightingProtocol.BuildChannelConfig(0, on: true, brightness: 200, timeMs: 50);
        Assert("BuildChannelConfig full", result, "$F0=1,L0=200,T0=50#");
    }

    private static void TestBuildNetworkConfig()
    {
        var result = LightingProtocol.BuildNetworkConfig(
            LightingNetworkMode.TcpServer, "192.168.1.2", "255.255.255.0", "192.168.1.1", 1200);
        Assert("BuildNetworkConfig TCP Server", result, "$NE=0,IP=192.168.1.2,IU=255.255.255.0,IS=192.168.1.1,IL=1200#");
    }

    // =====================================================================
    // Response Parser Tests
    // =====================================================================

    private static void TestParseOk()
    {
        var result = LightingProtocol.ParseResponse("+OK");
        Assert("ParseResponse +OK success", result.IsSuccess, true);
        Assert("ParseResponse +OK no error", result.ErrorCode, null);
    }

    private static void TestParseE1() => TestErrorCode("E1", "Command format error");
    private static void TestParseE2() => TestErrorCode("E2", "Data format error");
    private static void TestParseE3() => TestErrorCode("E3", "Invalid command name");
    private static void TestParseE4() => TestErrorCode("E4", "Invalid channel name");
    private static void TestParseE5() => TestErrorCode("E5", "Command name length error");
    private static void TestParseE6() => TestErrorCode("E6", "Data length error");
    private static void TestParseE7() => TestErrorCode("E7", "Channel name length error");
    private static void TestParseER() => TestErrorCode("ER", "Other command error");

    private static void TestErrorCode(string code, string expectedMessage)
    {
        var result = LightingProtocol.ParseResponse(code);
        Assert($"ParseResponse {code} not success", result.IsSuccess, false);
        Assert($"ParseResponse {code} error code", result.ErrorCode, code);
        Assert($"ParseResponse {code} error message", result.ErrorMessage, expectedMessage);
    }

    private static void TestParseDataResponse()
    {
        var raw = "$L0=200,T0=50,F0=1,TR=3#";
        var result = LightingProtocol.ParseResponse(raw);
        Assert("ParseDataResponse success", result.IsSuccess, true);
        Assert("ParseDataResponse has data", result.Data != null, true);
        Assert("ParseDataResponse L0=200", result.Data!.Channels[0].Brightness, 200);
        Assert("ParseDataResponse T0=50", result.Data.Channels[0].LightingTimeMs, 50);
        Assert("ParseDataResponse F0=1", result.Data.Channels[0].IsEnabled, true);
        Assert("ParseDataResponse TR=3", result.Data.TriggerMode, LightingTriggerMode.RisingEdge);
    }

    private static void TestParseFullRd9999Response()
    {
        var raw = "$ID=0,L0=100,T0=100,F0=1,L1=100,T1=100,F1=1,L2=100,T2=100,F2=1,L3=100,T3=100,F3=1,L4=100,T4=100,F4=1,L5=100,T5=100,F5=1,L6=100,T6=100,F6=100,L7=100,T7=100,F7=1,TR=0,FQ=0,FI=0,LC=0,PW=0,NE=2,IP=192.168.1.2,IU=255.255.255.0,IS=192.168.1.1,IL=1200,DP=192.168.1.3,DL=1200,MC=89438940C3520030#";
        var result = LightingProtocol.ParseResponse(raw);
        Assert("RD9999 success", result.IsSuccess, true);
        Assert("RD9999 has data", result.Data != null, true);

        var state = result.Data!;
        Assert("RD9999 CH1 brightness", state.Channels[0].Brightness, 100);
        Assert("RD9999 CH1 enabled", state.Channels[0].IsEnabled, true);
        Assert("RD9999 CH8 enabled", state.Channels[7].IsEnabled, true);
        Assert("RD9999 TR=0", state.TriggerMode, LightingTriggerMode.ExternalLow);
        Assert("RD9999 NE=2", state.NetworkMode, LightingNetworkMode.UdpBroadcast);
        Assert("RD9999 IP", state.IpAddress, "192.168.1.2");
        Assert("RD9999 SubnetMask", state.SubnetMask, "255.255.255.0");
        Assert("RD9999 Gateway", state.Gateway, "192.168.1.1");
        Assert("RD9999 LocalPort", state.LocalPort, 1200);
        Assert("RD9999 DestIp", state.DestinationIp, "192.168.1.3");
        Assert("RD9999 DestPort", state.DestinationPort, 1200);
        Assert("RD9999 MC", state.MC, "89438940C3520030");
        Assert("RD9999 ID", state.Id, 0);

        // F6=100 should be treated as enabled (non-zero)
        Assert("RD9999 CH7 F6=100 enabled", state.Channels[6].IsEnabled, true);
    }

    // =====================================================================
    // Validation Tests
    // =====================================================================

    private static void TestValidationBrightnessNegative()
    {
        AssertThrows<ArgumentOutOfRangeException>("Brightness -1",
            () => LightingProtocol.BuildSetBrightness(0, -1));
    }

    private static void TestValidationBrightness256()
    {
        AssertThrows<ArgumentOutOfRangeException>("Brightness 256",
            () => LightingProtocol.BuildSetBrightness(0, 256));
    }

    private static void TestValidationTime0()
    {
        AssertThrows<ArgumentOutOfRangeException>("Time 0",
            () => LightingProtocol.BuildSetLightingTime(0, 0));
    }

    private static void TestValidationTime1000()
    {
        AssertThrows<ArgumentOutOfRangeException>("Time 1000",
            () => LightingProtocol.BuildSetLightingTime(0, 1000));
    }

    private static void TestValidationChannelNegative()
    {
        AssertThrows<ArgumentOutOfRangeException>("Channel -1",
            () => LightingProtocol.BuildSetChannelPower(-1, true));
    }

    private static void TestValidationChannel8()
    {
        AssertThrows<ArgumentOutOfRangeException>("Channel 8",
            () => LightingProtocol.BuildSetChannelPower(8, true));
    }

    private static void TestValidationTriggerInvalid()
    {
        AssertThrows<ArgumentOutOfRangeException>("Trigger 99",
            () => LightingProtocol.BuildSetTriggerMode((LightingTriggerMode)99));
    }

    private static void TestSerialTransportAndInterfaceType()
    {
        Assert("InterfaceType Ethernet enum value", (int)LightingInterfaceType.Ethernet, 0);
        Assert("InterfaceType SerialCom enum value", (int)LightingInterfaceType.SerialCom, 1);

        using var serial = new SerialLightingTransport(readTimeoutMs: 1000, writeTimeoutMs: 1000);
        Assert("Serial transport initially disconnected", serial.IsConnected, false);
    }

    private static void TestEchoAndNewlineResponses()
    {
        // +OK with trailing CRLF
        var res1 = LightingProtocol.ParseResponse("+OK\r\n");
        Assert("Parse +OK CRLF", res1.IsSuccess, true);

        // +OK preceded by command echo
        var res2 = LightingProtocol.ParseResponse("$F0=1#\r\n+OK\r\n");
        Assert("Parse echoed +OK", res2.IsSuccess, true);

        // Echoed $RD=9999# with actual data
        var res3 = LightingProtocol.ParseResponse("$RD=9999#$ID=0,L0=255,F0=1#");
        Assert("Parse echoed RD data", res3.IsSuccess, true);
        Assert("Parse echoed RD brightness", res3.Data?.Channels[0].Brightness, 255);

        // Echoed error
        var res4 = LightingProtocol.ParseResponse("$F0=99#\r\nE2\r\n");
        Assert("Parse echoed E2", res4.ErrorCode, "E2");
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static void Assert<T>(string testName, T actual, T expected)
    {
        bool pass;
        if (expected == null)
            pass = actual == null;
        else
            pass = expected.Equals(actual);

        if (pass)
        {
            _passed++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅ PASS: {testName}");
        }
        else
        {
            _failed++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {testName} — Expected: [{expected}], Got: [{actual}]");
        }
        Console.ResetColor();
    }

    private static void AssertThrows<TException>(string testName, Action action) where TException : Exception
    {
        try
        {
            action();
            _failed++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {testName} — Expected {typeof(TException).Name} but no exception was thrown");
            Console.ResetColor();
        }
        catch (TException)
        {
            _passed++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅ PASS: {testName} (threw {typeof(TException).Name})");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            _failed++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {testName} — Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
        }
    }
}

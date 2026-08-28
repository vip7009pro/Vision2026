namespace VisionInspectionApp.Models;

// =====================================================================
// Lighting Controller 8-Channel ASCII Protocol — Data Models
// =====================================================================

/// <summary>Connection state of the Lighting Controller.</summary>
public enum LightingConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Error = 3
}

/// <summary>Physical communication interface type.</summary>
public enum LightingInterfaceType
{
    /// <summary>Ethernet (TCP/UDP)</summary>
    Ethernet = 0,

    /// <summary>Serial RS-232 / COM Port</summary>
    SerialCom = 1
}

/// <summary>Trigger mode of the Lighting Controller.</summary>
public enum LightingTriggerMode
{
    /// <summary>External trigger, active low (TR=0)</summary>
    ExternalLow = 0,

    /// <summary>External trigger, active high (TR=1)</summary>
    ExternalHigh = 1,

    /// <summary>External trigger, falling edge (TR=2)</summary>
    FallingEdge = 2,

    /// <summary>External trigger, rising edge (TR=3)</summary>
    RisingEdge = 3
}

/// <summary>Network mode of the Lighting Controller.</summary>
public enum LightingNetworkMode
{
    /// <summary>TCP Server (NE=0)</summary>
    TcpServer = 0,

    /// <summary>TCP Client (NE=1)</summary>
    TcpClient = 1,

    /// <summary>UDP Broadcast (NE=2)</summary>
    UdpBroadcast = 2
}

/// <summary>State of a single lighting channel (0-7).</summary>
public sealed class LightingChannelState
{
    /// <summary>Channel index 0-7.</summary>
    public int ChannelIndex { get; set; }

    /// <summary>Whether the channel is ON (F0-F7).</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Brightness 0-255 (L0-L7).</summary>
    public int Brightness { get; set; } = 100;

    /// <summary>Lighting time 1-999 ms (T0-T7).</summary>
    public int LightingTimeMs { get; set; } = 100;
}

/// <summary>Full state of the Lighting Controller (parsed from RD=9999 response).</summary>
public sealed class LightingControllerState
{
    public const int MaxChannels = 8;

    public LightingChannelState[] Channels { get; set; } = CreateDefaultChannels();

    public LightingTriggerMode TriggerMode { get; set; } = LightingTriggerMode.ExternalLow;

    public LightingNetworkMode NetworkMode { get; set; } = LightingNetworkMode.TcpServer;

    public string IpAddress { get; set; } = "192.168.1.2";

    public string SubnetMask { get; set; } = "255.255.255.0";

    public string Gateway { get; set; } = "192.168.1.1";

    public int LocalPort { get; set; } = 1200;

    public string DestinationIp { get; set; } = "192.168.1.3";

    public int DestinationPort { get; set; } = 1200;

    // Extra fields that may appear in RD=9999 response
    public int Id { get; set; }
    public int FQ { get; set; }
    public int FI { get; set; }
    public int LC { get; set; }
    public int PW { get; set; }
    public string MC { get; set; } = string.Empty;

    private static LightingChannelState[] CreateDefaultChannels()
    {
        var channels = new LightingChannelState[MaxChannels];
        for (int i = 0; i < MaxChannels; i++)
        {
            channels[i] = new LightingChannelState { ChannelIndex = i };
        }
        return channels;
    }
}

/// <summary>Result of a command sent to the Lighting Controller.</summary>
public sealed class LightingCommandResult
{
    public bool IsSuccess { get; set; }

    /// <summary>Error code string if not success (E1, E2, ..., E7, ER).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Human-readable error description.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Raw response string from controller.</summary>
    public string RawResponse { get; set; } = string.Empty;

    /// <summary>Parsed data if the response contains parameter data.</summary>
    public LightingControllerState? Data { get; set; }

    /// <summary>Create a success result.</summary>
    public static LightingCommandResult Ok(string raw = "+OK") => new()
    {
        IsSuccess = true,
        RawResponse = raw
    };

    /// <summary>Create an error result from a controller error code.</summary>
    public static LightingCommandResult Error(string errorCode, string raw) => new()
    {
        IsSuccess = false,
        ErrorCode = errorCode,
        ErrorMessage = GetErrorDescription(errorCode),
        RawResponse = raw
    };

    /// <summary>Create a data result (from RD response).</summary>
    public static LightingCommandResult WithData(LightingControllerState data, string raw) => new()
    {
        IsSuccess = true,
        Data = data,
        RawResponse = raw
    };

    /// <summary>Map error codes to human-readable descriptions.</summary>
    public static string GetErrorDescription(string code) => code switch
    {
        "E1" => "Command format error",
        "E2" => "Data format error",
        "E3" => "Invalid command name",
        "E4" => "Invalid channel name",
        "E5" => "Command name length error",
        "E6" => "Data length error",
        "E7" => "Channel name length error",
        "ER" => "Other command error",
        _ => $"Unknown error: {code}"
    };
}

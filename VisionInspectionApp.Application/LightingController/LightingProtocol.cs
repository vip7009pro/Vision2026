using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.LightingController;

/// <summary>
/// ASCII protocol command builder and response parser for the 8-channel Lighting Controller.
/// Protocol format: $CMD=VALUE# with comma-separated multi-commands.
/// </summary>
public static class LightingProtocol
{
    // =====================================================================
    // Validation
    // =====================================================================

    public static void ValidateChannel(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex > 7)
            throw new ArgumentOutOfRangeException(nameof(channelIndex), channelIndex, "Channel index must be 0-7.");
    }

    public static void ValidateBrightness(int brightness)
    {
        if (brightness < 0 || brightness > 255)
            throw new ArgumentOutOfRangeException(nameof(brightness), brightness, "Brightness must be 0-255.");
    }

    public static void ValidateLightingTime(int timeMs)
    {
        if (timeMs < 1 || timeMs > 999)
            throw new ArgumentOutOfRangeException(nameof(timeMs), timeMs, "Lighting time must be 1-999 ms.");
    }

    public static void ValidateTriggerMode(LightingTriggerMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid trigger mode.");
    }

    public static void ValidateIpAddress(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out _))
            throw new ArgumentException($"Invalid IP address: '{ip}'", nameof(ip));
    }

    public static void ValidatePort(int port)
    {
        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be 1-65535.");
    }

    // =====================================================================
    // Command Builders — Single commands
    // =====================================================================

    /// <summary>Build ON/OFF command: $F{channel}={0|1}#</summary>
    public static string BuildSetChannelPower(int channelIndex, bool on)
    {
        ValidateChannel(channelIndex);
        return $"$F{channelIndex}={( on ? "1" : "0" )}#";
    }

    /// <summary>Build brightness command: $L{channel}={value}#</summary>
    public static string BuildSetBrightness(int channelIndex, int brightness)
    {
        ValidateChannel(channelIndex);
        ValidateBrightness(brightness);
        return $"$L{channelIndex}={brightness}#";
    }

    /// <summary>Build lighting time command: $T{channel}={value}#</summary>
    public static string BuildSetLightingTime(int channelIndex, int timeMs)
    {
        ValidateChannel(channelIndex);
        ValidateLightingTime(timeMs);
        return $"$T{channelIndex}={timeMs}#";
    }

    /// <summary>Build trigger mode command: $TR={value}#</summary>
    public static string BuildSetTriggerMode(LightingTriggerMode mode)
    {
        ValidateTriggerMode(mode);
        return $"$TR={(int)mode}#";
    }

    /// <summary>Build read channel command: $RD={channel}#</summary>
    public static string BuildReadChannel(int channelIndex)
    {
        ValidateChannel(channelIndex);
        return $"$RD={channelIndex}#";
    }

    /// <summary>Build read all command: $RD=9999#</summary>
    public static string BuildReadAll() => "$RD=9999#";

    /// <summary>Build save command: $SA=1#</summary>
    public static string BuildSave() => "$SA=1#";

    /// <summary>Build factory reset command: $RS=1#</summary>
    public static string BuildFactoryReset() => "$RS=1#";

    /// <summary>Build lock command: $LC={0|1}#</summary>
    public static string BuildSetLock(bool locked) => $"$LC={(locked ? "1" : "0")}#";

    /// <summary>Build network configuration command.</summary>
    public static string BuildNetworkConfig(
        LightingNetworkMode mode,
        string ip,
        string subnetMask,
        string gateway,
        int localPort,
        string? destinationIp = null,
        int? destinationPort = null)
    {
        ValidateIpAddress(ip);
        ValidateIpAddress(subnetMask);
        ValidateIpAddress(gateway);
        ValidatePort(localPort);

        var parts = new List<string>
        {
            $"NE={(int)mode}",
            $"IP={ip}",
            $"IU={subnetMask}",
            $"IS={gateway}",
            $"IL={localPort}"
        };

        if (mode == LightingNetworkMode.TcpClient)
        {
            if (string.IsNullOrWhiteSpace(destinationIp))
                throw new ArgumentException("Destination IP is required for TCP Client mode.");
            ValidateIpAddress(destinationIp);
            parts.Add($"DP={destinationIp}");

            if (destinationPort.HasValue)
            {
                ValidatePort(destinationPort.Value);
                parts.Add($"DL={destinationPort.Value}");
            }
        }

        return $"${string.Join(",", parts)}#";
    }

    // =====================================================================
    // Command Builder — Multi/Batch commands
    // =====================================================================

    /// <summary>
    /// Build a batch command from multiple key=value pairs.
    /// RD commands are automatically placed last per protocol requirement.
    /// </summary>
    public static string BuildMultiCommand(params (string Key, string Value)[] commands)
    {
        if (commands == null || commands.Length == 0)
            throw new ArgumentException("At least one command is required.");

        // Separate RD commands (must be last) from others
        var normal = new List<string>();
        var rdCommands = new List<string>();

        foreach (var (key, value) in commands)
        {
            var entry = $"{key}={value}";
            if (key.Equals("RD", StringComparison.OrdinalIgnoreCase))
                rdCommands.Add(entry);
            else
                normal.Add(entry);
        }

        // RD must be last
        normal.AddRange(rdCommands);

        return $"${string.Join(",", normal)}#";
    }

    /// <summary>
    /// Build batch command for a single channel's full configuration.
    /// </summary>
    public static string BuildChannelConfig(int channelIndex, bool? on = null, int? brightness = null, int? timeMs = null)
    {
        ValidateChannel(channelIndex);

        var parts = new List<(string Key, string Value)>();

        if (on.HasValue)
            parts.Add(($"F{channelIndex}", on.Value ? "1" : "0"));

        if (brightness.HasValue)
        {
            ValidateBrightness(brightness.Value);
            parts.Add(($"L{channelIndex}", brightness.Value.ToString()));
        }

        if (timeMs.HasValue)
        {
            ValidateLightingTime(timeMs.Value);
            parts.Add(($"T{channelIndex}", timeMs.Value.ToString()));
        }

        if (parts.Count == 0)
            throw new ArgumentException("At least one parameter must be specified.");

        return BuildMultiCommand(parts.ToArray());
    }

    // =====================================================================
    // Response Parser
    // =====================================================================

    /// <summary>
    /// Attempt to extract a complete response (+OK, E1-E7, ER, or $...#) from a raw buffer.
    /// Handles echoed commands, leading/trailing whitespace, and newlines.
    /// </summary>
    public static string? TryExtractResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();

        // 1. Check for +OK (exact or embedded in response/echo)
        if (trimmed.Contains("+OK", StringComparison.OrdinalIgnoreCase))
            return "+OK";

        // 2. Check for error code E1-E7 or ER (at start/end of line or preceded by #/space/newline)
        var errMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"(^|[\r\n\s\+\#])(E[1-7]|ER)($|[\r\n\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (errMatch.Success)
            return errMatch.Groups[2].Value.ToUpperInvariant();

        // 3. Check for data block $...# (extract the last complete one if multiple)
        var dataMatches = System.Text.RegularExpressions.Regex.Matches(trimmed, @"\$[^$#]+#");
        if (dataMatches.Count > 0)
        {
            // If multiple blocks (e.g. echo of $RD=9999# followed by $ID=0,...#), pick the one containing response data
            for (int i = dataMatches.Count - 1; i >= 0; i--)
            {
                var matchVal = dataMatches[i].Value;
                if (matchVal.Contains("ID=", StringComparison.OrdinalIgnoreCase) ||
                    (matchVal.Contains("L", StringComparison.OrdinalIgnoreCase) && matchVal.Contains("T", StringComparison.OrdinalIgnoreCase)) ||
                    (matchVal.Contains("F", StringComparison.OrdinalIgnoreCase) && matchVal.Contains("L", StringComparison.OrdinalIgnoreCase)) ||
                    dataMatches.Count == 1)
                {
                    return matchVal;
                }
            }
            return dataMatches[^1].Value;
        }

        if (trimmed.Length <= 3 && trimmed.StartsWith("E", StringComparison.OrdinalIgnoreCase))
            return trimmed.ToUpperInvariant();

        return null;
    }

    /// <summary>
    /// Parse a response string from the Lighting Controller.
    /// Handles: +OK, E1-E7, ER, and data responses ($...#).
    /// </summary>
    public static LightingCommandResult ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return LightingCommandResult.Error("ER", raw ?? string.Empty);

        var extracted = TryExtractResponse(raw) ?? raw.Trim();

        // Success response
        if (extracted.Equals("+OK", StringComparison.OrdinalIgnoreCase))
            return LightingCommandResult.Ok(raw);

        // Error codes: E1-E7, ER
        if (extracted.Length <= 3 && (extracted.StartsWith("E", StringComparison.OrdinalIgnoreCase)))
        {
            return LightingCommandResult.Error(extracted.ToUpperInvariant(), raw);
        }

        // Data response: $key=value,key=value,...#
        if (extracted.StartsWith("$") && extracted.EndsWith("#"))
        {
            try
            {
                var state = ParseDataResponse(extracted);
                return LightingCommandResult.WithData(state, raw);
            }
            catch (Exception ex)
            {
                return new LightingCommandResult
                {
                    IsSuccess = false,
                    ErrorCode = "PARSE_ERROR",
                    ErrorMessage = $"Failed to parse response: {ex.Message}",
                    RawResponse = raw
                };
            }
        }

        // Unknown response
        return LightingCommandResult.Error("ER", raw);
    }

    /// <summary>
    /// Parse a data response (e.g., from RD=9999) into a LightingControllerState.
    /// Format: $ID=0,L0=100,T0=100,F0=1,...,TR=0,...,NE=2,...#
    /// </summary>
    public static LightingControllerState ParseDataResponse(string raw)
    {
        var state = new LightingControllerState();

        // Remove $ and #
        var content = raw.TrimStart('$').TrimEnd('#');

        // Split by comma
        var pairs = content.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            // Split by first '='
            var eqIdx = pair.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = pair.Substring(0, eqIdx).Trim();
            var value = pair.Substring(eqIdx + 1).Trim();

            ApplyKeyValue(state, key, value);
        }

        return state;
    }

    private static void ApplyKeyValue(LightingControllerState state, string key, string value)
    {
        // Channel power: F0-F7
        if (key.Length == 2 && key[0] == 'F' && char.IsDigit(key[1]))
        {
            var ch = key[1] - '0';
            if (ch >= 0 && ch < LightingControllerState.MaxChannels)
            {
                state.Channels[ch].IsEnabled = value != "0";
            }
            return;
        }

        // Channel brightness: L0-L7
        if (key.Length == 2 && key[0] == 'L' && char.IsDigit(key[1]))
        {
            var ch = key[1] - '0';
            if (ch >= 0 && ch < LightingControllerState.MaxChannels && int.TryParse(value, out var brightness))
            {
                state.Channels[ch].Brightness = brightness;
            }
            return;
        }

        // Channel lighting time: T0-T7
        if (key.Length == 2 && key[0] == 'T' && char.IsDigit(key[1]))
        {
            var ch = key[1] - '0';
            if (ch >= 0 && ch < LightingControllerState.MaxChannels && int.TryParse(value, out var timeMs))
            {
                state.Channels[ch].LightingTimeMs = timeMs;
            }
            return;
        }

        // Other known keys
        switch (key.ToUpperInvariant())
        {
            case "TR":
                if (int.TryParse(value, out var tr) && Enum.IsDefined(typeof(LightingTriggerMode), tr))
                    state.TriggerMode = (LightingTriggerMode)tr;
                break;
            case "NE":
                if (int.TryParse(value, out var ne) && Enum.IsDefined(typeof(LightingNetworkMode), ne))
                    state.NetworkMode = (LightingNetworkMode)ne;
                break;
            case "IP":
                state.IpAddress = value;
                break;
            case "IU":
                state.SubnetMask = value;
                break;
            case "IS":
                state.Gateway = value;
                break;
            case "IL":
                if (int.TryParse(value, out var il))
                    state.LocalPort = il;
                break;
            case "DP":
                state.DestinationIp = value;
                break;
            case "DL":
                if (int.TryParse(value, out var dl))
                    state.DestinationPort = dl;
                break;
            case "ID":
                if (int.TryParse(value, out var id))
                    state.Id = id;
                break;
            case "FQ":
                if (int.TryParse(value, out var fq))
                    state.FQ = fq;
                break;
            case "FI":
                if (int.TryParse(value, out var fi))
                    state.FI = fi;
                break;
            case "LC":
                if (int.TryParse(value, out var lc))
                    state.LC = lc;
                break;
            case "PW":
                if (int.TryParse(value, out var pw))
                    state.PW = pw;
                break;
            case "MC":
                state.MC = value;
                break;
        }
    }
}

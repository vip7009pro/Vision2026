using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.PlcBridge;

internal static class Program
{
    private const int DefaultPort = 39871;
    private static readonly CancellationTokenSource _cts = new();
    private static MxComWorker? _worker;
    private static readonly string _logFile = Path.Combine(Path.GetTempPath(), "VisionPlcBridge_debug.log");

    public static void Log(string msg)
    {
        try
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n";
            File.AppendAllText(_logFile, line);
            Console.WriteLine(msg);
        }
        catch { }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Log($"PlcBridge started with args: {string.Join(" ", args)}");

        int parentPid = -1;
        int port = DefaultPort;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--parent-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out parentPid);
            }
            else if (string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out port);
            }
        }

        if (parentPid > 0)
        {
            StartParentProcessWatcher(parentPid);
        }

        try
        {
            Log("Creating MxComWorker STA instance...");
            _worker = new MxComWorker();
            Log("MxComWorker created successfully.");
        }
        catch (Exception ex)
        {
            Log($"Error creating MxComWorker: {ex}");
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();

        try
        {
            Log($"Starting TCP Listener on 127.0.0.1:{port}...");
            RunTcpServerLoop(port, _cts.Token);
            Log("TCP Server loop ended.");
        }
        catch (Exception ex)
        {
            Log($"Unhandled error in TCP Server: {ex}");
        }
        finally
        {
            Cleanup();
        }
    }

    private static void StartParentProcessWatcher(int parentPid)
    {
        try
        {
            var parent = Process.GetProcessById(parentPid);
            parent.EnableRaisingEvents = true;
            parent.Exited += (s, e) =>
            {
                Log($"Parent process PID {parentPid} exited. Terminating bridge immediately.");
                Environment.Exit(0);
            };
        }
        catch (Exception ex)
        {
            Log($"Could not attach Exited event to parent PID {parentPid}: {ex.Message}");
        }

        Task.Run(async () =>
        {
            int deadConfirmationCount = 0;
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(1000, _cts.Token).ConfigureAwait(false);

                    bool isAlive = true;
                    try
                    {
                        var p = Process.GetProcessById(parentPid);
                        isAlive = !p.HasExited;
                    }
                    catch (ArgumentException)
                    {
                        // Process ID not found in system -> process has exited
                        isAlive = false;
                    }
                    catch
                    {
                        // Access denied or WOW64 permission issue -> double check via process enumeration
                        try
                        {
                            isAlive = Process.GetProcesses().Any(pr => pr.Id == parentPid);
                        }
                        catch
                        {
                            isAlive = true;
                        }
                    }

                    if (!isAlive)
                    {
                        deadConfirmationCount++;
                        if (deadConfirmationCount >= 2)
                        {
                            Log($"Parent process PID {parentPid} confirmed dead ({deadConfirmationCount} checks). Exiting bridge worker.");
                            Environment.Exit(0);
                        }
                    }
                    else
                    {
                        deadConfirmationCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Parent watcher exception: {ex.Message}");
            }
        });
    }

    private static void RunTcpServerLoop(int port, CancellationToken cancellationToken)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            Log($"TCP Listener successfully listening on 127.0.0.1:{port}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = listener.AcceptTcpClient();
                    Log("Accepted client connection.");
                    Task.Run(() => HandleClientConnectionAsync(client, cancellationToken), cancellationToken);
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    Log($"Error in AcceptTcpClient: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error starting TcpListener: {ex}");
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    private static async Task HandleClientConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: true) { AutoFlush = true };

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch
                {
                    break;
                }

                if (line == null) break;

                Log($"Received command: '{line}'");
                string response = await ProcessCommandAsync(line);
                Log($"Sending response: '{response}'");

                try
                {
                    await writer.WriteLineAsync(response.AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                }
                catch
                {
                    break;
                }

                // If client says TERMINATE, shutdown entire server
                if (string.Equals(line.Trim(), "TERMINATE_SERVER", StringComparison.OrdinalIgnoreCase))
                {
                    Log("Received TERMINATE_SERVER command. Shutting down.");
                    _cts.Cancel();
                    return;
                }

                // If client says EXIT, simply close this client socket, keep server listening
                if (string.Equals(line.Trim(), "EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    Log("Client disconnected with EXIT.");
                    break;
                }
            }
        }
    }

    private static async Task<string> ProcessCommandAsync(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return "ERR|-1|Empty command";
        if (_worker == null) return "ERR|-1|Worker not initialized";

        string[] parts = commandLine.Split('|');
        string cmd = parts[0].Trim().ToUpperInvariant();

        try
        {
            switch (cmd)
            {
                case "PING":
                    return "PONG";

                case "CONNECT":
                {
                    int stationNo = parts.Length > 1 && int.TryParse(parts[1], out int st) ? st : 1;
                    var (resCode, cpuName, cpuType, errMsg) = await _worker.ConnectAsync(stationNo);
                    if (resCode == 0)
                    {
                        return $"OK|{resCode}|{cpuName}|{cpuType}";
                    }
                    return $"ERR|{resCode}|{errMsg ?? "Connect failed"}";
                }

                case "DISCONNECT":
                {
                    int rc = await _worker.DisconnectAsync();
                    return $"OK|{rc}";
                }

                case "GET_DEVICE":
                {
                    if (parts.Length < 2) return "ERR|-1|Missing device";
                    string device = parts[1];
                    var (rc, val) = await _worker.GetDeviceAsync(device);
                    return rc == 0 ? $"OK|{val}" : $"ERR|{rc}";
                }

                case "SET_DEVICE":
                {
                    if (parts.Length < 3) return "ERR|-1|Missing arguments";
                    string device = parts[1];
                    int val = int.TryParse(parts[2], out int v) ? v : 0;
                    int rc = await _worker.SetDeviceAsync(device, val);
                    return rc == 0 ? "OK" : $"ERR|{rc}";
                }

                case "GET_DEVICE2":
                {
                    if (parts.Length < 2) return "ERR|-1|Missing device";
                    string device = parts[1];
                    var (rc, val) = await _worker.GetDevice2Async(device);
                    return rc == 0 ? $"OK|{val}" : $"ERR|{rc}";
                }

                case "SET_DEVICE2":
                {
                    if (parts.Length < 3) return "ERR|-1|Missing arguments";
                    string device = parts[1];
                    short val = short.TryParse(parts[2], out short v) ? v : (short)0;
                    int rc = await _worker.SetDevice2Async(device, val);
                    return rc == 0 ? "OK" : $"ERR|{rc}";
                }

                case "READ_BLOCK":
                {
                    if (parts.Length < 3) return "ERR|-1|Missing arguments";
                    string device = parts[1];
                    int size = int.TryParse(parts[2], out int s) ? s : 1;
                    var (rc, data) = await _worker.ReadDeviceBlockAsync(device, size);
                    if (rc == 0)
                    {
                        return $"OK|{string.Join(",", data)}";
                    }
                    return $"ERR|{rc}";
                }

                case "WRITE_BLOCK":
                {
                    if (parts.Length < 3) return "ERR|-1|Missing arguments";
                    string device = parts[1];
                    int[] data = System.Array.ConvertAll(parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries), s => int.TryParse(s, out int v) ? v : 0);
                    int rc = await _worker.WriteDeviceBlockAsync(device, data);
                    return rc == 0 ? "OK" : $"ERR|{rc}";
                }

                case "READ_BLOCK2":
                {
                    if (parts.Length < 3) return "ERR|-1|Missing arguments";
                    string device = parts[1];
                    int size = int.TryParse(parts[2], out int s) ? s : 1;
                    var (rc, data) = await _worker.ReadDeviceBlock2Async(device, size);
                    if (rc == 0)
                    {
                        return $"OK|{string.Join(",", data)}";
                    }
                    return $"ERR|{rc}";
                }

                case "WRITE_BLOCK2":
                {
                    if (parts.Length < 3) return "ERR|-1|Missing arguments";
                    string device = parts[1];
                    short[] data = System.Array.ConvertAll(parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries), s => short.TryParse(s, out short v) ? v : (short)0);
                    int rc = await _worker.WriteDeviceBlock2Async(device, data);
                    return rc == 0 ? "OK" : $"ERR|{rc}";
                }

                case "EXIT":
                case "TERMINATE_SERVER":
                    return "OK";

                default:
                    return $"ERR|-1|Unknown command: {cmd}";
            }
        }
        catch (Exception ex)
        {
            return $"ERR|-99|{ex.Message}";
        }
    }

    private static void Cleanup()
    {
        Log("PlcBridge cleaning up and exiting.");
        if (_worker != null)
        {
            try { _worker.Dispose(); } catch { }
            _worker = null;
        }
    }
}

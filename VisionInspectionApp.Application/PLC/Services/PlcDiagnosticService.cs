using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

public sealed class PlcDiagnosticResult
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TargetIp { get; set; } = string.Empty;
    public int TargetPort { get; set; }
    public PlcDriverType DriverType { get; set; }

    // 1. Ping ICMP
    public bool PingSuccess { get; set; }
    public long PingRoundtripMs { get; set; }
    public string PingMessage { get; set; } = string.Empty;

    // 2. TCP Socket
    public bool SocketConnected { get; set; }
    public long SocketConnectMs { get; set; }
    public string SocketMessage { get; set; } = string.Empty;

    // 3. MC Protocol Probe
    public bool McProtocolSuccess { get; set; }
    public string McProtocolResponseSummary { get; set; } = string.Empty;
    public string TxHexDump { get; set; } = string.Empty;
    public string RxHexDump { get; set; } = string.Empty;
    public ushort? ReturnCode { get; set; }
    public string CpuModelDetected { get; set; } = string.Empty;

    // 4. Report & Advice
    public string DiagnosisAdvice { get; set; } = string.Empty;
    public string FullReportText { get; set; } = string.Empty;
    public string? SavedLogFilePath { get; set; }
}

public sealed class PlcDiagnosticService
{
    public static async Task<PlcDiagnosticResult> RunDiagnosticAsync(
        string ip, 
        int port, 
        PlcDriverType driverType, 
        int logicalStation = 1, 
        CancellationToken cancellationToken = default)
    {
        var result = new PlcDiagnosticResult
        {
            TargetIp = ip?.Trim() ?? string.Empty,
            TargetPort = port,
            DriverType = driverType
        };

        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                 BÁO CÁO CHẨN ĐOÁN KẾT NỐI PLC & GÓI TIN MẠNG                   ");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Thời gian kiểm tra : {result.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Địa chỉ IP mục tiêu : {result.TargetIp}");
        sb.AppendLine($"Cổng mục tiêu (Port): {result.TargetPort}");
        sb.AppendLine($"Loại Driver         : {result.DriverType}");
        if (driverType == PlcDriverType.MitsubishiMxComponent)
        {
            sb.AppendLine($"Trạm Logic (Station): {logicalStation}");
        }
        sb.AppendLine("--------------------------------------------------------------------------------");

        // BƯỚC 1: KIỂM TRA PING ICMP
        sb.AppendLine("\n[BƯỚC 1] Kiểm tra kết nối mạng vật lý (ICMP Ping):");
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(result.TargetIp, 2000);
            if (reply.Status == IPStatus.Success)
            {
                result.PingSuccess = true;
                result.PingRoundtripMs = reply.RoundtripTime;
                result.PingMessage = $"Ping OK: {reply.RoundtripTime} ms (TTL: {reply.Options?.Ttl ?? 0})";
                sb.AppendLine($"  => THÀNH CÔNG: Thiết bị phản hồi Ping sau {reply.RoundtripTime} ms.");
            }
            else
            {
                result.PingSuccess = false;
                result.PingMessage = $"Ping thất bại: {reply.Status}";
                sb.AppendLine($"  => CẢNH BÁO: Ping không nhận được phản hồi ({reply.Status}).");
                sb.AppendLine("     (Lưu ý: Một số switch công nghiệp hoặc tường lửa chặn gói ICMP, tiến hành kiểm tra tiếp cổng TCP).");
            }
        }
        catch (Exception ex)
        {
            result.PingSuccess = false;
            result.PingMessage = $"Lỗi Ping: {ex.Message}";
            sb.AppendLine($"  => LỖI PING: {ex.Message}");
        }

        // BƯỚC 2: KIỂM TRA KẾT NỐI TCP SOCKET
        sb.AppendLine($"\n[BƯỚC 2] Kiểm tra kết nối TCP Socket tới cổng {result.TargetPort}:");
        TcpClient? tcpClient = null;
        NetworkStream? stream = null;
        var swSocket = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            tcpClient = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(3000));

            await tcpClient.ConnectAsync(result.TargetIp, result.TargetPort, cts.Token);
            swSocket.Stop();
            result.SocketConnectMs = swSocket.ElapsedMilliseconds;

            if (tcpClient.Connected)
            {
                result.SocketConnected = true;
                result.SocketMessage = $"Mở Socket TCP thành công sau {result.SocketConnectMs} ms";
                sb.AppendLine($"  => THÀNH CÔNG: Socket TCP đã kết nối tới {result.TargetIp}:{result.TargetPort} ({result.SocketConnectMs} ms).");
                stream = tcpClient.GetStream();
                stream.ReadTimeout = 2500;
                stream.WriteTimeout = 2500;
            }
            else
            {
                result.SocketConnected = false;
                result.SocketMessage = "Socket TCP không thể kết nối.";
                sb.AppendLine($"  => THẤT BẠI: Socket không thể kết nối tới {result.TargetIp}:{result.TargetPort}.");
            }
        }
        catch (Exception ex)
        {
            swSocket.Stop();
            result.SocketConnected = false;
            result.SocketMessage = $"Lỗi kết nối Socket: {ex.Message}";
            sb.AppendLine($"  => THẤT BẠI: Không thể mở cổng TCP {result.TargetPort}. Lỗi: {ex.Message}");
        }

        // BƯỚC 3: THĂM DÒ GÓI TIN MC PROTOCOL (NẾU SOCKET ĐÃ MỞ ĐƯỢC)
        if (result.SocketConnected && stream != null)
        {
            sb.AppendLine("\n[BƯỚC 3] Thăm dò gói tin MC Protocol 3E Binary:");

            // Thử lệnh MC Protocol 3E Binary: Command 0x0101 (Read CPU type)
            // Header 3E: Subheader 50 00, Net 00, PLC FF, TargetIO FF 03, Station 00, Length 0C 00, Timer 10 00, Cmd 01 01, Subcmd 00 00
            byte[] probe3e = new byte[] {
                0x50, 0x00,             // Subheader 3E
                0x00,                   // Network No
                0xFF,                   // PLC No
                0xFF, 0x03,             // Target IO: 0x03FF
                0x00,                   // Target Station
                0x0C, 0x00,             // Request Data Length (12 bytes)
                0x10, 0x00,             // CPU Timer
                0x01, 0x01,             // Command: 0x0101 (Read CPU Type)
                0x00, 0x00              // Subcommand: 0x0000
            };

            result.TxHexDump = BitConverter.ToString(probe3e).Replace("-", " ");
            sb.AppendLine($"  TX (Gửi đi - {probe3e.Length} bytes):");
            sb.AppendLine($"     {result.TxHexDump}");

            try
            {
                using var ctsProbe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ctsProbe.CancelAfter(TimeSpan.FromMilliseconds(2500));

                await stream.WriteAsync(probe3e, 0, probe3e.Length, ctsProbe.Token);

                byte[] rxBuffer = new byte[64];
                int readBytes = await stream.ReadAsync(rxBuffer, 0, rxBuffer.Length, ctsProbe.Token);

                if (readBytes > 0)
                {
                    byte[] receivedData = rxBuffer.Take(readBytes).ToArray();
                    result.RxHexDump = BitConverter.ToString(receivedData).Replace("-", " ");
                    sb.AppendLine($"  RX (Phản hồi từ PLC - {readBytes} bytes):");
                    sb.AppendLine($"     {result.RxHexDump}");

                    if (readBytes >= 11)
                    {
                        ushort subheader = (ushort)(receivedData[0] | (receivedData[1] << 8));
                        ushort retCode = (ushort)(receivedData[9] | (receivedData[10] << 8));
                        result.ReturnCode = retCode;

                        if (retCode == 0)
                        {
                            result.McProtocolSuccess = true;
                            if (readBytes >= 27)
                            {
                                string cpuStr = Encoding.ASCII.GetString(receivedData, 11, Math.Min(16, readBytes - 11)).Trim('\0', ' ');
                                result.CpuModelDetected = cpuStr;
                            }
                            else
                            {
                                result.CpuModelDetected = "Mitsubishi PLC (OK)";
                            }
                            result.McProtocolResponseSummary = $"Phản hồi 3E hợp lệ (Return Code = 0x0000). CPU: {result.CpuModelDetected}";
                            sb.AppendLine($"  => THÀNH CÔNG: Nhận phản hồi chuẩn MC Protocol 3E! CPU: '{result.CpuModelDetected}' (Code: 0x0000).");
                        }
                        else
                        {
                            result.McProtocolSuccess = false;
                            result.McProtocolResponseSummary = $"PLC phản hồi mã lỗi (End Code = 0x{retCode:X4})";
                            sb.AppendLine($"  => PLC PHẢN HỒI LỖI: End Code = 0x{retCode:X4}.");
                        }
                    }
                    else
                    {
                        result.McProtocolSuccess = false;
                        result.McProtocolResponseSummary = $"Gói tin nhận được quá ngắn ({readBytes} bytes < 11 bytes)";
                        sb.AppendLine($"  => CẢNH BÁO: Chiều dài phản hồi {readBytes} bytes không đủ chuẩn 11 bytes 3E Header.");
                    }
                }
                else
                {
                    result.McProtocolSuccess = false;
                    result.RxHexDump = "(0 bytes - PLC đóng kết nối hoặc không phản hồi)";
                    result.McProtocolResponseSummary = "PLC không phản hồi byte nào (0 bytes received).";
                    sb.AppendLine("  RX: (0 bytes nhận được - Socket bị đóng bởi PLC)");
                }
            }
            catch (OperationCanceledException)
            {
                result.McProtocolSuccess = false;
                result.RxHexDump = "(Timeout 2.5s - Không có phản hồi từ PLC)";
                result.McProtocolResponseSummary = $"Timeout sau 2.5s: Cổng {result.TargetPort} không phản hồi khung tin 3E Binary";
                sb.AppendLine($"  RX: Timeout (Không có phản hồi từ PLC sau 2.5s).");
            }
            catch (Exception ex)
            {
                result.McProtocolSuccess = false;
                result.RxHexDump = $"(Lỗi nhận gói tin: {ex.Message})";
                result.McProtocolResponseSummary = $"Lỗi đọc socket: {ex.Message}";
                sb.AppendLine($"  RX: Ngoại lệ đọc dữ liệu: {ex.Message}");
            }
        }
        else
        {
            sb.AppendLine("\n[BƯỚC 3] Thăm dò gói tin MC Protocol: Bỏ qua (Do Bước 2 không kết nối được Socket).");
        }

        // DỌN DẸP TÀI NGUYÊN SOCKET
        try { stream?.Dispose(); } catch { }
        try { tcpClient?.Close(); tcpClient?.Dispose(); } catch { }

        // BƯỚC 4: KẾT LUẬN & HƯỚNG DẪN XỬ LÝ (DIAGNOSIS ADVICE)
        sb.AppendLine("\n--------------------------------------------------------------------------------");
        sb.AppendLine("[BƯỚC 4] KẾT LUẬN & HƯỚNG DẪN XỬ LÝ:");

        if (result.McProtocolSuccess)
        {
            result.DiagnosisAdvice = "✅ KẾT NỐI HOÀN HẢO! PLC phản hồi chuẩn MC Protocol 3E Binary. Bạn có thể sử dụng driver Mitsubishi MC Protocol bình thường.";
            sb.AppendLine(result.DiagnosisAdvice);
        }
        else if (result.SocketConnected && !result.McProtocolSuccess)
        {
            result.DiagnosisAdvice = 
                $"⚠️ PHÂN TÍCH NGUYÊN NHÂN LỖI:\n" +
                $"1. Socket TCP tới IP {result.TargetIp} cổng {result.TargetPort} đã MỞ THÀNH CÔNG.\n" +
                $"   => Chứng tỏ đường dây mạng, switch và IP PLC hoàn toàn thông suốt.\n" +
                $"2. Tuy nhiên PLC KHÔNG PHẢN HỒI khung tin MC Protocol 3E Binary.\n" +
                $"3. Các nguyên nhân thực tế thường gặp:\n" +
                $"   - Cổng {result.TargetPort} đang được gán cho dịch vụ 'MELSOFT Connection' (dành cho GX Works hoặc MX Component), KHÔNG PHẢI cổng MC Protocol.\n" +
                $"   - Màn hình HMI Weintek đang duy trì kết nối TCP độc quyền tới cổng {result.TargetPort}. Trên PLC Mitsubishi, mỗi cổng TCP chỉ phục vụ 1 client độc quyền tại một thời điểm!\n" +
                $"   - Nếu PLC là dòng FX3U lắp module FX3U-ENET-ADP: Module này chỉ hỗ trợ MC Protocol 1E, không hỗ trợ 3E.\n\n" +
                $"👉 HƯỚNG DẪN XỬ LÝ DỨT ĐIỂM TRÊN PLC (GX Works 2 / GX Works 3):\n" +
                $"   1. Mở phần mềm GX Works kết nối vào PLC.\n" +
                $"   2. Vào mục 'Ethernet Configuration' (hoặc Network Parameter -> Ethernet -> Open Settings).\n" +
                $"   3. Mở thêm một kết nối riêng biệt (ví dụ Connection No. 2):\n" +
                $"      + Giao thức (Protocol): TCP\n" +
                $"      + Kiểu giao tiếp (Open System): MC Protocol (hoặc SLMP)\n" +
                $"      + Cổng (Host Station Port): Hãy đặt cổng riêng (ví dụ 5007 hoặc 6000), KHÔNG đặt trùng với {result.TargetPort}.\n" +
                $"      + Định dạng: Binary\n" +
                $"   4. Tải (Write) cấu hình xuống PLC và Reboot lại PLC.\n" +
                $"   5. Trên phần mềm Vision, nhập cổng mới (ví dụ 5007) và bấm Kết Nối.";

            sb.AppendLine(result.DiagnosisAdvice);
        }
        else if (!result.SocketConnected)
        {
            if (result.PingSuccess)
            {
                result.DiagnosisAdvice = 
                    $"❌ LỖI CỔNG TCP {result.TargetPort} BỊ ĐÓNG:\n" +
                    $"1. Thiết bị tại IP {result.TargetIp} có phản hồi Ping, nhưng từ chối kết nối cổng {result.TargetPort}.\n" +
                    $"2. Nguyên nhân: Cổng {result.TargetPort} chưa được mở trong cấu hình Ethernet của PLC, hoặc Windows Firewall / Switch chặn cổng này.\n" +
                    $"3. Hãy kiểm tra lại cấu hình Open Settings trong GX Works.";
            }
            else
            {
                result.DiagnosisAdvice = 
                    $"❌ KHÔNG TÌM THẤY THIẾT BỊ TẠI IP {result.TargetIp}:\n" +
                    $"1. Cả Ping và Socket đều thất bại.\n" +
                    $"2. Vui lòng kiểm tra cáp mạng cắm vào máy Vision PC và PLC.\n" +
                    $"3. Kiểm tra dải IP card mạng của PC (phải cùng lớp mạng 192.168.10.xxx với PLC, ví dụ đặt PC là 192.168.10.100, Subnet 255.255.255.0).";
            }
            sb.AppendLine(result.DiagnosisAdvice);
        }

        sb.AppendLine("================================================================================");
        result.FullReportText = sb.ToString();

        // TỰ ĐỘNG GHI FILE LOG VÀO APPDATA ĐỂ NGƯỜI DÙNG DỄ DÀNG COPY VỀ MÁY DEV
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDir = Path.Combine(appData, "Vision2026", "logs");
            Directory.CreateDirectory(logDir);
            string logFileName = $"plc_diag_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            string logFilePath = Path.Combine(logDir, logFileName);
            File.WriteAllText(logFilePath, result.FullReportText, Encoding.UTF8);
            result.SavedLogFilePath = logFilePath;
        }
        catch { }

        return result;
    }
}

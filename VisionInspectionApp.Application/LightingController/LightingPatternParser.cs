using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.LightingController;

/// <summary>
/// Động cơ phân tích cú pháp kịch bản nháy đèn thông minh (Smart Lighting Pattern Parser).
/// Hỗ trợ cả 3 phong cách: dấu phẩy (Token Stream), nhiều dòng/chấm phẩy (Structured Script), và Macro (STROBE, CHASE).
/// </summary>
public static class LightingPatternParser
{
    private static readonly char[] LineSeparators = new[] { '\r', '\n', ';' };

    /// <summary>
    /// Kiểm tra tính hợp lệ của kịch bản và tính toán ước lượng thời gian chạy của 1 chu kỳ.
    /// </summary>
    public static LightingPatternValidationResult Validate(string? script, int channelCount = 8)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return LightingPatternValidationResult.Error("Nội dung kịch bản đang để trống.");
        }

        try
        {
            var steps = Parse(script, channelCount);
            if (steps.Count == 0)
            {
                return LightingPatternValidationResult.Error("Kịch bản không chứa bất kỳ bước lệnh nào.");
            }

            int totalMs = steps.Sum(s => Math.Max(0, s.DelayMs));
            return LightingPatternValidationResult.Success(steps.Count, totalMs);
        }
        catch (FormatException fEx)
        {
            return LightingPatternValidationResult.Error(fEx.Message);
        }
        catch (Exception ex)
        {
            return LightingPatternValidationResult.Error($"Lỗi phân tích cú pháp: {ex.Message}");
        }
    }

    /// <summary>
    /// Phân tích kịch bản thành danh sách các bước lệnh tuần tự (LightingPatternStep).
    /// </summary>
    public static List<LightingPatternStep> Parse(string? script, int channelCount = 8)
    {
        var result = new List<LightingPatternStep>();
        if (string.IsNullOrWhiteSpace(script)) return result;

        int totalChannels = channelCount > 0 ? channelCount : 8;

        // Tiền xử lý: loại bỏ comment block /* ... */
        var cleanScript = Regex.Replace(script, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        // Tách thành các dòng / câu lệnh
        var rawLines = cleanScript.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

        int lineIndex = 0;
        foreach (var rawLine in rawLines)
        {
            lineIndex++;
            var line = rawLine.Trim();

            // Bỏ qua dòng comment hoặc dòng trống
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
            {
                continue;
            }

            // Cắt bỏ inline comment ở đuôi dòng nếu có
            int commentPos = line.IndexOf('#');
            if (commentPos >= 0) line = line.Substring(0, commentPos).Trim();
            commentPos = line.IndexOf("//", StringComparison.Ordinal);
            if (commentPos >= 0) line = line.Substring(0, commentPos).Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Kiểm tra xem dòng có phải là Macro (STROBE / CHASE) hay không
            if (line.StartsWith("STROBE", StringComparison.OrdinalIgnoreCase))
            {
                ParseStrobeMacro(line, totalChannels, result, lineIndex);
                continue;
            }

            if (line.StartsWith("CHASE", StringComparison.OrdinalIgnoreCase))
            {
                ParseChaseMacro(line, totalChannels, result, lineIndex);
                continue;
            }

            // Phân tích câu lệnh thông thường (hỗ trợ cả phân tách bằng phẩy hoặc khoảng trắng)
            ParseCommandLine(line, totalChannels, result, lineIndex);
        }

        return result;
    }

    /// <summary>
    /// Phân tích một dòng lệnh thông thường:
    /// Có thể là: "ALL OFF", "L1 ON 255 100", "DELAY 200", "L1, ON, 300, L1, OFF"
    /// </summary>
    private static void ParseCommandLine(string line, int totalChannels, List<LightingPatternStep> result, int lineIndex)
    {
        // Tách các token phân cách bởi dấu phẩy hoặc khoảng trắng
        var tokens = Regex.Matches(line, @"[^\s,]+").Cast<Match>().Select(m => m.Value).ToList();
        if (tokens.Count == 0) return;

        int i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            var upper = token.ToUpperInvariant();

            // 1. DELAY / WAIT
            if (upper == "DELAY" || upper == "WAIT" || upper == "SLEEP")
            {
                i++;
                if (i >= tokens.Count || !int.TryParse(tokens[i], out int delayMs))
                {
                    throw new FormatException($"[Dòng {lineIndex}] Lệnh '{upper}' cần kèm theo số mili-giây (ví dụ: DELAY 200).");
                }
                result.Add(new LightingPatternStep
                {
                    StepType = LightingPatternStepType.Delay,
                    DelayMs = Math.Max(1, delayMs),
                    SummaryText = $"Tạm dừng {delayMs}ms"
                });
                i++;
                continue;
            }

            // 2. Kênh đèn hoặc ALL
            var (isChannel, channels) = TryParseChannels(token, totalChannels);
            if (isChannel)
            {
                i++;
                if (i >= tokens.Count)
                {
                    throw new FormatException($"[Dòng {lineIndex}] Kênh '{token}' thiếu hành động tiếp theo (ON, OFF, SET).");
                }

                var action = tokens[i].ToUpperInvariant();
                i++;

                if (action == "ON")
                {
                    int? brightness = null;
                    int delayMs = 0;

                    // Kiểm tra tham số kế tiếp: độ sáng hoặc thời gian ms
                    if (i < tokens.Count && int.TryParse(tokens[i], out int p1))
                    {
                        i++;
                        // Nếu có tham số thứ hai dạng số
                        if (i < tokens.Count && int.TryParse(tokens[i], out int p2))
                        {
                            i++;
                            brightness = Math.Clamp(p1, 0, 255);
                            delayMs = Math.Max(0, p2);
                        }
                        else
                        {
                            // Chỉ có 1 số:
                            // Kiểm tra xem token kế tiếp có phải là Channel hoặc lệnh tiếp theo hay không
                            bool isNextTokenCommand = false;
                            if (i < tokens.Count)
                            {
                                var (isNextCh, _) = TryParseChannels(tokens[i], totalChannels);
                                var nextUpper = tokens[i].ToUpperInvariant();
                                if (isNextCh || nextUpper == "DELAY" || nextUpper == "WAIT" || nextUpper == "SLEEP" ||
                                    nextUpper == "ALL" || nextUpper == "STROBE" || nextUpper == "CHASE" ||
                                    nextUpper == "ON" || nextUpper == "OFF" || nextUpper == "SET")
                                {
                                    isNextTokenCommand = true;
                                }
                            }

                            // Quy tắc thông minh:
                            // 1. Nếu p1 > 255: Chắc chắn là thời gian trễ ms.
                            // 2. Nếu dòng có dấu phẩy hoặc token tiếp theo là channel/lệnh (chuỗi stream): p1 là thời gian trễ ms (ví dụ: L1, ON, 100, L1, OFF).
                            // 3. Nếu là dòng đơn lẻ không có token tiếp theo và không có dấu phẩy (ví dụ: ALL ON 200): p1 là độ sáng.
                            if (p1 > 255 || isNextTokenCommand || line.Contains(','))
                            {
                                delayMs = p1;
                            }
                            else
                            {
                                brightness = p1;
                            }
                        }
                    }

                    result.Add(new LightingPatternStep
                    {
                        StepType = LightingPatternStepType.Command,
                        Channels = channels,
                        PowerOn = true,
                        Brightness = brightness ?? 255,
                        DelayMs = delayMs,
                        SummaryText = $"Bật {FormatChannelList(channels)} (Sáng: {brightness ?? 255}){(delayMs > 0 ? $", chờ {delayMs}ms" : "")}"
                    });
                }
                else if (action == "OFF")
                {
                    int delayMs = 0;
                    if (i < tokens.Count && int.TryParse(tokens[i], out int p1))
                    {
                        i++;
                        delayMs = Math.Max(0, p1);
                    }

                    result.Add(new LightingPatternStep
                    {
                        StepType = LightingPatternStepType.Command,
                        Channels = channels,
                        PowerOn = false,
                        DelayMs = delayMs,
                        SummaryText = $"Tắt {FormatChannelList(channels)}{(delayMs > 0 ? $", chờ {delayMs}ms" : "")}"
                    });
                }
                else if (action == "SET")
                {
                    if (i >= tokens.Count || !int.TryParse(tokens[i], out int br))
                    {
                        throw new FormatException($"[Dòng {lineIndex}] Lệnh 'SET' cần độ sáng từ 0 đến 255 (ví dụ: {token} SET 150).");
                    }
                    i++;
                    int delayMs = 0;
                    if (i < tokens.Count && int.TryParse(tokens[i], out int p2))
                    {
                        i++;
                        delayMs = Math.Max(0, p2);
                    }

                    result.Add(new LightingPatternStep
                    {
                        StepType = LightingPatternStepType.Command,
                        Channels = channels,
                        PowerOn = true,
                        Brightness = Math.Clamp(br, 0, 255),
                        DelayMs = delayMs,
                        SummaryText = $"Đặt {FormatChannelList(channels)} sáng {br}{(delayMs > 0 ? $", chờ {delayMs}ms" : "")}"
                    });
                }
                else
                {
                    throw new FormatException($"[Dòng {lineIndex}] Hành động không hợp lệ '{action}' cho kênh '{token}'. Phải là ON, OFF, hoặc SET.");
                }

                continue;
            }

            // 3. Nếu là một con số đứng độc lập trong chuỗi (ví dụ: "L1, ON, L1, OFF, 200")
            if (int.TryParse(token, out int standaloneMs))
            {
                result.Add(new LightingPatternStep
                {
                    StepType = LightingPatternStepType.Delay,
                    DelayMs = Math.Max(1, standaloneMs),
                    SummaryText = $"Tạm dừng {standaloneMs}ms"
                });
                i++;
                continue;
            }

            throw new FormatException($"[Dòng {lineIndex}] Ký hiệu không xác định '{token}'. Hãy kiểm tra cú pháp tên kênh hoặc lệnh.");
        }
    }

    /// <summary>
    /// Bung Macro STROBE: STROBE [CHANNELS|ALL] <ON_MS> <OFF_MS> <COUNT> [BRIGHTNESS]
    /// Ví dụ: STROBE ALL 60 60 3 255
    /// </summary>
    private static void ParseStrobeMacro(string line, int totalChannels, List<LightingPatternStep> result, int lineIndex)
    {
        var tokens = Regex.Matches(line, @"[^\s,]+").Cast<Match>().Select(m => m.Value).ToList();
        // Cần ít nhất: STROBE <CH> <ON_MS> <OFF_MS> <COUNT> (5 tokens)
        if (tokens.Count < 5)
        {
            throw new FormatException($"[Dòng {lineIndex}] Cú pháp STROBE yêu cầu: STROBE <KÊNH|ALL> <ON_MS> <OFF_MS> <COUNT> [BRIGHTNESS]");
        }

        var (isChannel, channels) = TryParseChannels(tokens[1], totalChannels);
        if (!isChannel)
        {
            throw new FormatException($"[Dòng {lineIndex}] Kênh không hợp lệ '{tokens[1]}' trong lệnh STROBE.");
        }

        if (!int.TryParse(tokens[2], out int onMs) || onMs < 1)
            throw new FormatException($"[Dòng {lineIndex}] Thời gian sáng ON_MS '{tokens[2]}' trong STROBE phải là số nguyên > 0.");

        if (!int.TryParse(tokens[3], out int offMs) || offMs < 1)
            throw new FormatException($"[Dòng {lineIndex}] Thời gian tắt OFF_MS '{tokens[3]}' trong STROBE phải là số nguyên > 0.");

        if (!int.TryParse(tokens[4], out int count) || count < 1 || count > 100)
            throw new FormatException($"[Dòng {lineIndex}] Số lần chớp COUNT '{tokens[4]}' trong STROBE phải từ 1 đến 100.");

        int brightness = 255;
        if (tokens.Count >= 6 && int.TryParse(tokens[5], out int br))
        {
            brightness = Math.Clamp(br, 0, 255);
        }

        for (int c = 0; c < count; c++)
        {
            result.Add(new LightingPatternStep
            {
                StepType = LightingPatternStepType.Command,
                Channels = channels,
                PowerOn = true,
                Brightness = brightness,
                DelayMs = onMs,
                SummaryText = $"[STROBE #{c + 1}/{count}] Bật {FormatChannelList(channels)} sáng {brightness}, chờ {onMs}ms"
            });

            result.Add(new LightingPatternStep
            {
                StepType = LightingPatternStepType.Command,
                Channels = channels,
                PowerOn = false,
                DelayMs = offMs,
                SummaryText = $"[STROBE #{c + 1}/{count}] Tắt {FormatChannelList(channels)}, chờ {offMs}ms"
            });
        }
    }

    /// <summary>
    /// Bung Macro CHASE: CHASE <STEP_MS> [BRIGHTNESS]
    /// Ví dụ: CHASE 100 255 -> Chạy lần lượt từng kênh từ L1 đến LN
    /// </summary>
    private static void ParseChaseMacro(string line, int totalChannels, List<LightingPatternStep> result, int lineIndex)
    {
        var tokens = Regex.Matches(line, @"[^\s,]+").Cast<Match>().Select(m => m.Value).ToList();
        if (tokens.Count < 2 || !int.TryParse(tokens[1], out int stepMs) || stepMs < 1)
        {
            throw new FormatException($"[Dòng {lineIndex}] Cú pháp CHASE yêu cầu: CHASE <STEP_MS> [BRIGHTNESS] (ví dụ: CHASE 100 255)");
        }

        int brightness = 255;
        if (tokens.Count >= 3 && int.TryParse(tokens[2], out int br))
        {
            brightness = Math.Clamp(br, 0, 255);
        }

        for (int ch = 0; ch < totalChannels; ch++)
        {
            var singleList = new List<int> { ch };
            result.Add(new LightingPatternStep
            {
                StepType = LightingPatternStepType.Command,
                Channels = singleList,
                PowerOn = true,
                Brightness = brightness,
                DelayMs = stepMs,
                SummaryText = $"[CHASE] Bật L{ch + 1} sáng {brightness}, chờ {stepMs}ms"
            });

            result.Add(new LightingPatternStep
            {
                StepType = LightingPatternStepType.Command,
                Channels = singleList,
                PowerOn = false,
                DelayMs = 0,
                SummaryText = $"[CHASE] Tắt L{ch + 1}"
            });
        }
    }

    /// <summary>
    /// Nhận diện danh sách kênh từ token (ví dụ: "L1", "CH1", "ALL", "*", "L1,L2", "CH1-CH4").
    /// </summary>
    private static (bool Success, List<int> Channels) TryParseChannels(string token, int totalChannels)
    {
        var upper = token.ToUpperInvariant();

        if (upper == "ALL" || upper == "*")
        {
            return (true, Enumerable.Range(0, totalChannels).ToList());
        }

        // Hỗ trợ dải kênh: CH1-CH4 hoặc L1-L4
        var rangeMatch = Regex.Match(upper, @"^(?:CH|L)?([1-8])-(?:CH|L)?([1-8])$");
        if (rangeMatch.Success)
        {
            int start = int.Parse(rangeMatch.Groups[1].Value) - 1;
            int end = int.Parse(rangeMatch.Groups[2].Value) - 1;
            if (start > end) (start, end) = (end, start);
            var list = new List<int>();
            for (int ch = start; ch <= end && ch < totalChannels; ch++)
            {
                list.Add(ch);
            }
            return (list.Count > 0, list);
        }

        // Hỗ trợ đơn kênh: L1, CH1, 1
        var singleMatch = Regex.Match(upper, @"^(?:CH|L)?([1-8])$");
        if (singleMatch.Success)
        {
            int ch = int.Parse(singleMatch.Groups[1].Value) - 1;
            if (ch < totalChannels)
            {
                return (true, new List<int> { ch });
            }
        }

        return (false, new List<int>());
    }

    private static string FormatChannelList(List<int> channels)
    {
        if (channels.Count == 0) return "[]";
        if (channels.Count == 1) return $"L{channels[0] + 1}";
        return string.Join(",", channels.Select(c => $"L{c + 1}"));
    }
}

using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Models;

/// <summary>
/// Loại bước lệnh trong kịch bản nháy đèn.
/// </summary>
public enum LightingPatternStepType
{
    /// <summary>Điều khiển trạng thái Bật/Tắt và độ sáng của một hoặc nhiều kênh.</summary>
    Command,
    /// <summary>Tạm dừng chờ (Delay/Wait) trong khoảng thời gian ms.</summary>
    Delay
}

/// <summary>
/// Đại diện cho một bước lệnh cụ thể đã được biên dịch từ script.
/// </summary>
public sealed class LightingPatternStep
{
    /// <summary>Loại bước lệnh.</summary>
    public LightingPatternStepType StepType { get; set; } = LightingPatternStepType.Command;

    /// <summary>Danh sách chỉ số kênh (0-indexed, 0 đến 7).</summary>
    public List<int> Channels { get; set; } = new();

    /// <summary>Trạng thái nguồn (true = Bật, false = Tắt, null = Giữ nguyên).</summary>
    public bool? PowerOn { get; set; }

    /// <summary>Mức độ sáng (0 đến 255, null = Giữ nguyên hoặc mặc định).</summary>
    public int? Brightness { get; set; }

    /// <summary>Thời gian trễ (mili-giây) sau khi thực hiện lệnh hoặc thời gian của bước Delay.</summary>
    public int DelayMs { get; set; }

    /// <summary>Mô tả tóm tắt bước lệnh phục vụ debug / hiển thị.</summary>
    public string SummaryText { get; set; } = string.Empty;

    public override string ToString() => SummaryText;
}

/// <summary>
/// Kết quả kiểm tra cú pháp kịch bản nháy đèn.
/// </summary>
public sealed class LightingPatternValidationResult
{
    /// <summary>Cú pháp có hợp lệ hay không.</summary>
    public bool IsValid { get; set; }

    /// <summary>Thông báo lỗi chi tiết nếu không hợp lệ.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Vị trí dòng bị lỗi (1-indexed, 0 nếu không xác định).</summary>
    public int ErrorLine { get; set; }

    /// <summary>Số bước lệnh phân tích được.</summary>
    public int StepCount { get; set; }

    /// <summary>Tổng thời gian ước tính chạy cho 1 chu kỳ (ms).</summary>
    public int EstimatedDurationMsPerCycle { get; set; }

    public static LightingPatternValidationResult Success(int stepCount, int estimatedDurationMs) => new()
    {
        IsValid = true,
        StepCount = stepCount,
        EstimatedDurationMsPerCycle = estimatedDurationMs,
        ErrorMessage = string.Empty
    };

    public static LightingPatternValidationResult Error(string message, int line = 0) => new()
    {
        IsValid = false,
        ErrorMessage = message,
        ErrorLine = line,
        StepCount = 0,
        EstimatedDurationMsPerCycle = 0
    };
}

/// <summary>
/// Đại diện cho một kịch bản hiệu ứng nháy đèn (Blink Pattern Scenario).
/// </summary>
public sealed class LightingPatternModel
{
    /// <summary>Định danh duy nhất của kịch bản.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Tên hiển thị trực quan của kịch bản.</summary>
    public string Name { get; set; } = "Kịch bản mới";

    /// <summary>Mô tả tác dụng hoặc mục đích của kịch bản.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Số chu kỳ lặp lại (1..999, mặc định: 1).</summary>
    public int RepeatCycles { get; set; } = 1;

    /// <summary>Nội dung kịch bản dạng mã văn bản.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>Đánh dấu kịch bản mặc định tích hợp sẵn của hệ thống.</summary>
    public bool IsBuiltIn { get; set; }

    public LightingPatternModel() { }

    public LightingPatternModel(string id, string name, string description, int repeatCycles, string script, bool isBuiltIn = false)
    {
        Id = id;
        Name = name;
        Description = description;
        RepeatCycles = repeatCycles;
        Script = script;
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>
    /// Tạo bản sao độc lập của kịch bản.
    /// </summary>
    public LightingPatternModel Clone(string? newName = null)
    {
        return new LightingPatternModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = newName ?? $"{Name} (Bản sao)",
            Description = Description,
            RepeatCycles = RepeatCycles,
            Script = Script,
            IsBuiltIn = false
        };
    }

    /// <summary>
    /// Khởi tạo danh sách 5 kịch bản mẫu sẵn chuẩn công nghiệp.
    /// </summary>
    public static List<LightingPatternModel> CreateDefaultPatterns()
    {
        return new List<LightingPatternModel>
        {
            new LightingPatternModel(
                id: "pattern_welcome",
                name: "🌟 Chào Mừng Khởi Động (Welcome Chase)",
                description: "Hiệu ứng chạy đuổi mượt từ kênh 1 đến kênh 4/8 rồi nháy đồng loạt chào mừng khi mở app",
                repeatCycles: 1,
                script:
@"# ===================================================
# Kịch bản Chào Mừng Khi Mở Ứng Dụng (Startup Welcome)
# ===================================================
ALL OFF
DELAY 100

# Chạy đuổi lần lượt các kênh
L1 ON 255 120
L1 OFF
L2 ON 255 120
L2 OFF
L3 ON 255 120
L3 OFF
L4 ON 255 120
L4 OFF

# Chớp nháy đồng loạt xác nhận sẵn sàng
DELAY 100
ALL ON 255 200
ALL OFF 100
ALL ON 255 200
ALL OFF 100",
                isBuiltIn: true
            ),

            new LightingPatternModel(
                id: "pattern_shutdown",
                name: "🛑 Tắt Ứng Dụng (Shutdown Wave)",
                description: "Hiệu ứng lượn sóng ngược dần từ kênh cuối về kênh 1 rồi tắt toàn bộ nguồn đèn khi đóng app",
                repeatCycles: 1,
                script:
@"# ===================================================
# Kịch bản Tạm Biệt Khi Đóng Ứng Dụng (Shutdown Wave)
# ===================================================
ALL ON 200 150
ALL OFF 100

# Sóng tắt ngược từ kênh 4 về kênh 1
L4 ON 255 100
L4 OFF
L3 ON 255 100
L3 OFF
L2 ON 255 100
L2 OFF
L1 ON 255 100
L1 OFF

ALL OFF 100",
                isBuiltIn: true
            ),

            new LightingPatternModel(
                id: "pattern_ng_alert",
                name: "🚨 Cảnh Báo Lỗi NG (NG Strobe Alert)",
                description: "Hiệu ứng chớp nháy cảnh báo liên hoàn 3 lần khi phát hiện sản phẩm kiểm tra NG",
                repeatCycles: 1,
                script:
@"# ===================================================
# Kịch bản Báo Động Khi Kiểm Tra Hàng NG (NG Alert)
# Sử dụng Macro STROBE: chớp toàn bộ 3 lần cực nhanh
# ===================================================
STROBE ALL 70 70 3 255
DELAY 100",
                isBuiltIn: true
            ),

            new LightingPatternModel(
                id: "pattern_pingpong",
                name: "🏓 Đèn Chạy Ping-Pong (Knight Rider)",
                description: "Hiệu ứng quét đèn qua lại liên tục giữa các kênh đèn",
                repeatCycles: 2,
                script:
@"# Hiệu ứng quét tới rồi quét lui (Knight Rider)
ALL OFF
L1 ON 255 80; L1 OFF
L2 ON 255 80; L2 OFF
L3 ON 255 80; L3 OFF
L4 ON 255 80; L4 OFF
L3 ON 255 80; L3 OFF
L2 ON 255 80; L2 OFF
L1 ON 255 80; L1 OFF
DELAY 100",
                isBuiltIn: true
            ),

            new LightingPatternModel(
                id: "pattern_comma_demo",
                name: "⚡ Chớp Nháy Cú Pháp Phẩy (Comma Style)",
                description: "Mẫu kịch bản sử dụng cú pháp phân tách bằng dấu phẩy truyền thống (L1, ON, 200...)",
                repeatCycles: 2,
                script:
@"# Định dạng viết bằng dấu phẩy liên tục:
L1, ON, 200, L1, OFF, 50, L2, ON, 200, L2, OFF, 50, L3, ON, 200, L3, OFF, 50, L4, ON, 200, L4, OFF, 100",
                isBuiltIn: true
            )
        };
    }
}

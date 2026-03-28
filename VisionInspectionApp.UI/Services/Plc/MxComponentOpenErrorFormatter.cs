using System;
using System.Globalization;

namespace VisionInspectionApp.UI.Services.Plc;

/// <summary>
/// Human-readable hints for ActUtlType.Open() return codes (MX Component).
/// Official manuals list many 0x0180xxxx codes; we map known ones and always show hex for support.
/// </summary>
public static class MxComponentOpenErrorFormatter
{
    public static string Format(int returnCode)
    {
        var u = unchecked((uint)returnCode);
        var hex = "0x" + u.ToString("X8", CultureInfo.InvariantCulture);

        var specific = returnCode switch
        {
            // Common documented / community-reported patterns (values may vary by MX version)
            0x01801001 => "Logical station number chưa được đăng ký trong MX Component (hoặc không khớp).",
            0x01801002 => "Số station không hợp lệ / chưa cấu hình trong tiện ích Logical station number.",
            0x01801008 => "Lỗi đường truyền / không kết nối được CPU (cáp, driver, địa chỉ IP/ port nếu Ethernet).",
            _ => null
        };

        if (specific is not null)
        {
            return $"Open() RC={returnCode} ({hex}). {specific}";
        }

        // 25198599 = 0x01808007 — frequently reported when logical station / route / CPU selection is wrong
        if (u == 0x01808007 || returnCode == 25198599)
        {
            return $"Open() RC={returnCode} ({hex}). Thường gặp khi: (1) Số Logical station trong app không trùng station đã tạo trong MX Component; " +
                   "(2) Trong MX Component chưa cấu hình đường truyền tới CPU hoặc Communication test thất bại; " +
                   "(3) Sai loại CPU / module; (4) PLC không chạy hoặc cáp/IP không ổn định.";
        }

        return
            $"Open() RC={returnCode} ({hex}). " +
            "Kiểm tra: MX Component → cấu hình Logical station number (trùng với app), Communication test tới PLC, " +
            "cáp/USB/Ethernet, driver, và ActLogicalStationNumber trước khi Open(). " +
            "Xem thêm bảng mã lỗi trong manual MX Component (Programming Manual / Error Code).";
    }
}

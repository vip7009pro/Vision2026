using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VisionInspectionApp.UI.Services;

public record DocumentItem(
    string Id,
    string Title,
    string Category,
    string Icon,
    string RelativePath,
    string Description);

public static class DocumentationService
{
    public static string ResolveDocsDirectory()
    {
        // 1. Thư mục docs trong thư mục thực thi app
        var appDocs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");
        if (Directory.Exists(appDocs)) return appDocs;

        // 2. Tìm ngược lên các thư mục cha (khi chạy từ bin/Debug hoặc bin/Release)
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "docs");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "README.md")))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return appDocs;
    }

    public static List<DocumentItem> GetAvailableDocuments()
    {
        return new List<DocumentItem>
        {
            new(
                Id: "sop_oqc",
                Title: "SOP-OQC-VIS-01: Quy Trình Kiểm Tra Lấy Mẫu OQC",
                Category: "1. Vận Hành & OQC Sampling",
                Icon: "📋",
                RelativePath: Path.Combine("sop", "SOP_OQC_SAMPLING_INSPECTION.md"),
                Description: "Quy trình thao tác chuẩn 6 bước dành cho công nhân OQC kiểm tra mẫu phôi tại bàn OQC."
            ),
            new(
                Id: "eng_ch1",
                Title: "Chương 1: Kiến Trúc Hệ Thống & Giao Diện Làm Việc",
                Category: "2. Đào Tạo Kỹ Sư Vision",
                Icon: "📘",
                RelativePath: Path.Combine("training", "01_system_architecture_and_ui.md"),
                Description: "Tổng quan nền tảng .NET 8 WPF, bố cục 4 tab, phím tắt công nghiệp và đóng gói file .job."
            ),
            new(
                Id: "eng_ch2",
                Title: "Chương 2: Thiết Lập Camera Công Nghiệp & Hiệu Chuẩn",
                Category: "2. Đào Tạo Kỹ Sư Vision",
                Icon: "📷",
                RelativePath: Path.Combine("training", "02_camera_setup_and_calibration.md"),
                Description: "Kết nối Hikrobot GigE/USB3, tối ưu Jumbo Frame, hiệu chuẩn Pixels/mm và bàn cờ Chessboard."
            ),
            new(
                Id: "eng_ch3",
                Title: "Chương 3: Lập Trình Graph & Thuật Toán Đo Kiểm",
                Category: "2. Đào Tạo Kỹ Sư Vision",
                Icon: "🧰",
                RelativePath: Path.Combine("training", "03_tool_editor_and_inspection_flow.md"),
                Description: "Dạy mẫu Origin MvpShapeMatch2, đo Caliper/CircleFinder RANSAC, tính OverSpec và AI OCR."
            ),
            new(
                Id: "eng_ch4",
                Title: "Chương 4: Tích Hợp PLC, Database, Đèn & OTA",
                Category: "2. Đào Tạo Kỹ Sư Vision",
                Icon: "🔌",
                RelativePath: Path.Combine("training", "04_integration_plc_db_lighting.md"),
                Description: "Truyền thông Mitsubishi MC/MX, kết nối CSDL, điều khiển đèn RS232/TCP và cập nhật OTA."
            ),
            new(
                Id: "eng_syllabus",
                Title: "Mục Lục & Lộ Trình Đào Tạo Kỹ Sư 4 Ngày",
                Category: "2. Đào Tạo Kỹ Sư Vision",
                Icon: "📑",
                RelativePath: Path.Combine("training", "README.md"),
                Description: "Kế hoạch và bài tập thực hành thực tế cho kỹ sư làm chủ giải pháp Vision."
            ),
            new(
                Id: "portal_readme",
                Title: "Cổng Thông Tin & Danh Mục Tài Liệu Hệ Thống",
                Category: "3. Tổng Quan & Cổng Tra Cứu",
                Icon: "🏠",
                RelativePath: "README.md",
                Description: "Tổng hợp toàn bộ tài liệu hướng dẫn và liên kết tra cứu nhanh."
            )
        };
    }

    public static string LoadDocumentMarkdown(DocumentItem item)
    {
        var docsDir = ResolveDocsDirectory();
        var fullPath = Path.Combine(docsDir, item.RelativePath);
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath, Encoding.UTF8);
        }

        return $"# Không tìm thấy tài liệu\n\nĐường dẫn tệp không tồn tại: `{fullPath}`\nVui lòng kiểm tra lại thư mục `docs/`.";
    }

    public static string ConvertMarkdownToHtml(string markdown, string docsDir, bool isDarkTheme = true)
    {
        var sb = new StringBuilder();

        // 1. CSS Theme styles
        string bg = isDarkTheme ? "#0F172A" : "#F8FAFC";
        string cardBg = isDarkTheme ? "#1E293B" : "#FFFFFF";
        string text = isDarkTheme ? "#E2E8F0" : "#1E293B";
        string textMuted = isDarkTheme ? "#94A3B8" : "#64748B";
        string border = isDarkTheme ? "#334155" : "#E2E8F0";
        string accent = "#0F5FA8";
        string codeBg = isDarkTheme ? "#0B1120" : "#F1F5F9";

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'/>");
        sb.AppendLine("<meta http-equiv='X-UA-Compatible' content='IE=edge'/>");
        sb.AppendLine("<style>");
        sb.AppendLine($"body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: {bg}; color: {text}; margin: 0; padding: 24px 36px; line-height: 1.65; font-size: 14.5px; }}");
        sb.AppendLine($"h1 {{ color: #38BDF8; border-bottom: 2px solid {accent}; padding-bottom: 8px; font-size: 26px; margin-top: 10px; }}");
        sb.AppendLine($"h2 {{ color: #60A5FA; border-bottom: 1px solid {border}; padding-bottom: 6px; font-size: 20px; margin-top: 28px; }}");
        sb.AppendLine($"h3 {{ color: #93C5FD; font-size: 16.5px; margin-top: 20px; }}");
        sb.AppendLine($"h4 {{ color: #BAE6FD; font-size: 15px; margin-top: 16px; }}");
        sb.AppendLine($"p, li {{ font-size: 14.5px; }}");
        sb.AppendLine($"hr {{ border: none; border-top: 1px solid {border}; margin: 24px 0; }}");
        sb.AppendLine($"table {{ border-collapse: collapse; width: 100%; margin: 18px 0; background: {cardBg}; border-radius: 6px; overflow: hidden; border: 1px solid {border}; }}");
        sb.AppendLine($"th {{ background: #0B467D; color: #FFFFFF; text-align: left; padding: 10px 12px; font-weight: 600; font-size: 13.5px; border: 1px solid {border}; }}");
        sb.AppendLine($"td {{ padding: 9px 12px; border: 1px solid {border}; font-size: 13.5px; }}");
        sb.AppendLine($"tr:nth-child(even) {{ background: {(isDarkTheme ? "#162032" : "#F8FAFC")}; }}");
        sb.AppendLine($"blockquote {{ border-left: 4px solid {accent}; background: {cardBg}; margin: 16px 0; padding: 12px 18px; border-radius: 0 6px 6px 0; }}");
        sb.AppendLine($"pre {{ background: {codeBg}; border: 1px solid {border}; padding: 14px 16px; border-radius: 6px; overflow-x: auto; font-family: 'Consolas', 'Courier New', monospace; font-size: 13px; color: #F8FAFC; }}");
        sb.AppendLine($"code {{ background: {codeBg}; border: 1px solid {border}; padding: 2px 6px; border-radius: 4px; font-family: 'Consolas', monospace; font-size: 13px; color: #38BDF8; }}");
        sb.AppendLine($"pre code {{ border: none; padding: 0; background: transparent; color: inherit; }}");
        sb.AppendLine(".img-card { text-align: center; margin: 24px 0; background: " + cardBg + "; padding: 12px; border-radius: 8px; border: 1px solid " + border + "; }");
        sb.AppendLine(".img-card img { max-width: 95%; height: auto; border-radius: 6px; box-shadow: 0 4px 12px rgba(0,0,0,0.3); }");
        sb.AppendLine(".img-card .caption { margin-top: 8px; font-size: 12.5px; font-style: italic; color: " + textMuted + "; }");
        sb.AppendLine(".badge-pass { background: " + (isDarkTheme ? "#064E3B" : "#DCFCE7") + "; color: " + (isDarkTheme ? "#6EE7B7" : "#15803D") + "; padding: 2px 8px; border-radius: 4px; font-weight: 700; border: 1px solid " + (isDarkTheme ? "#059669" : "#86EFAC") + "; font-size: 12px; display: inline-block; vertical-align: middle; }");
        sb.AppendLine(".badge-ng { background: " + (isDarkTheme ? "#7F1D1D" : "#FEE2E2") + "; color: " + (isDarkTheme ? "#FCA5A5" : "#B91C1C") + "; padding: 2px 8px; border-radius: 4px; font-weight: 700; border: 1px solid " + (isDarkTheme ? "#DC2626" : "#FCA5A5") + "; font-size: 12px; display: inline-block; vertical-align: middle; }");
        sb.AppendLine(".alert-note { border-left: 4px solid #38BDF8; background: " + (isDarkTheme ? "#0C2340" : "#E0F2FE") + "; padding: 10px 16px; border-radius: 0 6px 6px 0; margin: 14px 0; }");
        sb.AppendLine(".alert-tip { border-left: 4px solid #10B981; background: " + (isDarkTheme ? "#064E3B" : "#D1FAE5") + "; padding: 10px 16px; border-radius: 0 6px 6px 0; margin: 14px 0; }");
        sb.AppendLine(".math-formula-box { margin: 20px 0; padding: 14px 22px; background: " + (isDarkTheme ? "#131E31" : "#F0F9FF") + "; border: 1px solid " + (isDarkTheme ? "#233858" : "#BAE6FD") + "; border-left: 4px solid #0284C7; border-radius: 8px; text-align: center; box-shadow: 0 2px 8px rgba(0,0,0,0.12); }");
        sb.AppendLine(".math-title { display: block; font-size: 11px; font-weight: 700; color: " + (isDarkTheme ? "#38BDF8" : "#0284C7") + "; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 10px; text-align: left; }");
        sb.AppendLine(".math-equation { display: inline-block; font-family: 'Segoe UI', Tahoma, sans-serif; font-size: 16px; color: " + (isDarkTheme ? "#F8FAFC" : "#0F172A") + "; vertical-align: middle; }");
        sb.AppendLine(".math-term { font-weight: 600; vertical-align: middle; display: inline-block; margin: 0 4px; }");
        sb.AppendLine(".math-fraction { display: inline-table; vertical-align: middle; border-collapse: collapse; margin: 0 6px; }");
        sb.AppendLine(".math-numerator { border-bottom: 2px solid " + (isDarkTheme ? "#38BDF8" : "#0284C7") + "; padding: 2px 12px; font-weight: 600; color: " + (isDarkTheme ? "#38BDF8" : "#0369A1") + "; text-align: center; font-size: 14px; }");
        sb.AppendLine(".math-denominator { padding: 2px 12px; font-weight: 600; color: " + (isDarkTheme ? "#7DD3FC" : "#0284C7") + "; text-align: center; font-size: 14px; }");
        sb.AppendLine(".math-inline { font-weight: 600; color: " + (isDarkTheme ? "#38BDF8" : "#0284C7") + "; padding: 0 2px; }");
        sb.AppendLine("</style></head><body>");

        // 2. Parse lines
        var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inCodeBlock = false;
        bool inTable = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // Code blocks
            if (line.TrimStart().StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    sb.AppendLine("<pre><code>");
                }
                else
                {
                    inCodeBlock = false;
                    sb.AppendLine("</code></pre>");
                }
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine(System.Net.WebUtility.HtmlEncode(line));
                continue;
            }

            // Image tag: ![alt](path)
            var imgMatch = Regex.Match(line, @"!\[(.*?)\]\((.*?)\)");
            if (imgMatch.Success)
            {
                string alt = imgMatch.Groups[1].Value;
                string relPath = imgMatch.Groups[2].Value.Trim();
                
                // Chuẩn hóa đường dẫn ảnh tuyệt đối file:///
                string fullImgPath;
                if (relPath.StartsWith("../images/"))
                {
                    fullImgPath = Path.Combine(docsDir, "images", relPath.Replace("../images/", ""));
                }
                else if (relPath.StartsWith("images/"))
                {
                    fullImgPath = Path.Combine(docsDir, "images", relPath.Replace("images/", ""));
                }
                else
                {
                    fullImgPath = Path.Combine(docsDir, relPath);
                }

                string fileUri = new Uri(fullImgPath).AbsoluteUri;
                sb.AppendLine($"<div class='img-card'>");
                sb.AppendLine($"  <img src='{fileUri}' alt='{System.Net.WebUtility.HtmlEncode(alt)}' />");
                sb.AppendLine($"  <div class='caption'>{System.Net.WebUtility.HtmlEncode(alt)}</div>");
                sb.AppendLine($"</div>");
                continue;
            }

            // Khối công thức toán học: $$...$$
            if (line.Trim().StartsWith("$$") && line.Trim().EndsWith("$$") && line.Trim().Length > 4)
            {
                sb.AppendLine(FormatMathBlock(line.Trim()));
                continue;
            }

            // Tables
            if (line.TrimStart().StartsWith("|") && line.TrimEnd().EndsWith("|"))
            {
                if (!inTable)
                {
                    inTable = true;
                    sb.AppendLine("<table>");
                    var headerCols = line.Split('|');
                    sb.AppendLine("<thead><tr>");
                    for (int c = 1; c < headerCols.Length - 1; c++)
                    {
                        sb.AppendLine($"<th>{FormatInline(headerCols[c].Trim())}</th>");
                    }
                    sb.AppendLine("</tr></thead><tbody>");
                    continue;
                }

                // Check divider line | :--- | :--- |
                if (line.Contains("---"))
                {
                    continue;
                }

                var cols = line.Split('|');
                sb.AppendLine("<tr>");
                for (int c = 1; c < cols.Length - 1; c++)
                {
                    sb.AppendLine($"<td>{FormatInline(cols[c].Trim())}</td>");
                }
                sb.AppendLine("</tr>");
                continue;
            }
            else if (inTable)
            {
                inTable = false;
                sb.AppendLine("</tbody></table>");
            }

            // Headings
            if (line.StartsWith("# "))
            {
                sb.AppendLine($"<h1>{FormatInline(line.Substring(2).Trim())}</h1>");
            }
            else if (line.StartsWith("## "))
            {
                sb.AppendLine($"<h2>{FormatInline(line.Substring(3).Trim())}</h2>");
            }
            else if (line.StartsWith("### "))
            {
                sb.AppendLine($"<h3>{FormatInline(line.Substring(4).Trim())}</h3>");
            }
            else if (line.StartsWith("#### "))
            {
                sb.AppendLine($"<h4>{FormatInline(line.Substring(5).Trim())}</h4>");
            }
            else if (line.Trim() == "---")
            {
                sb.AppendLine("<hr/>");
            }
            else if (line.StartsWith("> [!NOTE]"))
            {
                sb.AppendLine("<div class='alert-note'><strong>ℹ️ LƯU Ý:</strong> ");
            }
            else if (line.StartsWith("> [!TIP]"))
            {
                sb.AppendLine("<div class='alert-tip'><strong>💡 MẸO KỸ THUẬT:</strong> ");
            }
            else if (line.StartsWith("> [!IMPORTANT]") || line.StartsWith("> [!WARNING]"))
            {
                sb.AppendLine("<div class='alert-note' style='border-left-color: #F59E0B;'><strong>⚠️ CHÚ Ý QUAN TRỌNG:</strong> ");
            }
            else if (line.StartsWith("> "))
            {
                sb.AppendLine($"<blockquote>{FormatInline(line.Substring(2).Trim())}</blockquote>");
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                sb.AppendLine($"<li>{FormatInline(line.Substring(2).Trim())}</li>");
            }
            else if (Regex.IsMatch(line, @"^\d+\.\s"))
            {
                var textItem = Regex.Replace(line, @"^\d+\.\s", "");
                sb.AppendLine($"<li>{FormatInline(textItem.Trim())}</li>");
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine("<p></p>");
            }
            else
            {
                sb.AppendLine($"<p>{FormatInline(line)}</p>");
            }
        }

        if (inTable)
        {
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string FormatInline(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 1. Bảo vệ inline code `...` để không bị can thiệp bởi các bước định dạng bên dưới
        var codeBlocks = new List<string>();
        text = Regex.Replace(text, @"`([^`]+)`", m =>
        {
            var code = System.Net.WebUtility.HtmlEncode(m.Groups[1].Value);
            codeBlocks.Add($"<code>{code}</code>");
            return $"__CODE_TOKEN_{codeBlocks.Count - 1}__";
        });

        // 2. Chuyển đổi biểu thức toán học / ký hiệu LaTeX inline: $...$
        text = Regex.Replace(text, @"\$([^$]+)\$", m =>
        {
            var mathContent = m.Groups[1].Value.Trim();
            // Làm sạch \text{...} trước để tránh dấu ngoặc lồng nhau { ... }
            mathContent = Regex.Replace(mathContent, @"\\text\{([^}]+)\}", "$1");

            // Nếu có phân số \frac inline
            var frac = Regex.Match(mathContent, @"^(.*?)\\frac\{(.*?)\}\{(.*?)\}(.*)$");
            if (frac.Success)
            {
                string l = CleanMathExpression(frac.Groups[1].Value);
                string n = CleanMathExpression(frac.Groups[2].Value);
                string d = CleanMathExpression(frac.Groups[3].Value);
                string r = CleanMathExpression(frac.Groups[4].Value);
                return $"{l}<table class='math-fraction' style='font-size:12.5px;'><tr><td class='math-numerator'>{n}</td></tr><tr><td class='math-denominator'>{d}</td></tr></table>{r}";
            }
            return $"<span class='math-inline'>{CleanMathExpression(mathContent)}</span>";
        });

        // 3. Bold **text**
        text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<strong>$1</strong>");

        // 4. Italic *text*
        text = Regex.Replace(text, @"\*(.*?)\*", "<em>$1</em>");

        // 5. Badges kết quả kiểm tra PASS / NG / OK
        // Sử dụng \b (Word Boundary) trong .NET Regex:
        // Do tất cả các nguyên âm có dấu tiếng Việt (Ă, Ẳ, Â, Ô, Ơ, Ư...) đều thuộc lớp Unicode \w,
        // biểu thức \bNG\b sẽ KHÔNG BAO GIỜ khớp bên trong các từ như "THẲNG", "NGANG", "NGUYÊN", "HƯỚNG", "KHÔNG", "ĐÚNG"...
        // Chỉ khớp chính xác khi đứng độc lập: "kết quả NG", "(NG)", "PASS/NG", "| NG |", "**NG**", "NG: Lỗi"...
        text = Regex.Replace(text, @"\bPASS\b", "<span class='badge-pass'>PASS</span>");
        text = Regex.Replace(text, @"\bOK\b", "<span class='badge-pass'>OK</span>");
        text = Regex.Replace(text, @"\bNG\b", "<span class='badge-ng'>NG</span>");

        // 6. Khôi phục lại các đoạn inline code nguyên bản
        for (int i = 0; i < codeBlocks.Count; i++)
        {
            text = text.Replace($"__CODE_TOKEN_{i}__", codeBlocks[i]);
        }

        return text;
    }

    private static string FormatMathBlock(string line)
    {
        string expr = line.Trim();
        if (expr.StartsWith("$$") && expr.EndsWith("$$") && expr.Length >= 4)
        {
            expr = expr.Substring(2, expr.Length - 4).Trim();
        }

        // Bước 1: Làm sạch \text{...} trước để triệt tiêu dấu ngoặc lồng nhau { ... }
        expr = Regex.Replace(expr, @"\\text\{([^}]+)\}", "$1");

        // Bước 2: Kiểm tra xem có chứa phân số \frac{...}{...} không
        var fracMatch = Regex.Match(expr, @"^(.*?)\\frac\{(.*?)\}\{(.*?)\}(.*)$");
        if (fracMatch.Success)
        {
            string left = CleanMathExpression(fracMatch.Groups[1].Value.Trim());
            string num = CleanMathExpression(fracMatch.Groups[2].Value.Trim());
            string den = CleanMathExpression(fracMatch.Groups[3].Value.Trim());
            string right = CleanMathExpression(fracMatch.Groups[4].Value.Trim());

            var sb = new StringBuilder();
            sb.AppendLine("<div class='math-formula-box'>");
            sb.AppendLine("  <span class='math-title'>📐 CÔNG THỨC TOÁN HỌC & TỶ LỆ QUY ĐỔI:</span>");
            sb.AppendLine("  <div class='math-equation'>");
            if (!string.IsNullOrWhiteSpace(left))
            {
                sb.AppendLine($"    <span class='math-term'>{left}</span>");
            }
            sb.AppendLine("    <table class='math-fraction'>");
            sb.AppendLine($"      <tr><td class='math-numerator'>{num}</td></tr>");
            sb.AppendLine($"      <tr><td class='math-denominator'>{den}</td></tr>");
            sb.AppendLine("    </table>");
            if (!string.IsNullOrWhiteSpace(right))
            {
                sb.AppendLine($"    <span class='math-term'>{right}</span>");
            }
            sb.AppendLine("  </div>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        // Trường hợp công thức thông thường không có phân số
        string cleaned = CleanMathExpression(expr);
        return $"<div class='math-formula-box'><span class='math-title'>📐 CÔNG THỨC TOÁN HỌC:</span><div class='math-equation'><span class='math-term'>{cleaned}</span></div></div>";
    }

    private static string CleanMathExpression(string expr)
    {
        if (string.IsNullOrEmpty(expr)) return string.Empty;

        // Trích xuất nội dung từ \text{...}
        expr = Regex.Replace(expr, @"\\text\{([^}]+)\}", "$1");

        // Thay thế các ký hiệu LaTeX phổ biến sang ký tự Unicode đẹp và rõ nghĩa
        expr = Regex.Replace(expr, @"\\Delta\s*", "Δ");
        expr = Regex.Replace(expr, @"\\theta\s*", "θ");
        expr = expr.Replace(@"\rightarrow", "➔")
                   .Replace(@"\leftrightarrow", "↔")
                   .Replace(@"\ge", "≥")
                   .Replace(@"\le", "≤")
                   .Replace(@"\approx", "≈")
                   .Replace(@"\pm", "±")
                   .Replace(@"^\circ", "°")
                   .Replace(@"\circ", "°")
                   .Replace(@"\times", "×")
                   .Replace(@"\dots", "…")
                   .Replace(@"\cdot", "·");

        // Loại bỏ phân số còn sót nếu có dạng \frac{a}{b} -> (a / b)
        expr = Regex.Replace(expr, @"\\frac\{([^}]+)\}\{([^}]+)\}", "($1 / $2)");

        // Chuẩn hóa khoảng trắng kép
        expr = Regex.Replace(expr, @"\s{2,}", " ");

        return expr.Trim();
    }
}

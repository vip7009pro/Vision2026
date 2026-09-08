using System;
using System.IO;
using VisionInspectionApp.UI.Services;

namespace TestExtractApp;

public static class DocumentationSystemTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🧪 RUNNING TESTS: DOCUMENTATION & SOP SYSTEM TESTS");
        Console.WriteLine("=======================================================");

        TestResolveDocsDirectoryAndAvailableDocuments();
        TestMarkdownToHtmlConversionAndImageLinks();
        TestBadgeWordBoundaryFormatting();
        TestMathFormulaAndEquationFormatting();
        TestImagesExistAndAreNonEmpty();
        TestOqcSopContentCompleteness();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL DOCUMENTATION & SOP SYSTEM TESTS PASSED (100%)!");
        Console.WriteLine("=======================================================\n");
    }

    private static void TestResolveDocsDirectoryAndAvailableDocuments()
    {
        Console.WriteLine("--- Test 1: Kiểm Tra Đường Dẫn Thư Mục Docs & Danh Mục Tài Liệu ---");
        var docsDir = DocumentationService.ResolveDocsDirectory();
        if (!Directory.Exists(docsDir))
        {
            throw new Exception($"Docs directory not found at: {docsDir}");
        }

        var docs = DocumentationService.GetAvailableDocuments();
        if (docs.Count < 5)
        {
            throw new Exception($"Expected at least 5 registered documents, but found: {docs.Count}");
        }

        foreach (var doc in docs)
        {
            var content = DocumentationService.LoadDocumentMarkdown(doc);
            if (string.IsNullOrWhiteSpace(content) || content.StartsWith("# Không tìm thấy tài liệu"))
            {
                throw new Exception($"Failed to load document '{doc.Id}' from path: {doc.RelativePath}");
            }
            if (content.Length < 100)
            {
                throw new Exception($"Document '{doc.Id}' content is too short ({content.Length} chars)!");
            }

            // Kiểm tra render HTML: không được có thẻ code block unclosed
            var html = DocumentationService.ConvertMarkdownToHtml(content, docsDir, isDarkTheme: true);
            if (html.Contains("<pre><code>") && !html.Contains("</code></pre>"))
            {
                throw new Exception($"LỖI: Tài liệu '{doc.Id}' bị lỗi thẻ code block unclosed!");
            }

            // Kiểm tra đặc thù tài liệu Phần 3: Tool Editor & Inspection Flow
            if (doc.Id.Contains("03") || doc.RelativePath.Contains("03_tool"))
            {
                if (!html.Contains("<h3>2.1. So sánh 3 thuật toán Origin cốt lõi</h3>"))
                {
                    throw new Exception("LỖI: Tài liệu 03_tool_editor_and_inspection_flow mục 2.1 không render được tiêu đề h3!");
                }
                if (!html.Contains("MvpShapeMatch2") || !html.Contains("<table"))
                {
                    throw new Exception("LỖI: Tài liệu 03_tool_editor_and_inspection_flow không render được bảng so sánh thuật toán!");
                }
            }
        }

        Console.WriteLine($"  -> PASSED: Định vị docs tại '{docsDir}', nạp và render thành công 100% toàn bộ {docs.Count} tài liệu chuẩn.");
    }

    private static void TestMarkdownToHtmlConversionAndImageLinks()
    {
        Console.WriteLine("--- Test 2: Chuyển Đổi Markdown Sang HTML & Nhúng Ảnh Thẻ Local ---");
        var docsDir = DocumentationService.ResolveDocsDirectory();
        string sampleMd = "# Test Document\n\n| Item | Value |\n| :--- | :--- |\n| Status | PASS |\n| Error | NG |\n\n![Station Setup](../images/oqc_station_setup.jpg)\n\n> [!NOTE]\n> Sample alert text";

        string htmlDark = DocumentationService.ConvertMarkdownToHtml(sampleMd, docsDir, isDarkTheme: true);
        string htmlLight = DocumentationService.ConvertMarkdownToHtml(sampleMd, docsDir, isDarkTheme: false);

        if (!htmlDark.Contains("<meta http-equiv='X-UA-Compatible' content='IE=edge'/>"))
        {
            throw new Exception("HTML missing IE=edge compatibility meta tag!");
        }

        if (!htmlDark.Contains("<table") || !htmlDark.Contains("badge-pass") || !htmlDark.Contains("badge-ng"))
        {
            throw new Exception("HTML table or status badges not rendered properly!");
        }

        if (!htmlDark.Contains("class='img-card'") || !htmlDark.Contains("oqc_station_setup.jpg"))
        {
            throw new Exception("HTML image card not rendered properly!");
        }

        if (!htmlDark.Contains("#0F172A") || !htmlLight.Contains("#F8FAFC"))
        {
            throw new Exception("Dark / Light themes CSS missing expected background colors!");
        }

        Console.WriteLine("  -> PASSED: Renderer HTML hỗ trợ đầy đủ Table, Badges, Alert Notes, Local Images và Theme.");
    }

    private static void TestBadgeWordBoundaryFormatting()
    {
        Console.WriteLine("--- Test 2.1: Kiểm Tra Định Dạng Badge NG/PASS/OK (Word Boundary & Tiếng Việt) ---");
        var docsDir = DocumentationService.ResolveDocsDirectory();

        string testMarkdown = @"
# 1. HƯỚNG DẪN CĂN CHỈNH KÍCH THƯỚC THẲNG HÀNG VÀ ĐƯỜNG KÍNH
- Kiểm tra tính NGUYÊN BẢN và độ THẲNG của sản phẩm trong môi trường CÔNG NGHIỆP.
- Đảm bảo KHÔNG có sai số vượt ngưỡng và giá trị đo ĐÚNG quy chuẩn.
- Đừng BYPASS quy trình kiểm tra hoặc nhập sai PASSWORD.
- Thử nghiệm với mã lệnh `bool config.IsNg = false;` và `status == ""NG""`.
- Kết quả thực tế: mẫu đạt PASS, mẫu lỗi NG, hoặc PASS/NG kết hợp.
- Chi tiết: mẫu (NG) đưa vào khay màu đỏ, mẫu Đạt (OK) đưa vào khay màu xanh.
- Bảng kết quả:
| Mục | Trạng thái |
| Đường thẳng | PASS |
| Độ nghiêng | NG |
";

        string html = DocumentationService.ConvertMarkdownToHtml(testMarkdown, docsDir, isDarkTheme: true);

        // 1. Phải giữ nguyên các từ tiếng Việt hoa, KHÔNG ĐƯỢC chứa thẻ span bên trong từ
        string[] forbiddenSubstrings = new[]
        {
            "THẲ<span",
            "HƯỚ<span",
            "ĐƯỜ<span",
            "NG<span",
            "CÔ<span",
            "KHÔ<span",
            "ĐÚ<span",
            "BY<span",
            "span class='badge-pass'>PASS</span>WORD",
            "<code>bool config.Is<span",
            "<code>status == \"<span"
        };

        foreach (var forbidden in forbiddenSubstrings)
        {
            if (html.Contains(forbidden))
            {
                throw new Exception($"LỖI: Phát hiện từ bị format sai huy hiệu badge: '{forbidden}' trong HTML rendered!");
            }
        }

        // 2. Các từ tiếng Việt phải còn nguyên vẹn trong HTML
        string[] requiredIntactWords = new[]
        {
            "THẲNG HÀNG",
            "NGUYÊN BẢN",
            "HƯỚNG DẪN",
            "ĐƯỜNG KÍNH",
            "CÔNG NGHIỆP",
            "KHÔNG",
            "ĐÚNG",
            "BYPASS",
            "PASSWORD"
        };

        foreach (var word in requiredIntactWords)
        {
            if (!html.Contains(word))
            {
                throw new Exception($"LỖI: Từ tiếng Việt '{word}' bị cắt xén hoặc biến dạng trong HTML!");
            }
        }

        // 3. Phải format đúng huy hiệu badge cho các vị trí kết quả độc lập
        if (!html.Contains("<span class='badge-pass'>PASS</span>"))
        {
            throw new Exception("LỖI: Không tìm thấy huy hiệu badge PASS hợp lệ!");
        }

        if (!html.Contains("<span class='badge-ng'>NG</span>"))
        {
            throw new Exception("LỖI: Không tìm thấy huy hiệu badge NG hợp lệ!");
        }

        if (!html.Contains("<span class='badge-pass'>OK</span>"))
        {
            throw new Exception("LỖI: Không tìm thấy huy hiệu badge OK hợp lệ!");
        }

        if (!html.Contains("(<span class='badge-ng'>NG</span>)"))
        {
            throw new Exception("LỖI: Huy hiệu '(NG)' không được định dạng chính xác!");
        }

        if (!html.Contains("<span class='badge-pass'>PASS</span>/<span class='badge-ng'>NG</span>"))
        {
            throw new Exception("LỖI: Huy hiệu ghép 'PASS/NG' không được định dạng chính xác!");
        }

        // 4. Code block phải giữ nguyên vẹn
        if (!html.Contains("<code>bool config.IsNg = false;</code>"))
        {
            throw new Exception("LỖI: Inline code `bool config.IsNg = false;` bị biến dạng!");
        }

        Console.WriteLine("  -> PASSED: Từ tiếng Việt (THẲNG, NGUYÊN, HƯỚNG, ĐƯỜNG, KHÔNG, ĐÚNG...) và code an toàn 100%, chỉ format NG/PASS/OK độc lập!");
    }

    private static void TestMathFormulaAndEquationFormatting()
    {
        Console.WriteLine("--- Test 2.2: Kiểm Tra Định Dạng Khối Công Thức Toán & Ký Hiệu LaTeX ($$...$$ & $...$) ---");
        var docsDir = DocumentationService.ResolveDocsDirectory();

        string sampleMathMd = @"
# HIỆU CHUẨN TỶ LỆ QUANG HỌC

Công thức quy đổi hệ số:
$$\text{PixelsPerMm} = \frac{\text{Khoảng cách tính bằng Pixel}}{\text{Khoảng cách thực tế tính bằng mm}}$$

Ví dụ tính toán thực tế:
$$\text{PixelsPerMm} = \frac{542.40}{10.00} = 54.24\text{ px/mm}$$

Một số ký hiệu kỹ thuật inline:
- Kích thước đo: $10.00\text{ mm}$ và sai số $\pm 0.05\text{ mm}$.
- Trình tự thao tác: Menu **Tệp** $\rightarrow$ **Thoát**.
- Điều kiện kiểm tra: $\ge 3$ ảnh mẫu và Gain $\le 4\text{ dB}$.
- Biến thiên tọa độ: $\Delta X = 0.5\text{ mm}$ và góc $\pm 45^\circ$.
";

        string html = DocumentationService.ConvertMarkdownToHtml(sampleMathMd, docsDir, isDarkTheme: true);

        // 1. Phải có Math Formula Box và phân số HTML
        if (!html.Contains("class='math-formula-box'"))
        {
            throw new Exception("LỖI: Không tìm thấy thẻ 'math-formula-box' cho khối công thức $$...$$!");
        }

        if (!html.Contains("class='math-fraction'"))
        {
            throw new Exception("LỖI: Không tìm thấy thẻ phân số 'math-fraction'!");
        }

        if (!html.Contains("class='math-numerator'") || !html.Contains("class='math-denominator'"))
        {
            throw new Exception("LỖI: Thiếu tử số 'math-numerator' hoặc mẫu số 'math-denominator'!");
        }

        // 2. Nội dung công thức phải sạch sẽ, không còn cú pháp LaTeX thô
        if (html.Contains(@"$$\text{PixelsPerMm}") || html.Contains(@"\frac{") || html.Contains(@"\text{"))
        {
            throw new Exception("LỖI: HTML vẫn còn chứa chuỗi LaTeX thô chưa được format!");
        }

        if (!html.Contains("Khoảng cách tính bằng Pixel") || !html.Contains("Khoảng cách thực tế tính bằng mm"))
        {
            throw new Exception("LỖI: Tử số và mẫu số tiếng Việt không hiển thị chuẩn xác!");
        }

        if (!html.Contains("542.40") || !html.Contains("10.00") || !html.Contains("= 54.24 px/mm"))
        {
            throw new Exception("LỖI: Ví dụ số học tính toán tỷ lệ không hiển thị đầy đủ!");
        }

        // 3. Inline math phải được chuyển đổi ký hiệu Unicode trực quan
        if (!html.Contains("10.00 mm") || (!html.Contains("± 0.05 mm") && !html.Contains("±0.05 mm")))
        {
            throw new Exception("LỖI: Ký hiệu inline kích thước và dung sai không được định dạng sạch!");
        }

        if (!html.Contains("➔") && !html.Contains("→"))
        {
            throw new Exception("LỖI: Mũi tên LaTeX $\\rightarrow$ không được chuyển thành ký hiệu mũi tên!");
        }

        if (!html.Contains("≥ 3") || !html.Contains("≤ 4 dB"))
        {
            throw new Exception("LỖI: Ký hiệu so sánh $\\ge, \\le$ không được chuyển đổi sang Unicode!");
        }

        if ((!html.Contains("ΔX") && !html.Contains("Δ X")) || !html.Contains("45°"))
        {
            throw new Exception("LỖI: Ký hiệu Delta và độ góc không được chuyển đổi sang Unicode!");
        }

        Console.WriteLine("  -> PASSED: Khối công thức phân số và ký hiệu LaTeX inline hiển thị tuyệt đẹp, sạch sẽ 100%!");
    }

    private static void TestImagesExistAndAreNonEmpty()
    {
        Console.WriteLine("--- Test 3: Kiểm Tra Tính Toàn Vẹn & Kích Thước Các Tệp Ảnh Minh Họa ---");
        var docsDir = DocumentationService.ResolveDocsDirectory();
        var imagesDir = Path.Combine(docsDir, "images");

        string[] requiredImages =
        {
            "oqc_station_setup.jpg",
            "oqc_pass_vs_ng_screen.jpg",
            "vision_tool_graph_flow.jpg",
            "optical_calibration_target.jpg"
        };

        foreach (var img in requiredImages)
        {
            var fullPath = Path.Combine(imagesDir, img);
            if (!File.Exists(fullPath))
            {
                throw new Exception($"Missing illustration image: {fullPath}");
            }

            var fi = new FileInfo(fullPath);
            if (fi.Length < 50000) // At least 50KB for high-res photo
            {
                throw new Exception($"Image '{img}' is suspiciously small ({fi.Length} bytes)!");
            }
        }

        Console.WriteLine("  -> PASSED: Toàn bộ 4 ảnh minh họa chất lượng cao tồn tại với độ phân giải chuẩn.");
    }

    private static void TestOqcSopContentCompleteness()
    {
        Console.WriteLine("--- Test 4: Kiểm Tra Nội Dung Bản SOP Thao Tác Chuẩn OQC ---");
        var docsDir = DocumentationService.ResolveDocsDirectory();
        var sopPath = Path.Combine(docsDir, "sop", "SOP_OQC_SAMPLING_INSPECTION.md");

        if (!File.Exists(sopPath))
        {
            throw new Exception($"SOP file not found at: {sopPath}");
        }

        string sopContent = File.ReadAllText(sopPath);

        // Kiểm tra các từ khóa cốt lõi của quy trình
        string[] requiredKeywords =
        {
            "SOP-OQC-VIS-01",
            "Bước 1: Chuẩn bị",
            "Bước 2: Khởi động",
            "Bước 3: Quét mã vạch",
            "Bước 4: Đặt mẫu sản phẩm vào đồ gá",
            "Bước 5: Kích hoạt kiểm tra",
            "Bước 6: Đọc kết quả",
            "PASS",
            "NG",
            "Over Spec",
            "KHAY HÀNG ĐẠT",
            "KHAY HÀNG LỖI",
            "DỪNG NGAY LẬP TỨC VIỆC KIỂM TRA"
        };

        foreach (var kw in requiredKeywords)
        {
            if (!sopContent.Contains(kw))
            {
                throw new Exception($"SOP content missing critical section keyword: '{kw}'");
            }
        }

        Console.WriteLine("  -> PASSED: Bản SOP đầy đủ 6 bước chuẩn, cảnh báo an toàn chất lượng và xử lý khay PASS/NG.");
    }
}

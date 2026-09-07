$path = "g:\NODEJS\Vision2026\CONTEXT.md"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)

$task312Lines = @"
- **Cải Tiến Robustness & Khắc Phục Lỗi Nhận Diện Chữ Trên Nền Tối Cho Tool OCR (Task 312)**:
  - **Hiện Tượng & Yêu Cầu Người Dùng**:
    - Người dùng thử nghiệm Tool OCR trên ảnh thực tế (giao diện UI dark theme nền xám tối, chữ 'Line Mode' màu xám sáng), cấu hình AnyText + Đảo màu, Search ROI khoanh chữ 'Line Mode' nhưng khi chạy báo lỗi "Không đọc được ký tự" (Found = False, ROI đỏ NG).
  - **Phân Tích Kỹ Thuật & Nguyên Nhân Gốc Rễ**:
    1. *Lỗi chọn nhầm Đảo màu trên nền tối*: Ảnh gốc vốn là chữ sáng trên nền tối (chữ trắng/xám chiếm 5-25% diện tích). Khi người dùng tích "Đảo màu", ảnh bị đảo thành chữ đen trên nền trắng. Thuật toán Connected Components tìm blob trắng (255) nên chỉ thấy toàn bộ nền trắng (bị loại bỏ vì quá to) và bỏ qua chữ đen.
    2. *Thiếu chữ thường trong Font Library và CharWhitelist*: Chữ 'Line Mode' chứa các chữ thường 'i, n, e, o, d'. Thư viện Hershey và Whitelist trước đó chỉ có A-Z, 0-9 nên các ký tự thường bị loại bỏ hoàn toàn hoặc nhận diện sai thành dấu chấm do tương quan khối đặc.
    3. *Mép dưới Search ROI chạm đường kẻ ngang viền bảng*: Thanh viền phân cách bảng tạo ra một blob ngang dài, dính vào chân chữ.
    4. *Chữ cái dính nét khi nhị phân hóa*: Khoảng cách hẹp giữa các chữ cái trong từ 'Line' hoặc 'Mode' khiến chúng dính thành cụm blob lớn.
  - **Giải Pháp Kỹ Thuật Đã Triển Khai**:
    1. *Cơ chế Auto-Polarity thông minh (NormalizeTextPolarity)*:
       - Tự động kiểm tra tỷ lệ pixel trắng (whiteRatio) và tỷ lệ pixel trắng ở 4 đường viền (borderWhiteRatio). Nếu phát hiện nền là màu trắng (>50%), hệ thống tự động đảo ngược về chuẩn chữ trắng trên nền đen. Dù người dùng có tích nhầm hay không tích "Đảo màu", hệ thống luôn đảm bảo đầu ra chuẩn xác.
    2. *Lọc bỏ đường kẻ bảng & viền ngăn cách (ExtractCandidateBoxes)*:
       - Thêm điều kiện lọc các blob ngang dài (width >= 75% ROI, height <= 25% ROI) hoặc dọc dài, loại bỏ triệt để đường phân cách hàng/cột của bảng.
    3. *Bổ sung đầy đủ bộ ký tự in thường a-z và đa nét (InitializeIndustrialFontLibrary)*:
       - Mở rộng supportedChars và default CharWhitelist với toàn bộ alphabet thường a-z.
       - Bổ sung các biến thể nét chữ mỏng/dày (thicknesses = { 1, 2, 3 }) để tối ưu hóa nhận diện cả chữ nhỏ và nét mảnh.
    4. *Thuật toán tách chữ dính nét (SplitConnectedGlyphs & Vertical Projection Profile)*:
       - Tự động phát hiện các blob có chiều rộng bất thường (W > 1.25 * H), tính hình chiếu dọc (vertical projection), tìm các thung lũng (valleys/minima) và phân tách thành các ký tự đơn lẻ.
    5. *Ràng buộc hình học (Geometric Priors)*:
       - Dấu chấm '.' chỉ được match khi kích thước nhỏ (W, H <= 45% avgHeight). Chữ cái thông thường không bao giờ bị nhận diện nhầm thành dấu chấm.
    6. *Bổ sung bài kiểm thử tự động TestUserCaseLineModeDarkBackground*:
       - Kiểm tra trực tiếp với trường hợp 'Line Mode' trên nền tối cả khi InvertImage=true và InvertImage=false, 100% PASSED.
  - **Kiểm Thử**:
    - dotnet build VisionInspectionApp.slnx: 0 errors.
    - dotnet run --project TestExtractApp: 100% PASSED toàn bộ test suite.
"@ -split "`r?`n"

$newLines = [System.Collections.Generic.List[string]]::new()
$inserted = $false

foreach ($line in $lines) {
    if (-not $inserted -and $line -match "Task 311") {
        $newLines.AddRange($task312Lines)
        $newLines.Add("")
        $inserted = $true
    }
    $newLines.Add($line)
}

[System.IO.File]::WriteAllLines($path, $newLines, [System.Text.Encoding]::UTF8)
Write-Host "CONTEXT.md successfully updated before Task 311! Inserted = $inserted"

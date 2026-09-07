const fs = require('fs');

const contextPath = 'g:/NODEJS/Vision2026/CONTEXT.md';
const roadmapPath = 'g:/NODEJS/Vision2026/ROADMAP.md';

// 1. Update CONTEXT.md
let contextContent = fs.readFileSync(contextPath, 'utf8');

const task321Context = `- **Bổ Sung Chức Năng Cập Nhật Phần Mềm Từ Xa OTA (Over-The-Air Update) Cho Ứng Dụng (Task 321)**:
  - **Hiện Tượng & Yêu Cầu Người Dùng**:
    - Nhu cầu cập nhật phần mềm từ xa cho các máy tính công nghiệp IPC lắp đặt tại các dây chuyền nhà máy qua mạng LAN nội bộ hoặc GitHub Releases/Internet mà không cần kỹ sư phải cắm USB cập nhật thủ công từng máy.
    - Yêu cầu hỗ trợ cả 2 phương thức kết nối: Máy chủ nội bộ LAN (Custom Manifest \`version.json\`) và GitHub Releases API; đảm bảo an toàn tuyệt đối, xác thực tính toàn vẹn gói cập nhật, tự động sao lưu và khôi phục (Rollback) khi gặp sự cố.
  - **Giải Pháp Kỹ Thuật Đã Triển Khai**:
    1. *Tầng Dữ Liệu & Cấu Hình (Models & Settings)*:
       - Tạo \`UpdateManifest.cs\` trong \`VisionInspectionApp.Models/Ota/\`: Định nghĩa \`UpdateManifest\` (Version, ReleaseDate, Channel, IsMandatory, DownloadUrl, FileSize, Sha256, ReleaseNotes), \`UpdateCheckResult\` và \`UpdateProgressInfo\`.
       - Thêm \`OtaSettings\` vào \`GlobalAppSettings\`: Cấu hình \`AutoCheckOnStartup\`, \`UpdateSourceType\` ("Auto", "CustomManifest", "GitHub"), \`UpdateServerUrl\`, \`UpdateChannel\`, \`IgnoredVersion\`.
    2. *Tầng Dịch Vụ Ứng Dụng (Application Services)*:
       - \`IOtaUpdateService\` & \`OtaUpdateService\`:
         - Tự động nhận diện và đọc thông tin bản mới từ máy chủ HTTP nội bộ hoặc GitHub Releases API (\`tag_name\`, assets \`.zip\`).
         - So sánh phiên bản (\`Version.TryParse\`) giữa phiên bản hiện tại và phiên bản máy chủ.
         - Tải gói cập nhật dạng Stream theo chunks 80KB, báo cáo tiến trình % thời gian thực và tốc độ tải (MB/s).
         - Xác thực mã băm SHA-256 (\`VerifyPackageChecksum\`) bảo vệ an toàn gói cài đặt.
         - Khởi chạy trình cập nhật độc lập \`VisionUpdater.exe\` và đóng ứng dụng an toàn để giải phóng file locks.
    3. *Trình Cập Nhật Độc Lập (\`VisionInspectionApp.Updater\` -> \`VisionUpdater.exe\`)*:
       - Ứng dụng .NET 8 WPF độc lập, nhẹ, nhận tham số: \`--pid <id> --package <zip> --target <app_dir> --restart <exe>\`.
       - Chờ ứng dụng chính kết thúc để nhả file locks.
       - Tự động sao lưu toàn bộ các file hiện có sang thư mục \`backup/backup_{timestamp}/\`.
       - Giải nén ghi đè gói cập nhật mới vào thư mục cài đặt, có cơ chế chống Zip Slip vulnerability.
       - Nếu xảy ra lỗi giữa chừng (mất điện, lỗi ghi file): Tự động Rollback phục hồi từ bản sao lưu và khởi động lại bản cũ an toàn.
       - Khởi động lại ứng dụng chính kèm tham số \`--updated\`.
    4. *Giao Diện Người Dùng (UI & ViewModels)*:
       - Cửa sổ \`OtaUpdateDialog.xaml\` & \`OtaUpdateViewModel.cs\`:
         - Thẻ "Kiểm Tra & Cập Nhật": Thẻ so sánh phiên bản (Current vs Latest), khung xem Release Notes / Changelog, thanh tiến trình tải thời gian thực, các nút kiểm tra ngay, tải & cập nhật, hủy tải, cài đặt & khởi động lại, bỏ qua phiên bản này.
         - Thẻ "Cài Đặt Máy Chủ OTA": Cho phép chuyển đổi linh hoạt giữa Tự động / Local Server / GitHub Releases, nhập URL máy chủ và bật/tắt tự động kiểm tra khi mở app.
       - Tích hợp vào \`MainWindow.xaml\`:
         - Menu \`❓ Trợ Giúp\` -> Thêm mục \`🔄 Kiểm Tra Bản Cập Nhật (OTA Update)...\`.
         - TitleBar Col 3: Huy hiệu / nút nhấp nháy \`🚀 Có Bản Mới: vX.X.X\` khi phát hiện có bản cập nhật mới từ xa trong chế độ nền.
  - **Kiểm Thử & Xác Minh**:
    - Tạo bộ kiểm thử tự động \`TestExtractApp/OtaUpdateServiceTests.cs\` với 5 test suite toàn diện:
      1. \`TestCustomManifestParsingAndVersionComparison\`: PASSED.
      2. \`TestGitHubReleasesApiParsing\`: PASSED.
      3. \`TestSha256ChecksumVerification\`: PASSED.
      4. \`TestDownloadUpdatePackageWithProgress\`: PASSED.
      5. \`TestZipExtractionAndBackupLogic\`: PASSED.
    - Biên dịch Solution \`VisionInspectionApp.slnx\`: 0 Error(s).
    - Toàn bộ test suite chạy lệnh \`dotnet run --project TestExtractApp\`: 100% PASSED.
`;

const anchor = '- **Bổ Sung Khối Hiển Thị Tên Sản Phẩm';
if (contextContent.includes(anchor)) {
    contextContent = contextContent.replace(anchor, task321Context + '\n' + anchor);
    fs.writeFileSync(contextPath, contextContent, 'utf8');
    console.log('✅ Updated CONTEXT.md with Task 321!');
} else {
    console.error('❌ Could not find anchor in CONTEXT.md');
}

// 2. Update ROADMAP.md
let roadmapContent = fs.readFileSync(roadmapPath, 'utf8');

const task321Roadmap = `    - [x] **Task 321: Bổ Sung Chức Năng Cập Nhật Phần Mềm Từ Xa OTA (Over-The-Air Update) Cho Ứng Dụng**:
        - Hỗ trợ cập nhật từ cả 2 nguồn: Máy chủ nội bộ LAN (Custom Manifest \`version.json\`) và GitHub Releases API.
        - Xây dựng dịch vụ \`IOtaUpdateService\` & \`OtaUpdateService\` tải gói Stream theo tiến trình và xác thực mã băm SHA-256 Checksum.
        - Phát triển công cụ cập nhật độc lập \`VisionUpdater.exe\` (\`VisionInspectionApp.Updater\`) giải quyết triệt để Windows File Lock, tự động sao lưu (Backup) và tự phục hồi (Rollback) an toàn khi gặp sự cố.
        - Xây dựng giao diện \`OtaUpdateDialog.xaml\` hiển thị so sánh phiên bản, Changelog, thanh tiến trình và tích hợp thông báo nhấp nháy trên TitleBar và Menu Trợ Giúp.
        - Đạt 100% PASSED 5 bài kiểm thử tự động trong \`OtaUpdateServiceTests\`.
`;

const roadmapAnchor = '    - [x] **Task 320:';
if (roadmapContent.includes(roadmapAnchor)) {
    const searchFrom = roadmapContent.indexOf(roadmapAnchor);
    const endMarker = 'TestOqcProductNameAndLayout204040Configuration`.';
    let endPos = roadmapContent.indexOf(endMarker, searchFrom);
    if (endPos === -1) {
        endPos = roadmapContent.indexOf('`TestOqcProductNameAndLayout', searchFrom);
    }
    if (endPos !== -1) {
        const nextLinePos = roadmapContent.indexOf('\n', endPos);
        const insertPos = nextLinePos !== -1 ? nextLinePos + 1 : endPos;
        roadmapContent = roadmapContent.slice(0, insertPos) + '\n' + task321Roadmap + roadmapContent.slice(insertPos);
        fs.writeFileSync(roadmapPath, roadmapContent, 'utf8');
        console.log('✅ Updated ROADMAP.md with Task 321!');
    } else {
        roadmapContent = roadmapContent.replace(roadmapAnchor, task321Roadmap + '\n' + roadmapAnchor);
        fs.writeFileSync(roadmapPath, roadmapContent, 'utf8');
        console.log('✅ Updated ROADMAP.md with Task 321 (fallback)!');
    }
} else {
    console.error('❌ Could not find anchor in ROADMAP.md');
}

const fs = require('fs');
const path = require('path');

const contextPath = path.resolve('CONTEXT.md');
const content = fs.readFileSync(contextPath, 'utf8');

const marker = '- **Khắc Phục Lỗi Cài Đặt Gói Cập Nhật OTA Tại Máy Vision Client';
const task324 = `- **Khắc Phục Lỗi Ứng Dụng Không Tự Động Khởi Động Lại Sau Khi Cập Nhật Thành Công (Task 324)**:
  - **Nguyên nhân gốc rễ**: Khi truyền tham số qua CLI từ \`OtaUpdateService.cs\`, chuỗi \`--target "{appDir}"\` chứa dấu gạch chéo ngược ở cuối của \`AppDomain.CurrentDomain.BaseDirectory\` (ví dụ \`C:\\App\\\`). Windows CLI parser coi \`\\"\` là escape cho dấu ngoặc kép, khiến giá trị của \`--target\` nuốt chửng toàn bộ chuỗi \`--restart "C:\\App\\VisionInspectionApp.UI.exe"\`. Kết quả là biến \`_restartExePath\` trong \`VisionUpdater\` hoàn toàn rỗng, và updater giải nén thành công xong thì tự thoát mà không bật lại app chính.
  - **Giải pháp đã triển khai**:
    1. Chuẩn hóa đường dẫn trong \`OtaUpdateService.cs\`: Dùng \`TrimEnd('\\\\', '/')\` cho \`safeAppDir\`, \`safeZipPath\`, \`safeExePath\` để triệt tiêu hoàn toàn trailing slash trước dấu ngoặc kép; thiết lập \`WorkingDirectory = safeAppDir\`.
    2. Trong \`VisionInspectionApp.Updater/UpdaterWindow.xaml.cs\`:
       - \`ParseCommandLineArgs\`: Bổ sung cơ chế phòng vệ kép, tự động nhận diện và bóc tách chuỗi \`--restart\` nếu bị Windows gộp vào \`--target\`.
       - \`ResolveRestartExecutablePath\`: Cơ chế tự động dò tìm \`VisionInspectionApp.UI.exe\` hoặc các file thực thi trong thư mục cài đặt khi tham số \`--restart\` bị thiếu hoặc sai đường dẫn.
       - \`RestartMainApplication\`: Thiết lập \`WorkingDirectory\` chuẩn xác cho tiến trình mới để nạp đúng cấu hình tương đối; bổ sung cơ chế fallback \`UseShellExecute = false\` nếu quyền hạn Windows bị hạn chế; thêm độ trễ đệm trước khi thoát để tiến trình chính kịp nạp vào RAM; hiển thị hộp thoại nhắc nhở nếu việc tự động khởi chạy bị chặn.
  - **Kiểm thử**: Đạt 100% PASSED toàn bộ bộ kiểm thử TestExtractApp và biên dịch Release 0 lỗi.

`;

if (content.includes(marker)) {
    fs.writeFileSync(contextPath, content.replace(marker, task324 + marker), 'utf8');
    console.log('Successfully updated CONTEXT.md with Task 324');
} else {
    console.log('Marker not found in CONTEXT.md');
}

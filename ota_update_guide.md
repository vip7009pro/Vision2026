# Hướng Dẫn Phát Hành Bản Cập Nhật OTA Cho CMS VINA Vision System

Hệ thống hỗ trợ 2 phương thức phát hành bản cập nhật OTA:

---

## Cách 1: Tự Động Qua Giao Diện Phần Mềm (Khuyến nghị) 🚀

Trong phần mềm Vision System, mở menu:
`❓ Trợ Giúp` ➔ `🔄 Kiểm Tra Bản Cập Nhật (OTA Update)...` ➔ Chuyển sang Tab **"📦 Đóng Gói & Tải Lên (Publish)"**:

1. **Thư mục ứng dụng nguồn**:
   - Hệ thống tự động nhận diện thư mục `bin\Release\net8.0-windows` (hoặc bạn có thể bấm nút `📁 Duyệt...` để chọn thư mục build khác).
2. **Số phiên bản mới**:
   - Nhập số phiên bản mong muốn hoặc bấm các nút tăng nhanh:
     - `+0.0.0.1 (Patch)`: Tăng bản vá lỗi nhỏ.
     - `+0.0.1.0 (Minor)`: Tăng tính năng mới.
     - `+0.1.0.0 (Major)`: Tăng phiên bản lớn.
   - Tích chọn `☑ Tự động cập nhật số Version vào file .csproj` (hệ thống sẽ tự động ghi đè số phiên bản mới vào `VisionInspectionApp.UI.csproj`).
3. **Cấu hình máy chủ & Thư mục lưu trữ**:
   - **Đường dẫn script máy chủ**: Nhập URL file PHP trên server, ví dụ: `http://192.168.1.100/ota_server.php`
   - **Thư mục chứa file zip trên server**: Cho phép tùy chỉnh thư mục lưu trữ trên server, ví dụ: `uploads/ota_packages` hoặc `updates/line1`.
   - **API Token**: Nhập khóa bảo mật (nếu server có cấu hình `OTA_API_KEY`).
4. **Ghi chú phát hành**:
   - Nhập nội dung mô tả các tính năng mới / lỗi đã sửa.
5. **Thực thi**:
   - Bấm nút **`🚀 Đóng Gói Zip & Tải Lên Server`**.
   - Hệ thống sẽ tự động:
     - Cập nhật số phiên bản vào `VisionInspectionApp.UI.csproj`.
     - Nén toàn bộ thư mục thành tệp `.zip` (tự động bỏ qua các file tạm/rác).
     - Tính toán mã băm SHA-256 và dung lượng tệp.
     - Tạo file manifest `version.json`.
     - Tải tệp zip và `version.json` lên máy chủ PHP vào thư mục đã chọn.
     - Hiển thị nhật ký tiến trình chi tiết trên màn hình.

---

## Cách 2: Thủ Công Bằng Tay 🛠️

1. Mở file `VisionInspectionApp.UI.csproj`:
   - Sửa các thẻ `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>` thành số phiên bản mới.
2. Build ứng dụng ở chế độ Release:
   ```bash
   dotnet build VisionInspectionApp.slnx -c Release
   ```
3. Nén toàn bộ nội dung trong `VisionInspectionApp.UI\bin\Release\net8.0-windows` thành tệp `.zip`.
4. Tính toán mã băm SHA-256 của tệp zip bằng PowerShell:
   ```powershell
   Get-FileHash -Algorithm SHA256 path_to_file.zip
   ```
5. Cập nhật file `version.json` trên máy chủ web:
   ```json
   {
     "AppName": "CMS VINA Vision System",
     "Version": "1.0.0.1",
     "ReleaseDate": "2026-09-07T15:30:00Z",
     "Channel": "Stable",
     "IsMandatory": false,
     "DownloadUrl": "http://192.168.1.100/uploads/ota_packages/VisionUpdate_v1.0.0.1.zip",
     "FileSize": 12345678,
     "Sha256": "MÃ_SHA256_TÍNH_ĐƯỢC",
     "ReleaseNotes": "Nội dung cập nhật mới"
   }
   ```
6. Copy file `.zip` và `version.json` vào thư mục web trên máy chủ.
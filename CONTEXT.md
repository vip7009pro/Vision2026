# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, nạp bản vẽ kỹ thuật PDF, quản lý CSDL MES/ERP và giao tiếp PLC đa hãng.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (6/6 tests Release Config Persistence & Seeding, 10/10 Calib tests).

## 3. Khắc Phục Triệt Để Mất Cấu Hình Khi Build & Triển Khai Release (Task 374)
- **Vấn đề giải quyết**: Khi build Release hoặc chạy bản Release, ứng dụng bị mất trắng cấu hình OTA, cấu hình OQC Scanner & Database, Camera, PLC, Recent Jobs và Jobs.
- **Nguyên nhân**: Phân mảnh thư mục lưu trữ (`%AppData%\VisionInspectionApp\`, `%AppData%\Vision2026\`, `%AppData%\CMS_VINA_Vision\`, `BaseDir\`), thiếu seeding dự phòng khi AppData rỗng khiến service khởi tạo default ghi đè, thư mục output Release thiếu `jobs\` và `configs\`, OtaUpdateViewModel thiếu lưu trường Publisher và thiếu trigger lưu khi đóng dialog.
- **Giải pháp triển khai**:
  - `AppStoragePaths.cs`: Thống nhất mọi cấu hình vào `%AppData%\Vision2026\`, tự động di chuyển (Auto-migration) bảo toàn dữ liệu cũ từ mọi thư mục, và nạp hạt giống (Application Seeding) từ `BaseDirectory\configs\system\` khi chạy trên máy mới.
  - Cơ chế đồng bộ 2 chiều (`SyncConfigToAppBackup`): Mọi thao tác lưu cấu hình vào AppData đều tự động sao lưu dự phòng vào `BaseDirectory\configs\system\`.
  - MSBuild Target `SyncReleaseConfigurations`: Tự động đồng bộ toàn bộ file cấu hình JSON, thư mục `configs/` và thư mục `jobs/` vào `bin\Release\` và `bin\x64\Release\` sau mỗi lần build.
  - `OtaUpdateViewModel.SaveAllSettings()` & `OtaUpdateDialog.Closing`: Bổ sung lưu toàn diện cả Receiver và Publisher, tự động lưu khi bấm nút [X] đóng cửa sổ.

## 4. Các sự kiện & thay đổi gần đây
- Task 374: Khắc phục triệt để mất cấu hình OTA, OQC Scanner & Database khi build Release (Kiến trúc 2 tầng AppStoragePaths & MSBuild Sync).
- Task 373: Dải ô vuông Timing Breakdown nằm gọn trên 1 hàng ngang có thể cuộn ngang mượt mà bằng thanh cuộn hoặc con lăn chuột.
- Task 372: Nâng cấp toàn diện hệ thống Calib sang cơ chế 2 trục độc lập ($X$ và $Y$), giải quyết triệt để lỗi đo phôi chữ nhật.
- Task 371: Nút "Set as Calib Factor" trong Properties của các tool đo.
- Task 370: Tự động khớp ComboBox CSDL OQC & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống (1-click migrate).
- Task 369: Cải thiện nút Pan hiển thị rõ nét & sửa lỗi Origin Train Template từ nguồn PDF.

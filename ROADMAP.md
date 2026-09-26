# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 369: Hoàn thiện UI Pan và sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template".
- [x] Task 370: Tự động khớp CSDL OQC Scanner & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống:
  - [x] Sửa lỗi ComboBox CSDL OQC rỗng khi import với thuật toán 5 cấp `ResolveDatabaseId`.
  - [x] Bổ sung Menu Item `📦 Cấu Hình...` mở cửa sổ `SystemConfigBackupWindow` 3 tab.
  - [x] Xuất/Nạp toàn bộ cấu hình ra tệp `.viscfg` duy nhất, 1-click migration.
  - [x] Tự động ánh xạ lại CSDL ID cho OQC config và nạp nóng tức thì.
- [x] Task 371: Hiệu chuẩn tỉ lệ Calib trực tiếp từ Tool đo ("Set as Calib Factor") cho từng Job:
  - [x] Thêm nút `🎯 Đặt làm Hệ Số Calib` vào properties của các công cụ đo: `Distance`, `SegmentLineDistance`, `LineLineDistance`, `PointLineDistance`, `EdgePairDetect`, `EdgePair`, `Diameter`, và `CircleFinder`.
  - [x] Bổ sung `NominalDiameter` trong `CircleFinderDefinition` và ô nhập `Nom Dia (mm)` cho Circle Finder.
  - [x] Tự động trích xuất khoảng cách pixel thực tế (`measuredPx`) và kích thước danh định (`Nominal mm`).
- [x] Task 372: Nâng cấp hệ thống Calib sang cơ chế 2 trục độc lập ($X$ và $Y$):
  - [x] Bổ sung `PixelsPerMmX` và `PixelsPerMmY` trong `VisionConfig`, hỗ trợ tương thích ngược 100% qua `GetEffectivePpmX()`, `GetEffectivePpmY()`.
  - [x] Quy đổi khoảng cách vector 2D Euclidean: $\text{dist}_{\text{mm}} = \sqrt{(\Delta x / \text{ppm}_X)^2 + (\Delta y / \text{ppm}_Y)^2}$.
  - [x] Tự động nhận diện hướng vector đo: $\theta < 45^\circ \rightarrow$ Trục X (Ngang), $\theta \ge 45^\circ \rightarrow$ Trục Y (Dọc), Đường tròn $\rightarrow$ Đồng hướng.
  - [x] Thiết kế hộp thoại Dark mode `CalibAxisSelectionDialog` cho phép lựa chọn cập nhật Trục X, Trục Y hoặc Cả 2 trục.
  - [x] Hoàn thành 10/10 test cases chuyên sâu và vượt qua 100% regression test suite.
- [x] Task 373: Tối ưu khối Timing Breakdown Tool Editor thành 1 hàng ngang cuộn ScrollViewer:
  - [x] Đổi ItemsPanel từ `WrapPanel` sang `StackPanel Orientation="Horizontal"`.
  - [x] Bọc trong `ScrollViewer` cuộn ngang (`HorizontalScrollBarVisibility="Auto"`).
  - [x] Bổ sung `PreviewMouseWheel` hỗ trợ cuộn ngang mượt mà bằng con lăn chuột trực tiếp trên dải chip.
  - [x] Giữ nguyên chiều cao cố định ~45px, không còn đẩy bảng Spec Measurements và giao diện xuống dưới.
- [x] Task 374: Khắc phục triệt để mất cấu hình OTA, OQC Scanner, Database & Toàn bộ cấu hình hệ thống khi build Release:
  - [x] Điều tra và xác định 5 nguyên nhân gốc: Phân mảnh thư mục (`VisionInspectionApp`, `Vision2026`, `CMS_VINA_Vision`, `BaseDir`), thiếu seed cấu hình khi chạy bản Release trên máy mới, build output không có `jobs/` và `configs/`, và OtaUpdateViewModel thiếu lưu trường Publisher và thiếu trigger khi đóng dialog.
  - [x] Xây dựng kiến trúc 2 tầng chuẩn mực qua `AppStoragePaths.cs`: Thống nhất `%AppData%\Vision2026\`, cơ chế Auto-migration tự động chuyển toàn bộ dữ liệu cũ từ các thư mục khác, và cơ chế Fallback Seeding từ `BaseDirectory\configs\system\`.
  - [x] Triển khai cơ chế đồng bộ 2 chiều (`SyncConfigToAppBackup`): Mọi thay đổi cấu hình CSDL, OQC, OTA, Camera, PLC đều được đồng bộ tức thì ngược lại `BaseDirectory\configs\system\` để bản phát hành luôn sẵn sàng cấu hình mới nhất.
  - [x] Tích hợp MSBuild Target `SyncReleaseConfigurations`: Tự động đồng bộ toàn bộ file cấu hình JSON, thư mục cấu hình `configs/` và thư mục `jobs/` sang thư mục đầu ra `bin\Release` và `bin\x64\Release` ngay sau mỗi lần build.
  - [x] Bổ sung `SaveAllSettings()` cho `OtaUpdateViewModel` và sự kiện `Closing` cho `OtaUpdateDialog`, đảm bảo lưu toàn bộ cấu hình OTA Receiver & Publisher kể cả khi đóng dialog bằng nút [X].
  - [x] Tạo bộ kiểm thử tự động `ReleaseConfigPersistenceTests.cs` (6/6 test cases) xác minh 100% độ bền vững cấu hình, chạy pass thành công.

- [x] Task 375: Bổ sung thao tác kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF:
  - [x] Bổ sung `RenderRotatedPage` và `PlacePageOnCameraCanvas` vào `IPdfDocumentService` & `PdfDocumentService`, xử lý in-memory siêu mượt (<1ms).
  - [x] Thiết kế cử chỉ chuột trong `ImageViewerControl`: `EnablePdfPan`, `PdfPanChangedCommand`, `PdfPanDragInfo` record, con trỏ `SizeAll`/`Hand`, hủy bằng phím `Escape`.
  - [x] Tích hợp live drag 60 FPS in-memory trong `ToolEditorViewModel`: hiển thị tức thì chuyển động bản vẽ, cập nhật số Offset X/Y thời gian thực và chốt lưu ảnh khi nhả chuột.
  - [x] Bổ sung 2 nút ToggleButton trực quan `🖐️ Kéo Pan` trong panel thuộc tính Pan và `🖐️ Pan PDF` trên thanh công cụ xem trước Preview Header.
  - [x] Hoàn thành kiểm thử tự động `Test8_PdfMousePanDragInteractiveSimulation` (8/8 PDF tests PASS 100%).

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.

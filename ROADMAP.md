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

- [x] Task 376: Tách ô nhập SỐ ĐO THỰC TẾ (mm) thành field riêng cạnh nút "Đặt Hệ Số Calib" để không còn ghi đè ô Nominal (Spec):
  - [x] Bổ sung `CalibActualMmText`, `CalibActualMm`, `HasCalibActualMm`, `CalibActualMmHint`, `ResetCalibActualMm()` trong `ToolEditorViewModel.ToolCalibFactor.cs`.
  - [x] Thêm lớp toán học thuần (unit-testable) `CalibFactorMath` trong `VisionInspectionApp.VisionEngine`: `ParseMeasuredMm` (nhận cả `50.02` và `50,02`), `ResolveNominalMm` (ưu tiên số đo thực tế, fallback về Nominal để tương thích ngược 100%), `ComputePixelsPerMm` (chặn NaN/Infinity/giá trị ≤ 0).
  - [x] Giao diện: ô `Đo thực tế (mm)` nằm ngay BÊN TRÁI nút `🎯 Đặt Hệ Số Calib` tại cả 3 vị trí (Distance Spec dùng chung, Circle Finder với nhãn `Đo thực tế Ø (mm)`, Edge Pair Detect) kèm dòng ghi chú xác nhận ô Nominal (Spec) được giữ nguyên.
  - [x] Hộp thoại `CalibAxisSelectionDialog` hiển thị rõ nguồn kích thước đang dùng (`Số đo THỰC TẾ (mm)` hay `Nominal Spec`).
  - [x] Tự động xóa ô "Đo thực tế (mm)" khi chuyển sang công cụ khác; Status Bar ghi rõ nguồn kích thước đã dùng để calib.
  - [x] Bổ sung 2 test cases `TestCalibActualMmInputParsing` và `TestCalibActualMmSeparateFieldDoesNotOverwriteSpec` — 12/12 Calib tests PASS 100%.

- [x] Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm nút Auto Tune cho công cụ nhận diện:
  - [x] `ToolEditorViewModel.GraphOps.cs`: thay cơ chế clipboard 1 node (chỉ lưu `RefName`/`Type`) bằng danh sách snapshot nhiều node; paste nhân bản đầy đủ tất cả node trong vùng chọn (kèm định nghĩa + vị trí + cạnh nội bộ + cạnh vào từ node ngoài).
  - [x] Tách hàm dùng chung `CloneNodeDefinition(type, oldName, newName)` hỗ trợ 30+ loại node.
  - [x] Thêm `PreprocessAutoTuner` (VisionInspectionApp.Application): thử ~40 cấu hình Preprocess, chấm điểm theo kết quả nhận diện của Caliper / Line / EdgePairDetect / CircleFinder / CodeDetection, trả về cấu hình điểm cao nhất.
  - [x] Thêm panel `🎯 Auto Tune Preprocess` trong Tool Editor: nút Auto Tune/Hủy, hiển thị Preprocess cha, tiến trình (bước x/y), thông số đang thử và điểm cao nhất.
  - [x] Sau khi Auto Tune: tự động gán thông số tốt nhất vào tool Preprocess cha và refresh preview + autosave.

- [x] Task 378: Tối ưu hiệu năng/UX Tool Editor (ảnh 20MP nạp qua URL) & mở rộng Auto Tune:
  - [x] `SharedImageContext.Version`: tăng mỗi lần SetImage; dùng làm khoá cache dựng lại preview.
  - [x] Tool Editor: preview "Final" (ảnh + overlay toàn Flow) chỉ dựng lại khi ẢNH đổi / NỘI DUNG job đổi (`_previewContentRevision` theo `IsDirty`) / kết quả Run đổi. Trước đây luôn dựng lại mỗi lượt → trỏ qua lại giữa các node cũng phải clone ảnh 20MP + chạy lại preprocess + overlay cho MỌI tool (nguyên nhân lag chính).
  - [x] Sửa rò rỉ ~60MB mỗi lần tải/nạp ảnh URL (Mat không được dispose) bằng `SetImage(mat, transferOwnership: true)`; sửa luôn nhánh chụp snapshot camera.
  - [x] Auto Tune: nâng từ ~40 preset lên quy trình 2 tầng — Preset bao quát + **Coordinate Descent** quét toàn bộ giá trị của từng nhóm thông số (Màu, Chiếu sáng, Khử nhiễu, Tông màu, Cạnh, Nhị phân, Đảo, Morphology), lặp 3 vòng tới khi hội tụ (≈250–300 tổ hợp/lần).
  - [x] Hàm mục tiêu theo công cụ: Line/Caliper/EdgePairDetect tối đa độ phủ + độ mạnh cạnh; CircleFinder tối đa điểm số đường tròn; CodeDetection ưu tiên decode được mã, nếu chưa thì tối đa "độ nét biên" (Laplacian variance) để làm rõ barcode.
  - [x] Hiển thị tiến trình kèm thời gian chạy (`Bước x/y • t.giây`).

- [x] Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN:
  - [x] `PreprocessAutoTuner.Tune`: Stage 1 (preset) và mỗi nhóm của Stage 2 (Coordinate Descent) chạy `Parallel.For` trên nhiều nhân.
  - [x] Số luồng `MaxDegreeOfParallelism = clamp(ProcessorCount - 1, 1, cap)`; `cap = 4` với ảnh > 2MP (20MP), `cap = 8` với ảnh nhỏ — chừa 1 nhân cho UI và giới hạn đỉnh RAM (~60MB/ứng viên).
  - [x] Ghi nhận "điểm cao nhất" an toàn đa luồng (lock) + đếm tiến trình bằng `Interlocked`; chọn giá trị tốt nhất mỗi nhóm theo thứ tự cố định (kết quả tất định).
  - [x] Hủy giữa chừng vẫn hoạt động (hỗ trợ cả `OperationCanceledException` bọc trong `AggregateException` của `Parallel.For`).
  - [x] An toàn đa luồng: mỗi ứng viên tự tạo/thu hồi Mat riêng; `ImagePreprocessor` chỉ đọc kernel tĩnh dùng chung.

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.

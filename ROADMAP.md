# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 405: Khắc Phục Triệt Để Lỗi Contour Lệch/Báo Đỏ Trên Cùng Ảnh, Thêm Canny Slider Kèm Live Preview, Tích Hợp Đánh Giá Tổng Job & Bảng Đo Đạc:
  - [x] Sửa triệt để hiện tượng viền đỏ giả tạo trên cạnh thẳng và rãnh U:
    - [InspectionService.Pipeline.cs:1268](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/InspectionService.Pipeline.cs#L1268) chuyển `FindAllContours` sang `ContourApproximationModes.ApproxNone` giữ toàn vẹn mọi điểm viền.
    - Tích hợp **`Cv2.DistanceTransform`** (L2 Mask5) tính toán khoảng cách Euclidean thực tế liên tục tới đường viền trong $0.1\text{ms}$ ($O(1)$), triệt tiêu hoàn toàn sai số khoảng trống điểm của `ApproxSimple`.
    - Tự động chạy `MatchTemplate` cả khi `InspectRoi == TemplateRoi` để triệt tiêu sai số làm tròn tọa độ 1 pixel. Khi cùng ảnh, viền xanh ôm khít 100%, 0 đoạn đỏ, MaxDist = 0.00px.
  - [x] Bổ sung cụm Slider (1–255) + TextBox cho `Canny Thr 1` và `Canny Thr 2` trong Properties Panel ([ToolEditorView.xaml:2980-3020](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml#L2980-L3020)):
    - Kéo slider mượt mà, phản ánh tức thì lên khung xem trước `ContourCompare_TemplatePreviewImage` trên Properties Panel.
    - Đồng bộ 2 chiều qua `OnPropertyChanged` trong [ToolEditorViewModel.ToolContourCompare.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolContourCompare.cs).
  - [x] Tích hợp kết quả ContourCompare vào điều kiện đánh giá Job và Bảng kết quả đo đạc:
    - [InspectionService.Pipeline.cs:3340](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/InspectionService.Pipeline.cs#L3340): Thêm `&& result.ContourCompares.All(x => x.Pass)` vào `result.Pass`. Khi ContourCompare NG thì Job chắc chắn ra NG.
    - [ToolEditorViewModel.Inspection.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Inspection.cs) & [InspectionViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/InspectionViewModel.cs): Bổ sung dòng kết quả `SpecResultRow` của ContourCompare vào Bảng kết quả đo đạc `SpecResults`.
    - [OqcScannerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/OQC/OqcScannerService.cs): Cập nhật `ExtractMeasurementDetails` hiển thị chuẩn theo `MatchMethod` (Max Hausdorff Dist px / Area Diff % / Shape Score).
  - [x] Kiểm thử tự động: Bổ sung Test 6, 7, 8 trong [ContourCompareTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/ContourCompareTests.cs); toàn bộ 8/8 test PASSED 100%, 15/15 test OQC Scanner PASSED 100%, Solution build 0 lỗi.

- [x] Task 404: Khắc Phục Lỗi ContourCompare Báo NG Trên Cùng 1 Ảnh & Nâng Cấp Template Preview Kèm Viền Trực Quan:
  - [x] Sửa lỗi đồng bộ ảnh tiền xử lý trong Pipeline: truyền chính xác `matForCc` (ảnh sau node Preprocess) thay vì ảnh xám camera ban đầu.
  - [x] Hỗ trợ MatchTemplate + ICP định vị mẫu khi `InspectRoi` lớn hơn `TemplateRoi`.
  - [x] Tự động đồng bộ `InspectRoi = TemplateRoi` khi người dùng nhấn "Set Template" nếu chưa vẽ Search ROI.
  - [x] Tích hợp Template Preview & Contour Overlay lên Properties Panel của ContourCompare.

- [x] Task 403: Tự Động Lưu Job Đang Mở Khi Có Thay Đổi Trước Khi Tải Lên Server (JobManager):
  - [x] Nhận diện trạng thái Job đang mở chưa được lưu (`IsDirty == true` hoặc chưa có file trên đĩa).
  - [x] Xây dựng phương thức `EnsureActiveJobSavedBeforeUploadAsync` trong `JobManagerViewModel.Upload.cs`.
  - [x] Tích hợp tự động lưu vào cả 3 nút tải Job lên Server; module hóa `JobManagerViewModel.Upload.cs`.

- [x] Task 402: Bảo Mật Mật Khẩu Xóa Lịch Sử OQC Log & Checkbox Không Tên Lọc PASS (Ẩn NG):
  - [x] Cấu hình `DeleteHistoryPassword` (mặc định "1234") trong `OqcScannerConfig.cs` và hộp thoại xác thực `OqcPasswordPromptDialog.xaml`.
  - [x] Checkbox không tên lọc PASS (ẩn NG) trên thanh lọc cạnh ô tìm kiếm `OqcScanHistoryWindow.xaml`.

- [x] Task 401: Chuẩn Hóa Chu Trình 3 Bước Tab OQC Scanner Chế Độ Cú Đấm Thép (Tự Động Nạp Job Thứ 2 & Reset Counting Mẫu).
- [x] Task 400: Khắc Phục Triệt Để Lỗi Khớp Điểm Origin Thuật Toán MvpShapeMatch2 Với Ảnh Qua Tiền Xử Lý.
- [x] Task 399: Lưu Lịch Sử Kiểm Tra Cho Mọi Lần Chụp Và Kiểm Tra Trong Cùng 1 Phiên Job Trên Tab OQC Scanner.
- [x] Task 398: Tự Động Đóng Cửa Sổ Quản Lý Job Khi Bấm Huấn Luyện Từ Xa.
- [x] Task 397: Khắc Phục Lỗi Mất Dữ Liệu Cấu Hình OQC Scanner & Tra Cứu Database Khi Build App & Chạy Test.
- [x] Task 396: Khắc Phục Lỗi Mất Link Server OTA Khi Build App & Chạy Test.
- [x] Task 395: Tự Động Focus & Select All Scanned Text Trên Tab OQC Scanner (Ghi Đè Tự Động).
- [x] Task 394: Thêm Phím Tắt ESC Để Đóng Job & Xóa TextBox Trên Tab OQC Scanner (1 chạm không pop-up).
- [x] Task 393: Chuẩn Hóa Chu Trình 4 Bước Chế Độ "Cú đấm thép" Trên Tab OQC Scanner (Mặc Định Checked).
- [x] Task 392: Chuẩn Hóa Chu Trình Dịch Bit Hàng Đợi FX5U — Dịch Bit Theo Xung Kết Quả Vision Done (`M103`).
- [x] Task 391: Sửa Lỗi Không Truyền Bit NG M105 Sang GXWorks & Kích Hoạt Handshake Khi Test Ảnh Trên Tool Editor.
- [x] Task 390: Khắc phục hiện tượng Bắt Cạnh PLC (`LDP X0`) không tác động `SET Y0` từ nút Momentary HMI.
- [x] Task 389: Bắt Tay Bất Đồng Bộ Non-Blocking & Hàng Đợi 20 Phôi BSFLP M200..M219 Dừng Đúng Điểm Ra Ngoài Buồng.
- [x] Task 388: Khắc phục Mất Cấu Hình PLC Khi Build & Cơ Chế Test Handshake Giả Lập GX Works 3.
- [x] Task 387: Hiển thị Trực Quan Trạng Thái PLC Handshake trên UI Tool Editor & Phân tích Trigger Flow.
- [x] Task 386: Hướng dẫn Thông số Cài Đặt Handshake & Tags trên App Vision WPF.
- [x] Task 385: Chuẩn hóa lệnh dịch bit `BSFRP` cho PLC FX5U.
- [x] Task 384: Bộ mã Ladder Diagram (LD) đầy đủ 9 Networks cho PLC FX5U.
- [x] Task 383: Sửa lỗi cú pháp ST GX Works 3 `0x110E1A02`.
- [x] Task 382: Chuẩn hóa 100% định dạng CSV GX Works 3 (1.080J).
- [x] Task 381: Handshake PLC FX5U, Cơ chế Dừng NG Ngoài Buồng & Xem Lại Ảnh 20 Nấc Lịch Sử.
- [x] Task 380: OQC Scanner 1 nút Space / Ctrl+F8 luân phiên & Bật tắt Crosshair căn tâm.
- [x] Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- [x] Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent).
- [x] Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm Preprocess Auto Tuner.
- [x] Task 376: Ô nhập SỐ ĐO THỰC TẾ (mm) riêng cạnh nút "Đặt Hệ Số Calib".

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.

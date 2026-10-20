# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 409: Khắc Phục Triệt Để Lỗi Ảnh Template Của Node Tool SurfaceCompare Bị Đen Ngòm (Job GH68-58216B):
  - [x] Điều tra & định vị 2 nguyên nhân gốc rễ trên tệp `job_7A11034A_GH68-58216B_20261019_113956_a44536.job`:
    - Nguyên nhân 1 (Tiền xử lý kép): Template `kt mat net_sc.png` được lưu từ ảnh sau tiền xử lý `Nhi phan 4` (`InRange(128, 246)` nên chỉ gồm pixel `0` và `255`). Tại runtime, pipeline lại gọi tiếp `preprocessor.Run(templRaw, settings)`. Vì `0 < 128` và `255 > 246`, không có pixel nào trong $[128, 246]$, `Cv2.InRange` gán 100% pixel về `0` (Đen ngòm).
    - Nguyên nhân 2 (Ghi đè Search ROI): Tại `ToolEditorViewModel.Engine.cs`, khi người dùng kéo Search ROI (`SC`), sự kiện nhầm lẫn gọi hàm lưu template đè kích thước $500 \times 284$ của Search ROI lên `sc.TemplateImageFile` thay vì giữ $453 \times 39$ của Template ROI.
  - [x] Cải tiến & khắc phục triệt để:
    - Bỏ tiền xử lý lần 2 trên `templRaw` trong `InspectionService.Pipeline.cs`, giữ nguyên dữ liệu gốc sắc nét của template đã huấn luyện.
    - Cơ chế tự phục hồi thông minh: nếu file template trên đĩa có kích thước khớp `InspectRoi`, tự động trích xuất sub-patch tọa độ `TemplateRoi` thay vì méo hình bằng `Resize`.
    - Đồng bộ `ResolveToolPreprocess("SurfaceCompare", sc.Name)` tương tự `ContourCompare` để nhận trọn vẹn chuỗi tiền xử lý đa tầng (`Den trang` $\rightarrow$ `Nhi phan 4`).
    - Bỏ nhánh ghi đè template khi người dùng thao tác trên Search ROI `SC` tại `ToolEditorViewModel.Engine.cs`.
  - [x] Kiểm thử tự động:
    - Thêm `Test_ReproduceUserJobBlackTemplate` trong [SurfaceCompareAndCaliperRoiTests.cs](file:///g:/NODEJS\Vision2026\TestExtractApp\SurfaceCompareAndCaliperRoiTests.cs).
    - Xác minh trên đúng file Job của người dùng: `Template Image: 453x39, MinVal: 0, MaxVal: 255`, `Pass: True, Count: 0, MaxArea: 0` (ảnh sáng rõ nét 100%, không còn đen ngòm).
    - Toàn bộ 9/9 test SurfaceCompare & Caliper PASSED 100%.

- [x] Task 408: Rà Soát, Bỏ Control Vô Dụng và Tái Cấu Trúc Giao Diện Cửa Sổ Train Template (Tool Origin):
  - [x] Rà soát và loại bỏ 100% các control vô dụng (dummy) tồn đọng từ mẫu thiết kế cũ (Eraser, Size, Undo, Redo, Detection Roi radio, Shape Operations Table, Save button lơ lửng).
  - [x] Tái cấu trúc giao diện 2 cột hiện đại, khoa học, thẩm mỹ chuẩn công nghiệp.
  - [x] Kiểm thử tự động `Test_OriginTrainViewModel_LifecycleAndCommands` PASSED 100%.

- [x] Task 407: Bổ Sung Phím Tắt Ctrl + R (Giữ Nguyên ROI Width/Height, Chỉ Xoay Strip Trong ROI) và Ctrl + F4 (Đóng Job Đang Mở):
  - [x] Phím tắt `Ctrl + R` cho Tool `EdgePairDetect` và `Caliper`: Chuyển đổi luân phiên giữa `Vertical` $\leftrightarrow$ `Horizontal` khi đang chọn node tương ứng.
  - [x] Phím tắt `Ctrl + F4` đóng Job đang mở (`CloseJob`).
  - [x] Kiểm thử tự động PASSED 100%.

- [x] Task 406: Tự Động Co Giãn Strip Length & Strip Width Node EdgePairDetection Theo Kích Thước ROI.
- [x] Task 405: Khắc Phục Triệt Để Lỗi Contour Lệch/Báo Đỏ Trên Cùng Ảnh, Thêm Canny Slider Kèm Live Preview, Tích Hợp Đánh Giá Tổng Job & Bảng Đo Đạc.
- [x] Task 404: Khắc Phục Lỗi ContourCompare Báo NG Trên Cùng 1 Ảnh & Nâng Cấp Template Preview Kèm Viền Trực Quan.
- [x] Task 403: Tự Động Lưu Job Đang Mở Khi Có Thay Đổi Trước Khi Tải Lên Server (JobManager).
- [x] Task 402: Bảo Mật Mật Khẩu Xóa Lịch Sử OQC Log & Checkbox Không Tên Lọc PASS (Ẩn NG).
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

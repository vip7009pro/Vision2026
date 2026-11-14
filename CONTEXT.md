# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% toàn bộ test suite `TestExtractApp` (bao gồm ContinuousFlowRegression, ToolEditorContinuousRenderAndSpeed, ToolEditorColumnResize, ToolEditorAndOqcUx, PreprocessRoiMasking, MvpShapeMatch3 & Int8, Snapshot Frame Ownership, PlcHandshake, PlcContinuousQueue, v.v.).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 438: Tối Ưu Hóa Pipeline Chạy Liên Tục (Zero-Copy Frame Flow & Đồng Bộ Tốc Độ >= 5 pcs/s)
1. **Bối cảnh & Vấn đề**:
   - Khi chạy liên tục: Tốc độ kiểm tra bị nghẽn ở ~3.5 pcs/s làm hàng đợi 16 nấc đầy dần. Khi dừng máy, việc xả hàng trong hàng đợi đạt tốc độ tối đa 5 pcs/s (200ms/con).
   - Nguyên nhân: Tranh chấp bus bộ nhớ và L3 cache do sao chép 4 lần ảnh full-size 25MP (300MB/con hàng); bão cấp phát/hủy 75MB khi queue đầy; và bão Dispatcher UI ở mức ưu tiên Normal.
2. **Chi tiết các giải pháp đã triển khai**:
   - **Triệt tiêu 3 bản sao chép thừa (Memory Bandwidth Decongestion)**:
     - Bỏ clone 75MB vào `SetImageSourceCache` khi `IsRunningFolderFlow`.
     - Chuyển `_sharedImage.SetImage` có điều kiện vào khối `shouldRenderUi` (120ms/lần, khi NG hoặc khi queue vừa xả sạch), không cập nhật trên mọi frame.
     - Giảm tổng lượng bộ nhớ memcpy từ 300MB xuống còn 75.7MB cho mỗi con hàng (giảm 76% áp lực bus RAM).
   - **Thumbnail Proxy cho thanh 20 con hàng gần nhất**:
     - Với sản phẩm PASS: Thu nhỏ về proxy $640 \times 360$ (< 700KB thay vì 75MB), giảm 99% áp lực bộ nhớ LOH cho 20 slot.
     - Với sản phẩm NG: Giữ nguyên bản snapshot full-size 25MP để người vận hành soi khuyết tật.
   - **Cơ chế Drop-Before-Clone cho Hàng đợi 16 nấc**:
     - Kiểm tra và giải phóng envelope cũ trước khi clone frame mới, triệt tiêu việc giữ đồng thời 17 ảnh full-size trong RAM.
   - **Coalesce & Throttling Dispatcher UI**:
     - `UpdateQueueVisuals` và `UpdateRecentPartsVisuals` được bọc bởi cờ `_isQueueVisualUpdatePending` / `_isRecentPartsVisualUpdatePending` với `DispatcherPriority.Background`, loại bỏ hoàn toàn hiện tượng bão Dispatcher queue.

## 4. Các sự kiện & thay đổi gần đây
- **Task 439**: Rà soát PLC `PLC_Programs/Mitsubishi_GXWorks3/grok xml.xml` (FX5U). Lỗi chính: KHOI_4 ghi `D1000Z0`/`D2000Z1` nhưng Z0/Z1 không bao giờ được nạp từ D1101 → luôn ghi D1000/D2000; dữ liệu 32-bit cần Z = index×2; vùng D2000..D2199 chồng lên D2100/D2101; so sánh `>=` 16-bit với D600 32-bit (phải dùng `D>=`); Y1 double coil (KHOI_5 & KHOI_7); KHOI_7 pop dùng `AND M61` (ngược, phải `ANI M61`); KHOI_6 `INCP D300` không bao giờ chạy vì M103 đã RST trước đó. Chưa sửa file nguồn PLC (chỉ đưa mnemonic đề xuất).
- **Task 438**: Tối ưu hóa pipeline chạy liên tục (Zero-Copy Frame Flow, Thumbnail Proxy, Throttled Dispatcher); 100% test PASSED, 0 Errors.
- **Tasks 435, 436, 437**: Rà soát và tối ưu hiệu năng toàn diện Tool Editor (Canvas 60 FPS, Targeted Invalidation, Pipeline ảnh lớn 20-25MP).
- **Task 434**: Bổ sung tùy chọn màu vùng che Đen / Trắng cho Tool Preprocess ROI Masking; kiểm thử 8/8 test PASSED 100%.
- **Task 433**: Hiện thực hóa thành công 2 thuật toán tăng tốc `MvpShapeMatch3` (3.54ms) và `MvpShapeMatchInt8` (2.74ms); kiểm thử 6/6 test PASSED 100%.
- **Task 432**: Tự động ẩn Cột 1 & 2, hiển thị Cột 3 & 4 (50/50) khi Chạy liên tục; khôi phục khi dừng; kiểm thử 10/10 PASSED 100%.

# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% toàn bộ test suite `TestExtractApp` (bao gồm ToolEditorContinuousRenderAndSpeed, ToolEditorColumnResize, ToolEditorAndOqcUx, PreprocessRoiMasking, MvpShapeMatch3 & Int8, Snapshot Frame Ownership, PlcHandshake, PlcContinuousQueue, v.v.).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Đợt Tối Ưu Hiệu Năng Tool Editor (Tasks 435, 436, 437)
1. **Mục tiêu**:
   - Khắc phục triệt để tình trạng giật, lag, khựng khi tương tác, pan/zoom canvas đồ thị, chuyển qua lại giữa các node và nhập liệu thuộc tính khi canvas nhiều node và làm việc với ảnh độ phân giải cao (20MP - 25MP).
   - Bảo toàn 100% tính năng cốt lõi và kiến trúc MVVM.
2. **Chi tiết các cải tiến**:
   - **Task 435 (Canvas & Graph 60 FPS)**:
     - Triệt tiêu hoàn toàn các lệnh `UpdateLayout()` cưỡng bức trong `EditorCanvas_PreviewMouseWheel` và `AutoFitAndCenterGraph` ([ToolEditorView.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml.cs)), giải phóng UI thread khi zoom canvas 10.000x10.000 px.
     - Caching `PathGeometry` và đóng băng `geom.Freeze()` trong `ToolGraphEdgeViewModel.cs` ([ToolGraphEdgeViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolGraphEdgeViewModel.cs)), tái tạo chỉ khi node di chuyển.
     - Kích hoạt ảo hóa danh sách Tool theo nhóm (`VirtualizingPanel.IsVirtualizing="True"`, `VirtualizingPanel.ScrollUnit="Pixel"`) trên `ToolboxList` ([ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml)).
     - Chuyển `RenderOptions.BitmapScalingMode="LowQuality"` (Bilinear GPU) trên `PART_Image` ([ImageViewerControl.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Controls/ImageViewerControl.xaml)), tăng tốc độ raster GPU khi pan/zoom ảnh lớn.
   - **Task 436 (Targeted Property Invalidation & Chống Bão DataBinding/Disk I/O)**:
     - Tái cấu trúc `RaiseToolPropertyPanelsChanged()` ([ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs)): Phân luồng có điều kiện theo loại node đang chọn, giảm tải từ 150+ xuống chỉ 5-10 properties / lần click (giảm 94% overhead binding).
     - Cách ly Disk I/O `FileInfo` đồng bộ của template Origin/Contour, chỉ đọc đĩa khi đúng node được chọn.
     - Dọn dẹp 17 thông báo thuộc tính dư thừa trong `OnSelectedNodeChanged` ([ToolEditorViewModel.GraphOps.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.GraphOps.cs)).
     - Thay thế việc gọi `RaiseToolPropertyPanelsChanged()` trong setter đơn lẻ ([ToolContourCompare.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolContourCompare.cs), [ToolDistance.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolDistance.cs), [ToolSurface.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolSurface.cs), [ToolText.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolText.cs), [ToolObjectCounting.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolObjectCounting.cs), [ToolPreprocess.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolPreprocess.cs)) bằng `OnPropertyChanged(nameof(...))` đích danh.
   - **Task 437 (Pipeline Ảnh 20-25MP & Debounce Nhập Liệu)**:
     - Trong `GetNodeOutputImageForPreview(Mat raw, ToolGraphNodeViewModel node)`: Tái sử dụng snapshot `raw` hợp lệ trong RAM cho `ImageSource`, triệt tiêu hoàn toàn việc đọc và decode lại ảnh 25MP từ đĩa mỗi chu kỳ preview ([ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs)).
     - Giải phóng an toàn ma trận trung gian khi khử méo `ChessboardCalibrationService.Undistort`.
     - Nâng `RefreshDebounceIntervalMs` lên 50ms cho timer gộp preview, đảm bảo trải nghiệm gõ phím/kéo slider tức thì, giữ nguyên phản hồi thời gian thực cho thao tác ROI.

## 4. Các sự kiện & thay đổi gần đây
- **Tasks 435, 436, 437**: Hoàn thành đợt rà soát và tối ưu hóa hiệu năng toàn diện Tool Editor; 100% test PASSED, 0 Errors.
- **Task 434**: Bổ sung tùy chọn màu vùng che Đen / Trắng cho Tool Preprocess ROI Masking; kiểm thử 8/8 test PASSED 100%.
- **Task 433**: Hiện thực hóa thành công 2 thuật toán tăng tốc `MvpShapeMatch3` (3.54ms - nhanh hơn 11.7x) và `MvpShapeMatchInt8` (2.74ms - nhanh hơn 15.0x, đánh bại mốc 7ms của MVP); kiểm thử 6/6 test PASSED 100%.
- **Task 432**: Tự động ẩn Cột 1 & 2, hiển thị Cột 3 & 4 (50/50) khi Chạy liên tục; khôi phục khi dừng; kiểm thử 10/10 PASSED 100%.
- **Task 431**: Nâng cấp Preview Tool Editor khi bỏ Render Canvas & Bổ sung widget tốc độ tức thời (pcs/s); kiểm thử 8/8 PASSED 100%.
- **Task 430**: Chuyển đổi thủ công chế độ spec BlobDetection (theo pixel hoặc theo mm); kiểm thử 9/9 PASSED 100%.

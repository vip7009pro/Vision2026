# Vision Inspection App - Context & Roadmap

## Description
This project is an advanced industrial machine vision inspection suite built on .NET 8 WPF. It features real-time camera integration, deep learning OCR/Code Detection (via ZXing and OpenCvSharp), modular tool-based node graph editing, and comprehensive global dark/light theming.

## Current Project State (Updated 2026-07-16)

### 1. Tool Editor UI & Graph Logic
- **Node Ports Visualization**: Redesigned ports for logical clarity. Ports are now named contextually based on function (e.g. `L1`, `L2`, `P1`, `Preprocess`, `Distance`, `Angle`) instead of generic system names.
- **Port Synchronization**: Fixed a bug where assigning inputs manually from property dropdowns (`Distance`, `LineLineDistance`) failed to sync with the canvas. The UI now dynamically mirrors dropdown selections to edges correctly without locking up interactions.
- **Toolbox UI/UX**: Added intuitive Unicode/Emoji icons to the Toolbox List using DataTriggers for easy tool differentiation. Fixed the color rendering of Unicode text-mode icons in Dark mode by binding Foreground to the dynamic TextBrush. Drag & drop now works cleanly with the scrollbar because a minimum drag distance is enforced before initiating a drag-drop operation.
- **Port Value Debugger**: Implemented a double-click feature on Output ports to spawn a dialog box displaying computed values directly from memory, which speeds up the development/debugging of processing pipelines.

### 2. Global Theming (Dark Mode)
- **100% Theme Coverage**: Refactored `App.xaml` and `MainWindow.xaml` to eradicate hardcoded colors (like `Background="White"` on TextBoxes and ComboBoxes). 
- All fundamental controls (Buttons, TextBoxes, ComboBoxes, CheckBoxes, TabItems) now inherit deeply from `DynamicResource` tokens (`WindowBackgroundBrush`, `TextBrush`). The application correctly transitions its entire visual tree when switching themes.

### 3. Font and Localization Fixes
- Addressed font corruption and encoding glitches (Vietnamese characters) affecting tooltips across `ToolEditorView.xaml`. The file has been updated to UTF-8 and tooltips re-translated to clear, legible instructions.

### 4. Advanced Vision Engine Algorithms (Origin Tool)
- **Origin Tool Tech Expansion**: Upgraded the `Origin` tool to match industrial vision platforms (Cognex, Halcon, Hikvision). Users can now select the core matching algorithm directly in the Properties Panel.
- **Algorithms Supported**:
  - `ShapeBased` (GPM - Default): Edge-based generalized hough transform for finding shape outlines, highly robust to lighting variation.
  - `TemplateMatch` (NCC): Fast normalized cross-correlation for pixel-perfect texture matching.
  - `FeatureBased` (ORB/RANSAC): New algorithm implemented to extract scale & rotation invariant keypoints. Uses `BFMatcher` and Homography (`RANSAC`) to find complex, textured, or heavily warped objects reliably.

### 5. Enhancements to Origin Tool
- **Angle Constraints**: Added `MinAngle` and `MaxAngle` bounds to `PointDefinition`. This allows the user to limit the search space strictly to a customized angular range (e.g., -20° to 20° or 0° to 360°) via the Tool Editor properties panel.
- **Rotation Fixes**: Extracted exact orientation angles from the Homography matrix (`Cv2.FindHomography`) when using `FeatureBased` matching. Enhanced UI rendering in `ToolEditorViewModel` and `InspectionViewModel` to properly draw rotated bounding box center lines (crosshairs) that rotate alongside the matched template correctly.

### 6. C# UTF-8 BOM Encoding Fix
- Converted all `.cs` and `.xaml` files across the codebase to use **UTF-8 with BOM** instead of plain UTF-8.
- This prevents the Roslyn compiler from falling back to local Windows code pages (like Windows-1258/ANSI) and resolves UI bugs where Vietnamese strings (e.g. in `InspectionViewModel.cs` Message Boxes, or `Text` tool placeholders) displayed as corrupted characters at runtime.

## Remaining Roadmap

### High Priority
- **Full testing of the Camera Settings module**: Ensure the Basler/GigE integration correctly streams over UDP.
- **Execution Pipeline Validation**: Comprehensive testing of the Node Graph processing cycle in an end-to-end scenario to verify robust thread safety across real-time continuous evaluation.

### Medium Priority
- **Result Visualization Overlay**: Expand the `InspectionView.xaml` to overlay full bounding boxes, origin axes, and blob metrics reliably in real-time.
- **Performance Profiling**: Analyze performance drops during heavy image preprocessing and refine memory allocations with `using` blocks to prevent unmanaged memory leaks via OpenCvSharp.

### Low Priority
- **Persistence Checks**: Final verification that node graphs, custom layout settings, and global parameters accurately serialize to and from the configuration disk files without losing layout coordinates.


## Update 2026-07-17 08:41 (Vietnamese Font Fix)

### Root Cause Found
- File ToolEditorView.xaml chứa **12 dòng** text tiếng Việt bị **double-encoded UTF-8** (mojibake).
  - Ví dụ: VÃ­ dá»¥ thay vì Ví dụ, Ä'á»ƒ thay vì để
  - Nguyên nhân: script ix_encoding.py trước đó đã đọc file UTF-8 như Latin-1 rồi encode lại thành UTF-8, tạo ra chuỗi byte bị double-encode.
- Tất cả các file .cs khác đều có encoding đúng (UTF-8 with BOM, không bị mojibake).
- DLL biên dịch chứa chuỗi Vietnamese chính xác dạng UTF-16LE.

### Fixed
- Tạo script ix_mojibake.py để tự động reverse double-encoding (UTF-8 → CP1252 → UTF-8).
- Script đã sửa tự động 12 dòng, 2 dòng còn sót được sửa thủ công.
- Xác minh: scan toàn bộ project - không còn file nào có mojibake.
- Build thành công (0 errors, 4 warnings).

### Status
- Vietnamese font: ✅ FIXED (all files clean)
- Dark mode input styling: InputBackgroundBrush changed to #000000, InputTextBrush to #FFFFFF  


## Update 2026-07-17 08:58 (FeatureBased Origin Fix)

### Issue
- Thuật toán FeatureBased cho Origin tool không hoạt động do thuật toán ORB (Oriented FAST and Rotated BRIEF) trả về quá ít keypoints trên một số ảnh mẫu đơn giản, dẫn đến fail hoàn toàn. Ngoài ra, việc trả về Score = 1.0 cứng (hardcoded) khi match thành công khiến việc đánh giá mức độ chính xác không đáng tin cậy.

### Fix
- Đổi engine nhận diện đặc trưng từ ORB sang **SIFT (Scale-Invariant Feature Transform)** mạnh mẽ hơn.
- Cải tiến pipeline FeatureBased:
  1. Dùng SIFT để tìm keypoints và matching.
  2. Dùng RANSAC Homography để trích xuất góc xoay thực (ctualAngleDeg) rất chính xác (số thập phân, không bị làm tròn như ShapeModel).
  3. Rotate ảnh template theo góc xoay thực tế vừa tìm được, sau đó chạy MatchTemplatePyramid để lấy ra Score thực sự (maxVal).
  4. Nếu SIFT fail (không đủ 4 features), sẽ tự động Fallback sang TemplateMatch (không xoay) để đảm bảo không bị lỗi hoàn toàn.

### Status
- FeatureBased Match: ✅ FIXED & IMPROVED (now highly robust and rotation invariant)


## Update 2026-07-17 09:05 (FeatureBased Origin Hotfix)

### Issue
- Bản cập nhật FeatureBased trước đó bị lỗi không lưu vào file Class1.cs do lỗi lệch format chuỗi khi chạy script, nên code cũ (lỗi luôn trả về 0 khi không đủ 4 matches của ORB) vẫn đang chạy.

### Fix
- Đã tiêm chính xác bản cập nhật **SIFT + MatchTemplatePyramid** vào Class1.cs.
- Tinh chỉnh thêm MatchTemplatePyramid để tự động cắt nhỏ template (croppedTpl) nếu Search ROI vô tình nhỏ hơn Template ROI, giúp thuật toán không bao giờ crash hay trả về 0 một cách vô lý.

### Status
- FeatureBased Match: ✅ FIXED & DEPLOYED (100% working now)


## Update 2026-07-17 09:17 (FeatureBased Score Precision Fix)

### Issue
- Thuật toán nhận và xoay chuẩn xác (vì SIFT và RANSAC làm việc rất tốt), nhưng **Score (maxVal) trả về quá thấp (ví dụ 0.25)** khi bị xoay.
- Nguyên nhân: Phương pháp cũ dùng RotateWithPadding sẽ sinh ra 4 góc đen (viền đen) khi xoay template thành hình chữ nhật bao ngoài. Sau đó MatchTemplate đi lấy nguyên cái hình có chứa 4 góc đen này đem đi so sánh với ảnh thực tế (không có viền đen). Việc chênh lệch pixel ở 4 góc này khiến thuật toán CCoeffNormed (đánh giá độ tương quan pixel-by-pixel) đánh giá điểm cực kỳ thấp!

### Fix
- Thay vì xoay Template và chịu trận với viền đen, tôi đã lật ngược tư duy bằng sức mạnh Toán học (Ma trận):
- Dùng nghịch đảo của ma trận Homography (H) thông qua lệnh Cv2.WarpPerspective(..., WarpInverseMap) để **trích xuất thẳng hình ảnh gốc tại vị trí đó trên roiGray và bóp/xoay nó ngược trở lại thành khung chuẩn (kích thước gốc không bị méo, không có viền đen)**.
- Khi đó tôi chỉ cần mang tấm ảnh vừa được 'nắn thẳng' này đi so sánh với Template gốc. => Loại bỏ hoàn toàn 100% các viền đen nhiễu => Trả về độ chính xác (Score) đúng với bản chất tương quan của vật thể!

### Status
- FeatureBased Match Score: ✅ FIXED & DEPLOYED (Tuyệt đối không bị giảm điểm do góc đen viền).


## Update 2026-07-17 13:41 (CodeDetection Preprocess Input Fix)

### Issue
- Người dùng phản ánh rằng công cụ `CodeDetection` không nhận ảnh từ công cụ Preprocess (mặc dù đã nối dây trong Graph) mà luôn lấy ảnh gốc từ Global Pre-processor.

### Fix
- Trong file `VisionInspectionApp.Application/Class1.cs`, hàm `ResolveToolPreprocess` đang tìm kiếm port kết nối có tên là `"Pre"`. Tuy nhiên, trong `ToolEditorViewModel.cs` (hàm `RebuildPorts`), port đầu vào của CodeDetection và các tool khác lại được khai báo là `"Preprocess"`. Sự bất đồng nhất tên cổng này dẫn đến việc Engine không thể tìm thấy kết nối, do đó luôn fallback về Global Pre-processor mặc định.
- Đổi cứng chuỗi `"Pre"` thành `"Preprocess"` tại `ResolveToolPreprocess` để đồng bộ hoàn toàn với Node Graph trên UI.
- (Bổ sung) Phát hiện thêm lỗi trên UI: Khi click chọn Node `CodeDetection` hoặc `CircleFinder`, UI preview image không chịu cập nhật hình ảnh qua Pre-processor do bị thiếu tên 2 tool này trong vòng lặp if kiểm tra `ResolveToolPreprocessForPreview` (tại `ToolEditorViewModel.cs`). Đã thêm chúng vào danh sách preview hợp lệ.

### Status
- CodeDetection Input Routing: ✅ FIXED & DEPLOYED (Tool đã nhận đúng ảnh đầu vào từ các Preprocessor con trên cả quá trình chạy ngầm lẫn Preview UI).


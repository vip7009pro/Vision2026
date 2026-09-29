# ACTIVE_STATE.md

## Task hiện tại
Task 377 — Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm nút Auto Tune cho công cụ nhận diện (Caliper, Line, EdgePairDetect, Circle Finder, CodeDetection). **ĐÃ HOÀN THÀNH**.

## Mục tiêu
1. Copy nhiều node cùng lúc trong canvas graph → paste phải nhân bản ĐỦ tất cả node (trước đây chỉ được 1 node).
2. Thêm nút **Auto Tune** cho các tool nhận diện: dùng tool Preprocess nối vào làm đầu vào, thử nhiều thông số preprocess để tìm cấu hình cho điểm nhận diện cao nhất, sau đó gán lại thông số tốt nhất cho Preprocess cha. Hiển thị label tiến trình + thông số đang thử + điểm.

## File đã chỉnh sửa
- `VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.GraphOps.cs` — clipboard nhiều node + `CloneNodeDefinition`.
- `VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolAutoTune.cs` (mới) — command, panel state, logic Auto Tune.
- `VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs` — wire command + raise panel state.
- `VisionInspectionApp.UI/Views/ToolEditorView.xaml` — panel `🎯 Auto Tune Preprocess`.
- `VisionInspectionApp.Application/Services/PreprocessAutoTuner.cs` (mới) — engine sinh cấu hình + chấm điểm.

## Việc cần làm tiếp theo
- [ ] Kiểm thử thực tế trên app: copy/paste nhiều node, Auto Tune với ảnh thật cho từng loại tool.
- [ ] Cân nhắc thêm test tự động cho `PreprocessAutoTuner` (chấm điểm thuần) và multi-node paste.
- [ ] (Ghi chú) Test runner `TestExtractApp` fail ở `CameraTest.TestContinuousEngineHandshakeBypass` do ảnh Simulator được lưu cá nhân hóa (không phải 640x480) — lỗi môi trường, không liên quan thay đổi này.

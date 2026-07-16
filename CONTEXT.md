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

## Remaining Roadmap

### High Priority
- **Full testing of the Camera Settings module**: Ensure the Basler/GigE integration correctly streams over UDP.
- **Execution Pipeline Validation**: Comprehensive testing of the Node Graph processing cycle in an end-to-end scenario to verify robust thread safety across real-time continuous evaluation.

### Medium Priority
- **Result Visualization Overlay**: Expand the `InspectionView.xaml` to overlay full bounding boxes, origin axes, and blob metrics reliably in real-time.
- **Performance Profiling**: Analyze performance drops during heavy image preprocessing and refine memory allocations with `using` blocks to prevent unmanaged memory leaks via OpenCvSharp.

### Low Priority
- **Persistence Checks**: Final verification that node graphs, custom layout settings, and global parameters accurately serialize to and from the configuration disk files without losing layout coordinates.

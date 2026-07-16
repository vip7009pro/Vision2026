# Vision Inspection App - Context & Roadmap

## Description
This project is an advanced industrial machine vision inspection suite built on .NET 8 WPF. It features real-time camera integration, deep learning OCR/Code Detection (via ZXing and OpenCvSharp), modular tool-based node graph editing, and comprehensive global dark/light theming.

## Current Project State (Updated 2026-07-16)

### 1. Tool Editor UI & Graph Logic
- **Node Ports Visualization**: Redesigned ports for logical clarity. Ports are now named contextually based on function (e.g. `L1`, `L2`, `P1`, `Preprocess`, `Distance`, `Angle`) instead of generic system names.
- **Port Synchronization**: Fixed a bug where assigning inputs manually from property dropdowns (`Distance`, `LineLineDistance`) failed to sync with the canvas. The UI now dynamically mirrors dropdown selections to edges correctly without locking up interactions.
- **Toolbox UI/UX**: Added intuitive Unicode/Emoji icons to the Toolbox List using DataTriggers for easy tool differentiation. Drag & drop now works cleanly with the scrollbar because a minimum drag distance is enforced before initiating a drag-drop operation.
- **Port Value Debugger**: Implemented a double-click feature on Output ports to spawn a dialog box displaying computed values directly from memory, which speeds up the development/debugging of processing pipelines.

### 2. Global Theming (Dark Mode)
- **100% Theme Coverage**: Refactored `App.xaml` and `MainWindow.xaml` to eradicate hardcoded colors (like `Background="White"` on TextBoxes and ComboBoxes). 
- All fundamental controls (Buttons, TextBoxes, ComboBoxes, CheckBoxes, TabItems) now inherit deeply from `DynamicResource` tokens (`WindowBackgroundBrush`, `TextBrush`). The application correctly transitions its entire visual tree when switching themes.

### 3. Font and Localization Fixes
- Addressed font corruption and encoding glitches (Vietnamese characters) affecting tooltips across `ToolEditorView.xaml`. The file has been updated to UTF-8 and tooltips re-translated to clear, legible instructions.

## Remaining Roadmap

### High Priority
- **Full testing of the Camera Settings module**: Ensure the Basler/GigE integration correctly streams over UDP.
- **Execution Pipeline Validation**: Comprehensive testing of the Node Graph processing cycle in an end-to-end scenario to verify robust thread safety across real-time continuous evaluation.

### Medium Priority
- **Result Visualization Overlay**: Expand the `InspectionView.xaml` to overlay full bounding boxes, origin axes, and blob metrics reliably in real-time.
- **Performance Profiling**: Analyze performance drops during heavy image preprocessing and refine memory allocations with `using` blocks to prevent unmanaged memory leaks via OpenCvSharp.

### Low Priority
- **Persistence Checks**: Final verification that node graphs, custom layout settings, and global parameters accurately serialize to and from the configuration disk files without losing layout coordinates.

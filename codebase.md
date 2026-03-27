# Vision Inspection WPF Application - Detailed Codebase Analysis

## Project Structure Overview
- **VisionInspectionApp.UI** - Main WPF Desktop Application (net8.0-windows)
- **VisionInspectionApp.Application** - Business Logic & Services (net8.0)
- **VisionInspectionApp.Models** - Data Models & Configuration (net8.0)
- **VisionInspectionApp.Persistence** - Data Storage/JSON Config (net8.0)
- **VisionInspectionApp.VisionEngine** - Image Processing & Algorithms (net8.0)

## Architecture Pattern
- MVVM (Model-View-ViewModel) using Community Toolkit MVVM
- Dependency Injection via Microsoft.Extensions.Hosting
- Separation of Concerns: UI (Views) separated from Logic (ViewModels/Services)

## Key Components Identified

### UI Layer (VisionInspectionApp.UI)
**Main Window Structure:**
- Tab-based interface with 5 main modules
- Uses data templates for dynamic view switching
- Keyboard bindings (Ctrl+Z/Y for undo/redo)

**ViewModels (7 total):**
1. **MainWindowViewModel** - Container/orchestrator for all tabs
2. **TeachViewModel** - Template/tool definition and training interface
3. **ToolEditorViewModel** - Visual tool graph editor with node-based programming
4. **InspectionViewModel** - Automated inspection and measurement results
5. **CalibrationViewModel** - Camera calibration (pixels-to-mm conversion)
6. **ManualInspectionViewModel** - Manual measurement tools
7. **TeachTarget** - Enum for teach target types

**Views (5 total):**
- TeachView.xaml
- ToolEditorView.xaml
- InspectionView.xaml
- CalibrationView.xaml
- ManualInspectionView.xaml

**Controls:**
- **ImageViewerControl** - Advanced image display with:
  - Zoom/Pan capabilities
  - Interactive overlay rendering (rects, lines, points, crosshairs)
  - ROI (Region of Interest) editing
  - Line selection
  - Multiple interaction modes
  - Transform support (scale, translate)
- **GridSplitter** - Resizable vertical divider between toolbox and canvas

**Overlay Items:**
- OverlayRectItem (rectangles with labels)
- OverlayPointItem (circles/points)
- OverlayLineItem (lines with labels)
- LineSelection, RoiSelection records

**Services:**
1. **GlobalAppSettingsService** - Persists user settings (pixels/mm) to %AppData%
2. **SharedImageContext** - Thread-safe image sharing between ViewModels
3. **UndoRedoManager** - Command pattern implementation for undo/redo support

**Converters:**
- BoolToOkNgConverter (boolean → "OK"/"NG" strings)
- FlexibleDoubleConverter (flexible number parsing/formatting)
- HexToBrushConverter (hex string → SolidColorBrush for swatches)

### Application Layer (VisionInspectionApp.Application)
**Interfaces:**
- **IConfigService** - Configuration loading/saving abstraction
- **IInspectionService** - High-level inspection execution

**Classes:**
- InspectionService implementation
- Multiple result types (EdgePairDetectResult, CircleFinderResult, DiameterResult, etc.)
- LineDistance, Point, Angle measurement results

### Models Layer (VisionInspectionApp.Models)
**Core Data Structures:**
- **VisionConfig** - Master configuration container with all tool definitions
- **Roi** - Region of Interest (X, Y, Width, Height)
- **Point2dModel** - 2D point coordinates
- **PreprocessSettings** - Image preprocessing parameters

**Tool Definitions (20+ tool types):**
1. Origin - Reference point
2. Point - Template matching or edge detection
3. Line - Edge/line detection
4. Caliper - Edge pair detection
5. LinePairDetection - Parallel lines
6. EdgePairDetect - Pair of edges
7. CircleFinder - Circle detection (3 algorithms)
8. Diameter - Measurement from circle
9. Distance - Point-to-point measurement
10. LineLineDistance - Distance between lines
11. PointLineDistance - Distance from point to line
12. Angle - Angle between lines
13. EdgePair - Measurement between edges
14. Condition - Boolean logic evaluation
15. BlobDetection - Connected component analysis
16. SurfaceCompare - Template-based surface defect detection
17. CodeDetection - Barcode/QR code reading (5 symbologies)
18. DefectRoi - Defect region marking
19. Preprocess - Image preprocessing node
20. ToolGraph - Node-based flow graph
21. TextNode - Text rendering with variable substitution and color conditions

**Algorithms & Modes:**
- CircleFindAlgorithm: HoughCircles, ContourFit, RANSAC
- CaliperOrientation: Vertical, Horizontal
- EdgePolarity: Any, DarkToLight, LightToDark
- BlobPolarity: DarkOnLight, LightOnDark
- CodeSymbology: QR, Barcode1D, DataMatrix, PDF417, Aztec
- IlluminationCorrection: None, BackgroundSubtract, FlatFieldNormalize, CLAHE
- LineLineDistanceMode: 5 different calculation methods

**Tool Graph:**
- Nodes and edges for visual programming
- Node types correspond to tool definitions
- Supports parameter input/output connections

### Persistence Layer (VisionInspectionApp.Persistence)
**JsonConfigService:**
- Implements IConfigService
- Loads/saves configurations from JSON files
- Stores in "configs" directory
- Handles template image path normalization
- Automatic directory creation

### Vision Engine Layer (VisionInspectionApp.VisionEngine)
**Core Algorithms:**
- Geometry2D utilities (distance calculations, segment operations)
- ImagePreprocessor - Illumination correction, blur, morphology
- PatternMatcher - Template matching via ShapeModel
- DistanceCalculator - Measurement calculations
- LineDetector - Canny + Hough line detection
- IDefectDetector interface
- DefectDetector implementation

**Key Features:**
- Illumination correction (4 preset modes)
- CLAHE support
- Morphological operations
- Edge detection and analysis
- Template-based pattern matching
- Geometric calculations for 2D shapes

## Technology Stack
**Frameworks:**
- .NET 8.0 (Modern C# language features, implicit usings, nullable reference types)
- WPF (Windows Presentation Foundation - XAML-based UI)

**Key NuGet Packages:**
- OpenCvSharp4 v4.10.0 (Computer vision algorithms)
- CommunityToolkit.MVVM v8.4.0 (MVVM pattern helpers)
- Microsoft.Extensions.Hosting v9.0.0 (Dependency injection)
- ZXing.Net v0.16.9 (Barcode/QR code detection)
- System.Text.Json v9.0.0 (JSON serialization)
- System.Drawing.Common v8.0.0 (Graphics utilities)

## Data Flow

1. **Teach Tab** → Loads image → Defines tool templates & measurements
2. **Tool Editor** → Creates visual node graph → Chains tools together
3. **Calibration Tab** → Establishes pixel-to-mm conversion
4. **Manual Inspection** → Manual measurements on live images
5. **Inspection Tab** → Runs complete vision workflow → Reports pass/fail results

## Key Features Identified

1. **Image Processing Pipeline**
   - Preprocessing with illumination correction
   - Multi-algorithm support for each measurement type
   - Real-time preview and visualization

2. **Visual Tool Definition**
   - Template-based training (teach mode)
   - ROI definition with interactive editing
   - Point, line, circle, blob detection

3. **Measurements & Tolerancing**
   - Point-to-point distances
   - Line-to-line distances
   - Point-to-line distances
   - Angles, diameters, edges
   - Binary pass/fail evaluation with nominal + tolerances

4. **Code Detection**
   - Multiple barcode/QR formats supported
   - Integration with ZXing library

5. **Advanced Features**
   - Defect detection and inspection
   - Surface comparison (template-based)
   - Blob detection with ROI inclusion/exclusion
   - Undo/redo system
   - Node-based workflow graphs
   - Configuration persistence
   - Global settings management

## Configuration Model
- Product code-based configuration
- JSON-persisted tool definitions
- Template images stored alongside configs
- Visual tool graph as part of config
- Support for multiple products/SKUs

## UI/UX Features
- Tab-based modular interface
- Advanced image viewer with zoom/pan
- Real-time overlay visualization (multiple shapes)
- Undo/redo support with keyboard shortcuts (Ctrl+Z/Y)
- Region of Interest interactive editing
- Flexible number input converters
- Pass/fail status visualization
- Resizable UI layout with GridSplitter
- Native Color Picker dialog integration

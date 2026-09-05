using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        private void RequestAutoSave()
        {
            IsDirty = true;
            _autoSavePending = true;
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    
        private void AutoSaveNow()
        {
            _autoSaveTimer.Stop();
            if (!_autoSavePending)
            {
                return;
            }
    
            _autoSavePending = false;
            if (_config is null)
            {
                return;
            }
    
            if (!string.IsNullOrWhiteSpace(CurrentJobFilePath) && !string.IsNullOrWhiteSpace(CurrentTempWorkingDir))
            {
                SyncToolGraphToConfig();
                _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
            }
        }
    
        [ObservableProperty]
        private string? _currentJobFilePath;

        [ObservableProperty]
        private string? _currentTempWorkingDir;

        public VisionConfig? Config => _config;

        public ICommand OpenJobCommand { get; }
        public ICommand SaveJobCommand { get; }
        public ICommand SaveJobAsCommand { get; }
    
        public void CloseJob()
        {
            Nodes.Clear();
            Edges.Clear();
            _config = null;
            CurrentJobFilePath = null;
            CurrentTempWorkingDir = null;
            ProductCode = string.Empty;
            _lastRun = null;
            _lastRunError = null;
            SelectedNode = null;
            FinalOverlayItems = new List<OverlayItem>();
            SelectedNodeOverlayItems = new List<OverlayItem>();
            SelectedNodePreviewImage = null;
            FinalPreviewImage = null;
            _cachedFinalPreviewImage = null;
            LinePreviewImage = null;
            PointEdgePreviewImage = null;
            BlobThresholdPreviewImage = null;
            Origin_TemplatePreviewImage = null;

            ClearAllImageSourceCache();

            _sharedImage.SetImage(null);

            LastResult = null;
            SpecResults?.Clear();
            ToolTimings?.Clear();
            CodeDetectionResults?.Clear();
            SurfaceCompareDebugItems?.Clear();
            DebugTemplate = null;
            DebugCurrent = null;
            DebugBinary = null;
            DebugDiff = null;

            RefreshPreviews();
            IsDirty = false;
        }

        partial void OnIsDirtyChanged(bool value)
        {
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                var title = System.Windows.Application.Current.MainWindow.Title;
                if (value && !title.EndsWith("*"))
                {
                    System.Windows.Application.Current.MainWindow.Title = title + "*";
                }
                else if (!value && title.EndsWith("*"))
                {
                    System.Windows.Application.Current.MainWindow.Title = title.TrimEnd('*');
                }
            }
        }
    
        private void SyncEdgesToConfig()
        {
            if (_config?.ToolGraph is null)
            {
                return;
            }
    
            _config.ToolGraph.Edges = Edges.Select(e => new ToolGraphEdge { FromNodeId = e.FromNodeId, ToNodeId = e.ToNodeId, FromPort = e.FromPort, ToPort = e.ToPort }).ToList();
        }
    
        public Func<bool>? CheckShouldAutoRunOnJobLoad { get; set; }

        public void LoadJobFromFile(string filePath, bool? autoRun = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            bool shouldRun = autoRun ?? (CheckShouldAutoRunOnJobLoad?.Invoke() ?? false);

            ClearActiveGraph();
            ClearAllImageSourceCache();
            try
            {
                _config = _jobService.LoadJob(filePath, out var tempDir);
                CurrentJobFilePath = filePath;
                CurrentTempWorkingDir = tempDir;
                ProductCode = _config.ProductCode;
                EnsureTemplatePathsAbsolute(_config);
                RefreshOriginTemplatePreview();
                Nodes.Clear();
                Edges.Clear();
                foreach (var n in _config.ToolGraph.Nodes)
                {
                    var vm = new ToolGraphNodeViewModel
                    {
                        Id = n.Id,
                        Type = n.Type,
                        RefName = n.RefName,
                        X = n.X,
                        Y = n.Y,
                        InputCount = n.InputCount
                    };
                    vm.PropertyChanged += Node_PropertyChanged;
                    Nodes.Add(vm);
                }
    
                foreach (var e in _config.ToolGraph.Edges)
                {
                    var from = Nodes.FirstOrDefault(x => string.Equals(x.Id, e.FromNodeId, StringComparison.OrdinalIgnoreCase));
                    var to = Nodes.FirstOrDefault(x => string.Equals(x.Id, e.ToNodeId, StringComparison.OrdinalIgnoreCase));
                    if (from is null || to is null)
                    {
                        continue;
                    }
    
                    Edges.Add(new ToolGraphEdgeViewModel(from, to, e.FromPort, e.ToPort));
                }

                // Nạp ảnh mẫu dạy học (Teach Image) đa tầng: Ưu tiên ảnh gốc từ Decoupled Disk Cache -> Fallback Thumbnail -> Tương thích ngược Job cũ
                Mat? loadedTeachMat = null;
                string? teachMatKey = null;

                // Tầng 1: Kiểm tra Decoupled Disk Cache ngoài
                string teachCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "TeachImages");
                if (!string.IsNullOrWhiteSpace(ProductCode))
                {
                    string prdCache = Path.Combine(teachCacheDir, $"{ProductCode}_teach.png");
                    if (File.Exists(prdCache) && new FileInfo(prdCache).Length > 0)
                    {
                        try
                        {
                            loadedTeachMat = Cv2.ImRead(prdCache, ImreadModes.Color);
                            if (loadedTeachMat != null && !loadedTeachMat.Empty())
                            {
                                teachMatKey = prdCache;
                            }
                        }
                        catch { }
                    }
                }

                if ((loadedTeachMat == null || loadedTeachMat.Empty()) && !string.IsNullOrWhiteSpace(filePath))
                {
                    string jobName = Path.GetFileNameWithoutExtension(filePath);
                    string jobCache = Path.Combine(teachCacheDir, $"{jobName}_teach.png");
                    if (File.Exists(jobCache) && new FileInfo(jobCache).Length > 0)
                    {
                        try
                        {
                            loadedTeachMat = Cv2.ImRead(jobCache, ImreadModes.Color);
                            if (loadedTeachMat != null && !loadedTeachMat.Empty())
                            {
                                teachMatKey = jobCache;
                            }
                        }
                        catch { }
                    }
                }

                var urlSrc = _config.ImageSources?.FirstOrDefault(x => x.SourceType == ImageSourceType.Url);
                if ((loadedTeachMat == null || loadedTeachMat.Empty()) && urlSrc != null && !string.IsNullOrWhiteSpace(urlSrc.ImageUrl))
                {
                    loadedTeachMat = TryLoadUrlImageFromDiskCache(urlSrc.ImageUrl);
                    if (loadedTeachMat != null && !loadedTeachMat.Empty())
                    {
                        teachMatKey = urlSrc.ImageUrl;
                    }
                }

                // Tầng 2: Tương thích ngược với file .job cũ (nếu có teach_image.png được giải nén ra)
                if ((loadedTeachMat == null || loadedTeachMat.Empty()) && !string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
                {
                    string legacyTeachPng = Path.Combine(tempDir, "teach_image.png");
                    if (File.Exists(legacyTeachPng) && new FileInfo(legacyTeachPng).Length > 0)
                    {
                        try
                        {
                            loadedTeachMat = Cv2.ImRead(legacyTeachPng, ImreadModes.Color);
                            if (loadedTeachMat != null && !loadedTeachMat.Empty())
                            {
                                teachMatKey = legacyTeachPng;
                                // Tự động trích xuất sao lưu ra Cache ngoài để lần sau mở nhanh và làm sạch file .job
                                if (!string.IsNullOrWhiteSpace(ProductCode))
                                {
                                    Directory.CreateDirectory(teachCacheDir);
                                    Cv2.ImWrite(Path.Combine(teachCacheDir, $"{ProductCode}_teach.png"), loadedTeachMat);
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Tầng 3: Nạp Thumbnail nén siêu nhẹ teach_preview.jpg từ gói .job mới (nếu máy mới chưa có ảnh gốc ngoài)
                if ((loadedTeachMat == null || loadedTeachMat.Empty()) && !string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
                {
                    string previewThumb = Path.Combine(tempDir, "teach_preview.jpg");
                    if (File.Exists(previewThumb) && new FileInfo(previewThumb).Length > 0)
                    {
                        try
                        {
                            loadedTeachMat = Cv2.ImRead(previewThumb, ImreadModes.Color);
                            if (loadedTeachMat != null && !loadedTeachMat.Empty())
                            {
                                teachMatKey = previewThumb;
                            }
                        }
                        catch { }
                    }
                }

                // Áp dụng ảnh mẫu đã nạp vào Cache và SharedImage
                if (loadedTeachMat != null && !loadedTeachMat.Empty())
                {
                    var srcDef = urlSrc ?? _config.ImageSources?.FirstOrDefault();
                    if (srcDef != null)
                    {
                        SetImageSourceCache(srcDef.Name, srcDef.ImageUrl ?? teachMatKey ?? "teach_image", loadedTeachMat);
                    }
                    _sharedImage.SetImage(loadedTeachMat);
                }
                else if (urlSrc != null && !string.IsNullOrWhiteSpace(urlSrc.ImageUrl))
                {
                    // Tầng 4: Nếu hoàn toàn chưa có ảnh và có URL từ xa -> tải ngầm bất đồng bộ
                    ScheduleAsyncUrlImageFetch(urlSrc.Name, urlSrc.ImageUrl);
                }

                SelectedNode = Nodes.Count > 0 ? Nodes[0] : null;
                RaiseToolPropertyPanelsChanged();
                RefreshOriginTemplatePreview();
                OnPropertyChanged(nameof(PixelsPerMm));
                IsDirty = false;
                _recentJobsService?.AddRecentJob(filePath);
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - " + Path.GetFileName(CurrentJobFilePath);
                }

                TriggerAutoFitGraph();

                // Tự động nạp và áp dụng thông số Camera riêng biệt của Job này khi mở Job lần đầu
                var imageSourceNode = _config.ToolGraph.Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
                var imgSourceDef = (imageSourceNode != null 
                    ? _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, imageSourceNode.RefName, StringComparison.OrdinalIgnoreCase)) 
                    : null) 
                    ?? _config.ImageSources.FirstOrDefault(x => x.SourceType == ImageSourceType.Camera) 
                    ?? _config.ImageSources.FirstOrDefault();

                // Đảm bảo cấu hình đèn cho Job và nạp xuống thiết bị khi mở Job
                if (imgSourceDef != null)
                {
                    EnsureImageSourceLightingParams(imgSourceDef);
                }

                _ = Task.Run(async () =>
                {
                    // 1. Áp dụng Camera Parameters
                    if (imgSourceDef?.CameraParams != null)
                    {
                        try
                        {
                            await _cameraService.ApplyParametersAsync(imgSourceDef.CameraParams);
                            await Task.Delay(100);
                        }
                        catch { }
                    }

                    // 2. Áp dụng Lighting Parameters
                    if (imgSourceDef?.LightingParams != null && imgSourceDef.LightingParams.Enabled && _lightingControllerService != null && _lightingControllerService.IsConnected)
                    {
                        try
                        {
                            int count = imgSourceDef.LightingParams.ChannelCount == 8 ? 8 : 4;
                            for (int i = 0; i < count && i < imgSourceDef.LightingParams.Channels.Count; i++)
                            {
                                var ch = imgSourceDef.LightingParams.Channels[i];
                                await _lightingControllerService.SetChannelPowerAsync(ch.ChannelIndex, ch.IsEnabled);
                                if (ch.IsEnabled)
                                {
                                    await _lightingControllerService.SetBrightnessAsync(ch.ChannelIndex, ch.Brightness);
                                }
                            }
                        }
                        catch { }
                    }

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (shouldRun)
                    {
                        if (dispatcher != null)
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                RefreshOriginTemplatePreview();
                                OnRunOnceClicked();
                            });
                        }
                        else
                        {
                            RefreshOriginTemplatePreview();
                            OnRunOnceClicked();
                        }
                    }
                    else
                    {
                        if (dispatcher != null)
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                RefreshOriginTemplatePreview();
                                RefreshPreviews();
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load job: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ClearActiveGraph();
            }
        }

        private void OpenJob()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Job Files (*.job)|*.job|All Files (*.*)|*.*",
                Title = "Open Vision Job"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadJobFromFile(dialog.FileName);
            }
        }
    
        public static void CommitFocusedBinding()
        {
            try
            {
                if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    var focused = System.Windows.Input.Keyboard.FocusedElement as System.Windows.FrameworkElement;
                    if (focused is System.Windows.Controls.TextBox tb)
                    {
                        var be = System.Windows.Data.BindingOperations.GetBindingExpression(tb, System.Windows.Controls.TextBox.TextProperty);
                        be?.UpdateSource();
                    }
                    else if (focused is System.Windows.Controls.Primitives.TextBoxBase tbb)
                    {
                        var be = System.Windows.Data.BindingOperations.GetBindingExpression(tbb, System.Windows.Controls.TextBox.TextProperty);
                        be?.UpdateSource();
                    }
                }
                else
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        var focused = System.Windows.Input.Keyboard.FocusedElement as System.Windows.FrameworkElement;
                        if (focused is System.Windows.Controls.TextBox tb)
                        {
                            var be = System.Windows.Data.BindingOperations.GetBindingExpression(tb, System.Windows.Controls.TextBox.TextProperty);
                            be?.UpdateSource();
                        }
                        else if (focused is System.Windows.Controls.Primitives.TextBoxBase tbb)
                        {
                            var be = System.Windows.Data.BindingOperations.GetBindingExpression(tbb, System.Windows.Controls.TextBox.TextProperty);
                            be?.UpdateSource();
                        }
                    });
                }
            }
            catch
            {
                // Ignore any UI binding update exception during disposal
            }
        }

        public void SaveJob()
        {
            CommitFocusedBinding();
            if (string.IsNullOrWhiteSpace(CurrentJobFilePath))
            {
                SaveJobAs();
                return;
            }

            if (_config is null)
            {
                _config = new VisionConfig { ProductCode = ProductCode };
            }

            if (string.IsNullOrWhiteSpace(CurrentTempWorkingDir))
            {
                CurrentTempWorkingDir = Path.Combine(Path.GetTempPath(), "Vision2026", "Jobs", Guid.NewGuid().ToString());
                Directory.CreateDirectory(CurrentTempWorkingDir);
            }

            _config.ProductCode = ProductCode;
            SyncToolGraphToConfig();
            
            try
            {
                EnsureTeachImageSavedToCacheAndPreview();
                _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
                IsDirty = false;
                _recentJobsService?.AddRecentJob(CurrentJobFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save job: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RefreshPreviews();
        }

        private void EnsureTeachImageSavedToCacheAndPreview()
        {
            try
            {
                // 1. Dọn dẹp tệp teach_image.png cũ trong CurrentTempWorkingDir (nếu có) để triệt để không nhồi vào gói .job
                if (!string.IsNullOrWhiteSpace(CurrentTempWorkingDir) && Directory.Exists(CurrentTempWorkingDir))
                {
                    string oldTeachPath = Path.Combine(CurrentTempWorkingDir, "teach_image.png");
                    if (File.Exists(oldTeachPath))
                    {
                        try { File.Delete(oldTeachPath); } catch { }
                    }
                }

                // 2. Tìm ảnh mẫu gốc chất lượng cao từ cache ImageSource hoặc _sharedImage
                Mat? originalMat = null;
                bool shouldDisposeOriginal = false;

                var urlSource = _config?.ImageSources?.FirstOrDefault(x => x.SourceType == ImageSourceType.Url || x.SourceType == ImageSourceType.File);
                if (urlSource != null)
                {
                    var cached = GetImageSourceCache(urlSource.Name);
                    if (cached != null && !cached.Empty())
                    {
                        originalMat = cached;
                    }
                    else if (!string.IsNullOrWhiteSpace(urlSource.ImageUrl))
                    {
                        originalMat = TryLoadUrlImageFromDiskCache(urlSource.ImageUrl);
                        if (originalMat != null) shouldDisposeOriginal = true;
                    }
                }

                if (originalMat == null || originalMat.Empty())
                {
                    var snap = _sharedImage.GetSnapshot();
                    if (snap != null && !snap.Empty())
                    {
                        originalMat = snap;
                        shouldDisposeOriginal = true;
                    }
                }

                if (originalMat == null || originalMat.Empty())
                {
                    return;
                }

                try
                {
                    // 3. Lưu ảnh mẫu gốc độ nét cao vào Decoupled Disk Cache ngoài (Cache/TeachImages/)
                    string teachCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "TeachImages");
                    Directory.CreateDirectory(teachCacheDir);

                    // A. Lưu theo ProductCode
                    if (!string.IsNullOrWhiteSpace(ProductCode))
                    {
                        string prdCache = Path.Combine(teachCacheDir, $"{ProductCode}_teach.png");
                        Cv2.ImWrite(prdCache, originalMat);
                    }

                    // B. Lưu theo Job Name
                    if (!string.IsNullOrWhiteSpace(CurrentJobFilePath))
                    {
                        string jobName = Path.GetFileNameWithoutExtension(CurrentJobFilePath);
                        if (!string.IsNullOrWhiteSpace(jobName) && !string.Equals(jobName, ProductCode, StringComparison.OrdinalIgnoreCase))
                        {
                            string jobCache = Path.Combine(teachCacheDir, $"{jobName}_teach.png");
                            Cv2.ImWrite(jobCache, originalMat);
                        }
                    }

                    // C. Nếu có ImageUrl từ xa, lưu vào hash cache
                    if (urlSource != null && !string.IsNullOrWhiteSpace(urlSource.ImageUrl))
                    {
                        string teachPathByHash = JobManagerViewModel.GetDiskCacheFilePath(urlSource.ImageUrl);
                        Cv2.ImWrite(teachPathByHash, originalMat);
                    }

                    // 4. Tạo Thumbnail JPEG nén siêu nhẹ teach_preview.jpg trong CurrentTempWorkingDir (khoảng 20KB - 30KB)
                    // để phục vụ xem trước khi mở Job trên máy tính lạ chưa có cache ngoài
                    if (!string.IsNullOrWhiteSpace(CurrentTempWorkingDir) && Directory.Exists(CurrentTempWorkingDir))
                    {
                        string previewThumbPath = Path.Combine(CurrentTempWorkingDir, "teach_preview.jpg");

                        // Tính toán kích thước thu nhỏ (chiều dài nhất tối đa 1280px)
                        int maxDim = 1280;
                        int origW = originalMat.Width;
                        int origH = originalMat.Height;
                        Mat? thumbMat = null;
                        bool shouldDisposeThumb = false;

                        if (origW > maxDim || origH > maxDim)
                        {
                            double scale = Math.Min((double)maxDim / origW, (double)maxDim / origH);
                            int newW = Math.Max(1, (int)Math.Round(origW * scale));
                            int newH = Math.Max(1, (int)Math.Round(origH * scale));
                            thumbMat = new Mat();
                            Cv2.Resize(originalMat, thumbMat, new OpenCvSharp.Size(newW, newH), 0, 0, InterpolationFlags.Area);
                            shouldDisposeThumb = true;
                        }
                        else
                        {
                            thumbMat = originalMat;
                        }

                        try
                        {
                            var prms = new ImageEncodingParam(ImwriteFlags.JpegQuality, 55);
                            Cv2.ImWrite(previewThumbPath, thumbMat, prms);
                        }
                        finally
                        {
                            if (shouldDisposeThumb && thumbMat != null)
                            {
                                thumbMat.Dispose();
                            }
                        }
                    }
                }
                finally
                {
                    if (shouldDisposeOriginal && originalMat != null)
                    {
                        originalMat.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureTeachImageSavedToCacheAndPreview error: {ex.Message}");
            }
        }

        /// <summary>
        /// Chuẩn bị Job cho triển khai thực tế tại chuyền OQC: Nếu Job đang dùng nguồn ảnh Url/File để teach từ xa,
        /// tự động chuyển về Camera (bảo lưu 100% thông số Camera OQC và đèn gốc) và lưu lại Job trước khi upload lên Server.
        /// </summary>
        public bool PrepareJobForProductionUpload()
        {
            bool switched = false;
            if (_config?.ImageSources != null)
            {
                foreach (var imgSource in _config.ImageSources)
                {
                    if (imgSource.SourceType == ImageSourceType.Url || imgSource.SourceType == ImageSourceType.File)
                    {
                        imgSource.SourceType = ImageSourceType.Camera;
                        switched = true;
                    }
                }
            }

            SaveJob();
            return switched;
        }

        private void SaveJobAs()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Job Files (*.job)|*.job|All Files (*.*)|*.*",
                Title = "Save Vision Job As"
            };

            if (dialog.ShowDialog() == true)
            {
                CurrentJobFilePath = dialog.FileName;
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - " + Path.GetFileName(CurrentJobFilePath);
                }
                if (string.IsNullOrWhiteSpace(CurrentTempWorkingDir))
                {
                    CurrentTempWorkingDir = Path.Combine(Path.GetTempPath(), "Vision2026", "Jobs", Guid.NewGuid().ToString());
                    Directory.CreateDirectory(CurrentTempWorkingDir);
                }
                SaveJob();
            }
        }
    
        public void SyncToolGraphToConfig()
        {
            CommitFocusedBinding();
            if (_config?.ToolGraph is null)
            {
                return;
            }

            var validRefNames = new HashSet<string>(Nodes.Select(n => n.RefName).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            _config.PreprocessNodes.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Points.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Lines.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Calipers.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Distances.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.LineToLineDistances.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.PointToLineDistances.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Angles.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Conditions.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.TextNodes.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ImageSources.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.BlobDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.SurfaceCompares.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.LinePairDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.EdgePairs.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.EdgePairDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CircleFinders.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.Diameters.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CodeDetections.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.DbNodes?.RemoveAll(x => !validRefNames.Contains(x.RefName));
            _config.Crops.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ColorDiffs.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ImgArithmetics.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ImageOutputs?.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.SegmentLineDistances?.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ContourCompares.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CreatePoints.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CreateLines.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CreateRects.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.CreateCircles.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ResultTransfers?.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.PlcReads?.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.PlcWrites?.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.PlcWaits?.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.PlcTriggers?.RemoveAll(x => !validRefNames.Contains(x.Name));
            _config.ToolGraph.Nodes.Clear();
            foreach (var n in Nodes)
            {
                _config.ToolGraph.Nodes.Add(new ToolGraphNode { Id = n.Id, Type = n.Type, RefName = n.RefName, X = n.X, Y = n.Y, InputCount = n.InputCount });
            }
    
            _config.ToolGraph.Edges.Clear();
            foreach (var e in Edges)
            {
                _config.ToolGraph.Edges.Add(new ToolGraphEdge { FromNodeId = e.FromNodeId, ToNodeId = e.ToNodeId, FromPort = e.FromPort, ToPort = e.ToPort });
            }
        }

        public void InitializeWithConfig(VisionConfig config)
        {
            ClearActiveGraph();
            _config = config;
            ProductCode = config.ProductCode;
            Nodes.Clear();
            Edges.Clear();
            if (config.ToolGraph != null)
            {
                foreach (var n in config.ToolGraph.Nodes)
                {
                    var vm = new ToolGraphNodeViewModel
                    {
                        Id = n.Id,
                        Type = n.Type,
                        RefName = n.RefName,
                        X = n.X,
                        Y = n.Y,
                        InputCount = n.InputCount
                    };
                    vm.PropertyChanged += Node_PropertyChanged;
                    Nodes.Add(vm);
                }
            }
            SelectedNode = Nodes.Count > 0 ? Nodes[0] : null;
            RaiseToolPropertyPanelsChanged();
        }

        /// <summary>
        /// Tự động cập nhật mã sản phẩm và lưu vào file .job đang mở khi được gán từ ProductAssignDialog.
        /// </summary>
        public void ApplyAssignedProductCode(string productCode, string jobFilePath)
        {
            if (string.IsNullOrWhiteSpace(productCode)) return;

            ProductCode = productCode;

            if (_config != null &&
                (string.Equals(CurrentJobFilePath, jobFilePath, StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(CurrentJobFilePath) ||
                 CurrentJobFilePath == "-"))
            {
                _config.ProductCode = productCode;
                IsDirty = true;

                if (!string.IsNullOrWhiteSpace(CurrentJobFilePath) &&
                    !string.IsNullOrWhiteSpace(CurrentTempWorkingDir) &&
                    File.Exists(CurrentJobFilePath))
                {
                    try
                    {
                        _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
                        IsDirty = false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ApplyAssignedProductCode] SaveJob error: {ex.Message}");
                    }
                }
            }
        }
    }
}

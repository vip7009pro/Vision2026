using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel
{
    // ==================== CỘT KẾT QUẢ KIỂM TRA (CHẠY LIÊN TỤC) ====================

    [ObservableProperty]
    private GridLength _debugResultsRowHeight = new(1, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _debugSlotPreviewRowHeight = new(0);

    public double DebugResultsMinHeight => IsRunningContinuous ? 0 : 120;
    public double DebugSlotPreviewMinHeight => IsRunningContinuous ? 120 : 0;

    private void UpdateDebugColumnRowLayout(bool isContinuous)
    {
        if (isContinuous)
        {
            // Khi Chạy liên tục: Ẩn dòng kết quả kiểm tra hiện tại, chỉ hiển thị preview của nấc được chọn
            DebugResultsRowHeight = new GridLength(0);
            DebugSlotPreviewRowHeight = new GridLength(1.0, GridUnitType.Star);
        }
        else
        {
            DebugResultsRowHeight = new GridLength(1.0, GridUnitType.Star);
            DebugSlotPreviewRowHeight = new GridLength(0);
        }

        OnPropertyChanged(nameof(DebugResultsMinHeight));
        OnPropertyChanged(nameof(DebugSlotPreviewMinHeight));
    }

    // ==================== KHỐI THÔNG TIN TÓM TẮT (1/10 TRÊN TOOLBAR PREVIEW NẤC) ====================

    private static readonly SolidColorBrush SummaryEmptyFg = CreateFrozenBrush(Color.FromRgb(107, 114, 128));
    private static readonly SolidColorBrush SummaryEmptyBorder = CreateFrozenBrush(Color.FromRgb(148, 163, 184));

    [ObservableProperty]
    private string _recentSlotSummaryStatusText = "--";

    [ObservableProperty]
    private string _recentSlotSummarySubText = "Chọn một nấc (1–20) để xem trạng thái";

    [ObservableProperty]
    private Brush _recentSlotSummaryForegroundBrush = SummaryEmptyFg;

    [ObservableProperty]
    private Brush _recentSlotSummaryBorderBrush = SummaryEmptyBorder;

    [ObservableProperty]
    private int _continuousOkCount;

    [ObservableProperty]
    private int _continuousNgCount;

    [ObservableProperty]
    private string _latestContinuousTotalTimeMsText = "0.0";

    private int _continuousOkCountRaw;
    private int _continuousNgCountRaw;
    private double _latestContinuousTotalTimeMsRaw;

    /// <summary>
    /// Ghi nhận kết quả một con hàng vừa hoàn thành trong chế độ chạy liên tục (an toàn đa luồng).
    /// </summary>
    public void RecordContinuousPartCompleted(InspectionResult? result, bool updateStatsNow)
    {
        ProcessedImageCount++;
        RecordPieceCompleted();

        if (result != null && result.Pass)
        {
            Interlocked.Increment(ref _continuousOkCountRaw);
        }
        else
        {
            Interlocked.Increment(ref _continuousNgCountRaw);
        }

        double totalMs = result?.Timings?.TotalMs ?? 0.0;
        Volatile.Write(ref _latestContinuousTotalTimeMsRaw, totalMs);

        if (updateStatsNow)
        {
            UpdateContinuousStats();
        }
    }

    private void SyncContinuousSummaryMetrics()
    {
        int ok = Volatile.Read(ref _continuousOkCountRaw);
        int ng = Volatile.Read(ref _continuousNgCountRaw);
        double totalMs = Volatile.Read(ref _latestContinuousTotalTimeMsRaw);

        ContinuousOkCount = ok;
        ContinuousNgCount = ng;
        LatestContinuousTotalTimeMsText = $"{totalMs:0.0}";
    }

    private void ResetContinuousCounters()
    {
        ProcessedImageCount = 0;
        Interlocked.Exchange(ref _continuousOkCountRaw, 0);
        Interlocked.Exchange(ref _continuousNgCountRaw, 0);
        Volatile.Write(ref _latestContinuousTotalTimeMsRaw, 0.0);
        ContinuousOkCount = 0;
        ContinuousNgCount = 0;
        LatestContinuousTotalTimeMsText = "0.0";
    }

    // ==================== PREVIEW NẤC ĐƯỢC CHỌN & GHIM NẤC TRONG CHẠY LIÊN TỤC ====================

    [ObservableProperty]
    private ImageSource? _recentSlotPreviewImage;

    [ObservableProperty]
    private List<OverlayItem> _recentSlotOverlayItems = new();

    [ObservableProperty]
    private bool _autoFindNearestNgAfterSelectedSlot = true;

    [ObservableProperty]
    private bool _isPinnedRecentSlot;

    [ObservableProperty]
    private int _pinnedRecentSlotIndex = -1;

    [ObservableProperty]
    private string _recentSlotPreviewTitleText = "🖼️ Preview Nấc Thanh 20 Nấc";

    [ObservableProperty]
    private string _recentSlotPreviewBadgeText = string.Empty;

    [ObservableProperty]
    private bool _hasRecentSlotPreviewBadge;

    [ObservableProperty]
    private Brush _recentSlotPreviewBadgeBg = EmptySlotBrush;

    [ObservableProperty]
    private Brush _recentSlotPreviewBadgeBorder = EmptySlotBorder;

    [ObservableProperty]
    private Brush _recentSlotPreviewBadgeFg = Brushes.Gray;

    [ObservableProperty]
    private string _recentSlotEmptyHintText = "Chọn một nấc trên thanh 20 nấc (1–20) để xem preview.";

    public event Action? RequestAutoFitRecentSlotPreview;

    private bool _hasPinnedNgOutputDisplayed;
    private RecentPartInspectionSlotItem? _displayedRecentSlotItem;
    private RecentPartInspectionSlotItem? _pendingOrDisplayedRecentSlotItem;
    private int _displayedRecentSlotIndex = -1;
    private int _displayedRecentSlotSelectedIndex = -1;
    private bool _displayedRecentSlotLoadedFromOutput;
    private bool _displayedRecentSlotIsPinnedNg;
    private string? _displayedRecentSlotSavedPath;
    private long _recentSlotPreviewLoadVersion;

    partial void OnAutoFindNearestNgAfterSelectedSlotChanged(bool value)
    {
        RefreshRecentSlotPreviewAfterAutoNgOptionChanged(value);
        StatusBarText = value
            ? "🔎 Đã bật: Tự động hiển thị NG tại nấc chọn hoặc nấc gần nhất sau nấc chọn (khi toàn bộ OK sẽ hiện nấc chọn)."
            : "🔎 Đã tắt tìm NG sau nấc chọn: Chỉ hiển thị đúng con hàng tại nấc được chọn.";
    }

    private static readonly SolidColorBrush PinnedWaitBg = CreateFrozenBrush(Color.FromArgb(40, 217, 119, 6));
    private static readonly SolidColorBrush PinnedWaitBorder = CreateFrozenBrush(Color.FromRgb(217, 119, 6));
    private static readonly SolidColorBrush PinnedWaitFg = CreateFrozenBrush(Color.FromRgb(217, 119, 6));
    private static readonly SolidColorBrush SlotOkBadgeBg = CreateFrozenBrush(Color.FromArgb(36, 16, 185, 129));
    private static readonly SolidColorBrush SlotNgBadgeBg = CreateFrozenBrush(Color.FromArgb(42, 239, 68, 68));

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    [RelayCommand]
    public void TogglePinRecentSlot()
    {
        if (IsPinnedRecentSlot)
        {
            UnpinRecentSlot();
        }
        else
        {
            PinSelectedRecentSlot();
        }
    }

    private void ResolveTargetSlotForDisplay_Locked(
        int selectedIdx,
        out int resolvedSlotIndex,
        out RecentPartInspectionSlotItem? resolvedItem)
    {
        if (selectedIdx < 0 || selectedIdx >= 20)
        {
            resolvedSlotIndex = -1;
            resolvedItem = null;
            return;
        }

        var itemAtSelected = selectedIdx < _recentPartItems.Count
            ? _recentPartItems[selectedIdx]
            : null;

        if (itemAtSelected != null && !itemAtSelected.IsOk)
        {
            resolvedSlotIndex = selectedIdx;
            resolvedItem = itemAtSelected;
            return;
        }

        if (AutoFindNearestNgAfterSelectedSlot)
        {
            int limit = Math.Min(_recentPartItems.Count, 20);
            for (int i = selectedIdx + 1; i < limit; i++)
            {
                var candidate = _recentPartItems[i];
                if (!candidate.IsOk)
                {
                    resolvedSlotIndex = i;
                    resolvedItem = candidate;
                    return;
                }
            }
        }

        // Khi toàn bộ 20 nấc (hoặc các nấc từ nấc chọn trở về sau) đều OK -> quay về hiển thị nấc được chọn
        resolvedSlotIndex = selectedIdx;
        resolvedItem = itemAtSelected;
    }

    private void RefreshRecentSlotPreviewAfterAutoNgOptionChanged(bool autoFindEnabled)
    {
        int slotIndex = IsPinnedRecentSlot && PinnedRecentSlotIndex >= 0
            ? PinnedRecentSlotIndex
            : SelectedRecentSlotIndex;

        if (slotIndex < 0 || slotIndex >= 20)
        {
            return;
        }

        RecentPartInspectionSlotItem? resolvedItem = null;
        int resolvedIdx = slotIndex;
        bool isPinnedNg = false;

        lock (_recentPartsLock)
        {
            ResolveTargetSlotForDisplay_Locked(slotIndex, out resolvedIdx, out resolvedItem);

            if (IsPinnedRecentSlot)
            {
                if (resolvedItem != null && !resolvedItem.IsOk)
                {
                    _hasPinnedNgOutputDisplayed = true;
                    isPinnedNg = true;
                }
                else if (!autoFindEnabled && _displayedRecentSlotIndex != slotIndex)
                {
                    // Tắt tùy chọn tìm NG sau nấc chọn và ảnh ghim hiện tại thuộc nấc phía sau -> quay về đúng nấc được chọn
                    _hasPinnedNgOutputDisplayed = false;
                }
                else if (_hasPinnedNgOutputDisplayed)
                {
                    return;
                }
            }
        }

        LoadRecentSlotPreviewItem(
            resolvedItem,
            selectedSlotIndex: slotIndex,
            resolvedSlotIndex: resolvedIdx,
            preferOutputForNg: resolvedItem != null && !resolvedItem.IsOk,
            autoFit: false,
            isPinnedNg: isPinnedNg);
    }

    public void PinSelectedRecentSlot()
    {
        int selectedIdx = SelectedRecentSlotIndex;
        if (selectedIdx < 0 || selectedIdx >= 20)
        {
            IsPinnedRecentSlot = false;
            PinnedRecentSlotIndex = -1;
            lock (_recentPartsLock)
            {
                _hasPinnedNgOutputDisplayed = false;
            }
            RecentSlotEmptyHintText = "⚠️ Chưa có nấc nào được chọn. Hãy bấm chọn một nấc trên thanh 20 nấc (1–20) rồi bấm Ghim.";
            StatusBarText = "⚠️ Vui lòng chọn một nấc trên thanh 20 nấc trước khi bấm Ghim.";
            return;
        }

        IsPinnedRecentSlot = true;
        PinnedRecentSlotIndex = selectedIdx;

        RecentPartInspectionSlotItem? resolvedItem = null;
        int resolvedIdx = selectedIdx;
        bool hasNgToPin = false;
        lock (_recentPartsLock)
        {
            ResolveTargetSlotForDisplay_Locked(selectedIdx, out resolvedIdx, out resolvedItem);
            hasNgToPin = resolvedItem != null && !resolvedItem.IsOk;
            _hasPinnedNgOutputDisplayed = hasNgToPin;
        }

        int selectedDisplay = ToDisplaySlotNumber(selectedIdx);
        int resolvedDisplay = ToDisplaySlotNumber(resolvedIdx);

        if (hasNgToPin && resolvedItem != null)
        {
            // Dù đang Live View, nếu có ảnh NG tại nấc được chọn (hoặc nấc sau gần nhất khi bật checkbox) -> show ảnh output lên preview mới thêm
            LoadRecentSlotPreviewItem(resolvedItem, selectedIdx, resolvedIdx, preferOutputForNg: true, autoFit: true, isPinnedNg: true);
            string slotDesc = resolvedIdx == selectedIdx ? $"#{selectedDisplay}" : $"#{resolvedDisplay} (NG sau #{selectedDisplay})";
            StatusBarText = $"📌 Đã ghim nấc #{selectedDisplay}: Đang hiển thị ảnh Output NG tại nấc {slotDesc} lúc {resolvedItem.FormattedTimestamp} (giữ cố định khi Live View).";
        }
        else
        {
            // Chưa có ảnh NG tại nấc chọn / các nấc sau: hiển thị nấc được chọn và sẵn sàng bắt NG ngay khi xuất hiện
            LoadRecentSlotPreviewItem(resolvedItem, selectedIdx, resolvedIdx, preferOutputForNg: false, autoFit: false, isPinnedNg: false);
            StatusBarText = $"📌 Đã ghim nấc #{selectedDisplay}: Sẵn sàng hiển thị ngay ảnh Output khi có NG.";
        }

        UpdateRecentPartsVisuals();
    }

    public void UnpinRecentSlot()
    {
        lock (_recentPartsLock)
        {
            _hasPinnedNgOutputDisplayed = false;
        }
        IsPinnedRecentSlot = false;
        PinnedRecentSlotIndex = -1;

        SyncRecentSlotPreviewFromCurrentSelection(autoFit: false);
        UpdateRecentPartsVisuals();
        StatusBarText = "🔓 Đã bỏ ghim nấc trên thanh 20 nấc.";
    }

    private void ExitHistoricalViewPreservingSelection()
    {
        if (!IsViewingHistoricalRecentPart)
        {
            return;
        }

        IsViewingHistoricalRecentPart = false;
        HistoricalViewingStatusText = string.Empty;

        if (_hasSavedHistoricalOverlayVisibility)
        {
            ShowResultOverlay = _savedShowResultOverlay;
            ShowRoisInSelectedPreview = _savedShowRoisInSelectedPreview;
            _hasSavedHistoricalOverlayVisibility = false;
        }

        RequestAutoFitImagePreview?.Invoke();
        RefreshPreviewsNow();
    }

    private void HandleSelectRecentPartSlotContinuous(int slotIndex, RecentPartInspectionSlotItem? _)
    {
        // Trong khi Chạy liên tục: giữ nguyên Live View ở cột Preview chính,
        // chỉ cập nhật preview của nấc được chọn (hoặc nấc NG gần nhất sau nó) vào hàng dưới cột Kết quả kiểm tra.
        ExitHistoricalViewPreservingSelection();

        SelectedRecentSlotIndex = slotIndex;

        RecentPartInspectionSlotItem? resolvedItem = null;
        int resolvedIdx = slotIndex;
        bool isPinnedNg = false;

        lock (_recentPartsLock)
        {
            ResolveTargetSlotForDisplay_Locked(slotIndex, out resolvedIdx, out resolvedItem);

            if (IsPinnedRecentSlot)
            {
                PinnedRecentSlotIndex = slotIndex;
                isPinnedNg = resolvedItem != null && !resolvedItem.IsOk;
                _hasPinnedNgOutputDisplayed = isPinnedNg;
            }
        }

        LoadRecentSlotPreviewItem(
            resolvedItem,
            selectedSlotIndex: slotIndex,
            resolvedSlotIndex: resolvedIdx,
            preferOutputForNg: resolvedItem != null && !resolvedItem.IsOk,
            autoFit: true,
            isPinnedNg: isPinnedNg);

        int slotDisplay = ToDisplaySlotNumber(slotIndex);
        int resolvedDisplay = ToDisplaySlotNumber(resolvedIdx);

        if (resolvedItem == null)
        {
            StatusBarText = IsPinnedRecentSlot
                ? $"📌 Đang ghim nấc #{slotDisplay}: Chưa có dữ liệu sản phẩm, đang chờ ảnh NG..."
                : $"⚪ Nấc #{slotDisplay} chưa có dữ liệu sản phẩm.";
        }
        else if (resolvedIdx != slotIndex)
        {
            StatusBarText = $"🔍 Đang hiển thị nấc NG gần nhất #{resolvedDisplay} (sau nấc #{slotDisplay}) lúc {resolvedItem.FormattedTimestamp} — Live View vẫn tiếp tục chạy.";
        }
        else
        {
            StatusBarText = $"🔍 Preview nấc #{slotDisplay} ({resolvedItem.StatusText}) lúc {resolvedItem.FormattedTimestamp} — Live View vẫn tiếp tục chạy.";
        }

        UpdateRecentPartsVisuals();
    }

    private void SyncRecentSlotPreviewFromCurrentSelection(bool autoFit)
    {
        int slotIndex = IsPinnedRecentSlot && PinnedRecentSlotIndex >= 0
            ? PinnedRecentSlotIndex
            : SelectedRecentSlotIndex;

        if (slotIndex < 0 || slotIndex >= 20)
        {
            ClearRecentSlotPreviewDisplayOnly();
            return;
        }

        RecentPartInspectionSlotItem? resolvedItem = null;
        int resolvedIdx = slotIndex;
        bool isPinnedNg = false;
        lock (_recentPartsLock)
        {
            ResolveTargetSlotForDisplay_Locked(slotIndex, out resolvedIdx, out resolvedItem);

            if (IsPinnedRecentSlot && _hasPinnedNgOutputDisplayed && (resolvedItem == null || resolvedItem.IsOk))
            {
                return;
            }

            isPinnedNg = IsPinnedRecentSlot && resolvedItem != null && !resolvedItem.IsOk;
            if (isPinnedNg)
            {
                _hasPinnedNgOutputDisplayed = true;
            }
        }

        LoadRecentSlotPreviewItem(
            resolvedItem,
            selectedSlotIndex: slotIndex,
            resolvedSlotIndex: resolvedIdx,
            preferOutputForNg: resolvedItem != null && !resolvedItem.IsOk,
            autoFit: autoFit,
            isPinnedNg: isPinnedNg);
    }

    private void HandleRecentPartHistoryUpdatedAfterPush()
    {
        bool isPinned = IsPinnedRecentSlot;
        int targetIndex = isPinned ? PinnedRecentSlotIndex : SelectedRecentSlotIndex;
        if (targetIndex < 0 || targetIndex >= 20)
        {
            return;
        }

        if (!isPinned && !IsRunningContinuous)
        {
            return;
        }

        RecentPartInspectionSlotItem? resolvedItem = null;
        int resolvedIdx = targetIndex;
        bool isPinnedNg = false;
        Mat? snapshotClone = null;

        lock (_recentPartsLock)
        {
            ResolveTargetSlotForDisplay_Locked(targetIndex, out resolvedIdx, out resolvedItem);

            if (isPinned)
            {
                if (resolvedItem != null && !resolvedItem.IsOk)
                {
                    _hasPinnedNgOutputDisplayed = true;
                    isPinnedNg = true;
                }
                else if (_hasPinnedNgOutputDisplayed)
                {
                    // Đang ghim ảnh NG tại nấc đã chọn: không cho phép ảnh OK mới đẩy vào ghi đè ảnh NG đang ghim.
                    return;
                }
            }

            // Nếu sản phẩm cần hiển thị chính là item đang hiển thị (vd: con hàng NG vừa trượt thêm 1 nấc từ #1 -> #2),
            // giữ nguyên bitmap đang xem, chỉ cập nhật tiêu đề chỉ số nấc để đạt 0ms overhead.
            if (resolvedItem != null && ReferenceEquals(resolvedItem, _pendingOrDisplayedRecentSlotItem))
            {
                var capturedItem = resolvedItem;
                int capturedSelected = targetIndex;
                int capturedResolved = resolvedIdx;
                bool capturedIsPinnedNg = isPinnedNg;
                DispatchSlotPreviewUiUpdate(() =>
                {
                    if (ReferenceEquals(capturedItem, _displayedRecentSlotItem))
                    {
                        _displayedRecentSlotIndex = capturedResolved;
                        _displayedRecentSlotSelectedIndex = capturedSelected;
                        _displayedRecentSlotIsPinnedNg = capturedIsPinnedNg;
                        UpdateRecentSlotPreviewHeader(
                            capturedItem,
                            capturedSelected,
                            capturedResolved,
                            capturedIsPinnedNg,
                            _displayedRecentSlotLoadedFromOutput);
                    }
                });
                return;
            }

            if (resolvedItem?.MemorySnapshot != null && !resolvedItem.MemorySnapshot.IsDisposed && !resolvedItem.MemorySnapshot.Empty())
            {
                try
                {
                    snapshotClone = resolvedItem.MemorySnapshot.Clone();
                }
                catch
                {
                    snapshotClone = null;
                }
            }
        }

        LoadRecentSlotPreviewItemWithClonedMat(
            resolvedItem,
            snapshotClone,
            selectedSlotIndex: targetIndex,
            resolvedSlotIndex: resolvedIdx,
            preferOutputForNg: resolvedItem != null && !resolvedItem.IsOk,
            autoFit: false,
            isPinnedNg: isPinnedNg);
    }

    private void ClearRecentSlotPreviewDisplayOnly()
    {
        Interlocked.Increment(ref _recentSlotPreviewLoadVersion);
        _displayedRecentSlotItem = null;
        _pendingOrDisplayedRecentSlotItem = null;
        _displayedRecentSlotIndex = -1;
        _displayedRecentSlotSelectedIndex = -1;
        _displayedRecentSlotLoadedFromOutput = false;
        _displayedRecentSlotIsPinnedNg = false;
        _displayedRecentSlotSavedPath = null;
        RecentSlotPreviewImage = null;
        RecentSlotOverlayItems = new List<OverlayItem>();
        UpdateRecentSlotPreviewHeader(null, -1, -1, isPinnedNg: false, loadedFromOutputFile: false);
    }

    public void ClearRecentSlotPreviewState()
    {
        Interlocked.Increment(ref _recentSlotPreviewLoadVersion);
        lock (_recentPartsLock)
        {
            _hasPinnedNgOutputDisplayed = false;
        }
        IsPinnedRecentSlot = false;
        PinnedRecentSlotIndex = -1;
        _displayedRecentSlotItem = null;
        _pendingOrDisplayedRecentSlotItem = null;
        _displayedRecentSlotIndex = -1;
        _displayedRecentSlotSelectedIndex = -1;
        _displayedRecentSlotLoadedFromOutput = false;
        _displayedRecentSlotIsPinnedNg = false;
        _displayedRecentSlotSavedPath = null;
        RecentSlotPreviewImage = null;
        RecentSlotOverlayItems = new List<OverlayItem>();
        UpdateRecentSlotPreviewHeader(null, -1, -1, isPinnedNg: false, loadedFromOutputFile: false);
    }

    private void LoadRecentSlotPreviewItem(
        RecentPartInspectionSlotItem? item,
        int selectedSlotIndex,
        int resolvedSlotIndex,
        bool preferOutputForNg,
        bool autoFit,
        bool isPinnedNg)
    {
        Mat? snapshotClone = null;
        if (item != null)
        {
            lock (_recentPartsLock)
            {
                if (item.MemorySnapshot != null && !item.MemorySnapshot.IsDisposed && !item.MemorySnapshot.Empty())
                {
                    try
                    {
                        snapshotClone = item.MemorySnapshot.Clone();
                    }
                    catch
                    {
                        snapshotClone = null;
                    }
                }
            }
        }

        LoadRecentSlotPreviewItemWithClonedMat(item, snapshotClone, selectedSlotIndex, resolvedSlotIndex, preferOutputForNg, autoFit, isPinnedNg);
    }

    private void LoadRecentSlotPreviewItemWithClonedMat(
        RecentPartInspectionSlotItem? item,
        Mat? clonedSnapshot,
        int selectedSlotIndex,
        int resolvedSlotIndex,
        bool preferOutputForNg,
        bool autoFit,
        bool isPinnedNg)
    {
        long version = Interlocked.Increment(ref _recentSlotPreviewLoadVersion);
        _pendingOrDisplayedRecentSlotItem = item;

        if (item == null)
        {
            try { clonedSnapshot?.Dispose(); } catch { }
            DispatchSlotPreviewUiUpdate(() =>
            {
                if (version != Volatile.Read(ref _recentSlotPreviewLoadVersion)) return;
                _displayedRecentSlotItem = null;
                _displayedRecentSlotIndex = resolvedSlotIndex;
                _displayedRecentSlotSelectedIndex = selectedSlotIndex;
                _displayedRecentSlotLoadedFromOutput = false;
                _displayedRecentSlotIsPinnedNg = false;
                _displayedRecentSlotSavedPath = null;
                RecentSlotPreviewImage = null;
                RecentSlotOverlayItems = new List<OverlayItem>();
                UpdateRecentSlotPreviewHeader(null, selectedSlotIndex, resolvedSlotIndex, isPinnedNg: false, loadedFromOutputFile: false);
            });
            return;
        }

        var savedPath = item.SavedImagePath;
        var savedOutputNodeName = item.SavedOutputNodeName;
        var origW = item.OriginalImageWidth;
        var origH = item.OriginalImageHeight;
        var itemResult = item.Result;
        var isOutputIncludeOverlay = IsSavedOutputOverlayIncluded(savedOutputNodeName);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            // Headless unit test: thực thi đồng bộ để assert kết quả tức thì
            using (clonedSnapshot)
            {
                var (bmp, fromOutputFile) = DecodeSlotBitmapSync(savedPath, clonedSnapshot, origW, origH);
                ApplyDecodedSlotPreview(version, item, selectedSlotIndex, resolvedSlotIndex, bmp, fromOutputFile, isOutputIncludeOverlay, itemResult, origW, origH, isPinnedNg, autoFit);
            }
            return;
        }

        // Trong ứng dụng WPF thực tế: giải mã ảnh & đọc tệp trên luồng nền để không làm lag Live View
        _ = Task.Run(async () =>
        {
            try
            {
                using (clonedSnapshot)
                {
                    if (version != Volatile.Read(ref _recentSlotPreviewLoadVersion)) return;

                    BitmapSource? fileBmp = TryLoadSavedImageOutputFile(savedPath);
                    if (fileBmp != null)
                    {
                        await dispatcher.InvokeAsync(() =>
                        {
                            ApplyDecodedSlotPreview(version, item, selectedSlotIndex, resolvedSlotIndex, fileBmp, loadedFromOutputFile: true, isOutputIncludeOverlay, itemResult, origW, origH, isPinnedNg, autoFit);
                        }, DispatcherPriority.Background);
                        return;
                    }

                    // Nếu tệp chưa kịp ghi xong xuống đĩa (AsyncImageSaver đang ghi), hiển thị ngay snapshot RAM + overlay
                    if (clonedSnapshot != null && !clonedSnapshot.IsDisposed && !clonedSnapshot.Empty())
                    {
                        var ramBmp = ConvertSnapshotMatToBitmapSource(clonedSnapshot, origW, origH);
                        if (ramBmp != null)
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                ApplyDecodedSlotPreview(version, item, selectedSlotIndex, resolvedSlotIndex, ramBmp, loadedFromOutputFile: false, outputIncludesOverlay: false, itemResult, origW, origH, isPinnedNg, autoFit);
                            }, DispatcherPriority.Background);
                        }
                    }

                    // Nếu có đường dẫn ImageOutput đang chờ AsyncImageSaver ghi xong (đặc biệt cho ảnh NG output),
                    // kiểm tra ngắn tối đa ~800ms để tự động nâng cấp lên đúng ảnh output đã lưu trên đĩa.
                    if (preferOutputForNg && !string.IsNullOrEmpty(savedPath))
                    {
                        for (int attempt = 0; attempt < 10; attempt++)
                        {
                            if (version != Volatile.Read(ref _recentSlotPreviewLoadVersion)) return;
                            await Task.Delay(80).ConfigureAwait(false);
                            if (version != Volatile.Read(ref _recentSlotPreviewLoadVersion)) return;

                            var readyFileBmp = TryLoadSavedImageOutputFile(savedPath);
                            if (readyFileBmp != null)
                            {
                                await dispatcher.InvokeAsync(() =>
                                {
                                    int currentSelected = IsPinnedRecentSlot && PinnedRecentSlotIndex >= 0
                                        ? PinnedRecentSlotIndex
                                        : (_displayedRecentSlotSelectedIndex >= 0 ? _displayedRecentSlotSelectedIndex : selectedSlotIndex);
                                    int currentResolved = _displayedRecentSlotIndex >= 0 ? _displayedRecentSlotIndex : resolvedSlotIndex;
                                    ApplyDecodedSlotPreview(version, item, currentSelected, currentResolved, readyFileBmp, loadedFromOutputFile: true, isOutputIncludeOverlay, itemResult, origW, origH, isPinnedNg, autoFit: false);
                                }, DispatcherPriority.Background);
                                return;
                            }
                        }
                    }

                    // Trường hợp không có cả file lẫn RAM snapshot
                    if (clonedSnapshot == null || clonedSnapshot.IsDisposed || clonedSnapshot.Empty())
                    {
                        await dispatcher.InvokeAsync(() =>
                        {
                            ApplyDecodedSlotPreview(version, item, selectedSlotIndex, resolvedSlotIndex, null, loadedFromOutputFile: false, outputIncludesOverlay: false, itemResult, origW, origH, isPinnedNg, autoFit);
                        }, DispatcherPriority.Background);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadRecentSlotPreview] {ex.Message}");
            }
        });
    }

    private void DispatchSlotPreviewUiUpdate(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }

    private void ApplyDecodedSlotPreview(
        long version,
        RecentPartInspectionSlotItem item,
        int selectedSlotIndex,
        int resolvedSlotIndex,
        BitmapSource? bmp,
        bool loadedFromOutputFile,
        bool outputIncludesOverlay,
        InspectionResult? result,
        int origW,
        int origH,
        bool isPinnedNg,
        bool autoFit)
    {
        if (version != Volatile.Read(ref _recentSlotPreviewLoadVersion))
        {
            return;
        }

        _displayedRecentSlotItem = item;
        _displayedRecentSlotIndex = resolvedSlotIndex;
        _displayedRecentSlotSelectedIndex = selectedSlotIndex;
        _displayedRecentSlotLoadedFromOutput = loadedFromOutputFile;
        _displayedRecentSlotIsPinnedNg = isPinnedNg;
        _displayedRecentSlotSavedPath = item.SavedImagePath;

        List<OverlayItem> overlays;
        if (loadedFromOutputFile && outputIncludesOverlay)
        {
            // Ảnh output trên đĩa đã được BurnOverlaysToMat vẽ sẵn overlay/bảng kết quả
            overlays = new List<OverlayItem>();
        }
        else if (result != null)
        {
            int w = origW > 0 ? origW : (bmp?.PixelWidth ?? 0);
            int h = origH > 0 ? origH : (bmp?.PixelHeight ?? 0);
            overlays = BuildOverlaysForHistoricalResult(result, w, h);
        }
        else
        {
            overlays = new List<OverlayItem>();
        }

        if (autoFit)
        {
            RequestAutoFitRecentSlotPreview?.Invoke();
        }

        RecentSlotOverlayItems = overlays;
        RecentSlotPreviewImage = bmp;
        UpdateRecentSlotPreviewHeader(item, selectedSlotIndex, resolvedSlotIndex, isPinnedNg, loadedFromOutputFile);
    }

    /// <summary>
    /// Quy đổi chỉ số nội bộ (0..19) sang số thứ tự hiển thị trên thanh 20 nấc (1..20).
    /// </summary>
    private static int ToDisplaySlotNumber(int slotIndex) => slotIndex + 1;

    private void ApplyRecentSlotSummaryDisplay(
        string statusText,
        string subText,
        Brush foregroundBrush,
        Brush borderBrush)
    {
        RecentSlotSummaryStatusText = statusText;
        RecentSlotSummarySubText = subText;
        RecentSlotSummaryForegroundBrush = foregroundBrush;
        RecentSlotSummaryBorderBrush = borderBrush;
    }

    private void UpdateRecentSlotPreviewHeader(
        RecentPartInspectionSlotItem? item,
        int selectedSlotIndex,
        int resolvedSlotIndex,
        bool isPinnedNg,
        bool loadedFromOutputFile)
    {
        if (selectedSlotIndex < 0)
        {
            RecentSlotPreviewTitleText = "🖼️ Preview Nấc Thanh 20 Nấc";
            HasRecentSlotPreviewBadge = false;
            RecentSlotPreviewBadgeText = string.Empty;
            RecentSlotEmptyHintText = "Chọn một nấc trên thanh 20 nấc (1–20) để xem preview.";
            ApplyRecentSlotSummaryDisplay(
                "--",
                "Chọn một nấc (1–20) để xem trạng thái",
                SummaryEmptyFg,
                SummaryEmptyBorder);
            return;
        }

        int selectedDisplay = ToDisplaySlotNumber(selectedSlotIndex);
        int resolvedDisplay = ToDisplaySlotNumber(resolvedSlotIndex);
        string pinPrefix = IsPinnedRecentSlot ? "📌 Đang ghim " : "🔍 ";
        if (item == null)
        {
            RecentSlotPreviewTitleText = $"{pinPrefix}Nấc #{selectedDisplay}";
            HasRecentSlotPreviewBadge = IsPinnedRecentSlot;
            RecentSlotPreviewBadgeText = IsPinnedRecentSlot ? "CHỜ NG" : string.Empty;
            RecentSlotPreviewBadgeBg = PinnedWaitBg;
            RecentSlotPreviewBadgeBorder = PinnedWaitBorder;
            RecentSlotPreviewBadgeFg = PinnedWaitFg;
            RecentSlotEmptyHintText = IsPinnedRecentSlot
                ? $"📌 Đang ghim nấc #{selectedDisplay} — Chưa có dữ liệu, tự động hiển thị ảnh Output khi có NG."
                : $"⚪ Nấc #{selectedDisplay} chưa có dữ liệu sản phẩm.";
            ApplyRecentSlotSummaryDisplay(
                "--",
                IsPinnedRecentSlot ? $"📌 Đang ghim nấc #{selectedDisplay} • Chờ dữ liệu" : $"Nấc #{selectedDisplay} • Chưa có dữ liệu",
                IsPinnedRecentSlot ? PinnedWaitFg : SummaryEmptyFg,
                IsPinnedRecentSlot ? PinnedWaitBorder : SummaryEmptyBorder);
            return;
        }

        string sourceKind = loadedFromOutputFile ? "Ảnh Output" : (item.MemorySnapshot != null ? "RAM Output" : "Không có ảnh");
        string slotLabel = resolvedSlotIndex != selectedSlotIndex
            ? (IsPinnedRecentSlot ? $"#{selectedDisplay} ➔ Nấc #{resolvedDisplay}" : $"Nấc #{resolvedDisplay} (NG sau #{selectedDisplay})")
            : $"Nấc #{selectedDisplay}";

        RecentSlotPreviewTitleText = $"{pinPrefix}{slotLabel} • {item.FormattedTimestamp} ({sourceKind})";
        HasRecentSlotPreviewBadge = true;

        string summaryPinPrefix = IsPinnedRecentSlot ? "📌 " : string.Empty;
        string summarySubText = $"{summaryPinPrefix}{slotLabel} • {item.FormattedTimestamp}";

        if (!item.IsOk)
        {
            RecentSlotPreviewBadgeText = isPinnedNg ? "NG • GHIM OUTPUT" : "NG";
            RecentSlotPreviewBadgeBg = SlotNgBadgeBg;
            RecentSlotPreviewBadgeBorder = NgSlotBorder;
            RecentSlotPreviewBadgeFg = NgSlotBrush;
            ApplyRecentSlotSummaryDisplay("NG", summarySubText, NgSlotBrush, NgSlotBorder);
        }
        else
        {
            RecentSlotPreviewBadgeText = IsPinnedRecentSlot ? "OK • CHỜ NG" : "OK";
            RecentSlotPreviewBadgeBg = IsPinnedRecentSlot ? PinnedWaitBg : SlotOkBadgeBg;
            RecentSlotPreviewBadgeBorder = IsPinnedRecentSlot ? PinnedWaitBorder : OkSlotBorder;
            RecentSlotPreviewBadgeFg = IsPinnedRecentSlot ? PinnedWaitFg : OkSlotBrush;
            ApplyRecentSlotSummaryDisplay("OK", summarySubText, OkSlotBrush, OkSlotBorder);
        }

        RecentSlotEmptyHintText = $"⚠️ {slotLabel} ({item.StatusText}) lúc {item.FormattedTimestamp}: Không có dữ liệu ảnh.";
    }

    private bool IsSavedOutputOverlayIncluded(string? savedOutputNodeName)
    {
        if (_config?.ImageOutputs == null || _config.ImageOutputs.Count == 0)
        {
            return true;
        }

        var def = !string.IsNullOrWhiteSpace(savedOutputNodeName)
            ? _config.ImageOutputs.FirstOrDefault(x => string.Equals(x.Name, savedOutputNodeName, StringComparison.OrdinalIgnoreCase))
            : null;
        def ??= _config.ImageOutputs.FirstOrDefault(x => x.EnableOutput) ?? _config.ImageOutputs.FirstOrDefault();
        return def?.IncludeOverlay ?? true;
    }

    private static (BitmapSource? Bitmap, bool LoadedFromOutputFile) DecodeSlotBitmapSync(
        string? savedPath,
        Mat? clonedSnapshot,
        int origW,
        int origH)
    {
        var fileBmp = TryLoadSavedImageOutputFile(savedPath);
        if (fileBmp != null)
        {
            return (fileBmp, true);
        }

        if (clonedSnapshot != null && !clonedSnapshot.IsDisposed && !clonedSnapshot.Empty())
        {
            return (ConvertSnapshotMatToBitmapSource(clonedSnapshot, origW, origH), false);
        }

        return (null, false);
    }

    private static BitmapSource? TryLoadSavedImageOutputFile(string? savedPath)
    {
        if (string.IsNullOrWhiteSpace(savedPath) || !File.Exists(savedPath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(savedPath);
            if (info.Length <= 0)
            {
                return null;
            }

            // Đảm bảo AsyncImageSaver đã đóng handle ghi xong trước khi đọc
            using (new FileStream(savedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
            }

            using var fileMat = Cv2.ImRead(savedPath, ImreadModes.Color);
            if (fileMat != null && !fileMat.Empty())
            {
                return fileMat.ToBitmapSourceForDisplay();
            }
        }
        catch
        {
            // Bỏ qua nếu tệp đang được ghi dở hoặc không đọc được
        }

        return null;
    }

    private static BitmapSource? ConvertSnapshotMatToBitmapSource(Mat snapshot, int origW, int origH)
    {
        if (snapshot == null || snapshot.IsDisposed || snapshot.Empty())
        {
            return null;
        }

        var bmp = snapshot.ToBitmapSourceForDisplay();
        if (bmp != null && origW > 0 && origH > 0 && (bmp.PixelWidth != origW || bmp.PixelHeight != origH))
        {
            bmp.RegisterDisplaySourcePixelSize(origW, origH);
        }

        return bmp;
    }

    private List<OverlayItem> BuildOverlaysForHistoricalResult(InspectionResult result, int imageWidth, int imageHeight)
    {
        var dst = new List<OverlayItem>();
        if (result == null)
        {
            return dst;
        }

        BuildFinalOverlayFromRun(result, dst, _config, showRois: false);
        return dst;
    }
}

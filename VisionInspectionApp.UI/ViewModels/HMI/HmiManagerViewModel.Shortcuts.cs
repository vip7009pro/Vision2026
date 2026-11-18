using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.HMI;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels.HMI;

public partial class HmiManagerViewModel
{
    private readonly Dictionary<Key, List<HmiControlViewModel>> _activeKeyboardPressedControls = new();
    private readonly List<Task> _pendingShortcutTasks = new();
    private readonly object _pendingShortcutTasksLock = new();

    [ObservableProperty]
    private string _selectedControlShortcutWarning = "";

    public bool HasSelectedControlShortcutWarning => !string.IsNullOrWhiteSpace(SelectedControlShortcutWarning);

    partial void OnSelectedControlShortcutWarningChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedControlShortcutWarning));
    }

    public void AssignButtonShortcut(HmiControlViewModel? control, string? shortcutKey)
    {
        if (control == null || !control.IsButtonControl)
            return;

        string normalized = HmiShortcutHelper.NormalizeShortcutString(shortcutKey);
        string previous = control.ShortcutKey;

        if (string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            UpdateShortcutWarning();
            return;
        }

        _undoManager.Execute(new UndoRedoManager.DelegateAction(
            doAction: () =>
            {
                control.ShortcutKey = normalized;
                IsDirty = true;
                UpdateShortcutWarning();
            },
            undoAction: () =>
            {
                control.ShortcutKey = previous;
                IsDirty = true;
                UpdateShortcutWarning();
            }));

        if (string.IsNullOrEmpty(normalized))
        {
            StatusMessage = $"⌨️ Đã xóa phím tắt của nút '{control.Name}'.";
        }
        else
        {
            StatusMessage = $"⌨️ Đã gán phím tắt '{normalized}' cho nút '{control.Name}'.";
        }
        StatusBrush = Brushes.DodgerBlue;
    }

    [RelayCommand]
    private void ClearSelectedButtonShortcut()
    {
        AssignButtonShortcut(SelectedControl, string.Empty);
    }

    public void UpdateShortcutWarning()
    {
        if (SelectedControl == null || !SelectedControl.IsButtonControl || string.IsNullOrWhiteSpace(SelectedControl.ShortcutKey))
        {
            SelectedControlShortcutWarning = "";
            return;
        }

        string currentShortcut = SelectedControl.ShortcutKey;
        var duplicates = Controls
            .Where(c => !ReferenceEquals(c, SelectedControl) &&
                        c.Model.Type == HmiControlType.Button &&
                        string.Equals(c.ShortcutKey, currentShortcut, StringComparison.OrdinalIgnoreCase))
            .Select(c => string.IsNullOrWhiteSpace(c.Name) ? c.DisplayLabel : c.Name)
            .ToList();

        if (duplicates.Count > 0)
        {
            SelectedControlShortcutWarning = $"⚠️ Phím tắt '{currentShortcut}' đang trùng với nút: {string.Join(", ", duplicates)}.";
            return;
        }

        if (HmiShortcutHelper.IsReservedAppShortcut(currentShortcut))
        {
            SelectedControlShortcutWarning = $"ℹ️ Phím '{currentShortcut}' trùng phím tắt mặc định hệ thống — trong Tool Editor sẽ ưu tiên kích hoạt nút HMI này.";
            return;
        }

        SelectedControlShortcutWarning = "";
    }

    public bool TryHandleShortcutKeyDown(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool allowWhenNotRunMode,
        out List<HmiControlViewModel> triggeredControls)
    {
        triggeredControls = new List<HmiControlViewModel>();

        if (!IsRunMode && !allowWhenNotRunMode)
            return false;

        if (HmiShortcutHelper.IsModifierKey(key))
            return false;

        var matches = Controls
            .Where(c => c.Model.Type == HmiControlType.Button &&
                        HmiShortcutHelper.Matches(c.ShortcutKey, key, modifiers))
            .ToList();

        if (matches.Count == 0)
        {
            // Nếu người dùng đang giữ phím (auto-repeat) sau khi đã nhả phím bổ trợ (Ctrl/Alt/Shift), vẫn chặn lặp sự kiện
            return isRepeat && _activeKeyboardPressedControls.ContainsKey(key);
        }

        if (isRepeat)
            return true;

        _activeKeyboardPressedControls[key] = matches;
        triggeredControls = matches;

        foreach (var ctrl in matches)
        {
            TrackShortcutTask(ExecuteShortcutKeyDownSafeAsync(ctrl, allowWhenNotRunMode));
        }

        return true;
    }

    public bool TryHandleShortcutKeyUp(Key key, bool allowWhenNotRunMode)
    {
        if (!_activeKeyboardPressedControls.TryGetValue(key, out var pressedControls))
            return false;

        _activeKeyboardPressedControls.Remove(key);

        if (!IsRunMode && !allowWhenNotRunMode)
            return false;

        foreach (var ctrl in pressedControls)
        {
            TrackShortcutTask(ExecuteShortcutKeyUpSafeAsync(ctrl, allowWhenNotRunMode));
        }

        return true;
    }

    public void ReleaseAllPressedShortcutButtons(bool allowWhenNotRunMode = true)
    {
        if (_activeKeyboardPressedControls.Count == 0)
            return;

        var controlsToRelease = _activeKeyboardPressedControls.Values
            .SelectMany(list => list)
            .Distinct()
            .ToList();

        _activeKeyboardPressedControls.Clear();

        foreach (var ctrl in controlsToRelease)
        {
            TrackShortcutTask(ExecuteShortcutKeyUpSafeAsync(ctrl, allowWhenNotRunMode));
        }
    }

    public async Task WaitForPendingShortcutActionsAsync()
    {
        Task[] snapshot;
        lock (_pendingShortcutTasksLock)
        {
            snapshot = _pendingShortcutTasks.ToArray();
        }

        if (snapshot.Length > 0)
        {
            await Task.WhenAll(snapshot);
        }
    }

    private void TrackShortcutTask(Task task)
    {
        lock (_pendingShortcutTasksLock)
        {
            _pendingShortcutTasks.Add(task);
        }

        _ = task.ContinueWith(t =>
        {
            lock (_pendingShortcutTasksLock)
            {
                _pendingShortcutTasks.Remove(t);
            }
        }, TaskScheduler.Default);
    }

    private async Task ExecuteShortcutKeyDownSafeAsync(HmiControlViewModel ctrl, bool bypassRunModeCheck)
    {
        try
        {
            await ctrl.HandleShortcutKeyDownAsync(bypassRunModeCheck);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi thực thi phím tắt '{ctrl.ShortcutKey}': {ex.Message}";
            StatusBrush = Brushes.Red;
        }
    }

    private async Task ExecuteShortcutKeyUpSafeAsync(HmiControlViewModel ctrl, bool bypassRunModeCheck)
    {
        try
        {
            await ctrl.HandleShortcutKeyUpAsync(bypassRunModeCheck);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi nhả phím tắt '{ctrl.ShortcutKey}': {ex.Message}";
            StatusBrush = Brushes.Red;
        }
    }
}

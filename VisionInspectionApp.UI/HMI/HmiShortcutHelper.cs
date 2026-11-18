using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace VisionInspectionApp.UI.HMI;

/// <summary>
/// Chuẩn hóa, phân giải và so khớp phím tắt bàn phím cho thiết bị HMI Button.
/// </summary>
public static class HmiShortcutHelper
{
    private static readonly HashSet<string> MainWindowReservedShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        "F1", "F2", "F3", "F4", "F5", "F6",
        "Ctrl+N", "Ctrl+O", "Ctrl+S", "Ctrl+F1", "Ctrl+F4",
        "Ctrl+C", "Ctrl+V", "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z",
        "Ctrl+A", "Ctrl+R", "Delete"
    };

    public static Key ResolveActualKey(KeyEventArgs e)
    {
        if (e.Key == Key.System)
            return e.SystemKey;
        if (e.Key == Key.ImeProcessed)
            return e.ImeProcessedKey;
        if (e.Key == Key.DeadCharProcessed)
            return e.DeadCharProcessedKey;
        return e.Key;
    }

    public static ModifierKeys NormalizeModifiers(ModifierKeys modifiers)
    {
        return modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
    }

    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
            or Key.None;
    }

    public static bool IsFunctionKey(Key key) => key >= Key.F1 && key <= Key.F24;

    public static string FormatKeyToken(Key key)
    {
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            Key.Enter => "Enter",
            Key.Back => "Backspace",
            Key.CapsLock => "CapsLock",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.PrintScreen => "PrintScreen",
            _ => key.ToString()
        };
    }

    public static string FormatShortcut(ModifierKeys modifiers, Key key)
    {
        if (IsModifierKey(key))
            return string.Empty;

        var normalizedMods = NormalizeModifiers(modifiers);
        var parts = new List<string>(4);
        if ((normalizedMods & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
        if ((normalizedMods & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
        if ((normalizedMods & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
        parts.Add(FormatKeyToken(key));

        return string.Join("+", parts);
    }

    public static bool TryParseShortcut(string? shortcutText, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        if (string.IsNullOrWhiteSpace(shortcutText))
            return false;

        string[] rawParts = shortcutText.Split('+', StringSplitOptions.TrimEntries);
        if (rawParts.Length == 0)
            return false;

        for (int i = 0; i < rawParts.Length - 1; i++)
        {
            string modToken = rawParts[i];
            if (modToken.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                modToken.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
            }
            else if (modToken.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
            }
            else if (modToken.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
            }
            else
            {
                return false;
            }
        }

        string keyToken = rawParts[^1];
        if (string.IsNullOrWhiteSpace(keyToken))
            return false;

        if (keyToken.Length == 1 && keyToken[0] >= '0' && keyToken[0] <= '9')
        {
            key = Key.D0 + (keyToken[0] - '0');
            return true;
        }

        if (keyToken.Equals("Enter", StringComparison.OrdinalIgnoreCase) ||
            keyToken.Equals("Return", StringComparison.OrdinalIgnoreCase))
        {
            key = Key.Enter;
            return true;
        }

        if (keyToken.Equals("Backspace", StringComparison.OrdinalIgnoreCase) ||
            keyToken.Equals("Back", StringComparison.OrdinalIgnoreCase))
        {
            key = Key.Back;
            return true;
        }

        if (keyToken.Equals("Esc", StringComparison.OrdinalIgnoreCase) ||
            keyToken.Equals("Escape", StringComparison.OrdinalIgnoreCase))
        {
            key = Key.Escape;
            return true;
        }

        if (keyToken.Equals("Del", StringComparison.OrdinalIgnoreCase) ||
            keyToken.Equals("Delete", StringComparison.OrdinalIgnoreCase))
        {
            key = Key.Delete;
            return true;
        }

        if (Enum.TryParse<Key>(keyToken, ignoreCase: true, out var parsedKey) && !IsModifierKey(parsedKey))
        {
            key = parsedKey;
            return true;
        }

        return false;
    }

    public static string NormalizeShortcutString(string? shortcutText)
    {
        return TryParseShortcut(shortcutText, out var mods, out var key)
            ? FormatShortcut(mods, key)
            : string.Empty;
    }

    public static bool Matches(string? shortcutText, Key key, ModifierKeys modifiers)
    {
        if (IsModifierKey(key))
            return false;

        if (!TryParseShortcut(shortcutText, out var expectedMods, out var expectedKey))
            return false;

        return expectedKey == key && expectedMods == NormalizeModifiers(modifiers);
    }

    public static bool IsReservedAppShortcut(string? shortcutText)
    {
        string normalized = NormalizeShortcutString(shortcutText);
        return !string.IsNullOrEmpty(normalized) && MainWindowReservedShortcuts.Contains(normalized);
    }

    public static bool ShouldAllowShortcutForFocus(DependencyObject? focusedElement, Key key, ModifierKeys modifiers)
    {
        if (!IsTextInputFocused(focusedElement))
            return true;

        var normalizedMods = NormalizeModifiers(modifiers);
        // Trong ô nhập liệu văn bản, chỉ cho phép các phím chức năng F1..F24 (không kèm Alt)
        return IsFunctionKey(key) && (normalizedMods & ModifierKeys.Alt) == 0;
    }

    public static bool IsTextInputFocused(DependencyObject? element)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is TextBoxBase or PasswordBox or ComboBoxItem)
                return true;

            if (current is ComboBox cb && (cb.IsEditable || cb.IsDropDownOpen))
                return true;

            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}

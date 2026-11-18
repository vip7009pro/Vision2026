using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionInspectionApp.UI.HMI;
using VisionInspectionApp.UI.ViewModels.HMI;

namespace VisionInspectionApp.UI.Views.HMI;

public partial class HmiPropertyInspectorView : UserControl
{
    public HmiPropertyInspectorView()
    {
        InitializeComponent();
    }

    private void ShortcutKeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not HmiManagerViewModel vm ||
            vm.IsRunMode ||
            vm.SelectedControl == null ||
            !vm.SelectedControl.IsButtonControl)
        {
            return;
        }

        if (e.IsRepeat)
        {
            e.Handled = true;
            return;
        }

        Key actualKey = HmiShortcutHelper.ResolveActualKey(e);
        if (HmiShortcutHelper.IsModifierKey(actualKey))
        {
            e.Handled = true;
            return;
        }

        ModifierKeys modifiers = HmiShortcutHelper.NormalizeModifiers(Keyboard.Modifiers);
        if (actualKey == Key.Escape && modifiers == ModifierKeys.None)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        if (actualKey == Key.Tab && modifiers == ModifierKeys.None)
        {
            return;
        }

        string shortcut = HmiShortcutHelper.FormatShortcut(modifiers, actualKey);
        if (!string.IsNullOrEmpty(shortcut))
        {
            vm.AssignButtonShortcut(vm.SelectedControl, shortcut);
            Keyboard.ClearFocus();
        }

        e.Handled = true;
    }
}

using System;
using System.Windows;
using System.Windows.Input;

namespace VisionInspectionApp.UI.Views.OQC;

public partial class OqcPasswordPromptDialog : Window
{
    public string ExpectedPassword { get; set; } = "1234";
    public bool IsAuthenticated { get; private set; } = false;

    public OqcPasswordPromptDialog(string expectedPassword = "1234")
    {
        InitializeComponent();
        ExpectedPassword = !string.IsNullOrWhiteSpace(expectedPassword) ? expectedPassword : "1234";
        Loaded += (s, e) =>
        {
            PwdBox.Focus();
        };
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        ValidateAndSubmit();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        IsAuthenticated = false;
        try { DialogResult = false; } catch { }
        Close();
    }

    private void PwdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ValidateAndSubmit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BtnCancel_Click(sender, e);
            e.Handled = true;
        }
    }

    private void ValidateAndSubmit()
    {
        string input = PwdBox.Password ?? "";
        if (string.Equals(input, ExpectedPassword, StringComparison.Ordinal))
        {
            IsAuthenticated = true;
            try { DialogResult = true; } catch { }
            Close();
        }
        else
        {
            TxtErrorMessage.Visibility = Visibility.Visible;
            PwdBox.SelectAll();
            PwdBox.Focus();
        }
    }

    /// <summary>
    /// Hiển thị hộp thoại yêu cầu nhập mật khẩu. Trả về true nếu người dùng nhập đúng mật khẩu.
    /// </summary>
    public static bool PromptPassword(Window? owner, string expectedPassword)
    {
        var dlg = new OqcPasswordPromptDialog(expectedPassword);
        if (owner != null && owner.IsLoaded)
        {
            dlg.Owner = owner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        bool? result = dlg.ShowDialog();
        return result == true && dlg.IsAuthenticated;
    }
}

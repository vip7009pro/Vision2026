using System;
using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views.OTA;

public partial class OtaUpdateDialog : Window
{
    public OtaUpdateDialog(OtaUpdateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += () =>
        {
            Dispatcher.Invoke(Close);
        };

        Loaded += async (_, _) =>
        {
            // Tự động kiểm tra bản cập nhật khi mở dialog
            await viewModel.CheckForUpdateAsync();
        };
    }
}

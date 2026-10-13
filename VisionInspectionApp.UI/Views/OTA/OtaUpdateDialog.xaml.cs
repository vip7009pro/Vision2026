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

        Closing += (_, e) =>
        {
            if (viewModel.IsMandatory && !viewModel.IsUpdateInstalling)
            {
                e.Cancel = true;
                return;
            }

            viewModel.SaveAllSettings();
        };

        Loaded += async (_, _) =>
        {
            if (!viewModel.HasInitialManifest)
            {
                await viewModel.CheckForUpdateAsync();
            }
        };
    }
}

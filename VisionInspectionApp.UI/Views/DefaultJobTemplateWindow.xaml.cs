using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views;

public partial class DefaultJobTemplateWindow : Window
{
    public DefaultJobTemplateWindow(DefaultJobTemplateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += Close;
    }
}

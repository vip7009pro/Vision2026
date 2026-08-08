using System.Windows.Controls;
using System.Windows.Input;
using VisionInspectionApp.UI.ViewModels.HMI;

namespace VisionInspectionApp.UI.Views.HMI;

public partial class HmiControlView : UserControl
{
    public HmiControlView()
    {
        InitializeComponent();
    }

    private async void RootContainer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HmiControlViewModel vm && vm.IsRunMode)
        {
            await vm.HandleUserInteractionAsync();
        }
    }

    private async void RootContainer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HmiControlViewModel vm && vm.IsRunMode)
        {
            await vm.HandleMouseUpAsync();
        }
    }
}

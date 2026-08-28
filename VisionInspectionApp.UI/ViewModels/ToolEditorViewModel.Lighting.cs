using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Views;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        [RelayCommand]
        private void OpenLightingController()
        {
            var lightingService = _serviceProvider?.GetService<LightingControllerService>();
            var settingsService = _serviceProvider?.GetService<GlobalAppSettingsService>();

            if (lightingService == null || settingsService == null)
            {
                MessageBox.Show(
                    "Lighting Controller service is not available.\nPlease restart the application.",
                    "Service Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var vm = new LightingControllerViewModel(lightingService, settingsService);
            var activeWin = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? System.Windows.Application.Current?.MainWindow;
            var win = new LightingControllerWindow(vm)
            {
                Owner = activeWin
            };
            win.ShowDialog();
        }
    }
}

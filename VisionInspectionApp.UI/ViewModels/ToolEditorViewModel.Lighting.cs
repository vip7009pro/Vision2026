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
        private static LightingControllerWindow? _lightingControllerWindowInstance;

        [RelayCommand]
        private void OpenLightingController()
        {
            if (_lightingControllerWindowInstance != null && _lightingControllerWindowInstance.IsLoaded)
            {
                _lightingControllerWindowInstance.Activate();
                if (_lightingControllerWindowInstance.WindowState == WindowState.Minimized)
                    _lightingControllerWindowInstance.WindowState = WindowState.Normal;
                return;
            }

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
            _lightingControllerWindowInstance = new LightingControllerWindow(vm);
            _lightingControllerWindowInstance.Closed += (_, _) => _lightingControllerWindowInstance = null;
            _lightingControllerWindowInstance.Show();
        }
    }
}

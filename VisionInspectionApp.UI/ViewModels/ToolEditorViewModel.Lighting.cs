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
        private static LightingServerWindow? _lightingServerWindowInstance;
        private static LightingClientWindow? _lightingClientWindowInstance;

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
            var patternService = _serviceProvider?.GetService<LightingPatternService>();

            if (lightingService == null || settingsService == null)
            {
                MessageBox.Show(
                    "Lighting Controller service is not available.\nPlease restart the application.",
                    "Service Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var vm = new LightingControllerViewModel(lightingService, settingsService, patternService);
            _lightingControllerWindowInstance = new LightingControllerWindow(vm);
            var mainWin = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault() ?? System.Windows.Application.Current?.MainWindow;
            if (mainWin != null && mainWin != _lightingControllerWindowInstance && mainWin.IsLoaded)
            {
                _lightingControllerWindowInstance.Owner = mainWin;
            }
            _lightingControllerWindowInstance.Closed += (_, _) => _lightingControllerWindowInstance = null;
            _lightingControllerWindowInstance.Show();
        }

        [RelayCommand]
        private void OpenLightingServer()
        {
            if (_lightingServerWindowInstance != null && _lightingServerWindowInstance.IsLoaded)
            {
                _lightingServerWindowInstance.Activate();
                if (_lightingServerWindowInstance.WindowState == WindowState.Minimized)
                    _lightingServerWindowInstance.WindowState = WindowState.Normal;
                return;
            }

            var lightingService = _serviceProvider?.GetService<LightingControllerService>();
            var lightingServer = _serviceProvider?.GetService<LightingControlServer>();
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

            var vm = new LightingServerViewModel(lightingService, settingsService, lightingServer);
            _lightingServerWindowInstance = new LightingServerWindow(vm);
            var mainWin = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault() ?? System.Windows.Application.Current?.MainWindow;
            if (mainWin != null && mainWin != _lightingServerWindowInstance && mainWin.IsLoaded)
            {
                _lightingServerWindowInstance.Owner = mainWin;
            }
            _lightingServerWindowInstance.Closed += (_, _) => _lightingServerWindowInstance = null;
            _lightingServerWindowInstance.Show();
        }

        [RelayCommand]
        private void OpenLightingClient()
        {
            if (_lightingClientWindowInstance != null && _lightingClientWindowInstance.IsLoaded)
            {
                _lightingClientWindowInstance.Activate();
                if (_lightingClientWindowInstance.WindowState == WindowState.Minimized)
                    _lightingClientWindowInstance.WindowState = WindowState.Normal;
                return;
            }

            var settingsService = _serviceProvider?.GetService<GlobalAppSettingsService>();
            if (settingsService == null)
            {
                MessageBox.Show(
                    "Settings service is not available.\nPlease restart the application.",
                    "Service Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var vm = new LightingClientViewModel(settingsService);
            _lightingClientWindowInstance = new LightingClientWindow(vm);
            var mainWin = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault() ?? System.Windows.Application.Current?.MainWindow;
            if (mainWin != null && mainWin != _lightingClientWindowInstance && mainWin.IsLoaded)
            {
                _lightingClientWindowInstance.Owner = mainWin;
            }
            _lightingClientWindowInstance.Closed += (_, _) => _lightingClientWindowInstance = null;
            _lightingClientWindowInstance.Show();
        }
    }
}

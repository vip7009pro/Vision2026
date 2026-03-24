using System.Configuration;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisionInspectionApp.Application;
using VisionInspectionApp.Persistence;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new ConfigStoreOptions { ConfigRootDirectory = "configs" });
                services.AddSingleton<IConfigService, JsonConfigService>();

                services.AddSingleton<ImagePreprocessor>();
                services.AddSingleton<PatternMatcher>();
                services.AddSingleton<DistanceCalculator>();
                services.AddSingleton<LineDetector>();
                services.AddSingleton<IDefectDetector, DefectDetector>();
                services.AddSingleton<IInspectionService, InspectionService>();

                services.AddSingleton<UndoRedoManager>();
                services.AddSingleton<GlobalAppSettingsService>();
                services.AddSingleton<SharedImageContext>();

                // Camera & Batch Processing Services
                services.AddSingleton<CameraService>();
                services.AddSingleton<BatchProcessingService>();

                services.AddSingleton<TeachViewModel>();
                services.AddSingleton<ToolEditorViewModel>();
                services.AddSingleton<CalibrationViewModel>();
                services.AddSingleton<ManualInspectionViewModel>();
                services.AddSingleton<InspectionViewModel>();
                services.AddSingleton<LiveCameraViewModel>();
                services.AddSingleton<BatchProcessingViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}


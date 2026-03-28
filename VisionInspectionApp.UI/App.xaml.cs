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
using VisionInspectionApp.UI.Services.Plc;
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

                // PLC (MX Component)
                services.AddSingleton<IPlcClient, MxComponentPlcClient>();
                services.AddSingleton<PlcOrchestratorService>();
                services.AddSingleton<PlcViewModel>();

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

    protected override void OnExit(ExitEventArgs e)
    {
        // Must shut down COM / background work synchronously: async void OnExit returns before await,
        // which previously left the MX Component STA thread and host running (zombie process).
        try
        {
            if (_host is not null)
            {
                ShutdownGracefullyAsync().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // ignore — process is exiting
        }

        base.OnExit(e);
    }

    private async Task ShutdownGracefullyAsync()
    {
        var host = _host;
        if (host is null)
        {
            return;
        }

        var services = host.Services;

        try
        {
            if (services.GetService<PlcOrchestratorService>() is { } orchestrator)
            {
                await orchestrator.DisposeAsync().ConfigureAwait(false);
            }

            if (services.GetService<CameraService>() is { } camera)
            {
                await camera.StopCameraAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            await host.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        try
        {
            host.Dispose();
        }
        catch
        {
            // ignore
        }

        _host = null;
    }
}

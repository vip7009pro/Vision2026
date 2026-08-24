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
    public IServiceProvider ServiceProvider => _host!.Services;

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
                services.AddSingleton<IJobService, JobService>();

                services.AddSingleton<ImagePreprocessor>();
                services.AddSingleton<PatternMatcher>();
                services.AddSingleton<DistanceCalculator>();
                services.AddSingleton<LineDetector>();
                services.AddSingleton<IDefectDetector, DefectDetector>();

                // Database Framework
                services.AddSingleton<Application.DB.Services.IDbManagerService, Application.DB.Services.DbManagerService>();

                services.AddSingleton<IInspectionService, InspectionService>();

                services.AddSingleton<UndoRedoManager>();
                services.AddSingleton<GlobalAppSettingsService>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<SharedImageContext>();
                services.AddSingleton<VisionInspectionApp.Application.Services.IRecentJobsService, VisionInspectionApp.Application.Services.RecentJobsService>();

                // Camera & Batch Processing Services
                services.AddSingleton<CameraService>();
                services.AddSingleton<BatchProcessingService>();

                // PLC Framework
                services.AddSingleton<Application.PLC.Services.IPlcManagerService, Application.PLC.Services.PlcManagerService>();
                services.AddTransient<ViewModels.PLC.PlcManagerViewModel>();
                services.AddTransient<ViewModels.PLC.PlcMonitorViewModel>();
                services.AddTransient<ViewModels.PLC.PlcBrowserViewModel>();
                services.AddTransient<ViewModels.PLC.PlcOscilloscopeViewModel>();

                // Legacy PLC (MX Component)
                services.AddSingleton<IPlcClient, MxComponentPlcClient>();
                services.AddSingleton<PlcOrchestratorService>();

                // OQC Framework
                services.AddSingleton<Application.OQC.IOqcScannerService, Application.OQC.OqcScannerService>();

                services.AddSingleton<TeachViewModel>();
                services.AddSingleton<ToolEditorViewModel>();
                services.AddSingleton<CalibrationViewModel>();
                services.AddSingleton<ManualInspectionViewModel>();
                services.AddSingleton<InspectionViewModel>();
                services.AddSingleton<OqcScannerViewModel>();
                services.AddSingleton<CameraSettingsViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Start();

        var themeService = _host.Services.GetRequiredService<ThemeService>();
        themeService.ApplyTheme();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        var cameraService = _host.Services.GetRequiredService<CameraService>();
        _ = cameraService.StartSavedCameraAsync();
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

        // Terminate residual background threads / unmanaged COM hosts so process never lingers in Task Manager
        Environment.Exit(0);
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

            if (services.GetService<VisionInspectionApp.Application.PLC.Services.IPlcManagerService>() is { } plcManager)
            {
                plcManager.Dispose();
            }

            if (services.GetService<CameraService>() is { } camera)
            {
                await camera.StopCameraAsync().ConfigureAwait(false);
                camera.Dispose();
            }

            try
            {
                await VisionInspectionApp.Application.Services.AsyncImageSaver.Instance.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
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

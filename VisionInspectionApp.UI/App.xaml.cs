using System.Configuration;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.LightingController;
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
                services.AddSingleton<VisionInspectionApp.Application.Services.IInspectionLogService, VisionInspectionApp.Application.Services.InspectionLogService>();
                services.AddTransient<InspectionLogViewModel>();

                // PLC Framework
                services.AddSingleton<Application.PLC.Services.IPlcManagerService, Application.PLC.Services.PlcManagerService>();
                services.AddTransient<ViewModels.PLC.PlcManagerViewModel>();
                services.AddTransient<ViewModels.PLC.PlcMonitorViewModel>();
                services.AddTransient<ViewModels.PLC.PlcBrowserViewModel>();
                services.AddTransient<ViewModels.PLC.PlcOscilloscopeViewModel>();

                // Lighting Controller
                services.AddSingleton<LightingControllerService>();

                // Legacy PLC (MX Component)
                services.AddSingleton<IPlcClient, MxComponentPlcClient>();
                services.AddSingleton<PlcOrchestratorService>();

                // OQC Framework
                services.AddSingleton<Application.OQC.IOqcScannerService, Application.OQC.OqcScannerService>();

                // Remote Server & Job Manager Framework
                services.AddSingleton<VisionInspectionApp.Application.Services.IRemoteServerService, VisionInspectionApp.Application.Services.RemoteServerService>();
                services.AddSingleton<JobManagerViewModel>();
                services.AddTransient<Views.OQC.JobManagerWindow>();

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

        var plcManager = _host.Services.GetRequiredService<Application.PLC.Services.IPlcManagerService>();
        _ = plcManager.AutoConnectStartupAsync();

        // Auto-connect Lighting Controller & Apply Startup Lighting (non-blocking)
        _ = Task.Run(async () =>
        {
            try
            {
                var settingsService = _host.Services.GetRequiredService<GlobalAppSettingsService>();
                var lightingSettings = settingsService.Settings.Lighting;
                if (lightingSettings.AutoConnect)
                {
                    var lightingService = _host.Services.GetRequiredService<LightingControllerService>();
                    if (lightingSettings.InterfaceType == (int)VisionInspectionApp.Models.LightingInterfaceType.SerialCom)
                    {
                        string? le = lightingSettings.LineEnding switch
                        {
                            1 => "\r\n",
                            2 => "\r",
                            3 => "\n",
                            _ => null
                        };
                        await lightingService.ConnectSerialAsync(
                            lightingSettings.ComPort,
                            lightingSettings.BaudRate,
                            (System.IO.Ports.Parity)lightingSettings.Parity,
                            lightingSettings.DataBits,
                            (System.IO.Ports.StopBits)lightingSettings.StopBits,
                            readTimeoutMs: 3000,
                            writeTimeoutMs: 3000,
                            lineEnding: le,
                            dtrEnable: lightingSettings.DtrEnable,
                            rtsEnable: lightingSettings.RtsEnable,
                            autoReadState: lightingSettings.AutoReadOnConnect);
                    }
                    else
                    {
                        await lightingService.ConnectAsync(
                            lightingSettings.ControllerIp,
                            lightingSettings.Port,
                            (VisionInspectionApp.Models.LightingNetworkMode)lightingSettings.NetworkMode);
                    }

                    // Tự động bật đèn ở các channel và mức sáng đã cài đặt khi app khởi động
                    if (lightingService.IsConnected && lightingSettings.EnableStartupLighting)
                    {
                        var channels = lightingSettings.StartupChannels != null && lightingSettings.StartupChannels.Count > 0
                            ? lightingSettings.StartupChannels
                            : VisionInspectionApp.Models.LightingStartupChannelSettings.CreateDefaults(lightingSettings.ChannelCount);

                        int chCount = lightingSettings.ChannelCount == 8 ? 8 : 4;
                        for (int i = 0; i < chCount && i < channels.Count; i++)
                        {
                            var ch = channels[i];
                            await lightingService.SetChannelPowerAsync(ch.ChannelIndex, ch.IsEnabled);
                            if (ch.IsEnabled)
                            {
                                int br = Math.Clamp(ch.Brightness, 0, 255);
                                await lightingService.SetBrightnessAsync(ch.ChannelIndex, br);
                            }
                        }
                    }

                    if (lightingService.IsConnected)
                    {
                        var target = lightingSettings.InterfaceType == (int)VisionInspectionApp.Models.LightingInterfaceType.SerialCom
                            ? lightingSettings.ComPort
                            : $"{lightingSettings.ControllerIp}:{lightingSettings.Port}";
                        var successMsg = $"💡 [Đèn Chiếu Sáng] Đã kết nối ({target}) & thiết lập độ sáng khởi động thành công.";
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var mainVm = _host.Services.GetService<MainWindowViewModel>();
                                mainVm?.SetGlobalStatus(successMsg, "Success");
                            }
                            catch { }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Bắt toàn bộ lỗi timeout / không kết nối được để hiển thị thông báo ở status bar và không để văng app
                var warnMsg = $"⚠️ [Đèn Chiếu Sáng] {ex.Message}";
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var mainVm = _host.Services.GetService<MainWindowViewModel>();
                        mainVm?.SetGlobalStatus(warnMsg, "Warning");

                        var toolEditorVm = _host.Services.GetService<ToolEditorViewModel>();
                        if (toolEditorVm != null)
                        {
                            toolEditorVm.StatusBarText = warnMsg;
                        }
                    }
                    catch { }
                });
            }
        });
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

            if (services.GetService<LightingControllerService>() is { } lightingService)
            {
                try
                {
                    var settingsService = services.GetService<GlobalAppSettingsService>();
                    if (settingsService?.Settings?.Lighting?.AutoTurnOffOnExit == true && lightingService.IsConnected)
                    {
                        await lightingService.TurnOffAllChannelsAsync().ConfigureAwait(false);
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignore shutdown error
                }
                lightingService.Dispose();
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

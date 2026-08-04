using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Application.PLC.Drivers;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

public sealed class PlcConfigContainer
{
    public List<PlcModel> Plcs { get; set; } = new();
    public List<PlcTag> Tags { get; set; } = new();
}

public sealed class PlcManagerService : IPlcManagerService
{
    private readonly ConcurrentDictionary<string, IPlcDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _globalConfigFilePath;
    private bool _disposed;
    private bool _isLoading;

    public ObservableCollection<PlcModel> Plcs { get; } = new();

    public ObservableCollection<PlcTag> Tags { get; } = new();

    public PlcTagCache Cache { get; } = new();

    public IPlcLogger Logger { get; } = new PlcLogger();

    public PlcPollingEngine PollingEngine { get; }

    public event EventHandler<TagChangedEventArgs>? OnTagChanged;

    public event EventHandler<string>? OnConnected;

    public event EventHandler<string>? OnDisconnected;

    public event EventHandler<(string PlcId, string Message)>? OnError;

    public PlcManagerService()
    {
        PollingEngine = new PlcPollingEngine(Cache, Logger);
        PollingEngine.OnTagChanged += (s, e) => OnTagChanged?.Invoke(this, e);

        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vision2026");
        Directory.CreateDirectory(appDataDir);
        _globalConfigFilePath = Path.Combine(appDataDir, "plc_config.json");

        Plcs.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (PlcModel p in e.NewItems)
                {
                    p.PropertyChanged += Item_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (PlcModel p in e.OldItems)
                {
                    p.PropertyChanged -= Item_PropertyChanged;
                }
            }
            if (!_isLoading) SaveGlobalConfig();
        };

        Tags.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (PlcTag t in e.NewItems)
                {
                    t.PropertyChanged += Item_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (PlcTag t in e.OldItems)
                {
                    t.PropertyChanged -= Item_PropertyChanged;
                }
            }
            if (!_isLoading) SaveGlobalConfig();
        };

        LoadGlobalConfig();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isLoading) SaveGlobalConfig();
    }

    public void SaveGlobalConfig()
    {
        if (_isLoading) return;
        try
        {
            var container = new PlcConfigContainer
            {
                Plcs = Plcs.ToList(),
                Tags = Tags.ToList()
            };
            string json = JsonSerializer.Serialize(container, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_globalConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.LogWriteError("SYSTEM", "SaveGlobalConfig", ex.Message);
        }
    }

    public void LoadGlobalConfig()
    {
        _isLoading = true;
        try
        {
            if (File.Exists(_globalConfigFilePath))
            {
                string json = File.ReadAllText(_globalConfigFilePath);
                var container = JsonSerializer.Deserialize<PlcConfigContainer>(json);
                if (container != null && container.Plcs != null && container.Plcs.Count > 0)
                {
                    LoadConfigInternal(container.Plcs, container.Tags ?? new List<PlcTag>());
                    return;
                }
            }
        }
        catch { }
        finally
        {
            _isLoading = false;
        }

        if (Plcs.Count == 0)
        {
            var defaultPlc = new PlcModel
            {
                Id = "plc_1",
                Name = "PLC1",
                DriverType = PlcDriverType.MitsubishiMxComponent,
                LogicalStationNumber = 1,
                Enabled = true
            };

            var sampleTags = new List<PlcTag>
            {
                new PlcTag { PlcId = defaultPlc.Id, Name = "X0_Trigger", Address = "X0", DataType = PlcDataType.Bool },
                new PlcTag { PlcId = defaultPlc.Id, Name = "X1_Sensor", Address = "X1", DataType = PlcDataType.Bool },
                new PlcTag { PlcId = defaultPlc.Id, Name = "D100_Data", Address = "D100", DataType = PlcDataType.Int16 }
            };

            LoadConfigInternal(new[] { defaultPlc }, sampleTags);
            SaveGlobalConfig();
        }
    }

    public void LoadConfig(IEnumerable<PlcModel> plcs, IEnumerable<PlcTag> tags)
    {
        LoadConfigInternal(plcs, tags);
        SaveGlobalConfig();
    }

    private void LoadConfigInternal(IEnumerable<PlcModel> plcs, IEnumerable<PlcTag> tags)
    {
        _isLoading = true;
        try
        {
            StopPollingAsync().Wait();

            foreach (var d in _drivers.Values)
            {
                try { d.Dispose(); } catch { }
            }
            _drivers.Clear();
            Plcs.Clear();
            Tags.Clear();
            Cache.Clear();

            if (plcs != null)
            {
                foreach (var p in plcs)
                {
                    p.PropertyChanged += Item_PropertyChanged;
                    Plcs.Add(p);
                    CreateDriverForPlc(p);
                }
            }

            if (tags != null)
            {
                foreach (var t in tags)
                {
                    t.PropertyChanged += Item_PropertyChanged;
                    Tags.Add(t);
                }
            }

            StartPollingAsync();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public IPlcDriver? GetDriver(string plcId)
    {
        if (string.IsNullOrWhiteSpace(plcId))
        {
            var firstPlc = Plcs.FirstOrDefault();
            if (firstPlc != null) plcId = firstPlc.Id;
            else return _drivers.Values.FirstOrDefault();
        }

        var plc = Plcs.FirstOrDefault(p => string.Equals(p.Id, plcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, plcId, StringComparison.OrdinalIgnoreCase));
        if (plc == null) return _drivers.Values.FirstOrDefault();

        if (_drivers.TryGetValue(plc.Id, out var existingDriver))
        {
            bool isMxDriver = existingDriver is MitsubishiMxComponentDriver;
            bool shouldBeMx = plc.DriverType == PlcDriverType.MitsubishiMxComponent;

            if (isMxDriver != shouldBeMx)
            {
                try { existingDriver.Dispose(); } catch { }
                _drivers.TryRemove(plc.Id, out _);
                CreateDriverForPlc(plc);
                _drivers.TryGetValue(plc.Id, out existingDriver);
            }
            return existingDriver;
        }

        CreateDriverForPlc(plc);
        _drivers.TryGetValue(plc.Id, out var newDriver);
        return newDriver;
    }

    public IPlcDriver? GetDriverByName(string plcName)
    {
        var plc = Plcs.FirstOrDefault(p => string.Equals(p.Name, plcName, StringComparison.OrdinalIgnoreCase));
        if (plc != null) return GetDriver(plc.Id);
        return null;
    }

    public PlcTagValue? GetTagValue(string plcId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(plcId) || string.IsNullOrWhiteSpace(tagName)) return null;

        var plc = Plcs.FirstOrDefault(p => string.Equals(p.Id, plcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, plcId, StringComparison.OrdinalIgnoreCase));

        if (plc != null)
        {
            var valById = Cache.Get(plc.Id, tagName);
            if (valById != null) return valById;

            var valByName = Cache.Get(plc.Name, tagName);
            if (valByName != null) return valByName;
        }

        return Cache.Get(plcId, tagName);
    }

    public async Task<bool> WriteTagValueAsync(string plcId, string tagName, object value, CancellationToken cancellationToken = default)
    {
        var plc = Plcs.FirstOrDefault(p => string.Equals(p.Id, plcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, plcId, StringComparison.OrdinalIgnoreCase));
        string targetPlcId = plc?.Id ?? plcId;

        var tag = Tags.FirstOrDefault(t => (string.Equals(t.PlcId, targetPlcId, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, plcId, StringComparison.OrdinalIgnoreCase))
                                           && string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));
        if (tag == null)
        {
            Logger.LogWriteError(targetPlcId, tagName, "Tag definition not found.");
            return false;
        }

        if (tag.ReadOnly)
        {
            Logger.LogWriteError(targetPlcId, tagName, "Tag is read-only.");
            return false;
        }

        var driver = GetDriver(targetPlcId);
        if (driver == null)
        {
            Logger.LogWriteError(targetPlcId, tagName, "No active driver available.");
            return false;
        }

        try
        {
            bool success = await driver.WriteAsync(tag, value, cancellationToken);
            if (success)
            {
                Cache.Set(targetPlcId, tagName, value, TagQuality.Good);
                if (plc != null) Cache.Set(plc.Name, tagName, value, TagQuality.Good);
            }
            else
            {
                Logger.LogWriteError(targetPlcId, tagName, "Write operation failed.");
            }
            return success;
        }
        catch (Exception ex)
        {
            Logger.LogWriteError(targetPlcId, tagName, ex.Message);
            OnError?.Invoke(this, (targetPlcId, ex.Message));
            return false;
        }
    }

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plc in Plcs.Where(p => p.Enabled))
        {
            var driver = GetDriver(plc.Id);
            if (driver != null && !driver.IsConnected)
            {
                plc.State = PlcConnectionState.Connecting;
                bool ok = await driver.ConnectAsync(cancellationToken);
                if (ok)
                {
                    plc.State = PlcConnectionState.Connected;
                    Logger.LogConnect(plc.Id, plc.Name);
                    OnConnected?.Invoke(this, plc.Id);
                }
                else
                {
                    plc.State = PlcConnectionState.Error;
                    Logger.LogDisconnect(plc.Id, plc.Name);
                    OnDisconnected?.Invoke(this, plc.Id);
                }
            }
        }
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var plc in Plcs)
        {
            var driver = GetDriver(plc.Id);
            if (driver != null)
            {
                await driver.DisconnectAsync();
                plc.State = PlcConnectionState.Disconnected;
                Logger.LogDisconnect(plc.Id, plc.Name);
                OnDisconnected?.Invoke(this, plc.Id);
            }
        }
    }

    public async Task StartPollingAsync()
    {
        await ConnectAllAsync();
        PollingEngine.Start(Plcs.ToList(), Tags.ToList(), GetDriver);
    }

    public Task StopPollingAsync()
    {
        PollingEngine.Stop();
        return Task.CompletedTask;
    }

    private void CreateDriverForPlc(PlcModel plc)
    {
        IPlcDriver driver = plc.DriverType switch
        {
            PlcDriverType.Mitsubishi => new MitsubishiDriver(plc),
            PlcDriverType.MitsubishiMxComponent => new MitsubishiMxComponentDriver(plc),
            _ => new MitsubishiDriver(plc)
        };

        _drivers[plc.Id] = driver;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopPollingAsync().Wait();
        DisconnectAllAsync().Wait();

        foreach (var d in _drivers.Values)
        {
            d.Dispose();
        }
        _drivers.Clear();
    }
}

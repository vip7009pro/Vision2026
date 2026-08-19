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

public sealed class PlcManagerService : IPlcManagerService, IDisposable
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
        if (e.PropertyName == nameof(PlcModel.State) || e.PropertyName == nameof(PlcModel.CpuName))
        {
            return;
        }
        if (!_isLoading) SaveGlobalConfig();
    }

    private readonly object _saveLock = new();

    public void SaveGlobalConfig()
    {
        if (_isLoading) return;
        lock (_saveLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_globalConfigFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
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
                    foreach (var plc in container.Plcs)
                    {
                        plc.State = PlcConnectionState.Disconnected;
                        plc.CpuName = string.Empty;
                    }
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

    public async Task<bool> WriteTagValueAsync(string plcId, string tagName, object value, CancellationToken cancellationToken = default)
    {
        var plc = Plcs.FirstOrDefault(p => string.Equals(p.Id, plcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, plcId, StringComparison.OrdinalIgnoreCase))
                  ?? Plcs.FirstOrDefault();
        string targetPlcId = plc?.Id ?? plcId;

        var tag = Tags.FirstOrDefault(t => (string.Equals(t.PlcId, targetPlcId, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, plcId, StringComparison.OrdinalIgnoreCase))
                                           && (string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Address, tagName, StringComparison.OrdinalIgnoreCase)));

        if (tag == null)
        {
            PlcDataType defaultType = PlcDataType.Bool;
            if (value is bool) defaultType = PlcDataType.Bool;
            else if (value is short || value is ushort || value is int || value is uint) defaultType = PlcDataType.Int16;
            else if (value is float || value is double) defaultType = PlcDataType.Float;
            else if (value is string) defaultType = PlcDataType.String;

            tag = new PlcTag
            {
                PlcId = targetPlcId,
                Name = tagName,
                Address = tagName,
                DataType = defaultType
            };
        }

        if (tag.ReadOnly)
        {
            Logger.LogWriteError(targetPlcId, tagName, "Tag is read-only.");
            return false;
        }

        var driver = GetDriver(targetPlcId);
        if (driver == null && plc != null)
        {
            CreateDriverForPlc(plc);
            driver = GetDriver(targetPlcId);
        }

        if (driver == null)
        {
            Logger.LogWriteError(targetPlcId, tagName, "No active driver available.");
            return false;
        }

        if (!driver.IsConnected)
        {
            try
            {
                using var connCts = new CancellationTokenSource(1500);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connCts.Token);
                await driver.ConnectAsync(linkedCts.Token);
            }
            catch { }
        }

        try
        {
            using var writeCts = new CancellationTokenSource(2000);
            using var linkedWriteCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, writeCts.Token);
            bool success = await driver.WriteAsync(tag, value, linkedWriteCts.Token);
            if (success)
            {
                Cache.Set(targetPlcId, tagName, value, TagQuality.Good);
                if (plc != null) Cache.Set(plc.Name, tagName, value, TagQuality.Good);
                OnTagChanged?.Invoke(this, new TagChangedEventArgs(targetPlcId, tagName, null, value, DateTime.Now));
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

    private void LoadConfigInternal(IEnumerable<PlcModel> plcs, IEnumerable<PlcTag> tags)
    {
        _isLoading = true;
        try
        {
            PollingEngine.Stop();

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

            if (IsPollingActive)
            {
                Task.Run(() => StartPollingAsync());
            }
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

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plc in Plcs.Where(p => p.Enabled))
        {
            var driver = GetDriver(plc.Id);
            if (driver != null && !driver.IsConnected)
            {
                plc.State = PlcConnectionState.Connecting;
                try
                {
                    using var connCts = new CancellationTokenSource(2000);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connCts.Token);
                    bool ok = await driver.ConnectAsync(linkedCts.Token);
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
                catch
                {
                    plc.State = PlcConnectionState.Error;
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
                try
                {
                    await driver.DisconnectAsync();
                }
                catch { }
                plc.State = PlcConnectionState.Disconnected;
                Logger.LogDisconnect(plc.Id, plc.Name);
                OnDisconnected?.Invoke(this, plc.Id);
            }
        }
    }

    private readonly HashSet<string> _pollingSources = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPollingActive
    {
        get
        {
            lock (_pollingSources)
            {
                return _pollingSources.Count > 0;
            }
        }
    }

    public void AcquirePollingLock(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        bool shouldStart = false;
        lock (_pollingSources)
        {
            _pollingSources.Add(sourceId);
            shouldStart = _pollingSources.Count > 0;
        }

        if (shouldStart)
        {
            Task.Run(() => StartPollingAsync());
        }
    }

    public void ReleasePollingLock(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        bool shouldStop = false;
        lock (_pollingSources)
        {
            _pollingSources.Remove(sourceId);
            shouldStop = _pollingSources.Count == 0;
        }

        if (shouldStop)
        {
            Task.Run(() => StopPollingAsync());
        }
    }

    public async Task StartPollingAsync()
    {
        await ConnectAllAsync();
        PollingEngine.Start(() => Plcs.ToList(), () => Tags.ToList(), GetDriver);
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

        PollingEngine.Stop();

        foreach (var d in _drivers.Values)
        {
            try { d.Dispose(); } catch { }
        }
        _drivers.Clear();
    }
}

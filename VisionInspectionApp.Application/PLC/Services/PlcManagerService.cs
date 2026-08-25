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
    public PlcIndustrialConfig IndustrialConfig { get; set; } = new();
}

public sealed class PlcManagerService : IPlcManagerService, IDisposable
{
    private readonly ConcurrentDictionary<string, IPlcDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastConnectAttemptTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<IEnumerable<PlcTag>>> _dynamicTagProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _scanIntervalOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _globalConfigFilePath;
    private bool _disposed;
    private bool _isLoading;
    private PlcIndustrialConfig _industrialConfig = new();

    public PlcIndustrialConfig IndustrialConfig
    {
        get => _industrialConfig;
        set
        {
            _industrialConfig = value ?? new();
            OnIndustrialConfigChanged?.Invoke(this, _industrialConfig);
            if (!_isLoading) SaveGlobalConfig();
        }
    }

    public ObservableCollection<PlcModel> Plcs { get; } = new();

    public ObservableCollection<PlcTag> Tags { get; } = new();

    public PlcTagCache Cache { get; } = new();

    public IPlcLogger Logger { get; } = new PlcLogger();

    public PlcPollingEngine PollingEngine { get; }

    public event EventHandler<TagChangedEventArgs>? OnTagChanged;

    public event EventHandler<BatchPolledEventArgs>? OnBatchPolled;

    public event EventHandler<string>? OnConnected;

    public event EventHandler<string>? OnDisconnected;

    public event EventHandler<(string PlcId, string Message)>? OnError;

    public event EventHandler<PlcIndustrialConfig>? OnIndustrialConfigChanged;

    public PlcManagerService()
    {
        PollingEngine = new PlcPollingEngine(Cache, Logger);
        PollingEngine.OnTagChanged += (s, e) => OnTagChanged?.Invoke(this, e);
        PollingEngine.OnBatchPolled += (s, e) => OnBatchPolled?.Invoke(this, e);

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
                    Tags = Tags.ToList(),
                    IndustrialConfig = _industrialConfig
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
                if (container != null)
                {
                    if (container.IndustrialConfig != null)
                    {
                        _industrialConfig = container.IndustrialConfig;
                        OnIndustrialConfigChanged?.Invoke(this, _industrialConfig);
                    }

                    if (container.Plcs != null && container.Plcs.Count > 0)
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

    public static PlcDataType InferDataTypeFromAddress(string address, object? valueHint = null)
    {
        if (valueHint is bool) return PlcDataType.Bool;
        if (valueHint is short || valueHint is ushort || valueHint is int || valueHint is uint) return PlcDataType.Int16;
        if (valueHint is float || valueHint is double) return PlcDataType.Float;
        if (valueHint is string) return PlcDataType.String;

        if (string.IsNullOrWhiteSpace(address)) return PlcDataType.Bool;

        string trimmed = address.Trim();
        string upper = trimmed.ToUpperInvariant();

        // 1. Explicit 2+ char word register prefixes
        if (upper.StartsWith("MW") || upper.StartsWith("IW") || upper.StartsWith("QW") ||
            upper.StartsWith("SW") || upper.StartsWith("ZR") || upper.StartsWith("TN") ||
            upper.StartsWith("CN") || upper.StartsWith("SD") || upper.StartsWith("3X") ||
            upper.StartsWith("4X") || upper.StartsWith("HOLDING") || upper.StartsWith("INPUT"))
        {
            return PlcDataType.Int16;
        }

        // 2. Explicit 2+ char bit prefixes
        if (upper.StartsWith("SM") || upper.StartsWith("TS") || upper.StartsWith("TC") ||
            upper.StartsWith("SS") || upper.StartsWith("SC") || upper.StartsWith("CS") ||
            upper.StartsWith("CC") || upper.StartsWith("DX") || upper.StartsWith("DY") ||
            upper.StartsWith("0X") || upper.StartsWith("1X") || upper.StartsWith("COIL") ||
            upper.StartsWith("DISCRETE"))
        {
            return PlcDataType.Bool;
        }

        // 3. Single-char word prefixes (D, W, R, Z)
        if (upper.StartsWith("D") || upper.StartsWith("W") || upper.StartsWith("R") || upper.StartsWith("Z"))
        {
            return PlcDataType.Int16;
        }

        // 4. Single-char bit prefixes (X, Y, M, L, B, F, S)
        if (upper.StartsWith("X") || upper.StartsWith("Y") || upper.StartsWith("M") ||
            upper.StartsWith("L") || upper.StartsWith("B") || upper.StartsWith("F") ||
            upper.StartsWith("S"))
        {
            return PlcDataType.Bool;
        }

        return PlcDataType.Bool;
    }

    public IReadOnlyList<PlcTag> GetAllTagsToPoll()
    {
        var result = new List<PlcTag>(Tags);
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in Tags)
        {
            if (!string.IsNullOrWhiteSpace(t.Name)) existingKeys.Add($"{t.PlcId}:{t.Name}");
            if (!string.IsNullOrWhiteSpace(t.Address)) existingKeys.Add($"{t.PlcId}:{t.Address}");
        }

        var defaultPlcId = Plcs.FirstOrDefault()?.Id ?? "PLC1";

        void EnsureTagAddress(string? plcId, string? addressOrName, PlcDataType defaultType)
        {
            if (string.IsNullOrWhiteSpace(addressOrName)) return;
            string targetPlc = string.IsNullOrWhiteSpace(plcId) ? defaultPlcId : plcId;
            var targetPlcModel = Plcs.FirstOrDefault(p => string.Equals(p.Id, targetPlc, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, targetPlc, StringComparison.OrdinalIgnoreCase));
            string finalPlcId = targetPlcModel?.Id ?? targetPlc;

            string keyName = $"{finalPlcId}:{addressOrName}";
            if (!existingKeys.Contains(keyName))
            {
                existingKeys.Add(keyName);
                result.Add(new PlcTag
                {
                    PlcId = finalPlcId,
                    Name = addressOrName,
                    Address = addressOrName,
                    DataType = InferDataTypeFromAddress(addressOrName, null)
                });
            }
        }

        if (IndustrialConfig != null)
        {
            // Handshake
            EnsureTagAddress(IndustrialConfig.Handshake.PlcId, IndustrialConfig.Handshake.ReadyTagName, PlcDataType.Bool);
            EnsureTagAddress(IndustrialConfig.Handshake.PlcId, IndustrialConfig.Handshake.BusyTagName, PlcDataType.Bool);
            EnsureTagAddress(IndustrialConfig.Handshake.PlcId, IndustrialConfig.Handshake.DoneTagName, PlcDataType.Bool);
            EnsureTagAddress(IndustrialConfig.Handshake.PlcId, IndustrialConfig.Handshake.PassTagName, PlcDataType.Bool);
            EnsureTagAddress(IndustrialConfig.Handshake.PlcId, IndustrialConfig.Handshake.NgTagName, PlcDataType.Bool);
            EnsureTagAddress(IndustrialConfig.Handshake.PlcId, IndustrialConfig.Handshake.PlcAckTagName, PlcDataType.Bool);

            // Heartbeat
            EnsureTagAddress(IndustrialConfig.Heartbeat.PlcId, IndustrialConfig.Heartbeat.VisionHeartbeatTagName, PlcDataType.Bool);
            EnsureTagAddress(IndustrialConfig.Heartbeat.PlcId, IndustrialConfig.Heartbeat.PlcHeartbeatTagName, PlcDataType.Bool);
            EnsureTagAddress(IndustrialConfig.Heartbeat.PlcId, IndustrialConfig.Heartbeat.EmergencyStopTagName, PlcDataType.Bool);

            // Motion
            EnsureTagAddress(IndustrialConfig.Motion.PlcId, IndustrialConfig.Motion.EncoderTagName, PlcDataType.Int32);
            EnsureTagAddress(IndustrialConfig.Motion.PlcId, IndustrialConfig.Motion.SpeedTagName, PlcDataType.Float);

            // Shift Register
            EnsureTagAddress(IndustrialConfig.ShiftRegister.PlcId, IndustrialConfig.ShiftRegister.RejectTagName, PlcDataType.Bool);
        }

        // Dynamic Tag Providers (e.g. Oscilloscope channels, High-speed debug monitors)
        foreach (var provider in _dynamicTagProviders.Values)
        {
            try
            {
                var dynTags = provider();
                if (dynTags != null)
                {
                    foreach (var dt in dynTags)
                    {
                        if (dt != null)
                        {
                            string addr = !string.IsNullOrWhiteSpace(dt.Address) ? dt.Address : dt.Name;
                            EnsureTagAddress(dt.PlcId, addr, dt.DataType);
                        }
                    }
                }
            }
            catch { }
        }

        return result;
    }

    public void RegisterDynamicTagProvider(string providerId, Func<IEnumerable<PlcTag>> provider)
    {
        if (string.IsNullOrWhiteSpace(providerId) || provider == null) return;
        _dynamicTagProviders[providerId] = provider;
    }

    public void UnregisterDynamicTagProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;
        _dynamicTagProviders.TryRemove(providerId, out _);
    }

    public void RequestScanInterval(string sourceId, int intervalMs)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        if (intervalMs > 0)
        {
            _scanIntervalOverrides[sourceId] = intervalMs;
        }
    }

    public void ReleaseScanInterval(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        _scanIntervalOverrides.TryRemove(sourceId, out _);
    }

    public int GetEffectiveMinScanInterval(int baseScanMs)
    {
        if (_scanIntervalOverrides.Count > 0)
        {
            int minOverride = _scanIntervalOverrides.Values.Min();
            return Math.Min(baseScanMs, minOverride);
        }
        return baseScanMs;
    }

    public async Task<object?> ReadTagValueAsync(string plcId, string tagOrAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagOrAddress)) return null;

        var plc = Plcs.FirstOrDefault(p => string.Equals(p.Id, plcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, plcId, StringComparison.OrdinalIgnoreCase))
                  ?? Plcs.FirstOrDefault();
        string targetPlcId = plc?.Id ?? plcId;

        var tag = Tags.FirstOrDefault(t => (string.Equals(t.PlcId, targetPlcId, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, plcId, StringComparison.OrdinalIgnoreCase))
                                           && (string.Equals(t.Name, tagOrAddress, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Address, tagOrAddress, StringComparison.OrdinalIgnoreCase)));

        if (tag == null)
        {
            tag = new PlcTag
            {
                PlcId = targetPlcId,
                Name = tagOrAddress,
                Address = tagOrAddress,
                DataType = InferDataTypeFromAddress(tagOrAddress)
            };
        }

        var driver = GetDriver(targetPlcId);
        if (driver == null && plc != null)
        {
            CreateDriverForPlc(plc);
            driver = GetDriver(targetPlcId);
        }

        if (driver == null)
        {
            return GetTagValue(targetPlcId, tagOrAddress)?.CurrentValue;
        }

        if (!driver.IsConnected)
        {
            var now = DateTime.UtcNow;
            if (!_lastConnectAttemptTimes.TryGetValue(targetPlcId, out var lastAttempt) || (now - lastAttempt).TotalSeconds > 5)
            {
                _lastConnectAttemptTimes[targetPlcId] = now;
                try
                {
                    using var connCts = new CancellationTokenSource(500);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connCts.Token);
                    await driver.ConnectAsync(linkedCts.Token);
                }
                catch { }
            }

            if (!driver.IsConnected)
            {
                return GetTagValue(targetPlcId, tagOrAddress)?.CurrentValue;
            }
        }

        try
        {
            using var readCts = new CancellationTokenSource(2000);
            using var linkedReadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readCts.Token);
            var results = await driver.ReadBatchAsync(new[] { tag }, linkedReadCts.Token);
            if (results.TryGetValue(tag.Name, out var val))
            {
                Cache.Set(targetPlcId, tag.Name, val, TagQuality.Good);
                if (!string.IsNullOrWhiteSpace(tag.Address))
                {
                    Cache.Set(targetPlcId, tag.Address, val, TagQuality.Good);
                }
                if (plc != null)
                {
                    Cache.Set(plc.Name, tag.Name, val, TagQuality.Good);
                    if (!string.IsNullOrWhiteSpace(tag.Address))
                    {
                        Cache.Set(plc.Name, tag.Address, val, TagQuality.Good);
                    }
                }
                return val;
            }
        }
        catch (Exception ex)
        {
            Logger.LogReadError(targetPlcId, tagOrAddress, ex.Message);
        }

        return GetTagValue(targetPlcId, tagOrAddress)?.CurrentValue;
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
            tag = new PlcTag
            {
                PlcId = targetPlcId,
                Name = tagName,
                Address = tagName,
                DataType = InferDataTypeFromAddress(tagName, value)
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
            Cache.Set(targetPlcId, tag.Name, value, TagQuality.Good);
            if (!string.IsNullOrWhiteSpace(tag.Address)) Cache.Set(targetPlcId, tag.Address, value, TagQuality.Good);
            Cache.Set(plcId, tag.Name, value, TagQuality.Good);
            if (!string.IsNullOrWhiteSpace(tag.Address)) Cache.Set(plcId, tag.Address, value, TagQuality.Good);
            if (plc != null)
            {
                Cache.Set(plc.Name, tag.Name, value, TagQuality.Good);
                if (!string.IsNullOrWhiteSpace(tag.Address)) Cache.Set(plc.Name, tag.Address, value, TagQuality.Good);
            }
            NotifyTagChanged(targetPlcId, plcId, plc, tag, value);
            return true;
        }

        if (!driver.IsConnected)
        {
            var now = DateTime.UtcNow;
            if (!_lastConnectAttemptTimes.TryGetValue(targetPlcId, out var lastAttempt) || (now - lastAttempt).TotalSeconds > 5)
            {
                _lastConnectAttemptTimes[targetPlcId] = now;
                try
                {
                    using var connCts = new CancellationTokenSource(500);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connCts.Token);
                    await driver.ConnectAsync(linkedCts.Token);
                }
                catch { }
            }

            if (!driver.IsConnected)
            {
                Cache.Set(targetPlcId, tag.Name, value, TagQuality.Good);
                if (!string.IsNullOrWhiteSpace(tag.Address)) Cache.Set(targetPlcId, tag.Address, value, TagQuality.Good);
                Cache.Set(plcId, tag.Name, value, TagQuality.Good);
                if (!string.IsNullOrWhiteSpace(tag.Address)) Cache.Set(plcId, tag.Address, value, TagQuality.Good);
                if (plc != null)
                {
                    Cache.Set(plc.Name, tag.Name, value, TagQuality.Good);
                    if (!string.IsNullOrWhiteSpace(tag.Address)) Cache.Set(plc.Name, tag.Address, value, TagQuality.Good);
                }
                NotifyTagChanged(targetPlcId, plcId, plc, tag, value);
                return true;
            }
        }

        try
        {
            using var writeCts = new CancellationTokenSource(2000);
            using var linkedWriteCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, writeCts.Token);
            bool success = await driver.WriteAsync(tag, value, linkedWriteCts.Token);
            if (success)
            {
                Cache.Set(targetPlcId, tag.Name, value, TagQuality.Good);
                if (!string.IsNullOrWhiteSpace(tag.Address))
                {
                    Cache.Set(targetPlcId, tag.Address, value, TagQuality.Good);
                }

                Cache.Set(plcId, tag.Name, value, TagQuality.Good);
                if (!string.IsNullOrWhiteSpace(tag.Address))
                {
                    Cache.Set(plcId, tag.Address, value, TagQuality.Good);
                }

                if (plc != null)
                {
                    Cache.Set(plc.Name, tag.Name, value, TagQuality.Good);
                    if (!string.IsNullOrWhiteSpace(tag.Address))
                    {
                        Cache.Set(plc.Name, tag.Address, value, TagQuality.Good);
                    }
                }

                NotifyTagChanged(targetPlcId, plcId, plc, tag, value);
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

    private void NotifyTagChanged(string targetPlcId, string originalPlcId, PlcModel? plc, PlcTag tag, object value)
    {
        var sentPlcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void RaiseEvent(string? pid, string? tName)
        {
            if (string.IsNullOrWhiteSpace(pid) || string.IsNullOrWhiteSpace(tName)) return;
            string key = $"{pid}:{tName}";
            if (sentPlcIds.Add(key))
            {
                OnTagChanged?.Invoke(this, new TagChangedEventArgs(pid, tName, null, value, DateTime.Now));
            }
        }

        RaiseEvent(targetPlcId, tag.Name);
        RaiseEvent(originalPlcId, tag.Name);
        if (plc != null) RaiseEvent(plc.Name, tag.Name);

        if (!string.IsNullOrWhiteSpace(tag.Address))
        {
            RaiseEvent(targetPlcId, tag.Address);
            RaiseEvent(originalPlcId, tag.Address);
            if (plc != null) RaiseEvent(plc.Name, tag.Address);
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

    public bool IsPlcConnected(string plcId)
    {
        if (string.IsNullOrWhiteSpace(plcId)) return false;
        var driver = GetDriver(plcId) ?? GetDriverByName(plcId);
        return driver != null && driver.IsConnected;
    }

    public PlcTagValue? GetTagValue(string plcId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(plcId) || string.IsNullOrWhiteSpace(tagName)) return null;

        var plc = Plcs.FirstOrDefault(p => string.Equals(p.Id, plcId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, plcId, StringComparison.OrdinalIgnoreCase))
                  ?? Plcs.FirstOrDefault();
        string targetPlcId = plc?.Id ?? plcId;
        string targetPlcName = plc?.Name ?? plcId;

        // 1. Direct cache check by Name / Address
        var val = Cache.Get(targetPlcId, tagName) ?? Cache.Get(targetPlcName, tagName) ?? Cache.Get(plcId, tagName);
        if (val != null) return val;

        // 2. Cross-check with configured Tags (if user queried address vs name)
        var matchedTag = Tags.FirstOrDefault(t => (string.Equals(t.PlcId, targetPlcId, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, targetPlcName, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, plcId, StringComparison.OrdinalIgnoreCase))
                                                   && (string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Address, tagName, StringComparison.OrdinalIgnoreCase)));
        if (matchedTag != null)
        {
            var valByName = Cache.Get(targetPlcId, matchedTag.Name) ?? Cache.Get(targetPlcName, matchedTag.Name);
            if (valByName != null) return valByName;

            var valByAddr = Cache.Get(targetPlcId, matchedTag.Address) ?? Cache.Get(targetPlcName, matchedTag.Address);
            if (valByAddr != null) return valByAddr;
        }

        return null;
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
        PollingEngine.Start(() => Plcs.ToList(), () => GetAllTagsToPoll(), GetDriver, GetEffectiveMinScanInterval);
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

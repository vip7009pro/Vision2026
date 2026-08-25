using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels.PLC;

public sealed partial class PlcTagDisplayItem : ObservableObject
{
    public string PlcName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;

    [ObservableProperty]
    private string _currentValue = "N/A";

    public string Address { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public partial class PlcBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IPlcManagerService _plcService;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private readonly object _lockObj = new();
    private bool _disposed;

    [ObservableProperty]
    private string _filterText = string.Empty;

    public ObservableCollection<PlcTagDisplayItem> TagItems { get; } = new();

    public PlcBrowserViewModel(IPlcManagerService plcService)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));

        // Tự động giữ Polling Lock để Polling Engine quét dữ liệu từ PLC
        _plcService.AcquirePollingLock("PlcBrowser");

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _refreshTimer.Tick += (s, e) => RefreshTags();
        _refreshTimer.Start();

        _plcService.OnBatchPolled += HandleBatchPolled;
        _plcService.OnTagChanged += HandleTagChanged;

        RefreshTags();
    }

    private void HandleBatchPolled(object? sender, BatchPolledEventArgs e)
    {
        if (_disposed) return;
        System.Windows.Application.Current?.Dispatcher?.InvokeAsync(RefreshTags);
    }

    private void HandleTagChanged(object? sender, TagChangedEventArgs e)
    {
        if (_disposed) return;
        System.Windows.Application.Current?.Dispatcher?.InvokeAsync(RefreshTags);
    }

    partial void OnFilterTextChanged(string value)
    {
        RefreshTags();
    }

    public void RefreshTags()
    {
        if (_disposed) return;
        lock (_lockObj)
        {
            var tags = _plcService.Tags.ToList();
            var plcs = _plcService.Plcs.ToDictionary(p => p.Id, p => p.Name, StringComparer.OrdinalIgnoreCase);

            var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentItemsDict = TagItems.ToDictionary(i => $"{i.PlcName}:{i.TagName}", StringComparer.OrdinalIgnoreCase);

            foreach (var tag in tags)
            {
                plcs.TryGetValue(tag.PlcId, out var plcName);
                plcName ??= tag.PlcId;

                if (!string.IsNullOrWhiteSpace(FilterText) &&
                    !tag.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) &&
                    !plcName.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string key = $"{plcName}:{tag.Name}";
                validKeys.Add(key);

                var val = _plcService.GetTagValue(tag.PlcId, tag.Name) 
                          ?? _plcService.GetTagValue(plcName, tag.Name)
                          ?? _plcService.GetTagValue(tag.PlcId, tag.Address)
                          ?? _plcService.GetTagValue(plcName, tag.Address);
                string newStr = val?.CurrentValue != null ? val.CurrentValue.ToString()! : "N/A";

                if (currentItemsDict.TryGetValue(key, out var existingItem))
                {
                    if (existingItem.CurrentValue != newStr)
                    {
                        existingItem.CurrentValue = newStr;
                    }
                }
                else
                {
                    var newItem = new PlcTagDisplayItem
                    {
                        PlcName = plcName,
                        TagName = tag.Name,
                        DataType = tag.DataType.ToString(),
                        CurrentValue = newStr,
                        Address = tag.Address,
                        Description = tag.Description
                    };
                    TagItems.Add(newItem);
                }
            }

            // Remove items no longer present
            for (int i = TagItems.Count - 1; i >= 0; i--)
            {
                var item = TagItems[i];
                string key = $"{item.PlcName}:{item.TagName}";
                if (!validKeys.Contains(key))
                {
                    TagItems.RemoveAt(i);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer.Stop();
        _plcService.OnBatchPolled -= HandleBatchPolled;
        _plcService.OnTagChanged -= HandleTagChanged;
        _plcService.ReleasePollingLock("PlcBrowser");
    }
}

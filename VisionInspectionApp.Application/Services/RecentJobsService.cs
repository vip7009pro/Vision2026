using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VisionInspectionApp.Application.Services;

public interface IRecentJobsService
{
    IReadOnlyList<string> GetRecentJobs();
    void AddRecentJob(string jobFilePath);
    void RemoveRecentJob(string jobFilePath);
    void ClearRecentJobs();
    event Action? RecentJobsChanged;
}

public sealed class RecentJobsService : IRecentJobsService
{
    private const int MaxRecentJobs = 10;
    private readonly string _storageFilePath;
    private readonly List<string> _recentJobs = new();
    private readonly object _lock = new();

    public event Action? RecentJobsChanged;

    public RecentJobsService()
    {
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CMS_VINA_Vision");
        if (!Directory.Exists(appDataDir))
        {
            try { Directory.CreateDirectory(appDataDir); } catch { }
        }
        _storageFilePath = Path.Combine(appDataDir, "recent_jobs.json");
        LoadFromDisk();
    }

    public RecentJobsService(string customStorageFilePath)
    {
        _storageFilePath = customStorageFilePath;
        LoadFromDisk();
    }

    public IReadOnlyList<string> GetRecentJobs()
    {
        lock (_lock)
        {
            return _recentJobs.Where(File.Exists).Take(MaxRecentJobs).ToList();
        }
    }

    public void AddRecentJob(string jobFilePath)
    {
        if (string.IsNullOrWhiteSpace(jobFilePath))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(jobFilePath);
            lock (_lock)
            {
                _recentJobs.RemoveAll(x => string.Equals(x, fullPath, StringComparison.OrdinalIgnoreCase));
                _recentJobs.Insert(0, fullPath);

                if (_recentJobs.Count > MaxRecentJobs)
                {
                    _recentJobs.RemoveRange(MaxRecentJobs, _recentJobs.Count - MaxRecentJobs);
                }

                SaveToDisk();
            }

            RecentJobsChanged?.Invoke();
        }
        catch
        {
            // Ignore path normalization errors
        }
    }

    public void RemoveRecentJob(string jobFilePath)
    {
        if (string.IsNullOrWhiteSpace(jobFilePath))
        {
            return;
        }

        lock (_lock)
        {
            _recentJobs.RemoveAll(x => string.Equals(x, jobFilePath, StringComparison.OrdinalIgnoreCase));
            SaveToDisk();
        }

        RecentJobsChanged?.Invoke();
    }

    public void ClearRecentJobs()
    {
        lock (_lock)
        {
            _recentJobs.Clear();
            SaveToDisk();
        }

        RecentJobsChanged?.Invoke();
    }

    private void LoadFromDisk()
    {
        lock (_lock)
        {
            _recentJobs.Clear();
            if (!File.Exists(_storageFilePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_storageFilePath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    _recentJobs.AddRange(list.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).Take(MaxRecentJobs));
                }
            }
            catch
            {
                // Fallback on corrupt file
            }
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_recentJobs, new JsonSerializerOptions { WriteIndented = true });
            var dir = Path.GetDirectoryName(_storageFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_storageFilePath, json);
        }
        catch
        {
            // Ignore file write errors
        }
    }
}

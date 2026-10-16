using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class DefaultJobTemplateViewModel : ObservableObject
{
    private readonly GlobalAppSettingsService _settingsService;

    private string? _selectedJobPath;

    public string? SelectedJobPath
    {
        get => _selectedJobPath;
        set => SetProperty(ref _selectedJobPath, value);
    }

    public ObservableCollection<JobTemplateOption> AvailableJobs { get; } = new();
    public IRelayCommand BrowseCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public event Action? RequestClose;

    public DefaultJobTemplateViewModel(GlobalAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        SelectedJobPath = settingsService.Settings.DefaultJobTemplatePath;
        RefreshAvailableJobs();
        BrowseCommand = new RelayCommand(BrowseForJob);
        ClearCommand = new RelayCommand(() => SelectedJobPath = null);
        SaveCommand = new RelayCommand(Save);
    }

    private void RefreshAvailableJobs(string? additionalPath = null)
    {
        AppStoragePaths.EnsureStorageStructureAndMigrate();
        var selectedPath = SelectedJobPath;
        var jobPaths = new[]
            {
                AppStoragePaths.JobsDirectory,
                Path.Combine(AppStoragePaths.AppBaseDirectory, "jobs")
            }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.job", SearchOption.TopDirectoryOnly))
            .Concat(new[] { selectedPath, additionalPath }.OfType<string>().Where(File.Exists))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => new JobTemplateOption(path, Path.GetFileName(path)));

        AvailableJobs.Clear();
        foreach (var job in jobPaths)
        {
            AvailableJobs.Add(job);
        }
    }

    private void BrowseForJob()
    {
        var initialDirectory = !string.IsNullOrWhiteSpace(SelectedJobPath) && File.Exists(SelectedJobPath)
            ? Path.GetDirectoryName(SelectedJobPath)
            : AppStoragePaths.JobsDirectory;
        var dialog = new OpenFileDialog
        {
            Title = "Chọn Job mẫu mặc định",
            Filter = "Vision Job Files (*.job)|*.job|All Files (*.*)|*.*",
            InitialDirectory = initialDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPath = Path.GetFullPath(dialog.FileName);
            RefreshAvailableJobs(selectedPath);
            SelectedJobPath = selectedPath;
        }
    }

    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(SelectedJobPath) && !File.Exists(SelectedJobPath))
        {
            MessageBox.Show("Tệp Job mẫu đã chọn không còn tồn tại.", "Job mặc định", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = _settingsService.Settings;
        settings.DefaultJobTemplatePath = SelectedJobPath ?? string.Empty;
        _settingsService.UpdateSettings(settings);
        RequestClose?.Invoke();
    }
}

public sealed record JobTemplateOption(string Path, string Name);

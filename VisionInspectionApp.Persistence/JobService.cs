using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Persistence;

public sealed class JobService : IJobService
{
    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public VisionConfig LoadJob(string jobFilePath, out string tempWorkingDir)
    {
        if (string.IsNullOrWhiteSpace(jobFilePath) || !File.Exists(jobFilePath))
        {
            throw new FileNotFoundException($"Job file not found: {jobFilePath}");
        }

        tempWorkingDir = Path.Combine(Path.GetTempPath(), "Vision2026", "Jobs", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempWorkingDir);
        
        ZipFile.ExtractToDirectory(jobFilePath, tempWorkingDir, overwriteFiles: true);

        var jsonFiles = Directory.GetFiles(tempWorkingDir, "*.json");
        if (jsonFiles.Length == 0)
        {
            throw new FileNotFoundException("No configuration JSON found in the job file.");
        }
        
        var json = File.ReadAllText(jsonFiles[0]);
        var config = JsonSerializer.Deserialize<VisionConfig>(json, CreateJsonOptions());
        if (config is null) throw new InvalidDataException("Failed to deserialize job configuration.");
        
        return config;
    }

    public void SaveJob(VisionConfig config, string tempWorkingDir, string jobFilePath)
    {
        if (string.IsNullOrWhiteSpace(tempWorkingDir) || !Directory.Exists(tempWorkingDir))
        {
            throw new DirectoryNotFoundException($"Temp directory not found: {tempWorkingDir}");
        }
        
        var json = JsonSerializer.Serialize(config, CreateJsonOptions());
        var jsonFilePath = Path.Combine(tempWorkingDir, "config.json");
        File.WriteAllText(jsonFilePath, json);

        if (File.Exists(jobFilePath))
        {
            File.Delete(jobFilePath);
        }

        var dirName = Path.GetDirectoryName(jobFilePath);
        if (!string.IsNullOrWhiteSpace(dirName))
        {
            Directory.CreateDirectory(dirName);
        }

        ZipFile.CreateFromDirectory(tempWorkingDir, jobFilePath, CompressionLevel.Fastest, includeBaseDirectory: false);
    }
}

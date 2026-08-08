using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.HMI;

public class HmiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GlobalLibraryDirectory
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "VisionInspectionApp", "HMI_Library");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
    }

    public static async Task SaveHmiConfigAsync(string filePath, HmiScreenConfig config)
    {
        if (string.IsNullOrWhiteSpace(filePath) || config == null) return;

        string directory = Path.GetDirectoryName(filePath) ?? "";
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static HmiScreenConfig LoadHmiConfig(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new HmiScreenConfig();
        }

        try
        {
            string json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<HmiScreenConfig>(json, JsonOptions);
            return config ?? new HmiScreenConfig();
        }
        catch
        {
            return new HmiScreenConfig();
        }
    }

    public static string CopyImageToLibrary(string sourceImagePath, string projectRootDirectory = "")
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            return sourceImagePath;
        }

        string fileName = Path.GetFileName(sourceImagePath);
        string timePrefix = DateTime.Now.ToString("yyyyMMdd_HHmmss_");
        string targetFileName = timePrefix + fileName;

        // 1. Copy to Global Library
        string globalPath = Path.Combine(GlobalLibraryDirectory, targetFileName);
        File.Copy(sourceImagePath, globalPath, overwrite: true);

        // 2. Copy to Local Project Library if project path is provided
        if (!string.IsNullOrWhiteSpace(projectRootDirectory) && Directory.Exists(projectRootDirectory))
        {
            string projectHmiDir = Path.Combine(projectRootDirectory, "Resources", "HMI");
            if (!Directory.Exists(projectHmiDir))
            {
                Directory.CreateDirectory(projectHmiDir);
            }
            string localPath = Path.Combine(projectHmiDir, targetFileName);
            File.Copy(sourceImagePath, localPath, overwrite: true);
            return localPath;
        }

        return globalPath;
    }

    public static List<string> GetGlobalLibraryImages(string projectRootDirectory = "")
    {
        var images = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFilesFromDir(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;

            string[] extensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.svg" };
            foreach (var ext in extensions)
            {
                foreach (var file in Directory.GetFiles(dirPath, ext))
                {
                    if (seenNames.Add(Path.GetFileName(file)))
                    {
                        images.Add(file);
                    }
                }
            }
        }

        // Add Local project library images first
        if (!string.IsNullOrWhiteSpace(projectRootDirectory))
        {
            AddFilesFromDir(Path.Combine(projectRootDirectory, "Resources", "HMI"));
        }

        // Add Global Library images
        AddFilesFromDir(GlobalLibraryDirectory);

        return images;
    }
}

using System.Text.Json;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Persistence;

public sealed class JsonConfigService : IConfigService
{
    private readonly ConfigStoreOptions _options;

    public JsonConfigService(ConfigStoreOptions options)
    {
        _options = options;
    }

    public VisionConfig LoadConfig(string productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            throw new ArgumentException("Product code is required.", nameof(productCode));
        }

        var configFile = GetConfigFilePath(productCode);
        if (!File.Exists(configFile))
        {
            throw new FileNotFoundException($"Config not found for product '{productCode}'.", configFile);
        }

        var json = File.ReadAllText(configFile);
        var config = JsonSerializer.Deserialize<VisionConfig>(json, CreateJsonOptions());
        if (config is null)
        {
            throw new InvalidDataException($"Invalid config content: {configFile}");
        }

        config.ProductCode = productCode;

        NormalizeTemplatePathsForLoad(config);
        EnsureTemplateDirectory(productCode);

        return config;
    }

    public void SaveConfig(VisionConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.ProductCode))
        {
            throw new ArgumentException("ProductCode is required.", nameof(config));
        }

        EnsureConfigDirectory();
        EnsureTemplateDirectory(config.ProductCode);

        NormalizeTemplatePathsForSave(config);

        var json = JsonSerializer.Serialize(config, CreateJsonOptions());
        var configFile = GetConfigFilePath(config.ProductCode);
        File.WriteAllText(configFile, json);
    }

    private void EnsureConfigDirectory()
    {
        Directory.CreateDirectory(GetConfigRootDirectory());
    }

    private void EnsureTemplateDirectory(string productCode)
    {
        Directory.CreateDirectory(GetTemplateDirectory(productCode));
    }

    private string GetConfigRootDirectory()
    {
        var root = _options.ConfigRootDirectory;
        if (Path.IsPathRooted(root))
        {
            return root;
        }

        return Path.GetFullPath(root);
    }

    private string GetConfigFilePath(string productCode)
    {
        return Path.Combine(GetConfigRootDirectory(), $"{productCode}.json");
    }

    private string GetTemplateDirectory(string productCode)
    {
        return Path.Combine(GetConfigRootDirectory(), productCode, "templates");
    }

    private void NormalizeTemplatePathsForSave(VisionConfig config)
    {
        var templateDir = GetTemplateDirectory(config.ProductCode);

        NormalizePointTemplatePathForSave(config.Origin, templateDir);
        foreach (var p in config.Points)
        {
            NormalizePointTemplatePathForSave(p, templateDir);
        }

        foreach (var sc in config.SurfaceCompares)
        {
            NormalizeSurfaceCompareTemplatePathForSave(sc, templateDir);
        }
    }
    private void NormalizePointTemplatePathForSave(PointDefinition point, string templateDir)
    {
        if (string.IsNullOrWhiteSpace(point.TemplateImageFile))
        {
            return;
        }

        var full = Path.IsPathRooted(point.TemplateImageFile)
            ? point.TemplateImageFile
            : Path.GetFullPath(Path.Combine(templateDir, point.TemplateImageFile));

        if (full.StartsWith(templateDir, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(templateDir, full);
            point.TemplateImageFile = relative;
        }
        else
        {
            point.TemplateImageFile = full;
        }
    }

    private void NormalizeSurfaceCompareTemplatePathForSave(SurfaceCompareDefinition sc, string templateDir)
    {
        if (string.IsNullOrWhiteSpace(sc.TemplateImageFile))
        {
            return;
        }

        var full = Path.IsPathRooted(sc.TemplateImageFile)
            ? sc.TemplateImageFile
            : Path.GetFullPath(Path.Combine(templateDir, sc.TemplateImageFile));

        if (full.StartsWith(templateDir, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(templateDir, full);
            sc.TemplateImageFile = relative;
        }
        else
        {
            sc.TemplateImageFile = full;
        }
    }

    private void NormalizeTemplatePathsForLoad(VisionConfig config)
    {
        var templateDir = GetTemplateDirectory(config.ProductCode);

        NormalizePointTemplatePathForLoad(config.Origin, templateDir);
        foreach (var p in config.Points)
        {
            NormalizePointTemplatePathForLoad(p, templateDir);
        }

        foreach (var sc in config.SurfaceCompares)
        {
            NormalizeSurfaceCompareTemplatePathForLoad(sc, templateDir);
        }
    }

    private void NormalizePointTemplatePathForLoad(PointDefinition point, string templateDir)
    {
        if (string.IsNullOrWhiteSpace(point.TemplateImageFile))
        {
            return;
        }

        if (!Path.IsPathRooted(point.TemplateImageFile))
        {
            point.TemplateImageFile = Path.GetFullPath(Path.Combine(templateDir, point.TemplateImageFile));
        }
    }

    private void NormalizeSurfaceCompareTemplatePathForLoad(SurfaceCompareDefinition sc, string templateDir)
    {
        if (string.IsNullOrWhiteSpace(sc.TemplateImageFile))
        {
            return;
        }

        if (!Path.IsPathRooted(sc.TemplateImageFile))
        {
            sc.TemplateImageFile = Path.GetFullPath(Path.Combine(templateDir, sc.TemplateImageFile));
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }
}

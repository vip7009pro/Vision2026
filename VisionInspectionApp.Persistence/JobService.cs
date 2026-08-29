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
        
        // BẮT BUỘC CHỈ RESOLVE VÀ GÁN ĐƯỜNG DẪN TEMPLATE TRONG NỘI BỘ tempWorkingDir ĐƯỢC GIẢI NÉN TỪ FILE .JOB
        ResolveAndBindJobTemplates(config, tempWorkingDir);

        VisionInspectionApp.Application.Services.ChessboardCalibrationService.EnsureCalibration(config);

        return config;
    }

    public void SaveJob(VisionConfig config, string tempWorkingDir, string jobFilePath)
    {
        if (string.IsNullOrWhiteSpace(tempWorkingDir) || !Directory.Exists(tempWorkingDir))
        {
            throw new DirectoryNotFoundException($"Temp directory not found: {tempWorkingDir}");
        }

        var templatesDir = Path.Combine(tempWorkingDir, "templates");
        Directory.CreateDirectory(templatesDir);

        // 1. Đảm bảo tất cả file ảnh template thực tế được lưu/copy vào tempWorkingDir/templates/
        EnsureTemplateFilesPresentInTemp(config, tempWorkingDir, templatesDir);

        // 2. Chuẩn bị bản sao config sạch để serialize ra JSON trong file .job:
        // TẤT CẢ các templateImageFile CHỈ LƯU TÊN FILE TƯƠNG ĐỐI (ví dụ "origin.png", "p1.png"),
        // TUYỆT ĐỐI KHÔNG BAO GIỜ GHI ĐƯỜNG DẪN TUYỆT ĐỐI CỦA MÁY VÀO FILE .JOB
        var configJson = SerializeConfigWithRelativeTemplatePaths(config);
        var jsonFilePath = Path.Combine(tempWorkingDir, "config.json");
        File.WriteAllText(jsonFilePath, configJson);

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

    /// <summary>
    /// Tìm kiếm và gán đường dẫn file template CHỈ trong nội bộ thư mục giải nén của file Job.
    /// Không bao giờ tìm kiếm ra thư mục configs bên ngoài máy.
    /// </summary>
    public static void ResolveAndBindJobTemplates(VisionConfig config, string tempWorkingDir)
    {
        if (config is null || string.IsNullOrWhiteSpace(tempWorkingDir) || !Directory.Exists(tempWorkingDir))
            return;

        var templatesSubdir = Path.Combine(tempWorkingDir, "templates");

        string? FindFileInJob(string? currentPath, string fallbackFileName, string fallbackPattern)
        {
            // Trích xuất tên file sạch (bỏ mọi đường dẫn tuyệt đối cũ nếu có trong json)
            var cleanFileName = !string.IsNullOrWhiteSpace(currentPath) ? Path.GetFileName(currentPath) : fallbackFileName;

            // 1. Tìm trong tempWorkingDir/templates/{cleanFileName}
            if (!string.IsNullOrWhiteSpace(cleanFileName) && Directory.Exists(templatesSubdir))
            {
                var p1 = Path.Combine(templatesSubdir, cleanFileName);
                if (File.Exists(p1)) return Path.GetFullPath(p1);
            }

            // 2. Tìm trong tempWorkingDir/{cleanFileName} (gốc zip)
            if (!string.IsNullOrWhiteSpace(cleanFileName))
            {
                var p2 = Path.Combine(tempWorkingDir, cleanFileName);
                if (File.Exists(p2)) return Path.GetFullPath(p2);
            }

            // 3. Nếu cleanFileName khác fallbackFileName, thử tìm fallbackFileName
            if (!string.IsNullOrWhiteSpace(fallbackFileName))
            {
                if (Directory.Exists(templatesSubdir))
                {
                    var p3 = Path.Combine(templatesSubdir, fallbackFileName);
                    if (File.Exists(p3)) return Path.GetFullPath(p3);
                }
                var p4 = Path.Combine(tempWorkingDir, fallbackFileName);
                if (File.Exists(p4)) return Path.GetFullPath(p4);
            }

            // 4. Tìm kiếm theo wildcard pattern trong tempWorkingDir/templates
            if (!string.IsNullOrWhiteSpace(fallbackPattern) && Directory.Exists(templatesSubdir))
            {
                var matches = Directory.GetFiles(templatesSubdir, fallbackPattern);
                if (matches.Length > 0) return Path.GetFullPath(matches[0]);
            }

            // 5. Tìm kiếm theo wildcard pattern trong tempWorkingDir
            if (!string.IsNullOrWhiteSpace(fallbackPattern))
            {
                var matches = Directory.GetFiles(tempWorkingDir, fallbackPattern);
                if (matches.Length > 0) return Path.GetFullPath(matches[0]);
            }

            return null;
        }

        // Origin
        if (config.Origin != null)
        {
            var found = FindFileInJob(config.Origin.TemplateImageFile, "origin.png", "origin*.png");
            if (!string.IsNullOrWhiteSpace(found))
            {
                config.Origin.TemplateImageFile = found;
            }
        }

        // Points
        if (config.Points != null)
        {
            foreach (var p in config.Points)
            {
                if (p == null) continue;
                var fb = !string.IsNullOrWhiteSpace(p.Name) ? $"{p.Name.ToLowerInvariant()}.png" : "point.png";
                var pattern = !string.IsNullOrWhiteSpace(p.Name) ? $"{p.Name.ToLowerInvariant()}*.png" : "point*.png";
                var found = FindFileInJob(p.TemplateImageFile, fb, pattern);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    p.TemplateImageFile = found;
                }
            }
        }

        // SurfaceCompares
        if (config.SurfaceCompares != null)
        {
            foreach (var sc in config.SurfaceCompares)
            {
                if (sc == null) continue;
                var fb = !string.IsNullOrWhiteSpace(sc.Name) ? $"{sc.Name.ToLowerInvariant()}.png" : "surface.png";
                var pattern = !string.IsNullOrWhiteSpace(sc.Name) ? $"{sc.Name.ToLowerInvariant()}*.png" : "surface*.png";
                var found = FindFileInJob(sc.TemplateImageFile, fb, pattern);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    sc.TemplateImageFile = found;
                }
            }
        }

        // ContourCompares
        if (config.ContourCompares != null)
        {
            foreach (var cc in config.ContourCompares)
            {
                if (cc == null) continue;
                var fb = !string.IsNullOrWhiteSpace(cc.Name) ? $"{cc.Name.ToLowerInvariant()}.png" : "contour.png";
                var pattern = !string.IsNullOrWhiteSpace(cc.Name) ? $"{cc.Name.ToLowerInvariant()}*.png" : "contour*.png";
                var found = FindFileInJob(cc.TemplateImageFile, fb, pattern);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    cc.TemplateImageFile = found;
                }
            }
        }
    }

    private static void EnsureTemplateFilesPresentInTemp(VisionConfig config, string tempWorkingDir, string templatesDir)
    {
        void CheckAndCopy(string? sourcePath, string targetFileName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return;
            var destPath = Path.Combine(templatesDir, targetFileName);
            // Nếu trong tempWorkingDir/templates/ ĐÃ CÓ file của Job rồi thì ưu tiên giữ nguyên
            if (File.Exists(destPath))
            {
                return;
            }

            if (File.Exists(sourcePath) && !string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                catch { }
            }
        }

        if (config.Origin != null && !string.IsNullOrWhiteSpace(config.Origin.TemplateImageFile))
        {
            var fn = Path.GetFileName(config.Origin.TemplateImageFile);
            if (string.IsNullOrWhiteSpace(fn)) fn = "origin.png";
            CheckAndCopy(config.Origin.TemplateImageFile, fn);
        }

        if (config.Points != null)
        {
            foreach (var p in config.Points)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.TemplateImageFile)) continue;
                var fn = Path.GetFileName(p.TemplateImageFile);
                if (string.IsNullOrWhiteSpace(fn)) fn = $"{p.Name.ToLowerInvariant()}.png";
                CheckAndCopy(p.TemplateImageFile, fn);
            }
        }

        if (config.SurfaceCompares != null)
        {
            foreach (var sc in config.SurfaceCompares)
            {
                if (sc == null || string.IsNullOrWhiteSpace(sc.TemplateImageFile)) continue;
                var fn = Path.GetFileName(sc.TemplateImageFile);
                if (string.IsNullOrWhiteSpace(fn)) fn = $"{sc.Name.ToLowerInvariant()}.png";
                CheckAndCopy(sc.TemplateImageFile, fn);
            }
        }

        if (config.ContourCompares != null)
        {
            foreach (var cc in config.ContourCompares)
            {
                if (cc == null || string.IsNullOrWhiteSpace(cc.TemplateImageFile)) continue;
                var fn = Path.GetFileName(cc.TemplateImageFile);
                if (string.IsNullOrWhiteSpace(fn)) fn = $"{cc.Name.ToLowerInvariant()}.png";
                CheckAndCopy(cc.TemplateImageFile, fn);
            }
        }
    }

    private static string SerializeConfigWithRelativeTemplatePaths(VisionConfig config)
    {
        // Lưu tạm đường dẫn runtime hiện tại
        var originPath = config.Origin?.TemplateImageFile;
        var pointPaths = config.Points?.Select(p => p.TemplateImageFile).ToList();
        var scPaths = config.SurfaceCompares?.Select(sc => sc.TemplateImageFile).ToList();
        var ccPaths = config.ContourCompares?.Select(cc => cc.TemplateImageFile).ToList();

        try
        {
            // Chuyển toàn bộ sang tên file tương đối sạch (ví dụ "origin.png", "point1.png")
            if (config.Origin != null && !string.IsNullOrWhiteSpace(config.Origin.TemplateImageFile))
            {
                config.Origin.TemplateImageFile = Path.GetFileName(config.Origin.TemplateImageFile);
            }

            if (config.Points != null)
            {
                foreach (var p in config.Points)
                {
                    if (p != null && !string.IsNullOrWhiteSpace(p.TemplateImageFile))
                    {
                        p.TemplateImageFile = Path.GetFileName(p.TemplateImageFile);
                    }
                }
            }

            if (config.SurfaceCompares != null)
            {
                foreach (var sc in config.SurfaceCompares)
                {
                    if (sc != null && !string.IsNullOrWhiteSpace(sc.TemplateImageFile))
                    {
                        sc.TemplateImageFile = Path.GetFileName(sc.TemplateImageFile);
                    }
                }
            }

            if (config.ContourCompares != null)
            {
                foreach (var cc in config.ContourCompares)
                {
                    if (cc != null && !string.IsNullOrWhiteSpace(cc.TemplateImageFile))
                    {
                        cc.TemplateImageFile = Path.GetFileName(cc.TemplateImageFile);
                    }
                }
            }

            return JsonSerializer.Serialize(config, CreateJsonOptions());
        }
        finally
        {
            // Khôi phục lại đường dẫn runtime để ứng dụng tiếp tục hoạt động mượt mà
            if (config.Origin != null) config.Origin.TemplateImageFile = originPath;
            if (config.Points != null && pointPaths != null)
            {
                for (int i = 0; i < config.Points.Count && i < pointPaths.Count; i++)
                {
                    config.Points[i].TemplateImageFile = pointPaths[i];
                }
            }
            if (config.SurfaceCompares != null && scPaths != null)
            {
                for (int i = 0; i < config.SurfaceCompares.Count && i < scPaths.Count; i++)
                {
                    config.SurfaceCompares[i].TemplateImageFile = scPaths[i];
                }
            }
            if (config.ContourCompares != null && ccPaths != null)
            {
                for (int i = 0; i < config.ContourCompares.Count && i < ccPaths.Count; i++)
                {
                    config.ContourCompares[i].TemplateImageFile = ccPaths[i];
                }
            }
        }
    }
}

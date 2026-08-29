using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.Persistence;

namespace TestExtractApp;

public static class OriginTemplateJobTest
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {message}");
            Console.ResetColor();
            throw new Exception($"Assertion failed: {message}");
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✅ PASS: {message}");
        Console.ResetColor();
    }

    public static void RunTests()
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("🎯 RUNNING ORIGIN TEMPLATE JOB LOADING & RESOLUTION TESTS");
        Console.WriteLine("========================================================");

        var testBaseDir = Path.Combine(Path.GetTempPath(), "Vision2026_Test_Origin", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testBaseDir);

        try
        {
            // Helper resolve method matching ToolEditorViewModel.ResolveTemplatePath logic
            string? ResolvePath(string? currentPath, string tempWorkingDir, string? fallbackName = null, string? fallbackPattern = null)
            {
                if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                    return Path.GetFullPath(currentPath);

                var candidateDirs = new[]
                {
                    Path.Combine(tempWorkingDir, "templates"),
                    tempWorkingDir
                };

                var candidateFiles = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    var clean = currentPath.Trim().Replace('/', '\\');
                    candidateFiles.Add(clean);
                    var fnOnly = Path.GetFileName(clean);
                    if (!string.IsNullOrWhiteSpace(fnOnly)) candidateFiles.Add(fnOnly);
                    if (clean.StartsWith("templates\\", StringComparison.OrdinalIgnoreCase))
                        candidateFiles.Add(clean.Substring("templates\\".Length));
                }

                if (!string.IsNullOrWhiteSpace(fallbackName))
                {
                    candidateFiles.Add(fallbackName);
                    if (!fallbackName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        candidateFiles.Add($"{fallbackName}.png");
                }

                foreach (var dir in candidateDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in candidateFiles)
                    {
                        var full = Path.Combine(dir, f);
                        if (File.Exists(full)) return Path.GetFullPath(full);
                    }
                }

                if (!string.IsNullOrWhiteSpace(fallbackPattern))
                {
                    foreach (var dir in candidateDirs)
                    {
                        if (!Directory.Exists(dir)) continue;
                        var matches = Directory.GetFiles(dir, fallbackPattern);
                        if (matches.Length > 0) return Path.GetFullPath(matches[0]);
                    }
                }

                return null;
            }

            // Test 1: File origin.png in templates/ folder
            var temp1 = Path.Combine(testBaseDir, "job1");
            var templDir1 = Path.Combine(temp1, "templates");
            Directory.CreateDirectory(templDir1);
            var file1 = Path.Combine(templDir1, "origin.png");
            using (var m = new Mat(50, 50, MatType.CV_8UC1, new Scalar(128)))
            {
                Cv2.ImWrite(file1, m);
            }

            var resolved1 = ResolvePath("origin.png", temp1, "origin.png", "origin*.png");
            Assert(resolved1 != null && File.Exists(resolved1), "Test 1: Resolve origin.png in templates/ subdir");

            // Test 2: File origin.png in root temp directory (no templates/ folder)
            var temp2 = Path.Combine(testBaseDir, "job2");
            Directory.CreateDirectory(temp2);
            var file2 = Path.Combine(temp2, "origin.png");
            using (var m = new Mat(40, 40, MatType.CV_8UC1, new Scalar(200)))
            {
                Cv2.ImWrite(file2, m);
            }

            var resolved2 = ResolvePath("origin.png", temp2, "origin.png", "origin*.png");
            Assert(resolved2 != null && File.Exists(resolved2), "Test 2: Resolve origin.png at root of temp directory");

            // Test 3: Old rooted path from another machine/previous temp folder (C:\OldTemp\guid\templates\origin.png)
            var temp3 = Path.Combine(testBaseDir, "job3");
            var templDir3 = Path.Combine(temp3, "templates");
            Directory.CreateDirectory(templDir3);
            var file3 = Path.Combine(templDir3, "origin.png");
            using (var m = new Mat(30, 30, MatType.CV_8UC1, new Scalar(100)))
            {
                Cv2.ImWrite(file3, m);
            }

            var oldRootedPath = @"C:\Users\OldUser\AppData\Local\Temp\Vision2026\Jobs\non_existent_guid\templates\origin.png";
            var resolved3 = ResolvePath(oldRootedPath, temp3, "origin.png", "origin*.png");
            Assert(resolved3 != null && File.Exists(resolved3) && resolved3 == Path.GetFullPath(file3), "Test 3: Strip old non-existent rooted path and resolve to current temp/templates/origin.png");

            // Test 4: Path with 'templates/origin.png' relative prefix
            var resolved4 = ResolvePath("templates/origin.png", temp1, "origin.png", "origin*.png");
            Assert(resolved4 != null && File.Exists(resolved4), "Test 4: Resolve relative path with 'templates/origin.png' prefix");

            // Test 5: Fallback wildcard when TemplateImageFile is null/empty
            var resolved5 = ResolvePath(null, temp1, "origin.png", "origin*.png");
            Assert(resolved5 != null && File.Exists(resolved5), "Test 5: Fallback wildcard search for 'origin*.png' when path is null");

            // Test 6: Full .job packaging, extraction and Mat decoding
            var jobWorkingDir = Path.Combine(testBaseDir, "job_package_src");
            var jobTemplatesDir = Path.Combine(jobWorkingDir, "templates");
            Directory.CreateDirectory(jobTemplatesDir);
            var originImgPath = Path.Combine(jobTemplatesDir, "origin.png");
            var p1ImgPath = Path.Combine(jobTemplatesDir, "p1.png");

            using (var m = new Mat(64, 64, MatType.CV_8UC1, new Scalar(220)))
            {
                Cv2.ImWrite(originImgPath, m);
            }
            using (var m = new Mat(32, 32, MatType.CV_8UC1, new Scalar(150)))
            {
                Cv2.ImWrite(p1ImgPath, m);
            }

            var jobConfig = new VisionConfig
            {
                ProductCode = "TEST_PROD",
                Origin = new PointDefinition
                {
                    Name = "Origin",
                    TemplateImageFile = @"G:\NODEJS\Vision2026\VisionInspectionApp.UI\bin\x64\Debug\net8.0-windows\configs\templates\origin.png", // Giả lập đường dẫn tuyệt đối cũ
                    TemplateRoi = new Roi { X = 10, Y = 10, Width = 64, Height = 64 }
                }
            };
            jobConfig.Points.Add(new PointDefinition
            {
                Name = "P1",
                TemplateImageFile = @"C:\OldTemp\Jobs\templates\p1.png", // Giả lập đường dẫn tuyệt đối cũ
                TemplateRoi = new Roi { X = 5, Y = 5, Width = 32, Height = 32 }
            });

            var jobService = new JobService();
            var jobFilePath = Path.Combine(testBaseDir, "SampleJob.job");
            jobService.SaveJob(jobConfig, jobWorkingDir, jobFilePath);

            Assert(File.Exists(jobFilePath), "Test 6.1: JobService.SaveJob created .job zip package");

            // Test 7: Verify config.json INSIDE .job package ONLY has relative filenames ("origin.png", "p1.png")
            var inspectTemp = Path.Combine(testBaseDir, "inspect_json");
            Directory.CreateDirectory(inspectTemp);
            ZipFile.ExtractToDirectory(jobFilePath, inspectTemp);
            var jsonText = File.ReadAllText(Path.Combine(inspectTemp, "config.json"));

            Assert(!jsonText.Contains(@"G:\NODEJS", StringComparison.OrdinalIgnoreCase), "Test 7.1: config.json does NOT contain machine absolute path 'G:\\NODEJS'");
            Assert(!jsonText.Contains(@"C:\OldTemp", StringComparison.OrdinalIgnoreCase), "Test 7.2: config.json does NOT contain machine absolute path 'C:\\OldTemp'");
            Assert(jsonText.Contains("\"templateImageFile\": \"origin.png\"", StringComparison.OrdinalIgnoreCase), "Test 7.3: config.json Origin has clean relative 'origin.png'");
            Assert(jsonText.Contains("\"templateImageFile\": \"p1.png\"", StringComparison.OrdinalIgnoreCase), "Test 7.4: config.json P1 has clean relative 'p1.png'");

            // Test 8: LoadJob on a target machine extracts and binds directly to the tempWorkingDir
            var loadedConfig = jobService.LoadJob(jobFilePath, out var extractedTempDir);

            Assert(loadedConfig != null && loadedConfig.Origin != null, "Test 8.1: JobService.LoadJob loaded config successfully");
            Assert(loadedConfig.Origin.TemplateImageFile != null && loadedConfig.Origin.TemplateImageFile.StartsWith(extractedTempDir, StringComparison.OrdinalIgnoreCase), "Test 8.2: Loaded Origin template points directly inside extracted job temp dir");
            Assert(loadedConfig.Points[0].TemplateImageFile != null && loadedConfig.Points[0].TemplateImageFile.StartsWith(extractedTempDir, StringComparison.OrdinalIgnoreCase), "Test 8.3: Loaded P1 template points directly inside extracted job temp dir");
            Assert(File.Exists(loadedConfig.Origin.TemplateImageFile), "Test 8.4: Extracted Origin template file exists on disk");
            Assert(File.Exists(loadedConfig.Points[0].TemplateImageFile), "Test 8.5: Extracted P1 template file exists on disk");

            using var decodedOriginMat = Cv2.ImRead(loadedConfig.Origin.TemplateImageFile, ImreadModes.Color);
            Assert(decodedOriginMat != null && !decodedOriginMat.Empty() && decodedOriginMat.Width == 64 && decodedOriginMat.Height == 64, "Test 8.6: Decoded Origin Mat valid (64x64 non-empty)");

            using var decodedP1Mat = Cv2.ImRead(loadedConfig.Points[0].TemplateImageFile, ImreadModes.Color);
            Assert(decodedP1Mat != null && !decodedP1Mat.Empty() && decodedP1Mat.Width == 32 && decodedP1Mat.Height == 32, "Test 8.7: Decoded P1 Mat valid (32x32 non-empty)");

            Console.WriteLine("✅ ALL ISOLATED JOB TEMPLATE TESTS PASSED (100%)!\n");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testBaseDir))
                    Directory.Delete(testBaseDir, recursive: true);
            }
            catch { }
        }
    }
}

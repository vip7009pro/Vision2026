using System;
using System.IO;
using VisionInspectionApp.Application.PLC.Services;

namespace TestExtractApp;

public static class TestPlcConfigHelper
{
    public static string CreateIsolatedTestConfigPath()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "Vision2026_Tests");
        Directory.CreateDirectory(testDir);
        return Path.Combine(testDir, $"test_plc_{Guid.NewGuid():N}.json");
    }

    public static PlcManagerService CreateIsolatedPlcManager()
    {
        return new PlcManagerService(CreateIsolatedTestConfigPath());
    }
}

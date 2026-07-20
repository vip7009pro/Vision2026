namespace VisionInspectionApp.Application;
using VisionInspectionApp.Models;

public interface IJobService
{
    VisionConfig LoadJob(string jobFilePath, out string tempWorkingDir);
    void SaveJob(VisionConfig config, string tempWorkingDir, string jobFilePath);
}

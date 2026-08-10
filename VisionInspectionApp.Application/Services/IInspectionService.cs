using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application;

public interface IInspectionService
{
    InspectionResult Inspect(Mat image, VisionConfig config, DB.Services.IDbManagerService? dbManagerOverride = null);
    void ResetTracking(string? productCode = null);
}

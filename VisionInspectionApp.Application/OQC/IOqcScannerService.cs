using System.Data;
using System.Threading.Tasks;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.OQC;

public class CameraCodeScanResult
{
    public bool Success { get; set; }
    public string RawCode { get; set; } = "";
    public string ProcessedCode { get; set; } = "";
    public string CodeType { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}

public interface IOqcScannerService
{
    OqcScannerConfig Config { get; }

    void LoadConfig();
    void SaveConfig(OqcScannerConfig config);
    bool ExportConfigToFile(string filePath, OqcScannerConfig config);
    (bool success, OqcScannerConfig? config, string errorMessage) ImportConfigFromFile(string filePath);

    void SaveScanHistory(System.Collections.Generic.IEnumerable<OqcScanHistoryEntry> history);
    System.Collections.Generic.List<OqcScanHistoryEntry> LoadScanHistory();

    System.Collections.Generic.List<OqcMeasurementDetail> ExtractMeasurementDetails(InspectionResult result, VisionConfig config);

    CameraCodeScanResult DecodeCodeFromImage(OpenCvSharp.Mat image, OqcScannerConfig? config = null);

    (bool success, string processedCode, string rawCode, string errorMessage) ProcessRawCodeString(string rawInput, OqcScannerConfig? config = null);

    Task<(bool Found, string JobFilePath, string ErrorMessage)> LookupJobAsync(
        string scannedCode, IDbManagerService dbManager);

    Task<(bool Found, string ProductName, string ErrorMessage)> LookupProductNameAsync(
        string scannedCode, IDbManagerService dbManager);

    Task<(bool Success, DataTable? Table, string ErrorMessage)> GetProductListAsync(
        string searchText, int pageIndex, IDbManagerService dbManager);

    Task<(bool Success, string Message)> AssignProductJobAsync(
        string productCode, string jobFilePath, IDbManagerService dbManager);

    Task<(bool Success, string Message)> LogInspectionResultAsync(
        string scannedCode, string uuid, string jobFilePath, InspectionResult result, VisionConfig config, IDbManagerService dbManager, System.Collections.Generic.List<OqcMeasurementDetail>? measurementDetails = null);
}

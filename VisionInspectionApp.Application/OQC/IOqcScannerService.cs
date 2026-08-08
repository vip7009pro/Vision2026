using System.Data;
using System.Threading.Tasks;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.OQC;

public interface IOqcScannerService
{
    OqcScannerConfig Config { get; }

    void LoadConfig();
    void SaveConfig(OqcScannerConfig config);

    Task<(bool Found, string JobFilePath, string ErrorMessage)> LookupJobAsync(
        string scannedCode, IDbManagerService dbManager);

    Task<(bool Found, string ProductName, string ErrorMessage)> LookupProductNameAsync(
        string scannedCode, IDbManagerService dbManager);

    Task<(bool Success, DataTable? Table, string ErrorMessage)> GetProductListAsync(
        string searchText, int pageIndex, IDbManagerService dbManager);

    Task<(bool Success, string Message)> AssignProductJobAsync(
        string productCode, string jobFilePath, IDbManagerService dbManager);

    Task<(bool Success, string Message)> LogInspectionResultAsync(
        string scannedCode, string jobFilePath, InspectionResult result, VisionConfig config, IDbManagerService dbManager);
}

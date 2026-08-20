using System;

namespace VisionInspectionApp.Models;

public class OqcScannerConfig
{
    // ─── Job File Tra cứu (Lookup Query) ───
    public string LookupDbId { get; set; } = "";
    public string LookupQuery { get; set; } = "SELECT JobFilePath FROM ProductJobs WHERE ProductCode = '{ScannedCode}'";
    public string JobFilePathColumn { get; set; } = "JobFilePath";
    public string JobRootDirectory { get; set; } = @"C:\VisionJobs";

    // ─── Tên sản phẩm Tra cứu (Product Name Lookup Query) ───
    public bool EnableProductNameLookup { get; set; } = true;
    public string ProductNameDbId { get; set; } = "";
    public string ProductNameQuery { get; set; } = "SELECT G_NAME_KD FROM M100 WHERE G_CODE = '{ScannedCode}'";
    public string ProductNameColumn { get; set; } = "G_NAME_KD";

    // ─── Danh sách sản phẩm (Product List Browser Query) ───
    public string ProductListDbId { get; set; } = "";
    public string ProductListQuery { get; set; } = "SELECT G_CODE, G_NAME_KD FROM M100 WHERE G_CODE LIKE '%{SearchText}%' OR G_NAME_KD LIKE '%{SearchText}%' ORDER BY G_CODE OFFSET {Offset} ROWS FETCH NEXT {PageSize} ROWS ONLY";
    public int ProductListPageSize { get; set; } = 50;

    // ─── Gán sản phẩm ↔ Job File (Assign/Upsert Query) ───
    public string AssignDbId { get; set; } = "";
    public string AssignQuery { get; set; } = "IF EXISTS (SELECT 1 FROM ProductJobs WHERE ProductCode = '{ProductCode}') UPDATE ProductJobs SET JobFilePath = '{JobFilePath}' WHERE ProductCode = '{ProductCode}' ELSE INSERT INTO ProductJobs (ProductCode, JobFilePath) VALUES ('{ProductCode}', '{JobFilePath}')";

    // ─── Ghi log kết quả OQC vào DB (Upload Log Query) ───
    public bool LogResultToDb { get; set; } = false;
    public string LogResultDbId { get; set; } = "";
    public string LogResultQuery { get; set; } = "INSERT INTO OqcLogs (ScannedCode, JobFilePath, Pass, NgReasons, InspectDateTime) VALUES ('{ScannedCode}', '{JobFilePath}', {PassBit}, '{NgReasons}', GETDATE())";

    // ─── Cấu hình quét mã Barcode / QR Code từ Camera ───
    public bool EnableCameraBarcodeScan { get; set; } = true;
    public string TargetCodeType { get; set; } = "ALL";
    public bool EnableLengthFilter { get; set; } = false;
    public int RequiredCodeLength { get; set; } = 0;
    public bool EnableCodeCrop { get; set; } = false;
    public int CropStartIndex { get; set; } = 0;
    public int CropLength { get; set; } = 0;
    public int ScanTimeoutMs { get; set; } = 3000;
    public bool UseExternalScanner { get; set; } = false;
}

public class OqcScanHistoryEntry : System.ComponentModel.INotifyPropertyChanged
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string ScannedCode { get; set; } = "";

    private string _productName = "";
    public string ProductName
    {
        get => _productName;
        set
        {
            if (_productName != value)
            {
                _productName = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ProductName)));
            }
        }
    }

    public string JobFilePath { get; set; } = "";
    public bool Success { get; set; } = false;
    public string Message { get; set; } = "";

    private string _inspectResult = "-";
    public string InspectResult
    {
        get => _inspectResult;
        set
        {
            if (_inspectResult != value)
            {
                _inspectResult = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(InspectResult)));
            }
        }
    }

    private string _inspectDetails = "";
    public string InspectDetails
    {
        get => _inspectDetails;
        set
        {
            if (_inspectDetails != value)
            {
                _inspectDetails = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(InspectDetails)));
            }
        }
    }

    private string _resultBrushHex = "#888888";
    public string ResultBrushHex
    {
        get => _resultBrushHex;
        set
        {
            if (_resultBrushHex != value)
            {
                _resultBrushHex = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ResultBrushHex)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

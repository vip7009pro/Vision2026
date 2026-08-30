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
    public string ProductListCodeColumn { get; set; } = "G_CODE";
    public string ProductListNameColumn { get; set; } = "G_NAME_KD";
    public int ProductListPageSize { get; set; } = 50;

    // ─── Gán sản phẩm ↔ Job File (Assign/Upsert Query) ───
    public string AssignDbId { get; set; } = "";
    public string AssignQuery { get; set; } = "IF EXISTS (SELECT 1 FROM ProductJobs WHERE ProductCode = '{ProductCode}') UPDATE ProductJobs SET JobFilePath = '{JobFilePath}' WHERE ProductCode = '{ProductCode}' ELSE INSERT INTO ProductJobs (ProductCode, JobFilePath) VALUES ('{ProductCode}', '{JobFilePath}')";

    // ─── Ghi log kết quả OQC vào DB (Upload Log Query) ───
    public bool LogResultToDb { get; set; } = false;
    public string LogResultDbId { get; set; } = "";
    public string LogResultQuery { get; set; } = "INSERT INTO OqcLogs (CTR_CD, ScannedCode, UUID, JobFilePath, Pass, NgReasons, InspectDateTime) VALUES ('002', '{ScannedCode}', '{UUID}', '{JobFilePath}', {PassBit}, N'{NgReasons}', GETDATE())";

    // ─── Ghi log chi tiết từng phép đo OQC vào DB (Upload Detail Measurements Query) ───
    public bool LogDetailResultToDb { get; set; } = false;
    public string LogDetailResultDbId { get; set; } = "";
    public string LogDetailResultQuery { get; set; } = "INSERT INTO OqcInspectResult (CTR_CD, ScannedCode, UUID, ToolName, Spec, [Tol +], [Tol -], [Min], [Max], Result, Judge, InspectDateTime) VALUES ('002', '{ScannedCode}', '{UUID}', '{ToolName}', {Spec}, {TolPlus}, {TolMinus}, {Min}, {Max}, {Result}, '{Judge}', GETDATE())";

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

public class OqcMeasurementDetail
{
    public int Index { get; set; } = 1;
    public string ToolName { get; set; } = "";
    public string ToolType { get; set; } = "";
    public bool HasNumericSpec { get; set; } = true;
    public string CustomSpecText { get; set; } = "";
    public string CustomResultText { get; set; } = "";
    public double Spec { get; set; } = 0;
    public double TolPlus { get; set; } = 0;
    public double TolMinus { get; set; } = 0;
    public double Min { get; set; } = 0;
    public double Max { get; set; } = 0;
    public double Result { get; set; } = 0;
    public string Unit { get; set; } = "mm";
    public bool Pass { get; set; } = true;

    public string Judge => Pass ? "PASS" : "NG";
    public string JudgeBrushHex => Pass ? "#2E7D32" : "#D32F2F";

    public string FormattedSpec
    {
        get
        {
            if (!string.IsNullOrEmpty(CustomSpecText)) return CustomSpecText;
            if (!HasNumericSpec) return "";
            return string.IsNullOrWhiteSpace(Unit) ? $"{Spec:F3}" : $"{Spec:F3} {Unit}".Trim();
        }
    }

    public string FormattedTol
    {
        get
        {
            if (!HasNumericSpec) return "";
            return $"+{TolPlus:F3} / -{TolMinus:F3}";
        }
    }

    public string FormattedRange
    {
        get
        {
            if (!HasNumericSpec) return "";
            return $"[{Min:F3} ~ {Max:F3}]";
        }
    }

    public string FormattedResult
    {
        get
        {
            if (!string.IsNullOrEmpty(CustomResultText)) return CustomResultText;
            if (double.IsNaN(Result)) return "N/A";
            return string.IsNullOrWhiteSpace(Unit) ? $"{Result:F3}" : $"{Result:F3} {Unit}".Trim();
        }
    }
}

public class OqcScanHistoryEntry : System.ComponentModel.INotifyPropertyChanged
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string ScannedCode { get; set; } = "";
    public string Uuid { get; set; } = "";

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
    public string OutputImagePath { get; set; } = "";
    public bool Success { get; set; } = false;
    public string Message { get; set; } = "";

    private string _dbLogStatus = "";
    public string DbLogStatus
    {
        get => _dbLogStatus;
        set
        {
            if (_dbLogStatus != value)
            {
                _dbLogStatus = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DbLogStatus)));
            }
        }
    }

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

    public System.Collections.Generic.List<OqcMeasurementDetail> MeasurementDetails { get; set; } = new();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

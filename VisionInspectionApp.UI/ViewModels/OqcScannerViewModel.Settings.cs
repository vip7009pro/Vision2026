using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels;

public partial class OqcScannerViewModel
{
    // ─── Settings Properties ───
    [ObservableProperty]
    private string _lookupDbId = "";

    [ObservableProperty]
    private string _lookupQuery = "";

    [ObservableProperty]
    private string _jobFilePathColumn = "";

    [ObservableProperty]
    private string _jobRootDirectory = "";

    [ObservableProperty]
    private bool _enableProductNameLookup = true;

    [ObservableProperty]
    private string _productNameDbId = "";

    [ObservableProperty]
    private string _productNameQuery = "";

    [ObservableProperty]
    private string _productNameColumn = "";

    [ObservableProperty]
    private string _productListDbId = "";

    [ObservableProperty]
    private string _productListQuery = "";

    [ObservableProperty]
    private int _productListPageSize = 50;

    [ObservableProperty]
    private string _assignDbId = "";

    [ObservableProperty]
    private string _assignQuery = "";

    [ObservableProperty]
    private bool _logResultToDb = false;

    [ObservableProperty]
    private string _logResultDbId = "";

    [ObservableProperty]
    private string _logResultQuery = "";

    // ─── Camera Barcode Reader Settings Properties ───
    [ObservableProperty]
    private bool _enableCameraBarcodeScan = true;

    [ObservableProperty]
    private string _targetCodeType = "ALL";

    [ObservableProperty]
    private bool _enableLengthFilter = false;

    [ObservableProperty]
    private int _requiredCodeLength = 0;

    [ObservableProperty]
    private bool _enableCodeCrop = false;

    [ObservableProperty]
    private int _cropStartIndex = 0;

    [ObservableProperty]
    private int _cropLength = 0;

    public IReadOnlyList<string> AvailableCodeTypes { get; } = new List<string>
    {
        "ALL",
        "QR_CODE",
        "CODE_128",
        "CODE_39",
        "DATA_MATRIX",
        "EAN_13",
        "EAN_8",
        "PDF_417",
        "AZTEC",
        "BARCODE_1D"
    };

    public IReadOnlyList<DbModel> AvailableDatabases => _dbManager.Databases;

    public IRelayCommand SaveConfigCommand { get; private set; } = null!;

    // ─── Product Assign Dialog Properties ───
    [ObservableProperty]
    private string _assignJobFilePath = "";

    [ObservableProperty]
    private string _productSearchText = "";

    [ObservableProperty]
    private DataView? _productListTable;

    [ObservableProperty]
    private int _currentPageIndex = 0;

    [ObservableProperty]
    private string _pageIndicatorText = "Trang 1";

    [ObservableProperty]
    private DataRowView? _selectedProductRow;

    [ObservableProperty]
    private string _assignStatusMessage = "";

    [ObservableProperty]
    private Brush _assignStatusBrush = Brushes.Gray;

    public IAsyncRelayCommand SearchProductsCommand { get; private set; } = null!;
    public IAsyncRelayCommand NextPageCommand { get; private set; } = null!;
    public IAsyncRelayCommand PrevPageCommand { get; private set; } = null!;
    public IAsyncRelayCommand AssignProductCommand { get; private set; } = null!;

    private void InitSettingsProperties()
    {
        SaveConfigCommand = new RelayCommand(SaveSettingsToConfig);

        SearchProductsCommand = new AsyncRelayCommand(ExecuteSearchProductsAsync);
        NextPageCommand = new AsyncRelayCommand(ExecuteNextPageAsync);
        PrevPageCommand = new AsyncRelayCommand(ExecutePrevPageAsync);
        AssignProductCommand = new AsyncRelayCommand(ExecuteAssignProductAsync);

        LoadSettingsFromConfig();
    }

    private void LoadSettingsFromConfig()
    {
        var cfg = _oqcService.Config;
        LookupDbId = cfg.LookupDbId;
        LookupQuery = cfg.LookupQuery;
        JobFilePathColumn = cfg.JobFilePathColumn;
        JobRootDirectory = cfg.JobRootDirectory;

        EnableProductNameLookup = cfg.EnableProductNameLookup;
        ProductNameDbId = cfg.ProductNameDbId;
        ProductNameQuery = cfg.ProductNameQuery;
        ProductNameColumn = cfg.ProductNameColumn;

        ProductListDbId = cfg.ProductListDbId;
        ProductListQuery = cfg.ProductListQuery;
        ProductListPageSize = cfg.ProductListPageSize;

        AssignDbId = cfg.AssignDbId;
        AssignQuery = cfg.AssignQuery;

        LogResultToDb = cfg.LogResultToDb;
        LogResultDbId = cfg.LogResultDbId;
        LogResultQuery = cfg.LogResultQuery;

        EnableCameraBarcodeScan = cfg.EnableCameraBarcodeScan;
        TargetCodeType = cfg.TargetCodeType ?? "ALL";
        EnableLengthFilter = cfg.EnableLengthFilter;
        RequiredCodeLength = cfg.RequiredCodeLength;
        EnableCodeCrop = cfg.EnableCodeCrop;
        CropStartIndex = cfg.CropStartIndex;
        CropLength = cfg.CropLength;
    }

    private void SaveSettingsToConfig()
    {
        var cfg = new OqcScannerConfig
        {
            LookupDbId = LookupDbId,
            LookupQuery = LookupQuery,
            JobFilePathColumn = JobFilePathColumn,
            JobRootDirectory = JobRootDirectory,

            EnableProductNameLookup = EnableProductNameLookup,
            ProductNameDbId = ProductNameDbId,
            ProductNameQuery = ProductNameQuery,
            ProductNameColumn = ProductNameColumn,

            ProductListDbId = ProductListDbId,
            ProductListQuery = ProductListQuery,
            ProductListPageSize = ProductListPageSize > 0 ? ProductListPageSize : 50,

            AssignDbId = AssignDbId,
            AssignQuery = AssignQuery,

            LogResultToDb = LogResultToDb,
            LogResultDbId = LogResultDbId,
            LogResultQuery = LogResultQuery,

            EnableCameraBarcodeScan = EnableCameraBarcodeScan,
            TargetCodeType = TargetCodeType,
            EnableLengthFilter = EnableLengthFilter,
            RequiredCodeLength = RequiredCodeLength,
            EnableCodeCrop = EnableCodeCrop,
            CropStartIndex = CropStartIndex,
            CropLength = CropLength
        };

        _oqcService.SaveConfig(cfg);
        StatusMessage = "⚙ Đã lưu cấu hình OQC Scanner!";
        StatusBrush = Brushes.Green;
    }

    private async Task ExecuteSearchProductsAsync()
    {
        CurrentPageIndex = 0;
        await FetchProductsPageAsync();
    }

    private async Task ExecuteNextPageAsync()
    {
        CurrentPageIndex++;
        await FetchProductsPageAsync();
    }

    private async Task ExecutePrevPageAsync()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            await FetchProductsPageAsync();
        }
    }

    private async Task FetchProductsPageAsync()
    {
        AssignStatusMessage = "🔍 Đang nạp danh sách sản phẩm...";
        AssignStatusBrush = Brushes.DodgerBlue;

        var (success, table, error) = await _oqcService.GetProductListAsync(ProductSearchText, CurrentPageIndex, _dbManager);
        if (success && table != null)
        {
            ProductListTable = table.DefaultView;
            PageIndicatorText = $"Trang {CurrentPageIndex + 1} ({table.Rows.Count} sản phẩm)";
            AssignStatusMessage = $"✅ Đã tải trang {CurrentPageIndex + 1}.";
            AssignStatusBrush = Brushes.Green;
        }
        else
        {
            ProductListTable = null;
            PageIndicatorText = $"Trang {CurrentPageIndex + 1}";
            AssignStatusMessage = $"❌ Lỗi nạp danh sách: {error}";
            AssignStatusBrush = Brushes.Red;
        }
    }

    private async Task ExecuteAssignProductAsync()
    {
        if (SelectedProductRow == null)
        {
            AssignStatusMessage = "⚠️ Vui lòng chọn một sản phẩm trong bảng!";
            AssignStatusBrush = Brushes.Orange;
            return;
        }

        if (string.IsNullOrWhiteSpace(AssignJobFilePath))
        {
            AssignStatusMessage = "⚠️ Vui lòng chọn hoặc nhập tệp Job!";
            AssignStatusBrush = Brushes.Orange;
            return;
        }

        // Find primary code column (G_CODE, ProductCode, or Column 0)
        string code = "";
        if (SelectedProductRow.Row.Table.Columns.Contains("G_CODE"))
        {
            code = SelectedProductRow["G_CODE"]?.ToString() ?? "";
        }
        else if (SelectedProductRow.Row.Table.Columns.Contains("ProductCode"))
        {
            code = SelectedProductRow["ProductCode"]?.ToString() ?? "";
        }
        else if (SelectedProductRow.Row.Table.Columns.Count > 0)
        {
            code = SelectedProductRow[0]?.ToString() ?? "";
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            AssignStatusMessage = "⚠️ Sản phẩm được chọn không có mã (cột rỗng)!";
            AssignStatusBrush = Brushes.Red;
            return;
        }

        AssignStatusMessage = $"⏳ Đang gán '{code}' → '{System.IO.Path.GetFileName(AssignJobFilePath)}'...";
        AssignStatusBrush = Brushes.DodgerBlue;

        var (success, msg) = await _oqcService.AssignProductJobAsync(code, AssignJobFilePath, _dbManager);
        if (success)
        {
            AssignStatusMessage = msg;
            AssignStatusBrush = Brushes.Green;
        }
        else
        {
            AssignStatusMessage = $"❌ {msg}";
            AssignStatusBrush = Brushes.Red;
        }
    }
}

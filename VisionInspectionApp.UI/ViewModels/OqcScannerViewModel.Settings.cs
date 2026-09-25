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
    private string _productListCodeColumn = "G_CODE";

    [ObservableProperty]
    private string _productListNameColumn = "G_NAME_KD";

    [ObservableProperty]
    private int _productListPageSize = 50;

    [ObservableProperty]
    private string _assignDbId = "";

    [ObservableProperty]
    private string _assignQuery = "";

    // ─── Cập nhật riêng Ảnh Mẫu Teach Image ───
    [ObservableProperty]
    private string _updateTeachImageDbId = "";

    [ObservableProperty]
    private string _updateTeachImageQuery = "IF EXISTS (SELECT 1 FROM ProductJobs WHERE ProductCode = '{ProductCode}') UPDATE ProductJobs SET TeachImagePath = '{TeachImagePath}', UpdatedAt = GETDATE() WHERE ProductCode = '{ProductCode}' ELSE INSERT INTO ProductJobs (ProductCode, TeachImagePath, UpdatedAt) VALUES ('{ProductCode}', '{TeachImagePath}', GETDATE())";

    [ObservableProperty]
    private bool _logResultToDb = false;

    [ObservableProperty]
    private string _logResultDbId = "";

    [ObservableProperty]
    private string _logResultQuery = "";

    // ─── Ghi log chi tiết từng phép đo lên DB ───
    [ObservableProperty]
    private bool _logDetailResultToDb = false;

    [ObservableProperty]
    private string _logDetailResultDbId = "";

    [ObservableProperty]
    private string _logDetailResultQuery = "";

    // ─── Cấu hình Máy Chủ Web (Server API / Upload Endpoint) ───
    [ObservableProperty]
    private string _serverApiUrl = "http://localhost/vision_upload.php";

    [ObservableProperty]
    private string _teachImageColumn = "TeachImagePath";

    [ObservableProperty]
    private string _pingServerStatusText = "Chưa kiểm tra kết nối";

    [ObservableProperty]
    private Brush _pingServerStatusBrush = Brushes.Gray;

    // ─── Quản lý Job trên CSDL & Server (Job Manager Query) ───
    [ObservableProperty]
    private string _jobManagerDbId = "";

    [ObservableProperty]
    private string _jobManagerQuery = "SELECT ProductCode, ProductName, JobFilePath, TeachImagePath, UpdatedAt FROM ProductJobs WHERE ProductCode LIKE '%{SearchText}%' OR ProductName LIKE '%{SearchText}%' ORDER BY ProductCode OFFSET {Offset} ROWS FETCH NEXT {PageSize} ROWS ONLY";

    [ObservableProperty]
    private string _jobManagerProductCodeColumn = "ProductCode";

    [ObservableProperty]
    private string _jobManagerProductNameColumn = "ProductName";

    [ObservableProperty]
    private string _jobManagerJobFileColumn = "JobFilePath";

    [ObservableProperty]
    private string _jobManagerTeachImageColumn = "TeachImagePath";

    [ObservableProperty]
    private string _jobManagerUpdatedColumn = "UpdatedAt";

    [ObservableProperty]
    private int _jobManagerPageSize = 50;

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

    [ObservableProperty]
    private int _scanTimeoutMs = 3000;

    [ObservableProperty]
    private bool _useExternalScanner = false;

    [ObservableProperty]
    private bool _onlyOriginMode = true;

    private bool _isSuppressingConfigSave = false;

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
    public IRelayCommand ExportConfigCommand { get; private set; } = null!;
    public IRelayCommand ImportConfigCommand { get; private set; } = null!;
    public IAsyncRelayCommand PingServerCommand { get; private set; } = null!;

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
        ExportConfigCommand = new RelayCommand(ExecuteExportConfig);
        ImportConfigCommand = new RelayCommand(ExecuteImportConfig);
        PingServerCommand = new AsyncRelayCommand(ExecutePingServerAsync);

        SearchProductsCommand = new AsyncRelayCommand(ExecuteSearchProductsAsync);
        NextPageCommand = new AsyncRelayCommand(ExecuteNextPageAsync);
        PrevPageCommand = new AsyncRelayCommand(ExecutePrevPageAsync);
        AssignProductCommand = new AsyncRelayCommand(ExecuteAssignProductAsync);

        if (_dbManager != null)
        {
            _dbManager.DatabasesChanged += (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(AvailableDatabases));
                    LoadSettingsFromConfig();
                });
            };
        }

        LoadSettingsFromConfig();
    }

    public string ResolveDatabaseId(string? dbId, string? dbName)
    {
        return ResolveDatabaseId(dbId, dbName, _dbManager?.Databases);
    }

    public static string ResolveDatabaseId(string? dbId, string? dbName, IEnumerable<DbModel>? databases)
    {
        if (databases == null)
            return dbId ?? "";

        var dbList = databases as IList<DbModel> ?? databases.ToList();
        if (dbList.Count == 0)
            return dbId ?? "";

        // 1. Khớp chính xác theo ID (GUID)
        if (!string.IsNullOrWhiteSpace(dbId))
        {
            var matchById = dbList.FirstOrDefault(d => string.Equals(d.Id, dbId, StringComparison.OrdinalIgnoreCase));
            if (matchById != null)
                return matchById.Id;

            // 2. Khớp nếu dbId lưu tên database (VD: "MainDB", "CMS_DB", "DB_OQC")
            var matchByNameFromId = dbList.FirstOrDefault(d => string.Equals(d.Name, dbId, StringComparison.OrdinalIgnoreCase));
            if (matchByNameFromId != null)
                return matchByNameFromId.Id;

            // 3. Khớp nếu dbId lưu tên catalog DB (DatabaseName)
            var matchByDbCatalogFromId = dbList.FirstOrDefault(d => string.Equals(d.DatabaseName, dbId, StringComparison.OrdinalIgnoreCase));
            if (matchByDbCatalogFromId != null)
                return matchByDbCatalogFromId.Id;
        }

        // 4. Khớp theo tên dbName lưu kèm khi xuất cấu hình
        if (!string.IsNullOrWhiteSpace(dbName))
        {
            var matchByName = dbList.FirstOrDefault(d => string.Equals(d.Name, dbName, StringComparison.OrdinalIgnoreCase));
            if (matchByName != null)
                return matchByName.Id;

            var matchByDbCatalog = dbList.FirstOrDefault(d => string.Equals(d.DatabaseName, dbName, StringComparison.OrdinalIgnoreCase));
            if (matchByDbCatalog != null)
                return matchByDbCatalog.Id;
        }

        // 5. Fallback thông minh: Nếu trường này có giá trị nhưng không khớp GUID máy cũ, fallback sang DB đang bật hoặc DB đầu tiên
        if (!string.IsNullOrWhiteSpace(dbId) || !string.IsNullOrWhiteSpace(dbName))
        {
            var activeDb = dbList.FirstOrDefault(d => d.IsEnabled) ?? dbList.FirstOrDefault();
            if (activeDb != null)
                return activeDb.Id;
        }

        return dbId ?? "";
    }

    public void LoadSettingsFromConfig()
    {
        _isSuppressingConfigSave = true;
        try
        {
            OnPropertyChanged(nameof(AvailableDatabases));

            var cfg = _oqcService.Config;
            LookupDbId = ResolveDatabaseId(cfg.LookupDbId, cfg.LookupDbName);
            LookupQuery = cfg.LookupQuery;
            JobFilePathColumn = cfg.JobFilePathColumn;
            JobRootDirectory = cfg.JobRootDirectory;

            EnableProductNameLookup = cfg.EnableProductNameLookup;
            ProductNameDbId = ResolveDatabaseId(cfg.ProductNameDbId, cfg.ProductNameDbName);
            ProductNameQuery = cfg.ProductNameQuery;
            ProductNameColumn = cfg.ProductNameColumn;

            ProductListDbId = ResolveDatabaseId(cfg.ProductListDbId, cfg.ProductListDbName);
            ProductListQuery = cfg.ProductListQuery;
            ProductListCodeColumn = !string.IsNullOrWhiteSpace(cfg.ProductListCodeColumn) ? cfg.ProductListCodeColumn : "G_CODE";
            ProductListNameColumn = !string.IsNullOrWhiteSpace(cfg.ProductListNameColumn) ? cfg.ProductListNameColumn : "G_NAME_KD";
            ProductListPageSize = cfg.ProductListPageSize;

            AssignDbId = ResolveDatabaseId(cfg.AssignDbId, cfg.AssignDbName);
            AssignQuery = cfg.AssignQuery;

            UpdateTeachImageDbId = ResolveDatabaseId(cfg.UpdateTeachImageDbId, cfg.UpdateTeachImageDbName);
            UpdateTeachImageQuery = !string.IsNullOrWhiteSpace(cfg.UpdateTeachImageQuery) ? cfg.UpdateTeachImageQuery : "IF EXISTS (SELECT 1 FROM ProductJobs WHERE ProductCode = '{ProductCode}') UPDATE ProductJobs SET TeachImagePath = '{TeachImagePath}', UpdatedAt = GETDATE() WHERE ProductCode = '{ProductCode}' ELSE INSERT INTO ProductJobs (ProductCode, TeachImagePath, UpdatedAt) VALUES ('{ProductCode}', '{TeachImagePath}', GETDATE())";

            ServerApiUrl = !string.IsNullOrWhiteSpace(cfg.ServerApiUrl) ? cfg.ServerApiUrl : "http://localhost/vision_upload.php";
            TeachImageColumn = !string.IsNullOrWhiteSpace(cfg.TeachImageColumn) ? cfg.TeachImageColumn : "TeachImagePath";

            JobManagerDbId = ResolveDatabaseId(cfg.JobManagerDbId, cfg.JobManagerDbName);
            JobManagerQuery = !string.IsNullOrWhiteSpace(cfg.JobManagerQuery) ? cfg.JobManagerQuery : "SELECT ProductCode, ProductName, JobFilePath, TeachImagePath, UpdatedAt FROM ProductJobs WHERE ProductCode LIKE '%{SearchText}%' OR ProductName LIKE '%{SearchText}%' ORDER BY ProductCode OFFSET {Offset} ROWS FETCH NEXT {PageSize} ROWS ONLY";
            JobManagerProductCodeColumn = !string.IsNullOrWhiteSpace(cfg.JobManagerProductCodeColumn) ? cfg.JobManagerProductCodeColumn : "ProductCode";
            JobManagerProductNameColumn = !string.IsNullOrWhiteSpace(cfg.JobManagerProductNameColumn) ? cfg.JobManagerProductNameColumn : "ProductName";
            JobManagerJobFileColumn = !string.IsNullOrWhiteSpace(cfg.JobManagerJobFileColumn) ? cfg.JobManagerJobFileColumn : "JobFilePath";
            JobManagerTeachImageColumn = !string.IsNullOrWhiteSpace(cfg.JobManagerTeachImageColumn) ? cfg.JobManagerTeachImageColumn : "TeachImagePath";
            JobManagerUpdatedColumn = !string.IsNullOrWhiteSpace(cfg.JobManagerUpdatedColumn) ? cfg.JobManagerUpdatedColumn : "UpdatedAt";
            JobManagerPageSize = cfg.JobManagerPageSize > 0 ? cfg.JobManagerPageSize : 50;

            LogResultToDb = cfg.LogResultToDb;
            LogResultDbId = ResolveDatabaseId(cfg.LogResultDbId, cfg.LogResultDbName);
            LogResultQuery = cfg.LogResultQuery;

            LogDetailResultToDb = cfg.LogDetailResultToDb;
            LogDetailResultDbId = ResolveDatabaseId(cfg.LogDetailResultDbId, cfg.LogDetailResultDbName);
            LogDetailResultQuery = cfg.LogDetailResultQuery;

            EnableCameraBarcodeScan = cfg.EnableCameraBarcodeScan;
            TargetCodeType = cfg.TargetCodeType ?? "ALL";
            EnableLengthFilter = cfg.EnableLengthFilter;
            RequiredCodeLength = cfg.RequiredCodeLength;
            EnableCodeCrop = cfg.EnableCodeCrop;
            CropStartIndex = cfg.CropStartIndex;
            CropLength = cfg.CropLength;
            ScanTimeoutMs = cfg.ScanTimeoutMs > 0 ? cfg.ScanTimeoutMs : 3000;
            UseExternalScanner = cfg.UseExternalScanner;
            AutoRunJob = cfg.AutoRunJob;
            OnlyOriginMode = cfg.OnlyOriginMode;
        }
        finally
        {
            _isSuppressingConfigSave = false;
        }
    }

    public async Task ExecutePingServerAsync()
    {
        PingServerStatusText = "⏳ Đang kết nối tới Server...";
        PingServerStatusBrush = Brushes.DodgerBlue;

        var (success, msg) = await _remoteServerService.PingServerAsync(ServerApiUrl);
        if (success)
        {
            PingServerStatusText = msg;
            PingServerStatusBrush = Brushes.Green;
        }
        else
        {
            PingServerStatusText = $"❌ {msg}";
            PingServerStatusBrush = Brushes.Red;
        }
    }

    private void SaveSettingsToConfig()
    {
        var cfg = new OqcScannerConfig
        {
            LookupDbId = LookupDbId,
            LookupDbName = _dbManager.GetDatabase(LookupDbId)?.Name ?? "",
            LookupQuery = LookupQuery,
            JobFilePathColumn = JobFilePathColumn,
            JobRootDirectory = JobRootDirectory,

            EnableProductNameLookup = EnableProductNameLookup,
            ProductNameDbId = ProductNameDbId,
            ProductNameDbName = _dbManager.GetDatabase(ProductNameDbId)?.Name ?? "",
            ProductNameQuery = ProductNameQuery,
            ProductNameColumn = ProductNameColumn,

            ProductListDbId = ProductListDbId,
            ProductListDbName = _dbManager.GetDatabase(ProductListDbId)?.Name ?? "",
            ProductListQuery = ProductListQuery,
            ProductListCodeColumn = ProductListCodeColumn,
            ProductListNameColumn = ProductListNameColumn,
            ProductListPageSize = ProductListPageSize > 0 ? ProductListPageSize : 50,

            AssignDbId = AssignDbId,
            AssignDbName = _dbManager.GetDatabase(AssignDbId)?.Name ?? "",
            AssignQuery = AssignQuery,

            UpdateTeachImageDbId = UpdateTeachImageDbId,
            UpdateTeachImageDbName = _dbManager.GetDatabase(UpdateTeachImageDbId)?.Name ?? "",
            UpdateTeachImageQuery = UpdateTeachImageQuery,

            ServerApiUrl = ServerApiUrl,
            TeachImageColumn = TeachImageColumn,

            JobManagerDbId = JobManagerDbId,
            JobManagerDbName = _dbManager.GetDatabase(JobManagerDbId)?.Name ?? "",
            JobManagerQuery = JobManagerQuery,
            JobManagerProductCodeColumn = JobManagerProductCodeColumn,
            JobManagerProductNameColumn = JobManagerProductNameColumn,
            JobManagerJobFileColumn = JobManagerJobFileColumn,
            JobManagerTeachImageColumn = JobManagerTeachImageColumn,
            JobManagerUpdatedColumn = JobManagerUpdatedColumn,
            JobManagerPageSize = JobManagerPageSize > 0 ? JobManagerPageSize : 50,

            LogResultToDb = LogResultToDb,
            LogResultDbId = LogResultDbId,
            LogResultDbName = _dbManager.GetDatabase(LogResultDbId)?.Name ?? "",
            LogResultQuery = LogResultQuery,

            LogDetailResultToDb = LogDetailResultToDb,
            LogDetailResultDbId = LogDetailResultDbId,
            LogDetailResultDbName = _dbManager.GetDatabase(LogDetailResultDbId)?.Name ?? "",
            LogDetailResultQuery = LogDetailResultQuery,

            EnableCameraBarcodeScan = EnableCameraBarcodeScan,
            TargetCodeType = TargetCodeType,
            EnableLengthFilter = EnableLengthFilter,
            RequiredCodeLength = RequiredCodeLength,
            EnableCodeCrop = EnableCodeCrop,
            CropStartIndex = CropStartIndex,
            CropLength = CropLength,
            ScanTimeoutMs = ScanTimeoutMs > 0 ? ScanTimeoutMs : 3000,
            UseExternalScanner = UseExternalScanner,
            AutoRunJob = AutoRunJob,
            OnlyOriginMode = OnlyOriginMode
        };

        _oqcService.SaveConfig(cfg);
        StatusMessage = "⚙ Đã lưu cấu hình OQC Scanner!";
        StatusBrush = Brushes.Green;
    }

    private void ExecuteExportConfig()
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Xuất Cấu Hình OQC Scanner",
                Filter = "Tệp Cấu Hình JSON (*.json)|*.json",
                FileName = $"OqcScanner_Config_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (sfd.ShowDialog() == true)
            {
                var cfg = new OqcScannerConfig
                {
                    LookupDbId = LookupDbId,
                    LookupDbName = _dbManager.GetDatabase(LookupDbId)?.Name ?? "",
                    LookupQuery = LookupQuery,
                    JobFilePathColumn = JobFilePathColumn,
                    JobRootDirectory = JobRootDirectory,

                    EnableProductNameLookup = EnableProductNameLookup,
                    ProductNameDbId = ProductNameDbId,
                    ProductNameDbName = _dbManager.GetDatabase(ProductNameDbId)?.Name ?? "",
                    ProductNameQuery = ProductNameQuery,
                    ProductNameColumn = ProductNameColumn,

                    ProductListDbId = ProductListDbId,
                    ProductListDbName = _dbManager.GetDatabase(ProductListDbId)?.Name ?? "",
                    ProductListQuery = ProductListQuery,
                    ProductListCodeColumn = ProductListCodeColumn,
                    ProductListNameColumn = ProductListNameColumn,
                    ProductListPageSize = ProductListPageSize > 0 ? ProductListPageSize : 50,

                    AssignDbId = AssignDbId,
                    AssignDbName = _dbManager.GetDatabase(AssignDbId)?.Name ?? "",
                    AssignQuery = AssignQuery,

                    UpdateTeachImageDbId = UpdateTeachImageDbId,
                    UpdateTeachImageDbName = _dbManager.GetDatabase(UpdateTeachImageDbId)?.Name ?? "",
                    UpdateTeachImageQuery = UpdateTeachImageQuery,

                    ServerApiUrl = ServerApiUrl,
                    TeachImageColumn = TeachImageColumn,

                    JobManagerDbId = JobManagerDbId,
                    JobManagerDbName = _dbManager.GetDatabase(JobManagerDbId)?.Name ?? "",
                    JobManagerQuery = JobManagerQuery,
                    JobManagerProductCodeColumn = JobManagerProductCodeColumn,
                    JobManagerProductNameColumn = JobManagerProductNameColumn,
                    JobManagerJobFileColumn = JobManagerJobFileColumn,
                    JobManagerTeachImageColumn = JobManagerTeachImageColumn,
                    JobManagerUpdatedColumn = JobManagerUpdatedColumn,
                    JobManagerPageSize = JobManagerPageSize > 0 ? JobManagerPageSize : 50,

                    LogResultToDb = LogResultToDb,
                    LogResultDbId = LogResultDbId,
                    LogResultDbName = _dbManager.GetDatabase(LogResultDbId)?.Name ?? "",
                    LogResultQuery = LogResultQuery,

                    LogDetailResultToDb = LogDetailResultToDb,
                    LogDetailResultDbId = LogDetailResultDbId,
                    LogDetailResultDbName = _dbManager.GetDatabase(LogDetailResultDbId)?.Name ?? "",
                    LogDetailResultQuery = LogDetailResultQuery,

                    EnableCameraBarcodeScan = EnableCameraBarcodeScan,
                    TargetCodeType = TargetCodeType,
                    EnableLengthFilter = EnableLengthFilter,
                    RequiredCodeLength = RequiredCodeLength,
                    EnableCodeCrop = EnableCodeCrop,
                    CropStartIndex = CropStartIndex,
                    CropLength = CropLength,
                    ScanTimeoutMs = ScanTimeoutMs > 0 ? ScanTimeoutMs : 3000,
                    UseExternalScanner = UseExternalScanner,
                    AutoRunJob = AutoRunJob,
                    OnlyOriginMode = OnlyOriginMode
                };

                if (_oqcService.ExportConfigToFile(sfd.FileName, cfg))
                {
                    System.Windows.MessageBox.Show($"✅ Xuất cấu hình thành công!\nĐường dẫn: {sfd.FileName}", "Thành Công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("❌ Lỗi khi ghi tệp cấu hình ra đĩa.", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Lỗi xuất cấu hình: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ExecuteImportConfig()
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Nạp Cấu Hình OQC Scanner",
                Filter = "Tệp Cấu Hình JSON (*.json)|*.json"
            };

            if (ofd.ShowDialog() == true)
            {
                var (success, loadedConfig, error) = _oqcService.ImportConfigFromFile(ofd.FileName);
                if (success && loadedConfig != null)
                {
                    OnPropertyChanged(nameof(AvailableDatabases));
                    LoadSettingsFromConfig();
                    // Lưu lại cấu hình đã ánh xạ ID tự động để lần mở sau không cần nạp lại
                    SaveSettingsToConfig();
                    System.Windows.MessageBox.Show("✅ Nạp cấu hình thành công!\nCác mục cơ sở dữ liệu đã được tự động khớp và chọn sẵn.", "Thành Công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show($"❌ Không thể nạp cấu hình: {error}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Lỗi nạp cấu hình: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
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

        // 1. Tìm mã sản phẩm (ProductCode) dùng để gán CSDL: Ưu tiên theo cấu hình ProductListCodeColumn
        string productCode = "";
        var configuredCodeCol = !string.IsNullOrWhiteSpace(ProductListCodeColumn) ? ProductListCodeColumn.Trim() : "";

        if (!string.IsNullOrEmpty(configuredCodeCol) && SelectedProductRow.Row.Table.Columns.Contains(configuredCodeCol))
        {
            productCode = SelectedProductRow[configuredCodeCol]?.ToString() ?? "";
        }
        else if (SelectedProductRow.Row.Table.Columns.Contains("G_CODE"))
        {
            productCode = SelectedProductRow["G_CODE"]?.ToString() ?? "";
        }
        else if (SelectedProductRow.Row.Table.Columns.Contains("ProductCode"))
        {
            productCode = SelectedProductRow["ProductCode"]?.ToString() ?? "";
        }
        else if (SelectedProductRow.Row.Table.Columns.Count > 0)
        {
            productCode = SelectedProductRow[0]?.ToString() ?? "";
        }

        if (string.IsNullOrWhiteSpace(productCode))
        {
            AssignStatusMessage = "⚠️ Sản phẩm được chọn không có mã sản phẩm (cột rỗng hoặc sai tên cột cấu hình mã SP)!";
            AssignStatusBrush = Brushes.Red;
            return;
        }

        // 2. Tìm tên sản phẩm (ProductName) dùng để auto-fill vào ô Mã SP trong Tool Editor: Ưu tiên theo cấu hình ProductListNameColumn
        string productName = "";
        var configuredNameCol = !string.IsNullOrWhiteSpace(ProductListNameColumn) ? ProductListNameColumn.Trim() : "";

        if (!string.IsNullOrEmpty(configuredNameCol) && SelectedProductRow.Row.Table.Columns.Contains(configuredNameCol))
        {
            productName = SelectedProductRow[configuredNameCol]?.ToString() ?? "";
        }
        else if (SelectedProductRow.Row.Table.Columns.Contains("G_NAME_KD"))
        {
            productName = SelectedProductRow["G_NAME_KD"]?.ToString() ?? "";
        }
        else if (SelectedProductRow.Row.Table.Columns.Contains("ProductName"))
        {
            productName = SelectedProductRow["ProductName"]?.ToString() ?? "";
        }

        // Tên dùng để fill vào ô Mã SP trong Tool Editor: Ưu tiên productName, nếu rỗng thì fallback về productCode
        var nameForToolEditor = !string.IsNullOrWhiteSpace(productName) ? productName : productCode;

        AssignStatusMessage = $"⏳ Đang gán mã '{productCode}' → '{System.IO.Path.GetFileName(AssignJobFilePath)}'...";
        AssignStatusBrush = Brushes.DodgerBlue;

        // Gán Job vào sản phẩm trong CSDL theo ProductCode
        var (success, msg) = await _oqcService.AssignProductJobAsync(productCode, AssignJobFilePath, _dbManager);
        if (success)
        {
            // TỰ ĐỘNG ĐIỀN VÀ LƯU VÀO Ô MÃ SP CỦA TOOL EDITOR THEO PRODUCTNAME
            SyncProductCodeToToolEditor(nameForToolEditor, AssignJobFilePath);

            AssignStatusMessage = $"{msg} (Đã gán Mã SP: '{productCode}', Điền Tool Editor: '{nameForToolEditor}')";
            AssignStatusBrush = Brushes.Green;
        }
        else
        {
            AssignStatusMessage = $"❌ {msg}";
            AssignStatusBrush = Brushes.Red;
        }
    }

    /// <summary>
    /// Tự động đồng bộ mã sản phẩm vào ô Mã SP của Tool Editor và tự động lưu vào file .job đang mở.
    /// </summary>
    private void SyncProductCodeToToolEditor(string productCode, string jobFilePath)
    {
        if (string.IsNullOrWhiteSpace(productCode)) return;

        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            try
            {
                _toolEditorViewModel?.ApplyAssignedProductCode(productCode, jobFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncProductCodeToToolEditor] Error: {ex.Message}");
            }
        });
    }
}



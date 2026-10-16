using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

    // ─── Mật khẩu xóa dữ liệu lịch sử OQC ───
    [ObservableProperty]
    private string _deleteHistoryPassword = "1234";

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
    public IRelayCommand ResetFactorySettingsCommand { get; private set; } = null!;
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

    public ObservableCollection<DataRowView> SelectedProductRows { get; } = new();

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
        ResetFactorySettingsCommand = new RelayCommand(ResetFactorySettings);
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
            SteelPunchMode = cfg.SteelPunchMode;
            DeleteHistoryPassword = !string.IsNullOrWhiteSpace(cfg.DeleteHistoryPassword) ? cfg.DeleteHistoryPassword : "1234";
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
            OnlyOriginMode = OnlyOriginMode,
            SteelPunchMode = SteelPunchMode,
            DeleteHistoryPassword = !string.IsNullOrWhiteSpace(DeleteHistoryPassword) ? DeleteHistoryPassword : "1234"
        };

        _oqcService.SaveConfig(cfg);
        StatusMessage = "⚙ Đã lưu cấu hình OQC Scanner!";
        StatusBrush = Brushes.Green;
    }

    /// <summary>
    /// Khôi phục toàn bộ thông tin tra cứu & CSDL về chuẩn cấu hình xưởng CMS_VINA.
    /// </summary>
    public void ResetFactorySettings()
    {
        // 1. Tìm hoặc khôi phục kết nối CSDL CMS_VINA nếu chưa có
        string cmsDbId = "6b50f44b-86cd-4c0e-91a8-9f385efbaf6d";
        var existingCms = _dbManager.GetDatabase(cmsDbId)
            ?? _dbManager.Databases.FirstOrDefault(d => string.Equals(d.Name, "CMS_VINA", StringComparison.OrdinalIgnoreCase));

        if (existingCms == null)
        {
            var defaultCmsDb = new DbModel
            {
                Id = cmsDbId,
                Name = "CMS_VINA",
                ProviderType = DbProviderType.SqlServer,
                Server = "192.168.1.2",
                Port = 6789,
                DatabaseName = "CMS_VINA",
                Username = "sa",
                Password = "*11021201$",
                IsEnabled = true
            };
            _dbManager.AddDatabase(defaultCmsDb);
        }
        else
        {
            cmsDbId = existingCms.Id;
        }

        // 2. Tạo cấu hình chuẩn xưởng CMS_VINA
        var factory = OqcScannerConfig.CreateFactoryStandard(cmsDbId, "CMS_VINA");

        LookupDbId = factory.LookupDbId;
        LookupQuery = factory.LookupQuery;
        JobFilePathColumn = factory.JobFilePathColumn;
        JobRootDirectory = factory.JobRootDirectory;

        EnableProductNameLookup = factory.EnableProductNameLookup;
        ProductNameDbId = factory.ProductNameDbId;
        ProductNameQuery = factory.ProductNameQuery;
        ProductNameColumn = factory.ProductNameColumn;

        ProductListDbId = factory.ProductListDbId;
        ProductListQuery = factory.ProductListQuery;
        ProductListCodeColumn = factory.ProductListCodeColumn;
        ProductListNameColumn = factory.ProductListNameColumn;
        ProductListPageSize = factory.ProductListPageSize;

        AssignDbId = factory.AssignDbId;
        AssignQuery = factory.AssignQuery;

        UpdateTeachImageDbId = factory.UpdateTeachImageDbId;
        UpdateTeachImageQuery = factory.UpdateTeachImageQuery;

        ServerApiUrl = factory.ServerApiUrl;
        TeachImageColumn = factory.TeachImageColumn;

        JobManagerDbId = factory.JobManagerDbId;
        JobManagerQuery = factory.JobManagerQuery;
        JobManagerProductCodeColumn = factory.JobManagerProductCodeColumn;
        JobManagerProductNameColumn = factory.JobManagerProductNameColumn;
        JobManagerJobFileColumn = factory.JobManagerJobFileColumn;
        JobManagerTeachImageColumn = factory.JobManagerTeachImageColumn;
        JobManagerUpdatedColumn = factory.JobManagerUpdatedColumn;
        JobManagerPageSize = factory.JobManagerPageSize;

        LogResultToDb = factory.LogResultToDb;
        LogResultDbId = factory.LogResultDbId;
        LogResultQuery = factory.LogResultQuery;

        LogDetailResultToDb = factory.LogDetailResultToDb;
        LogDetailResultDbId = factory.LogDetailResultDbId;
        LogDetailResultQuery = factory.LogDetailResultQuery;

        EnableCameraBarcodeScan = factory.EnableCameraBarcodeScan;
        TargetCodeType = factory.TargetCodeType;
        EnableLengthFilter = factory.EnableLengthFilter;
        RequiredCodeLength = factory.RequiredCodeLength;
        EnableCodeCrop = factory.EnableCodeCrop;
        CropStartIndex = factory.CropStartIndex;
        CropLength = factory.CropLength;
        ScanTimeoutMs = factory.ScanTimeoutMs;
        UseExternalScanner = factory.UseExternalScanner;
        AutoRunJob = factory.AutoRunJob;
        OnlyOriginMode = factory.OnlyOriginMode;
        SteelPunchMode = factory.SteelPunchMode;
        DeleteHistoryPassword = factory.DeleteHistoryPassword;

        // Lưu trực tiếp
        SaveSettingsToConfig();

        StatusMessage = "↺ Đã khôi phục cấu hình tra cứu xưởng CMS_VINA (192.168.1.2:6789)!";
        StatusBrush = Brushes.DodgerBlue;
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
                    OnlyOriginMode = OnlyOriginMode,
                    SteelPunchMode = SteelPunchMode,
                    DeleteHistoryPassword = !string.IsNullOrWhiteSpace(DeleteHistoryPassword) ? DeleteHistoryPassword : "1234"
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
        SelectedProductRows.Clear();
        SelectedProductRow = null;
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
        var selectedRows = SelectedProductRows.Count > 0
            ? SelectedProductRows.ToArray()
            : SelectedProductRow is null ? Array.Empty<DataRowView>() : new[] { SelectedProductRow };
        if (selectedRows.Length == 0)
        {
            AssignStatusMessage = "⚠️ Vui lòng chọn ít nhất một sản phẩm trong bảng!";
            AssignStatusBrush = Brushes.Orange;
            return;
        }

        if (string.IsNullOrWhiteSpace(AssignJobFilePath))
        {
            AssignStatusMessage = "⚠️ Vui lòng chọn hoặc nhập tệp Job!";
            AssignStatusBrush = Brushes.Orange;
            return;
        }

        var configuredCodeCol = !string.IsNullOrWhiteSpace(ProductListCodeColumn) ? ProductListCodeColumn.Trim() : "";
        string productName = "";
        var configuredNameCol = !string.IsNullOrWhiteSpace(ProductListNameColumn) ? ProductListNameColumn.Trim() : "";

        var products = ExtractProductAssignments(selectedRows, configuredCodeCol, configuredNameCol);
        var invalidProducts = products.Where(product => string.IsNullOrWhiteSpace(product.Code)).ToList();
        if (invalidProducts.Count > 0)
        {
            AssignStatusMessage = $"⚠️ {invalidProducts.Count} sản phẩm được chọn không có mã sản phẩm hợp lệ.";
            AssignStatusBrush = Brushes.Red;
            return;
        }

        AssignStatusMessage = $"⏳ Đang gán Job '{System.IO.Path.GetFileName(AssignJobFilePath)}' cho {products.Count} sản phẩm...";
        AssignStatusBrush = Brushes.DodgerBlue;

        var failures = new List<string>();
        foreach (var product in products)
        {
            var (success, msg) = await _oqcService.AssignProductJobAsync(product.Code, AssignJobFilePath, _dbManager);
            if (!success) failures.Add($"{product.Code}: {msg}");
        }

        if (failures.Count == 0)
        {
            if (products.Count == 1)
            {
                var product = products[0];
                SyncProductCodeToToolEditor(!string.IsNullOrWhiteSpace(product.Name) ? product.Name : product.Code, AssignJobFilePath);
            }
            AssignStatusMessage = $"✅ Đã gán Job cho toàn bộ {products.Count} sản phẩm.";
            AssignStatusBrush = Brushes.Green;
        }
        else
        {
            AssignStatusMessage = $"⚠️ Gán thành công {products.Count - failures.Count}/{products.Count}. Lỗi: {string.Join("; ", failures)}";
            AssignStatusBrush = Brushes.Orange;
        }
    }

    public void SetSelectedProductRows(System.Collections.IList rows)
    {
        SelectedProductRows.Clear();
        foreach (var item in rows)
            if (item is DataRowView row) SelectedProductRows.Add(row);
    }

    public static List<(string Code, string Name)> ExtractProductAssignments(
        IEnumerable<DataRowView> rows, string configuredCodeColumn, string configuredNameColumn)
    {
        string GetValue(DataRowView row, string preferredColumn, string fallbackColumn1, string fallbackColumn2, bool useFirstColumnFallback)
        {
            if (!string.IsNullOrWhiteSpace(preferredColumn) && row.Row.Table.Columns.Contains(preferredColumn))
                return row[preferredColumn]?.ToString() ?? "";
            if (row.Row.Table.Columns.Contains(fallbackColumn1)) return row[fallbackColumn1]?.ToString() ?? "";
            if (row.Row.Table.Columns.Contains(fallbackColumn2)) return row[fallbackColumn2]?.ToString() ?? "";
            return useFirstColumnFallback && row.Row.Table.Columns.Count > 0 ? row[0]?.ToString() ?? "" : "";
        }

        return rows.Select(row => (
            Code: GetValue(row, configuredCodeColumn, "G_CODE", "ProductCode", true),
            Name: GetValue(row, configuredNameColumn, "G_NAME_KD", "ProductName", false)))
            .ToList();
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



using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OpenCvSharp;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.Persistence;

namespace VisionInspectionApp.UI.ViewModels;

public partial class JobManagerViewModel
{
    /// <summary>
    /// Đảm bảo Job đang mở trong Tool Editor được lưu sạch sẽ xuống đĩa trước khi tải lên Server.
    /// Nếu Job đang mở chưa được lưu (do có thay đổi IsDirty hoặc chưa có tệp trên đĩa):
    /// Hệ thống sẽ tự động lưu Job trước rồi mới cho phép tải lên Server.
    /// </summary>
    /// <param name="targetItem">Sản phẩm đang được chọn trong danh sách</param>
    /// <returns>
    /// Tuple gồm:
    /// - HasJob: Có Job hợp lệ sẵn sàng để tải lên hay không
    /// - JobFilePath: Đường dẫn tệp Job trên đĩa đã được lưu
    /// - AutoSwitchedToCamera: Đã tự động chuyển cấu hình sang Camera sản xuất hay chưa
    /// </returns>
    public async Task<(bool HasJob, string JobFilePath, bool AutoSwitchedToCamera)> EnsureActiveJobSavedBeforeUploadAsync(JobManagerItem targetItem)
    {
        // 1. Kiểm tra xem Tool Editor có đang mở một Job hay không
        bool hasActiveJob = !string.IsNullOrWhiteSpace(_toolEditorViewModel.CurrentJobFilePath)
                            || _toolEditorViewModel.Nodes.Count > 0
                            || _toolEditorViewModel.Config != null
                            || _toolEditorViewModel.IsDirty;

        if (!hasActiveJob)
        {
            return (false, string.Empty, false);
        }

        // 2. Kiểm tra Job đang mở có cần lưu (chưa lưu do có thay đổi hoặc chưa có tệp trên đĩa) hay không
        bool fileExistsOnDisk = !string.IsNullOrWhiteSpace(_toolEditorViewModel.CurrentJobFilePath)
                                && File.Exists(_toolEditorViewModel.CurrentJobFilePath);

        bool needsSave = _toolEditorViewModel.IsDirty || !fileExistsOnDisk;

        string finalJobPath = _toolEditorViewModel.CurrentJobFilePath ?? string.Empty;
        bool autoSwitched = false;

        // 3. Nếu chưa có file trên đĩa: Xác định đường dẫn tệp mặc định theo mã sản phẩm đang chọn
        if (!fileExistsOnDisk)
        {
            string defaultJobDir = !string.IsNullOrWhiteSpace(_oqcService.Config.JobRootDirectory) && Directory.Exists(_oqcService.Config.JobRootDirectory)
                ? _oqcService.Config.JobRootDirectory
                : AppStoragePaths.JobsDirectory;
            Directory.CreateDirectory(defaultJobDir);

            string safeCode = RemoteServerService.SanitizeIdentifier(targetItem.ProductCode);
            string safeName = RemoteServerService.SanitizeIdentifier(targetItem.ProductName);
            string id = !string.IsNullOrWhiteSpace(safeName) ? $"{safeCode}_{safeName}" : safeCode;
            if (string.IsNullOrWhiteSpace(id)) id = "job_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            finalJobPath = Path.Combine(defaultJobDir, $"{id}.job");
            _toolEditorViewModel.CurrentJobFilePath = finalJobPath;
            if (!string.IsNullOrWhiteSpace(targetItem.ProductCode))
            {
                _toolEditorViewModel.ProductCode = targetItem.ProductCode;
            }
            needsSave = true;
        }

        // 4. Nếu có thay đổi (IsDirty) hoặc chưa có file: Lưu Job trước trên UI Dispatcher
        if (needsSave)
        {
            StatusMessage = $"💾 Đang lưu Job '{Path.GetFileName(finalJobPath)}' trước khi tải lên Server...";
            StatusBrush = Brushes.DodgerBlue;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Tự động chuẩn bị Job cho sản xuất (chuyển URL ảnh mẫu về Camera OQC) và lưu xuống file
                autoSwitched = _toolEditorViewModel.PrepareJobForProductionUpload();
            });

            // Nếu PrepareJobForProductionUpload chưa tạo file hoặc file chưa có, gọi SaveJob trực tiếp
            if (!File.Exists(finalJobPath))
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _toolEditorViewModel.SaveJob();
                });
            }

            if (!File.Exists(finalJobPath))
            {
                MessageBox.Show($"Không thể lưu tệp Job vào đường dẫn:\n{finalJobPath}\nVui lòng lưu Job trong Tool Editor trước khi tải lên.", "Lỗi Lưu Job", MessageBoxButton.OK, MessageBoxImage.Error);
                return (false, string.Empty, false);
            }

            StatusMessage = $"💾 Đã lưu Job '{Path.GetFileName(finalJobPath)}' thành công. Đang tải lên Server...";
        }
        else
        {
            // Trường hợp file đã tồn tại và không dirty: Vẫn đảm bảo chuyển nguồn ảnh về Camera OQC nếu đang ở chế độ xem ảnh mẫu URL
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                autoSwitched = _toolEditorViewModel.PrepareJobForProductionUpload();
            });
        }

        return (true, finalJobPath, autoSwitched);
    }

    /// <summary>
    /// Chụp ảnh mẫu hiện tại và tải đồng thời ảnh cùng Job đang mở lên Server.
    /// Nếu Job đang mở chưa được lưu (do có thay đổi), tự động lưu Job trước rồi mới up lên Server.
    /// </summary>
    public async Task ExecuteUploadTeachAndJobAsync()
    {
        var selectedItem = SelectedItem;
        if (selectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trước khi tải ảnh và Job lên Server!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 1. Kiểm tra và lưu Job đang mở trước nếu có thay đổi chưa lưu
        var (hasJob, jobFilePath, autoSwitchedToCamera) = await EnsureActiveJobSavedBeforeUploadAsync(selectedItem);
        if (!hasJob || string.IsNullOrWhiteSpace(jobFilePath) || !File.Exists(jobFilePath))
        {
            MessageBox.Show("Không có Job hiện tại đang mở trong Tool Editor.\nVui lòng mở hoặc thiết lập cấu hình Job trước khi tải ảnh + Job.", "Thiếu Job", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 2. Lấy dữ liệu ảnh từ Camera hoặc màn hình snapshot
        byte[]? imageBytes = null;
        if (_cameraService.IsRunning)
        {
            using var frame = _cameraService.TryGetLatestFrameClone();
            if (frame != null && !frame.Empty()) imageBytes = frame.ToBytes(".png");
        }
        if (imageBytes == null || imageBytes.Length == 0)
        {
            using var snapshot = _sharedImage.GetSnapshot();
            if (snapshot != null && !snapshot.Empty()) imageBytes = snapshot.ToBytes(".png");
        }
        if (imageBytes == null || imageBytes.Length == 0)
        {
            MessageBox.Show("Không lấy được ảnh mẫu từ Camera hoặc màn hình hiện tại.", "Thiếu ảnh", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        BusyMessage = "Đang tải ảnh mẫu và Job hiện tại lên Server...";
        StatusMessage = $"📤 Đang tải ảnh và Job cho '{selectedItem.ProductCode}'...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            string safeCode = RemoteServerService.SanitizeIdentifier(selectedItem.ProductCode);
            string safeName = RemoteServerService.SanitizeIdentifier(selectedItem.ProductName);
            string identifier = !string.IsNullOrWhiteSpace(safeName) ? $"{safeCode}_{safeName}" : safeCode;
            string imageFileName = $"teach_{identifier}_{DateTime.Now:yyyyMMdd_HHmmss}.png";

            var imageUploadTask = TryUploadTeachImageAsync(imageBytes, imageFileName, selectedItem);
            var jobUploadTask = TryUploadJobAsync(jobFilePath, selectedItem);
            await Task.WhenAll(imageUploadTask, jobUploadTask);

            var imageUpload = await imageUploadTask;
            var jobUpload = await jobUploadTask;
            var messages = new List<string>();
            bool databaseUpdatesSucceeded = true;

            if (imageUpload.Success)
            {
                string teachPath = !string.IsNullOrWhiteSpace(imageUpload.RelativePath) ? imageUpload.RelativePath : imageUpload.Url;
                var (updated, updateMessage) = await _oqcService.UpdateTeachImagePathAsync(selectedItem.ProductCode, teachPath, _dbManager);
                if (updated)
                {
                    selectedItem.TeachImagePath = teachPath;
                    selectedItem.HasTeachImage = true;
                    await LoadTeachImagePreviewAsync(teachPath);
                    messages.Add("Ảnh mẫu đã tải lên và cập nhật CSDL");
                }
                else
                {
                    databaseUpdatesSucceeded = false;
                    messages.Add($"Ảnh đã tải lên nhưng cập nhật CSDL thất bại: {updateMessage}");
                }
            }
            else messages.Add($"Tải ảnh thất bại: {imageUpload.ErrorMessage}");

            if (jobUpload.Success)
            {
                string jobPath = !string.IsNullOrWhiteSpace(jobUpload.RelativePath) ? jobUpload.RelativePath : jobUpload.Url;
                var (assigned, assignMessage) = await _oqcService.AssignProductJobAsync(
                    selectedItem.ProductCode, jobPath, _dbManager, selectedItem.TeachImagePath);
                if (assigned)
                {
                    selectedItem.JobFilePath = jobPath;
                    selectedItem.HasJobFile = true;
                    messages.Add("Job đã tải lên và cập nhật CSDL");
                }
                else
                {
                    databaseUpdatesSucceeded = false;
                    messages.Add($"Job đã tải lên nhưng cập nhật CSDL thất bại: {assignMessage}");
                }
            }
            else messages.Add($"Tải Job thất bại: {jobUpload.ErrorMessage}");

            bool allSucceeded = imageUpload.Success && jobUpload.Success && databaseUpdatesSucceeded;
            StatusMessage = $"{(allSucceeded ? "✅" : "⚠️")} {string.Join("; ", messages)}";
            StatusBrush = allSucceeded ? Brushes.Green : Brushes.Orange;
            string productionNotice = autoSwitchedToCamera
                ? "\n\nNguồn ảnh của Job đã được chuyển về Camera OQC để dùng trong sản xuất."
                : string.Empty;
            MessageBox.Show($"{string.Join("\n", messages)}{productionNotice}", allSucceeded ? "Hoàn Tất" : "Kết Quả Tải Lên", MessageBoxButton.OK,
                allSucceeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi tải ảnh + Job: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show(StatusMessage, "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Tải tệp Job (.job) hiện tại lên Server và tự động cập nhật đường dẫn vào CSDL.
    /// Nếu Job đang mở chưa được lưu (do có thay đổi), tự động lưu Job trước rồi mới up lên Server.
    /// </summary>
    public async Task ExecuteUploadCurrentJobAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi tải Job lên Server!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? jobFilePath = null;
        bool autoSwitchedToCamera = false;

        // 1. Kiểm tra Job đang mở trong Tool Editor: Nếu có thay đổi chưa lưu thì lưu trước
        var (hasJob, savedPath, switched) = await EnsureActiveJobSavedBeforeUploadAsync(SelectedItem);
        if (hasJob && !string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            jobFilePath = savedPath;
            autoSwitchedToCamera = switched;
        }
        else
        {
            // 2. Nếu Tool Editor không có Job nào đang mở, cho phép chọn tệp .job từ đĩa
            var ofd = new OpenFileDialog
            {
                Title = $"Chọn tệp Job (.job) để tải lên Server cho '{SelectedItem.ProductCode}'",
                Filter = "Vision Job Files (*.job)|*.job|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() != true) return;
            jobFilePath = ofd.FileName;
        }

        if (string.IsNullOrWhiteSpace(jobFilePath) || !File.Exists(jobFilePath))
        {
            MessageBox.Show("Không tìm thấy tệp Job hợp lệ để tải lên Server.", "Thiếu Tệp Job", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        BusyMessage = "Đang tải tệp Job lên Server XAMPP...";
        StatusMessage = $"📤 Đang tải tệp Job '{Path.GetFileName(jobFilePath)}' lên Server...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            var (uploadOk, fullUrl, relPath, uploadErr) = await _remoteServerService.UploadJobAsync(
                jobFilePath, SelectedItem.ProductCode, _oqcService.Config.ServerApiUrl, SelectedItem.ProductName);

            if (!uploadOk)
            {
                MessageBox.Show($"Lỗi tải tệp Job lên máy chủ:\n{uploadErr}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"❌ Lỗi Upload Job: {uploadErr}";
                StatusBrush = Brushes.Red;
                return;
            }

            string jobPathToSave = !string.IsNullOrWhiteSpace(relPath) ? relPath : fullUrl;
            var (assignOk, assignMsg) = await _oqcService.AssignProductJobAsync(
                SelectedItem.ProductCode, jobPathToSave, _dbManager, SelectedItem.TeachImagePath);

            if (assignOk)
            {
                SelectedItem.JobFilePath = jobPathToSave;
                SelectedItem.HasJobFile = true;
                SelectedItem.StatusMessage = "Đã đồng bộ Job lên Server";

                string cameraNotice = autoSwitchedToCamera
                    ? "\n\n⚡ Nguồn ảnh đã được tự động cấu hình về Camera OQC gốc (bảo lưu toàn bộ thông số Camera & Đèn) để kích hoạt camera thực tế khi chạy kiểm tra dưới chuyền."
                    : "";

                StatusMessage = $"✅ Tải tệp Job lên Server thành công và cập nhật CSDL cho '{SelectedItem.ProductCode}'!";
                StatusBrush = Brushes.Green;
                MessageBox.Show($"✅ Tải tệp Job lên Server thành công!\nURL: {fullUrl}\nĐã cập nhật liên kết trong CSDL cho mã '{SelectedItem.ProductCode}'.{cameraNotice}", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"⚠️ Tệp Job đã upload nhưng lỗi ghi CSDL: {assignMsg}";
                StatusBrush = Brushes.Orange;
                MessageBox.Show($"Tệp Job đã tải lên Server thành công nhưng lỗi cập nhật CSDL:\n{assignMsg}", "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ngoại lệ Upload Job: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Ngoại lệ khi upload Job: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> TryUploadTeachImageAsync(
        byte[] imageBytes, string fileName, JobManagerItem item)
    {
        try
        {
            return await _remoteServerService.UploadImageAsync(
                imageBytes, fileName, item.ProductCode, _oqcService.Config.ServerApiUrl, item.ProductName);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, string.Empty, ex.Message);
        }
    }

    private async Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> TryUploadJobAsync(
        string jobFilePath, JobManagerItem item)
    {
        try
        {
            return await _remoteServerService.UploadJobAsync(
                jobFilePath, item.ProductCode, _oqcService.Config.ServerApiUrl, item.ProductName);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Chụp ảnh trực tiếp từ Camera hiện tại và tải lên Server làm Teach Image cho sản phẩm được chọn.
    /// </summary>
    public async Task ExecuteUploadTeachFromCameraAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi chụp ảnh mẫu!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        BusyMessage = "Đang chụp ảnh từ Camera & tải lên Server...";
        StatusMessage = $"📸 Đang chụp ảnh mẫu cho mã '{SelectedItem.ProductCode}'...";
        StatusBrush = Brushes.DodgerBlue;

        try
        {
            byte[]? imageBytes = null;

            // 1. Thử lấy ảnh trực tiếp từ Camera
            if (_cameraService.IsRunning)
            {
                using var frame = _cameraService.TryGetLatestFrameClone();
                if (frame != null && !frame.Empty())
                {
                    imageBytes = frame.ToBytes(".png");
                }
            }

            // 2. Fallback từ SharedImageContext
            if (imageBytes == null || imageBytes.Length == 0)
            {
                using var snap = _sharedImage.GetSnapshot();
                if (snap != null && !snap.Empty())
                {
                    imageBytes = snap.ToBytes(".png");
                }
            }

            if (imageBytes == null || imageBytes.Length == 0)
            {
                MessageBox.Show("Không thể lấy ảnh từ Camera hoặc màn hình kiểm tra hiện tại. Hãy bật Camera hoặc mở ảnh trước!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "❌ Không có dữ liệu ảnh để tải lên.";
                StatusBrush = Brushes.Red;
                return;
            }

            // Upload lên Server
            string safeCode = RemoteServerService.SanitizeIdentifier(SelectedItem.ProductCode);
            string safeName = RemoteServerService.SanitizeIdentifier(SelectedItem.ProductName);
            string id = !string.IsNullOrWhiteSpace(safeName) ? $"{safeCode}_{safeName}" : safeCode;
            string fileName = $"teach_{id}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var (uploadOk, fullUrl, relPath, uploadErr) = await _remoteServerService.UploadImageAsync(
                imageBytes, fileName, SelectedItem.ProductCode, _oqcService.Config.ServerApiUrl, SelectedItem.ProductName);

            if (!uploadOk)
            {
                MessageBox.Show($"Lỗi tải ảnh lên máy chủ:\n{uploadErr}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"❌ Lỗi Upload: {uploadErr}";
                StatusBrush = Brushes.Red;
                return;
            }

            // Cập nhật CSDL
            string teachPathToSave = !string.IsNullOrWhiteSpace(relPath) ? relPath : fullUrl;
            var (assignOk, assignMsg) = await _oqcService.UpdateTeachImagePathAsync(
                SelectedItem.ProductCode, teachPathToSave, _dbManager);

            if (assignOk)
            {
                SelectedItem.TeachImagePath = teachPathToSave;
                SelectedItem.HasTeachImage = true;
                SelectedItem.StatusMessage = "Đã cập nhật ảnh Teach";

                await LoadTeachImagePreviewAsync(teachPathToSave);

                StatusMessage = $"✅ Tải ảnh Teach Image thành công ({fileName}) và cập nhật CSDL!";
                StatusBrush = Brushes.Green;
                MessageBox.Show($"✅ Tải ảnh Teach Image thành công!\nURL: {fullUrl}\nĐã cập nhật vào CSDL cho mã '{SelectedItem.ProductCode}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"⚠️ Ảnh đã upload nhưng lỗi ghi CSDL: {assignMsg}";
                StatusBrush = Brushes.Orange;
                MessageBox.Show($"Ảnh đã tải lên Server thành công nhưng lỗi cập nhật CSDL:\n{assignMsg}", "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ngoại lệ Upload: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Ngoại lệ khi upload ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Chọn ảnh từ máy tính (File) và tải lên Server làm Teach Image cho sản phẩm được chọn.
    /// </summary>
    public async Task ExecuteUploadTeachFromFileAsync()
    {
        if (SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn một sản phẩm trong danh sách trước khi chọn ảnh!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ofd = new OpenFileDialog
        {
            Title = $"Chọn Ảnh Mẫu (Teach Image) cho '{SelectedItem.ProductCode}'",
            Filter = "Tệp Ảnh (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Tất cả tệp (*.*)|*.*"
        };

        if (ofd.ShowDialog() != true) return;

        IsBusy = true;
        BusyMessage = "Đang đọc & tải ảnh lên Server...";

        try
        {
            byte[] imageBytes = await File.ReadAllBytesAsync(ofd.FileName);
            string fileName = Path.GetFileName(ofd.FileName);

            var (uploadOk, fullUrl, relPath, uploadErr) = await _remoteServerService.UploadImageAsync(
                imageBytes, fileName, SelectedItem.ProductCode, _oqcService.Config.ServerApiUrl);

            if (!uploadOk)
            {
                MessageBox.Show($"Lỗi tải ảnh lên máy chủ:\n{uploadErr}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"❌ Lỗi Upload: {uploadErr}";
                StatusBrush = Brushes.Red;
                return;
            }

            string teachPathToSave = !string.IsNullOrWhiteSpace(relPath) ? relPath : fullUrl;
            var (assignOk, assignMsg) = await _oqcService.UpdateTeachImagePathAsync(
                SelectedItem.ProductCode, teachPathToSave, _dbManager);

            if (assignOk)
            {
                SelectedItem.TeachImagePath = teachPathToSave;
                SelectedItem.HasTeachImage = true;
                SelectedItem.StatusMessage = "Đã cập nhật ảnh Teach";

                await LoadTeachImagePreviewAsync(teachPathToSave);

                StatusMessage = $"✅ Tải ảnh Teach Image thành công ({fileName}) và cập nhật CSDL!";
                StatusBrush = Brushes.Green;
                MessageBox.Show($"✅ Tải ảnh Teach Image thành công!\nURL: {fullUrl}\nĐã cập nhật vào CSDL cho mã '{SelectedItem.ProductCode}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"⚠️ Ảnh đã upload nhưng lỗi ghi CSDL: {assignMsg}";
                StatusBrush = Brushes.Orange;
                MessageBox.Show($"Ảnh đã tải lên Server thành công nhưng lỗi cập nhật CSDL:\n{assignMsg}", "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ngoại lệ Upload: {ex.Message}";
            StatusBrush = Brushes.Red;
            MessageBox.Show($"Ngoại lệ khi upload ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

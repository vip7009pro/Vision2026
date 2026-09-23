using System;
using System.Collections.Generic;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Quản lý phiên cuộn và cơ sở dữ liệu vết lỗi, tính toán toạ độ vật lý mét dài của từng khuyết tật
/// </summary>
public sealed class RollDefectManager
{
    private readonly object _lock = new();
    private RollSession _currentSession = new();

    /// <summary>
    /// Giới hạn số vết lỗi giữ trong bộ nhớ cho mỗi phiên cuộn.
    /// Chạy Continuous 24/7 thì danh sách Defects sẽ phình vô hạn nếu không chặn
    /// => tiến trình càng chạy càng chậm. Vượt ngưỡng sẽ loại bỏ các vết lỗi CŨ NHẤT.
    /// </summary>
    public int MaxDefectsInMemory { get; set; } = 50_000;

    public RollSession CurrentSession
    {
        get
        {
            lock (_lock)
            {
                return _currentSession;
            }
        }
    }

    public event EventHandler<RollDefectItem>? OnDefectRecorded;
    public event EventHandler<RollSession>? OnSessionStarted;
    public event EventHandler<RollSession>? OnSessionEnded;

    public RollDefectManager()
    {
    }

    /// <summary>
    /// Bắt đầu một phiên cuộn mới
    /// </summary>
    public RollSession StartSession(string lotNumber = "LOT-001", string operatorName = "Operator", string jobName = "DefaultJob", double rollWidthMm = 500.0)
    {
        lock (_lock)
        {
            _currentSession = new RollSession
            {
                SessionId = $"ROLL-{DateTime.Now:yyyyMMdd-HHmmss}",
                LotNumber = lotNumber,
                OperatorName = operatorName,
                JobName = jobName,
                RollWidthMm = rollWidthMm,
                StartTime = DateTime.UtcNow
            };

            OnSessionStarted?.Invoke(this, _currentSession);
            return _currentSession;
        }
    }

    /// <summary>
    /// Khởi tạo lại một phiên cuộn mới (alias)
    /// </summary>
    public RollSession StartNewSession(string rollId = "LOT-001", double rollWidthMm = 500.0, double initialMeters = 0.0)
    {
        lock (_lock)
        {
            _currentSession = new RollSession
            {
                SessionId = string.IsNullOrWhiteSpace(rollId) ? $"ROLL-{DateTime.Now:yyyyMMdd-HHmmss}" : rollId,
                LotNumber = rollId,
                RollWidthMm = rollWidthMm,
                TotalLengthMeters = initialMeters,
                StartTime = DateTime.UtcNow
            };

            OnSessionStarted?.Invoke(this, _currentSession);
            return _currentSession;
        }
    }

    /// <summary>
    /// Thêm vết lỗi vào phiên hiện tại (đã trong lock) và dọn bớt nếu vượt ngưỡng bộ nhớ.
    /// </summary>
    private void AddDefectLocked(RollDefectItem item)
    {
        _currentSession.Defects.Add(item);

        var max = MaxDefectsInMemory;
        if (max <= 0 || _currentSession.Defects.Count <= max)
        {
            return;
        }

        // Giữ lại các vết lỗi mới nhất, loại bỏ dần các vết lỗi cũ nhất.
        int removeCount = _currentSession.Defects.Count - max;
        if (removeCount > 0)
        {
            _currentSession.Defects.RemoveRange(0, removeCount);
        }
    }

    /// <summary>
    /// Kết thúc phiên cuộn hiện tại
    /// </summary>
    public RollSession EndSession(double? finalLengthMeters = null)
    {
        lock (_lock)
        {
            _currentSession.EndTime = DateTime.UtcNow;
            if (finalLengthMeters.HasValue && finalLengthMeters.Value > 0)
            {
                _currentSession.TotalLengthMeters = finalLengthMeters.Value;
            }

            OnSessionEnded?.Invoke(this, _currentSession);
            return _currentSession;
        }
    }

    /// <summary>
    /// Trích xuất và ghi nhận toàn bộ vết lỗi từ kết quả kiểm tra vào cơ sở dữ liệu cuộn
    /// </summary>
    public List<RollDefectItem> RecordDefectsFromInspectionResult(InspectionResult result, FrameMetadata? meta, Mat? frame = null)
    {
        if (result == null || meta == null) return new List<RollDefectItem>();

        var recordedItems = new List<RollDefectItem>();

        lock (_lock)
        {
            // Cập nhật mét dài hiện tại của cuộn
            double currentMeter = meta.WebPositionMm / 1000.0;
            if (currentMeter > _currentSession.TotalLengthMeters)
            {
                _currentSession.TotalLengthMeters = currentMeter;
            }

            // 1. Trích xuất từ DefectDetectionResult (White/Black Spots, Pinholes, Scratches)
            if (result.Defects != null && result.Defects.Defects.Count > 0)
            {
                foreach (var defectBlob in result.Defects.Defects)
                {
                    double centerX = defectBlob.BoundingBox.X + (defectBlob.BoundingBox.Width / 2.0);
                    double centerY = defectBlob.BoundingBox.Y + (defectBlob.BoundingBox.Height / 2.0);

                    var (webX, webY) = meta.ConvertToWebCoordinates(centerX, centerY);

                    var item = new RollDefectItem
                    {
                        RollSessionId = _currentSession.SessionId,
                        FrameIndex = meta.FrameIndex,
                        Timestamp = meta.HostTimestamp,
                        DefectType = defectBlob.Type ?? "Defect",
                        Severity = DefectSeverity.Reject,
                        WebX_Mm = webX,
                        WebY_Mm = webY,
                        Width_Mm = defectBlob.BoundingBox.Width * meta.MmPerPixel,
                        Length_Mm = defectBlob.BoundingBox.Height * meta.MmPerPixel,
                        Area_Mm2 = defectBlob.Area * meta.MmPerPixel * meta.MmPerPixel,
                        BoundingBox = new DefectBox(defectBlob.BoundingBox.X, defectBlob.BoundingBox.Y, defectBlob.BoundingBox.Width, defectBlob.BoundingBox.Height)
                    };

                    AddDefectLocked(item);
                    recordedItems.Add(item);
                    OnDefectRecorded?.Invoke(this, item);
                }
            }

            // 2. Trích xuất từ BlobDetections
            if (result.BlobDetections != null)
            {
                foreach (var blobResult in result.BlobDetections)
                {
                    if (blobResult.Blobs != null && blobResult.Blobs.Count > 0)
                    {
                        foreach (var blob in blobResult.Blobs)
                        {
                            var (webX, webY) = meta.ConvertToWebCoordinates(blob.BoundingBox.X + (blob.BoundingBox.Width / 2.0), blob.BoundingBox.Y + (blob.BoundingBox.Height / 2.0));
                            var item = new RollDefectItem
                            {
                                RollSessionId = _currentSession.SessionId,
                                FrameIndex = meta.FrameIndex,
                                Timestamp = meta.HostTimestamp,
                                DefectType = $"{blobResult.Name}_Blob",
                                Severity = DefectSeverity.Reject,
                                WebX_Mm = webX,
                                WebY_Mm = webY,
                                Width_Mm = blob.BoundingBox.Width * meta.MmPerPixel,
                                Length_Mm = blob.BoundingBox.Height * meta.MmPerPixel,
                                Area_Mm2 = blob.Area * meta.MmPerPixel * meta.MmPerPixel,
                                BoundingBox = new DefectBox(blob.BoundingBox.X, blob.BoundingBox.Y, blob.BoundingBox.Width, blob.BoundingBox.Height)
                            };

                            AddDefectLocked(item);
                            recordedItems.Add(item);
                            OnDefectRecorded?.Invoke(this, item);
                        }
                    }
                }
            }

            // 3. Trích xuất từ SurfaceCompares
            if (result.SurfaceCompares != null)
            {
                foreach (var surfResult in result.SurfaceCompares)
                {
                    if (!surfResult.Pass && surfResult.Defects != null && surfResult.Defects.Count > 0)
                    {
                        foreach (var defect in surfResult.Defects)
                        {
                            var (webX, webY) = meta.ConvertToWebCoordinates(defect.BoundingBox.X + (defect.BoundingBox.Width / 2.0), defect.BoundingBox.Y + (defect.BoundingBox.Height / 2.0));
                            var item = new RollDefectItem
                            {
                                RollSessionId = _currentSession.SessionId,
                                FrameIndex = meta.FrameIndex,
                                Timestamp = meta.HostTimestamp,
                                DefectType = $"{surfResult.Name}_Surface",
                                Severity = DefectSeverity.Reject,
                                WebX_Mm = webX,
                                WebY_Mm = webY,
                                Width_Mm = defect.BoundingBox.Width * meta.MmPerPixel,
                                Length_Mm = defect.BoundingBox.Height * meta.MmPerPixel,
                                Area_Mm2 = defect.Area * meta.MmPerPixel * meta.MmPerPixel,
                                BoundingBox = new DefectBox(defect.BoundingBox.X, defect.BoundingBox.Y, defect.BoundingBox.Width, defect.BoundingBox.Height)
                            };

                            AddDefectLocked(item);
                            recordedItems.Add(item);
                            OnDefectRecorded?.Invoke(this, item);
                        }
                    }
                }
            }
        }

        return recordedItems;
    }
}

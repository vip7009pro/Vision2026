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

                    _currentSession.Defects.Add(item);
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

                            _currentSession.Defects.Add(item);
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

                            _currentSession.Defects.Add(item);
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

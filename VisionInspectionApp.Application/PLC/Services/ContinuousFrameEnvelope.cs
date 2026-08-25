using System;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

/// <summary>
/// Bao gói Frame ảnh kèm Siêu dữ liệu Motion/Encoder tại đúng thời điểm mili-giây chụp ảnh
/// </summary>
public sealed class ContinuousFrameEnvelope : IDisposable
{
    public required Mat Frame { get; init; }
    public required FrameMetadata Metadata { get; init; }

    public void Dispose()
    {
        Frame?.Dispose();
    }
}

using System;
using System.Collections.Concurrent;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public partial class InspectionService : IInspectionService
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly PatternMatcher _matcher;
    private readonly DistanceCalculator _distanceCalculator;
    private readonly LineDetector _lineDetector;
    private readonly IDefectDetector _defectDetector;

    private sealed class TrackState
    {
        public Point2d? LastOriginPos { get; set; }
        public double LastAngleDeg { get; set; }
        public ConcurrentDictionary<string, Point2d> LastPointPos { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<string, TrackState> _trackByProductCode = new(StringComparer.OrdinalIgnoreCase);

    private readonly PLC.Services.IPlcManagerService? _plcManager;
    private readonly DB.Services.IDbManagerService? _dbManager;

    public InspectionService(
        ImagePreprocessor preprocessor,
        PatternMatcher matcher,
        DistanceCalculator distanceCalculator,
        LineDetector lineDetector,
        IDefectDetector defectDetector,
        PLC.Services.IPlcManagerService? plcManager = null,
        DB.Services.IDbManagerService? dbManager = null)
    {
        _preprocessor = preprocessor;
        _matcher = matcher;
        _distanceCalculator = distanceCalculator;
        _lineDetector = lineDetector;
        _defectDetector = defectDetector;
        _plcManager = plcManager;
        _dbManager = dbManager;
    }

    public InspectionResult Inspect(Mat image, VisionConfig config)
    {
        return Inspect(image, config, dbManagerOverride: null);
    }

    public void ResetTracking(string? productCode = null)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            _trackByProductCode.Clear();
            return;
        }

        _trackByProductCode.TryRemove(productCode, out _);
    }
}

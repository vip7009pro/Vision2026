using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspectionApp.UI.Models.ManualInspection;

public enum ToleranceStatus
{
    NONE,
    PASS,
    NG
}

public sealed partial class ManualMeasurementRecord : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private ManualMeasurementType _toolType;

    [ObservableProperty]
    private string _toolName = string.Empty;

    [ObservableProperty]
    private double _valueMm;

    [ObservableProperty]
    private double _valuePx;

    [ObservableProperty]
    private string _unit = "mm";

    [ObservableProperty]
    private double? _nominal;

    [ObservableProperty]
    private double? _upperTolerance;

    [ObservableProperty]
    private double? _lowerTolerance;

    [ObservableProperty]
    private ToleranceStatus _status = ToleranceStatus.NONE;

    [ObservableProperty]
    private string _details = string.Empty;

    public List<GeoPoint2D> Points { get; set; } = new();

    partial void OnNominalChanged(double? value) => EvaluateTolerance();
    partial void OnUpperToleranceChanged(double? value) => EvaluateTolerance();
    partial void OnLowerToleranceChanged(double? value) => EvaluateTolerance();
    partial void OnValueMmChanged(double value) => EvaluateTolerance();

    public void EvaluateTolerance()
    {
        if (!Nominal.HasValue)
        {
            Status = ToleranceStatus.NONE;
            return;
        }

        double nom = Nominal.Value;
        double upper = nom + (UpperTolerance ?? 0.0);
        double lower = nom - (LowerTolerance ?? 0.0);

        if (UpperTolerance.HasValue && !LowerTolerance.HasValue)
        {
            lower = nom;
        }
        else if (!UpperTolerance.HasValue && LowerTolerance.HasValue)
        {
            upper = nom;
        }

        if (ValueMm >= lower - 1e-6 && ValueMm <= upper + 1e-6)
        {
            Status = ToleranceStatus.PASS;
        }
        else
        {
            Status = ToleranceStatus.NG;
        }
    }
}

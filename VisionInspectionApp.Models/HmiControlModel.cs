using System;

namespace VisionInspectionApp.Models;

public class HmiControlModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Control_01";
    public HmiControlType Type { get; set; } = HmiControlType.Button;

    // Geometry & Layout
    public double X { get; set; } = 50;
    public double Y { get; set; } = 50;
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 80;
    public int ZIndex { get; set; } = 1;

    // PLC Tag Binding
    public string ReadPlcId { get; set; } = "";
    public string ReadTagName { get; set; } = "";

    public string WritePlcId { get; set; } = "";
    public string WriteTagName { get; set; } = "";
    public bool UseSeparateWriteAddress { get; set; } = false;

    // Control Behavior
    public HmiButtonBehavior ButtonBehavior { get; set; } = HmiButtonBehavior.Toggle;
    public string WriteValueOn { get; set; } = "True";
    public string WriteValueOff { get; set; } = "False";

    // Numeric & Text Input Limits
    public double MinValue { get; set; } = 0;
    public double MaxValue { get; set; } = 999999;
    public string DefaultText { get; set; } = "";

    // Visual Appearance
    public string LabelText { get; set; } = "";
    public double FontSize { get; set; } = 12;
    public string ForegroundHex { get; set; } = "#FFFFFF";
    public string BackgroundHex { get; set; } = "#262626";

    // Custom Image Settings
    public bool UseCustomImage { get; set; } = false;
    public string CustomImagePathOn { get; set; } = "";
    public string CustomImagePathOff { get; set; } = "";

    // Value Display & Formatting Settings
    public PlcDataType ValueDataType { get; set; } = PlcDataType.Float;
    public string ValueFormat { get; set; } = "0.##";
    public HmiTextAlignment Alignment { get; set; } = HmiTextAlignment.Center;

    // Vector Graphics Preset Theme
    public HmiColorTheme Theme { get; set; } = HmiColorTheme.Green;
}

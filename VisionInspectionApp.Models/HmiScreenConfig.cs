using System.Collections.Generic;

namespace VisionInspectionApp.Models;

public class HmiScreenConfig
{
    public string ScreenId { get; set; } = "MainScreen";
    public string ScreenName { get; set; } = "Màn hình chính HMI";
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public string BackgroundHex { get; set; } = "#1E1E1E";

    public bool ShowGrid { get; set; } = true;
    public double GridSize { get; set; } = 10;

    public string TargetPlcId { get; set; } = "PLC_01";

    public List<HmiControlModel> Controls { get; set; } = new();
}

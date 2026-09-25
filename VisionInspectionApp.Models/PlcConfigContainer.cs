using System.Collections.Generic;

namespace VisionInspectionApp.Models;

public sealed class PlcConfigContainer
{
    public List<PlcModel> Plcs { get; set; } = new();
    public List<PlcTag> Tags { get; set; } = new();
    public PlcIndustrialConfig IndustrialConfig { get; set; } = new();
}

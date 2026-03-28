using System;

namespace VisionInspectionApp.UI.Services.Plc;

public sealed class PlcException : Exception
{
    public PlcException(string message) : base(message) { }
    public PlcException(string message, Exception innerException) : base(message, innerException) { }
}


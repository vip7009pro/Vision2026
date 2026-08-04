namespace VisionInspectionApp.Models;

public enum PlcDriverType
{
    Mitsubishi = 0,             // MC Protocol 3E Binary (Ethernet TCP)
    MitsubishiMxComponent = 1,  // Mitsubishi MX Component (ActUtlType / Logical Station Number)
    Siemens = 2,
    Omron = 3,
    Modbus = 4,
    OpcUa = 5
}

public enum PlcConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Error = 3
}

public enum PlcDataType
{
    Bool = 0,
    Int16 = 1,
    UInt16 = 2,
    Int32 = 3,
    UInt32 = 4,
    Float = 5,
    Double = 6,
    String = 7
}

public enum TagQuality
{
    Good = 0,
    Bad = 1,
    Uncertain = 2
}

public enum PlcTriggerEdge
{
    RisingEdge = 0,
    FallingEdge = 1,
    Changed = 2
}

public enum PlcCompareOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    LessThan = 3,
    GreaterOrEqual = 4,
    LessOrEqual = 5
}

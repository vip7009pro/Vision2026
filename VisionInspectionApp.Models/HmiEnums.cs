namespace VisionInspectionApp.Models;

public enum HmiControlType
{
    Button,
    Lamp,
    Switch,
    Label,
    ValueDisplay,
    Conveyor,
    Cylinder,
    NumericDisplay,
    NumericInput,
    TextInput,
    CustomImage
}

public enum HmiTextAlignment
{
    Left,
    Center,
    Right
}

public enum HmiButtonBehavior
{
    Momentary, // Press = ON, Release = OFF
    Toggle,    // Click = Flip State
    SetTrue,   // Click = Set 1
    SetFalse   // Click = Set 0
}

public enum HmiColorTheme
{
    Green,
    Red,
    Blue,
    Amber,
    Yellow,
    Cyan,
    Purple,
    Orange,
    Magenta,
    White,
    IndustrialGray
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisionInspectionApp.Models;

public sealed class PlcTag : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _id = Guid.NewGuid().ToString();
    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    private string _plcId = string.Empty;
    public string PlcId
    {
        get => _plcId;
        set { if (_plcId != value) { _plcId = value; OnPropertyChanged(); } }
    }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    private string _address = string.Empty;
    public string Address
    {
        get => _address;
        set { if (_address != value) { _address = value; OnPropertyChanged(); } }
    }

    private PlcDataType _dataType = PlcDataType.Bool;
    public PlcDataType DataType
    {
        get => _dataType;
        set { if (_dataType != value) { _dataType = value; OnPropertyChanged(); } }
    }

    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    private bool _readOnly = false;
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly != value) { _readOnly = value; OnPropertyChanged(); } }
    }

    private object? _defaultValue;
    public object? DefaultValue
    {
        get => _defaultValue;
        set { if (_defaultValue != value) { _defaultValue = value; OnPropertyChanged(); } }
    }

    private double _scale = 1.0;
    public double Scale
    {
        get => _scale;
        set { if (_scale != value) { _scale = value; OnPropertyChanged(); } }
    }

    private string _unit = string.Empty;
    public string Unit
    {
        get => _unit;
        set { if (_unit != value) { _unit = value; OnPropertyChanged(); } }
    }

    private string _category = "General";
    public string Category
    {
        get => _category;
        set { if (_category != value) { _category = value; OnPropertyChanged(); } }
    }
}

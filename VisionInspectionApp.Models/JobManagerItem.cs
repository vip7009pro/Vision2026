using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisionInspectionApp.Models;

public class JobManagerItem : INotifyPropertyChanged
{
    private string _productCode = string.Empty;
    private string _productName = string.Empty;
    private string _jobFilePath = string.Empty;
    private string _teachImagePath = string.Empty;
    private string _updatedAt = string.Empty;
    private bool _hasJobFile;
    private bool _hasTeachImage;
    private string _statusMessage = string.Empty;

    public string ProductCode
    {
        get => _productCode;
        set => SetProperty(ref _productCode, value);
    }

    public string ProductName
    {
        get => _productName;
        set => SetProperty(ref _productName, value);
    }

    public string JobFilePath
    {
        get => _jobFilePath;
        set
        {
            if (SetProperty(ref _jobFilePath, value))
            {
                HasJobFile = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public string TeachImagePath
    {
        get => _teachImagePath;
        set
        {
            if (SetProperty(ref _teachImagePath, value))
            {
                HasTeachImage = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public string UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    public bool HasJobFile
    {
        get => _hasJobFile;
        set => SetProperty(ref _hasJobFile, value);
    }

    public bool HasTeachImage
    {
        get => _hasTeachImage;
        set => SetProperty(ref _hasTeachImage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

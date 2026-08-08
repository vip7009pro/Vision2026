using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.HMI;

namespace VisionInspectionApp.UI.ViewModels.HMI;

public partial class HmiControlViewModel : ObservableObject
{
    private readonly IPlcManagerService? _plcService;

    [ObservableProperty]
    private HmiControlModel _model;

    [ObservableProperty]
    private HmiScreenConfig? _parentScreenConfig;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isRunMode;

    [ObservableProperty]
    private bool _isOn;

    [ObservableProperty]
    private string _currentDisplayValue = "";

    [ObservableProperty]
    private ImageSource? _currentImageSource;

    public string EffectiveReadPlcId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model.ReadPlcId)) return Model.ReadPlcId;
            if (ParentScreenConfig != null && !string.IsNullOrWhiteSpace(ParentScreenConfig.TargetPlcId)) return ParentScreenConfig.TargetPlcId;
            return "PLC_01";
        }
    }

    public string EffectiveWritePlcId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model.WritePlcId)) return Model.WritePlcId;
            return EffectiveReadPlcId;
        }
    }

    public string EffectiveReadAddress => !string.IsNullOrWhiteSpace(Model.ReadTagName) ? Model.ReadTagName : "";
    public string EffectiveWriteAddress => Model.UseSeparateWriteAddress && !string.IsNullOrWhiteSpace(Model.WriteTagName)
        ? Model.WriteTagName
        : EffectiveReadAddress;

    public string Name
    {
        get => Model.Name;
        set { if (Model.Name != value) { Model.Name = value; NotifyModelChanged(); } }
    }

    public string LabelText
    {
        get => Model.LabelText;
        set { if (Model.LabelText != value) { Model.LabelText = value; NotifyModelChanged(); } }
    }

    public double FontSize
    {
        get => Model.FontSize;
        set { if (Math.Abs(Model.FontSize - value) > 0.01) { Model.FontSize = value; NotifyModelChanged(); } }
    }

    public double X
    {
        get => Model.X;
        set { if (Math.Abs(Model.X - value) > 0.01) { Model.X = value; NotifyModelChanged(); } }
    }

    public double Y
    {
        get => Model.Y;
        set { if (Math.Abs(Model.Y - value) > 0.01) { Model.Y = value; NotifyModelChanged(); } }
    }

    public double Width
    {
        get => Model.Width;
        set { if (Math.Abs(Model.Width - value) > 0.01) { Model.Width = value; NotifyModelChanged(); } }
    }

    public double Height
    {
        get => Model.Height;
        set { if (Math.Abs(Model.Height - value) > 0.01) { Model.Height = value; NotifyModelChanged(); } }
    }

    public HmiColorTheme Theme
    {
        get => Model.Theme;
        set { if (Model.Theme != value) { Model.Theme = value; UpdateVisualState(); NotifyModelChanged(); } }
    }

    public string ReadPlcId
    {
        get => Model.ReadPlcId;
        set { if (Model.ReadPlcId != value) { Model.ReadPlcId = value; NotifyModelChanged(); } }
    }

    public string ReadTagName
    {
        get => Model.ReadTagName;
        set { if (Model.ReadTagName != value) { Model.ReadTagName = value; NotifyModelChanged(); } }
    }

    public string WritePlcId
    {
        get => Model.WritePlcId;
        set { if (Model.WritePlcId != value) { Model.WritePlcId = value; NotifyModelChanged(); } }
    }

    public string WriteTagName
    {
        get => Model.WriteTagName;
        set { if (Model.WriteTagName != value) { Model.WriteTagName = value; NotifyModelChanged(); } }
    }

    public bool UseSeparateWriteAddress
    {
        get => Model.UseSeparateWriteAddress;
        set { if (Model.UseSeparateWriteAddress != value) { Model.UseSeparateWriteAddress = value; NotifyModelChanged(); } }
    }

    public HmiButtonBehavior ButtonBehavior
    {
        get => Model.ButtonBehavior;
        set { if (Model.ButtonBehavior != value) { Model.ButtonBehavior = value; NotifyModelChanged(); } }
    }

    public string WriteValueOn
    {
        get => Model.WriteValueOn;
        set { if (Model.WriteValueOn != value) { Model.WriteValueOn = value; NotifyModelChanged(); } }
    }

    public string WriteValueOff
    {
        get => Model.WriteValueOff;
        set { if (Model.WriteValueOff != value) { Model.WriteValueOff = value; NotifyModelChanged(); } }
    }

    public HmiControlViewModel(HmiControlModel model, IPlcManagerService? plcService = null, HmiScreenConfig? parentScreenConfig = null)
    {
        _model = model;
        _plcService = plcService;
        _parentScreenConfig = parentScreenConfig;
        _currentDisplayValue = model.DefaultText;

        UpdateVisualState();
        EnsureDirectAddressRegistered(EffectiveReadAddress);
    }

    public void EnsureDirectAddressRegistered(string address)
    {
        if (_plcService == null || string.IsNullOrWhiteSpace(address)) return;

        string plcId = EffectiveReadPlcId;
        var realPlc = _plcService.Plcs.FirstOrDefault(p =>
            string.Equals(p.Id, plcId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, plcId, StringComparison.OrdinalIgnoreCase)) ?? _plcService.Plcs.FirstOrDefault();

        string targetPlcId = realPlc != null ? realPlc.Id : (string.IsNullOrWhiteSpace(plcId) ? "PLC_01" : plcId);

        bool exists = _plcService.Tags.Any(t =>
            (string.Equals(t.PlcId, targetPlcId, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, plcId, StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(t.Name, address, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Address, address, StringComparison.OrdinalIgnoreCase)));

        if (!exists)
        {
            var tag = new PlcTag
            {
                PlcId = targetPlcId,
                Name = address,
                Address = address,
                DataType = Model.ValueDataType
            };
            _plcService.Tags.Add(tag);
        }
    }

    public PlcDataType ValueDataType
    {
        get => Model.ValueDataType;
        set { if (Model.ValueDataType != value) { Model.ValueDataType = value; NotifyModelChanged(); } }
    }

    public string ValueFormat
    {
        get => Model.ValueFormat;
        set { if (Model.ValueFormat != value) { Model.ValueFormat = value; NotifyModelChanged(); } }
    }

    public HmiTextAlignment Alignment
    {
        get => Model.Alignment;
        set { if (Model.Alignment != value) { Model.Alignment = value; NotifyModelChanged(); } }
    }

    public void OnPlcTagUpdated(string plcId, string tagName, object newValue)
    {
        string readAddr = EffectiveReadAddress;
        if (string.IsNullOrWhiteSpace(readAddr)) return;

        bool tagMatch = string.Equals(readAddr, tagName, StringComparison.OrdinalIgnoreCase);

        if (tagMatch)
        {
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                bool targetIsOn = false;
                string targetDisplay = "";

                switch (Model.ValueDataType)
                {
                    case PlcDataType.Bool:
                        if (newValue is bool b) targetIsOn = b;
                        else if (newValue is IConvertible conv)
                        {
                            try { targetIsOn = conv.ToDouble(null) != 0; } catch { }
                        }
                        else
                        {
                            string s = newValue?.ToString() ?? "";
                            targetIsOn = s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
                        }
                        targetDisplay = targetIsOn ? "TRUE" : "FALSE";
                        break;

                    case PlcDataType.Float:
                        try
                        {
                            double dVal = Convert.ToDouble(newValue);
                            targetIsOn = Math.Abs(dVal) > 0.0001;
                            string fmt = !string.IsNullOrWhiteSpace(Model.ValueFormat) ? Model.ValueFormat : "0.##";
                            targetDisplay = dVal.ToString(fmt);
                        }
                        catch
                        {
                            targetDisplay = newValue?.ToString() ?? "0";
                        }
                        break;

                    case PlcDataType.Int16:
                    case PlcDataType.UInt16:
                    case PlcDataType.Int32:
                        try
                        {
                            long iVal = Convert.ToInt64(newValue);
                            targetIsOn = iVal != 0;
                            targetDisplay = iVal.ToString();
                        }
                        catch
                        {
                            targetDisplay = newValue?.ToString() ?? "0";
                        }
                        break;

                    case PlcDataType.String:
                        targetDisplay = newValue?.ToString() ?? "";
                        targetIsOn = !string.IsNullOrWhiteSpace(targetDisplay);
                        break;

                    default:
                        targetDisplay = newValue?.ToString() ?? "";
                        targetIsOn = !string.IsNullOrWhiteSpace(targetDisplay) && targetDisplay != "0";
                        break;
                }

                if (IsOn != targetIsOn) IsOn = targetIsOn;
                if (CurrentDisplayValue != targetDisplay) CurrentDisplayValue = targetDisplay;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    public void UpdateVisualState()
    {
        if (Model.UseCustomImage)
        {
            string imgPath = IsOn ? Model.CustomImagePathOn : Model.CustomImagePathOff;
            if (!string.IsNullOrWhiteSpace(imgPath) && File.Exists(imgPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(imgPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    CurrentImageSource = bmp;
                    return;
                }
                catch
                {
                    // Fallback to vector drawing on image load failure
                }
            }
        }

        // Default Vector Asset Drawing
        CurrentImageSource = HmiVectorAssets.GetAssetDrawing(Model.Type, IsOn, Model.Theme);
    }

    partial void OnIsOnChanged(bool value)
    {
        UpdateVisualState();
    }

    public async Task HandleUserInteractionAsync()
    {
        if (!IsRunMode || _plcService == null) return;

        switch (Model.Type)
        {
            case HmiControlType.Button:
            case HmiControlType.Switch:
                await ExecuteButtonOrSwitchActionAsync();
                break;

            case HmiControlType.NumericInput:
                await PromptAndSetNumericValueAsync();
                break;

            case HmiControlType.TextInput:
                await PromptAndSetTextValueAsync();
                break;
        }
    }

    public async Task HandleMouseUpAsync()
    {
        if (!IsRunMode || _plcService == null) return;

        // If Momentary button: release turns it OFF
        if ((Model.Type == HmiControlType.Button || Model.Type == HmiControlType.Switch) &&
            Model.ButtonBehavior == HmiButtonBehavior.Momentary)
        {
            IsOn = false;
            object writeVal = ParseWriteValue(!string.IsNullOrWhiteSpace(Model.WriteValueOff) ? Model.WriteValueOff : "False");
            string writeAddr = EffectiveWriteAddress;
            if (!string.IsNullOrWhiteSpace(writeAddr))
            {
                EnsureDirectAddressRegistered(writeAddr);
                await _plcService.WriteTagValueAsync(EffectiveWritePlcId, writeAddr, writeVal);
            }
        }
    }

    private object ParseWriteValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return false;
        string s = rawValue.Trim();
        if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || s == "0") return false;
        if (int.TryParse(s, out int intVal)) return intVal;
        if (double.TryParse(s, out double dblVal)) return dblVal;
        return rawValue;
    }

    private async Task ExecuteButtonOrSwitchActionAsync()
    {
        if (_plcService == null) return;
        string writeAddr = EffectiveWriteAddress;
        if (string.IsNullOrWhiteSpace(writeAddr)) return;

        EnsureDirectAddressRegistered(writeAddr);

        switch (Model.ButtonBehavior)
        {
            case HmiButtonBehavior.Momentary:
                IsOn = true;
                object valOn = ParseWriteValue(!string.IsNullOrWhiteSpace(Model.WriteValueOn) ? Model.WriteValueOn : "True");
                await _plcService.WriteTagValueAsync(EffectiveWritePlcId, writeAddr, valOn);
                break;

            case HmiButtonBehavior.Toggle:
                IsOn = !IsOn;
                object targetVal = IsOn
                    ? ParseWriteValue(!string.IsNullOrWhiteSpace(Model.WriteValueOn) ? Model.WriteValueOn : "True")
                    : ParseWriteValue(!string.IsNullOrWhiteSpace(Model.WriteValueOff) ? Model.WriteValueOff : "False");

                await _plcService.WriteTagValueAsync(EffectiveWritePlcId, writeAddr, targetVal);
                break;

            case HmiButtonBehavior.SetTrue:
                IsOn = true;
                await _plcService.WriteTagValueAsync(EffectiveWritePlcId, writeAddr, true);
                break;

            case HmiButtonBehavior.SetFalse:
                IsOn = false;
                await _plcService.WriteTagValueAsync(EffectiveWritePlcId, writeAddr, false);
                break;
        }
    }

    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model.LabelText))
                return Model.LabelText;

            string addr = EffectiveReadAddress;
            if (!string.IsNullOrWhiteSpace(addr))
                return $"{Model.Name} [{addr}]";

            return Model.Name;
        }
    }

    public void NotifyModelChanged()
    {
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(EffectiveReadPlcId));
        OnPropertyChanged(nameof(EffectiveWritePlcId));
        OnPropertyChanged(nameof(EffectiveReadAddress));
        OnPropertyChanged(nameof(EffectiveWriteAddress));
        EnsureDirectAddressRegistered(EffectiveReadAddress);
    }

    private async Task PromptAndSetNumericValueAsync()
    {
        if (_plcService == null) return;

        string title = $"Nhập giá trị số cho '{Model.Name}'";
        string prompt = $"Nhập giá trị (Từ {Model.MinValue} đến {Model.MaxValue}):";
        string defaultValue = CurrentDisplayValue;

        string resultStr = Microsoft.VisualBasic.Interaction.InputBox(prompt, title, defaultValue);
        if (double.TryParse(resultStr, out double numVal))
        {
            if (numVal >= Model.MinValue && numVal <= Model.MaxValue)
            {
                CurrentDisplayValue = numVal.ToString();
                string writeAddr = EffectiveWriteAddress;
                if (!string.IsNullOrWhiteSpace(writeAddr))
                {
                    EnsureDirectAddressRegistered(writeAddr);
                    await _plcService.WriteTagValueAsync(EffectiveWritePlcId, writeAddr, numVal);
                }
            }
        }
    }

    private async Task PromptAndSetTextValueAsync()
    {
        if (_plcService == null) return;

        string title = $"Nhập chuỗi ký tự cho '{Model.Name}'";
        string prompt = "Nhập văn bản:";
        string defaultValue = CurrentDisplayValue;

        string resultStr = Microsoft.VisualBasic.Interaction.InputBox(prompt, title, defaultValue);
        if (resultStr != null)
        {
            CurrentDisplayValue = resultStr;
            string writeAddr = EffectiveWriteAddress;
            if (!string.IsNullOrWhiteSpace(writeAddr))
            {
                EnsureDirectAddressRegistered(writeAddr);
                await _plcService.WriteTagValueAsync(EffectiveWritePlcId, writeAddr, resultStr);
            }
        }
    }
}

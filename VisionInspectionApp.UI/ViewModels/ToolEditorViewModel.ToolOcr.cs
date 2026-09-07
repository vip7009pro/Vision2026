using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel : ObservableObject
{
    private OcrDefinition? SelectedOcrDef()
    {
        if (_config is null || SelectedNode is null)
            return null;
        if (!string.Equals(SelectedNode.Type, "OCR", StringComparison.OrdinalIgnoreCase))
            return null;
        return _config.Ocrs.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<OcrEngineMode> OcrEngineModes { get; } = new[]
    {
        OcrEngineMode.HeuristicFast,
        OcrEngineMode.DeepLearningOnnx,
        OcrEngineMode.HybridAuto
    };

    public IReadOnlyList<OcrMatchingMode> OcrMatchingModes { get; } = new[]
    {
        OcrMatchingMode.AnyText,
        OcrMatchingMode.ExactMatch,
        OcrMatchingMode.RegexPattern,
        OcrMatchingMode.Contains
    };

    public IReadOnlyList<OcrBinarizeMethod> OcrBinarizeMethods { get; } = new[]
    {
        OcrBinarizeMethod.Otsu,
        OcrBinarizeMethod.Sauvola,
        OcrBinarizeMethod.AdaptiveMean,
        OcrBinarizeMethod.AdaptiveGaussian
    };

    public OcrEngineMode Ocr_EngineMode
    {
        get => SelectedOcrDef()?.EngineMode ?? OcrEngineMode.HeuristicFast;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.EngineMode == value) return;
            d.EngineMode = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public OcrMatchingMode Ocr_MatchingMode
    {
        get => SelectedOcrDef()?.MatchingMode ?? OcrMatchingMode.AnyText;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.MatchingMode == value) return;
            d.MatchingMode = value;
            if (value == OcrMatchingMode.AnyText && (!d.WhitelistChars.Any(char.IsLower) || d.WhitelistChars.Trim() == "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-/. :"))
            {
                d.WhitelistChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-/. :";
                OnPropertyChanged(nameof(Ocr_WhitelistChars));
            }
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public OcrBinarizeMethod Ocr_BinarizeMethod
    {
        get => SelectedOcrDef()?.BinarizeMethod ?? OcrBinarizeMethod.Sauvola;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.BinarizeMethod == value) return;
            d.BinarizeMethod = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string Ocr_ExpectedText
    {
        get => SelectedOcrDef()?.ExpectedText ?? string.Empty;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.ExpectedText == value) return;
            d.ExpectedText = value ?? string.Empty;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string Ocr_RegexPattern
    {
        get => SelectedOcrDef()?.RegexPattern ?? string.Empty;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.RegexPattern == value) return;
            d.RegexPattern = value ?? string.Empty;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string Ocr_WhitelistChars
    {
        get => SelectedOcrDef()?.WhitelistChars ?? string.Empty;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.WhitelistChars == value) return;
            d.WhitelistChars = value ?? string.Empty;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public double Ocr_MinConfidence
    {
        get => SelectedOcrDef()?.MinConfidence ?? 0.5;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || Math.Abs(d.MinConfidence - value) < 0.001) return;
            d.MinConfidence = Math.Clamp(value, 0.0, 1.0);
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public bool Ocr_EnableDotMatrix
    {
        get => SelectedOcrDef()?.EnableDotMatrixConnector ?? false;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.EnableDotMatrixConnector == value) return;
            d.EnableDotMatrixConnector = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Ocr_DotMatrixKernelSize
    {
        get => SelectedOcrDef()?.DotMatrixKernelSize ?? 3;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.DotMatrixKernelSize == value) return;
            d.DotMatrixKernelSize = Math.Clamp(value, 2, 15);
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public bool Ocr_InvertPolarity
    {
        get => SelectedOcrDef()?.InvertPolarity ?? false;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.InvertPolarity == value) return;
            d.InvertPolarity = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Ocr_MinCharArea
    {
        get => SelectedOcrDef()?.MinCharArea ?? 20;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.MinCharArea == value) return;
            d.MinCharArea = Math.Max(1, value);
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Ocr_MaxCharArea
    {
        get => SelectedOcrDef()?.MaxCharArea ?? 50000;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.MaxCharArea == value) return;
            d.MaxCharArea = Math.Max(10, value);
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Ocr_CharSpacingThreshold
    {
        get => SelectedOcrDef()?.CharSpacingThreshold ?? 15;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.CharSpacingThreshold == value) return;
            d.CharSpacingThreshold = Math.Max(1, value);
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string Ocr_OnnxModelPath
    {
        get => SelectedOcrDef()?.OnnxModelPath ?? string.Empty;
        set
        {
            var d = SelectedOcrDef();
            if (d is null || d.OnnxModelPath == value) return;
            d.OnnxModelPath = value ?? string.Empty;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    private ICommand? _browseOcrModelCommand;
    public ICommand BrowseOcrModelCommand => _browseOcrModelCommand ??= new RelayCommand(() =>
    {
        var ofd = new OpenFileDialog
        {
            Title = "Chọn Model OCR ONNX (*.onnx)",
            Filter = "ONNX Model (*.onnx)|*.onnx|All Files (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            Ocr_OnnxModelPath = ofd.FileName;
        }
    });

    private ICommand? _ocrSetFullWhitelistCommand;
    public ICommand OcrSetFullWhitelistCommand => _ocrSetFullWhitelistCommand ??= new RelayCommand(() =>
    {
        Ocr_WhitelistChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-/. :";
    });

    private ICommand? _ocrSetFromExpectedTextCommand;
    public ICommand OcrSetFromExpectedTextCommand => _ocrSetFromExpectedTextCommand ??= new RelayCommand(() =>
    {
        var exp = Ocr_ExpectedText;
        if (!string.IsNullOrWhiteSpace(exp))
        {
            var chars = new HashSet<char>("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-/. :");
            foreach (var c in exp) chars.Add(c);
            Ocr_WhitelistChars = new string(chars.ToArray());
        }
    });

    public string Ocr_TrainedCharactersSummary
    {
        get
        {
            var d = SelectedOcrDef();
            if (d == null || d.TrainedCharacters == null || d.TrainedCharacters.Count == 0)
                return "Chưa học ký tự nào";

            var distinct = d.TrainedCharacters.Select(c => c.Character).Distinct().ToList();
            return $"Đã học {d.TrainedCharacters.Count} ký tự mẫu: {string.Join(", ", distinct)}";
        }
    }

    private ICommand? _ocrTeachCharactersCommand;
    public ICommand OcrTeachCharactersCommand => _ocrTeachCharactersCommand ??= new RelayCommand(() =>
    {
        var d = SelectedOcrDef();
        if (d == null) return;

        using var snap = GetCurrentWorkingImageSnapshot();
        if (snap.Empty())
        {
            System.Windows.MessageBox.Show("Không có ảnh làm việc để học ký tự.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        GetOriginPose(out var originTeach, out var originFound, out var originAngleDeg);

        using var inputMat = SelectedNode != null ? ResolveToolImageForPreview(snap, SelectedNode) : snap.Clone();

        var trained = VisionInspectionApp.VisionEngine.OcrDetector.TeachCharacters(
            inputMat,
            d,
            labelSequence: d.ExpectedText,
            originTeach: originTeach,
            originFound: originFound,
            originAngleDeg: originAngleDeg);

        if (trained.Count == 0)
        {
            System.Windows.MessageBox.Show("Không tìm thấy ký tự hợp lệ trong Search ROI để học mẫu.\nVui lòng kiểm tra lại Search ROI, Phương pháp nhị phân hoặc Chuỗi mẫu.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        d.TrainedCharacters = trained;
        OnPropertyChanged(nameof(Ocr_TrainedCharactersSummary));
        RefreshPreviews();
        RequestAutoSave();

        System.Windows.MessageBox.Show($"Đã học thành công {trained.Count} ký tự mẫu: {string.Join(", ", trained.Select(x => x.Character))}\nĐã lưu vào thư viện ký tự của Job.", "Thành công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    });

    private ICommand? _ocrClearTrainedCharactersCommand;
    public ICommand OcrClearTrainedCharactersCommand => _ocrClearTrainedCharactersCommand ??= new RelayCommand(() =>
    {
        var d = SelectedOcrDef();
        if (d == null || d.TrainedCharacters == null || d.TrainedCharacters.Count == 0) return;

        d.TrainedCharacters.Clear();
        OnPropertyChanged(nameof(Ocr_TrainedCharactersSummary));
        RefreshPreviews();
        RequestAutoSave();
    });

    private ICommand? _openOcrModelFolderCommand;
    public ICommand OpenOcrModelFolderCommand => _openOcrModelFolderCommand ??= new RelayCommand(() =>
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelDir = Path.Combine(baseDir, "models", "ocr");
            if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = modelDir,
                UseShellExecute = true
            });
        }
        catch { }
    });

    public string? Ocr_LastRunMessage
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;
            var d = _lastRun.Ocrs?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            return d?.ErrorReason ?? d?.Message;
        }
    }

    public double? Ocr_LastRunConfidence
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;
            var d = _lastRun.Ocrs?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            return d?.Confidence;
        }
    }

    private void RaiseOcrPropertiesChanged()
    {
        OnPropertyChanged(nameof(Ocr_EngineMode));
        OnPropertyChanged(nameof(Ocr_MatchingMode));
        OnPropertyChanged(nameof(Ocr_BinarizeMethod));
        OnPropertyChanged(nameof(Ocr_ExpectedText));
        OnPropertyChanged(nameof(Ocr_RegexPattern));
        OnPropertyChanged(nameof(Ocr_WhitelistChars));
        OnPropertyChanged(nameof(Ocr_MinConfidence));
        OnPropertyChanged(nameof(Ocr_EnableDotMatrix));
        OnPropertyChanged(nameof(Ocr_DotMatrixKernelSize));
        OnPropertyChanged(nameof(Ocr_InvertPolarity));
        OnPropertyChanged(nameof(Ocr_MinCharArea));
        OnPropertyChanged(nameof(Ocr_MaxCharArea));
        OnPropertyChanged(nameof(Ocr_CharSpacingThreshold));
        OnPropertyChanged(nameof(Ocr_OnnxModelPath));
        OnPropertyChanged(nameof(Ocr_TrainedCharactersSummary));
        OnPropertyChanged(nameof(Ocr_LastRunMessage));
        OnPropertyChanged(nameof(Ocr_LastRunConfidence));
        OnPropertyChanged(nameof(SelectedRunText));
        OnPropertyChanged(nameof(SelectedRunPass));
    }
}

using System;
using System.Windows;
using VisionInspectionApp.UI;

namespace VisionInspectionApp.UI.Services;

public class ThemeService
{
    private readonly GlobalAppSettingsService _settingsService;

    public ThemeService(GlobalAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void ApplyTheme()
    {
        var isDarkMode = _settingsService.Settings.IsDarkMode;
        ApplyTheme(isDarkMode);
    }

    public void ApplyTheme(bool isDarkMode)
    {
        var dict = new ResourceDictionary();
        dict.Source = isDarkMode
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        
        // Remove existing theme
        ResourceDictionary? existingTheme = null;
        foreach (var md in merged)
        {
            if (md.Source != null && md.Source.OriginalString.Contains("Theme.xaml"))
            {
                existingTheme = md;
                break;
            }
        }

        if (existingTheme != null)
        {
            merged.Remove(existingTheme);
        }

        merged.Add(dict);

        // Update settings if changed
        if (_settingsService.Settings.IsDarkMode != isDarkMode)
        {
            _settingsService.Settings.IsDarkMode = isDarkMode;
            _settingsService.Save();
        }
    }
}

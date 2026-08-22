using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VisionInspectionApp.UI;

namespace VisionInspectionApp.UI.Services;

public sealed class ThemeOption
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Group { get; init; } = "";
    public string ResourcePath { get; init; } = "";
    public string PrimaryColorHex { get; init; } = "";
    public string AccentColorHex { get; init; } = "";
    public bool IsDark { get; init; } = false;
}

public class ThemeService
{
    public static readonly IReadOnlyList<ThemeOption> AvailableThemes = new List<ThemeOption>
    {
        // ☀️ Nhóm Tươi Sáng & Năng Động (Vibrant Bright Themes)
        new() { Id = "SunriseCoral", Name = "🌅 Sunrise Coral", Description = "Cam Đào San Hô & Vàng Nắng Rực Rỡ", Group = "☀️ Tone Tươi Sáng & Năng Động", ResourcePath = "Themes/SunriseCoralTheme.xaml", PrimaryColorHex = "#FF5E62", AccentColorHex = "#EA580C", IsDark = false },
        new() { Id = "OceanBreeze", Name = "🌊 Ocean Breeze", Description = "Sóng Biển Xanh Ngọc & Biển Sâu", Group = "☀️ Tone Tươi Sáng & Năng Động", ResourcePath = "Themes/OceanBreezeTheme.xaml", PrimaryColorHex = "#00B4DB", AccentColorHex = "#0284C7", IsDark = false },
        new() { Id = "FreshMint", Name = "🌿 Fresh Mint", Description = "Bạc Hà Tươi Mát & Xanh Chuối Non", Group = "☀️ Tone Tươi Sáng & Năng Động", ResourcePath = "Themes/FreshMintTheme.xaml", PrimaryColorHex = "#11998E", AccentColorHex = "#059669", IsDark = false },
        new() { Id = "CherryBlossom", Name = "🌸 Cherry Blossom", Description = "Hoa Anh Đào Hồng Pastel Tươi Trẻ", Group = "☀️ Tone Tươi Sáng & Năng Động", ResourcePath = "Themes/CherryBlossomTheme.xaml", PrimaryColorHex = "#FF758C", AccentColorHex = "#E11D48", IsDark = false },
        new() { Id = "ElectricNeon", Name = "⚡ Electric Neon", Description = "Vàng Hoàng Kim Năng Lượng Cyber", Group = "☀️ Tone Tươi Sáng & Năng Động", ResourcePath = "Themes/ElectricNeonTheme.xaml", PrimaryColorHex = "#F7971E", AccentColorHex = "#D97706", IsDark = false },
        new() { Id = "TitaniumLight", Name = "❄️ Titanium Light", Description = "Bạch Kim Sáng Thanh Lịch Công Nghiệp", Group = "☀️ Tone Tươi Sáng & Năng Động", ResourcePath = "Themes/LightTheme.xaml", PrimaryColorHex = "#E8ECF2", AccentColorHex = "#2563EB", IsDark = false },

        // 🌙 Nhóm Tối Đậm Sắc Màu & Sâu Lắng (Rich Dark Themes)
        new() { Id = "MidnightBlue", Name = "🌌 Midnight Blue", Description = "Xanh Biển Sâu Công Nghệ Cao", Group = "🌙 Tone Tối Công Nghệ & Sâu Lắng", ResourcePath = "Themes/MidnightBlueTheme.xaml", PrimaryColorHex = "#0B192C", AccentColorHex = "#0284C7", IsDark = true },
        new() { Id = "CyberEmerald", Name = "🌿 Cyber Emerald", Description = "Xanh Ngọc Lục Bảo Matrix", Group = "🌙 Tone Tối Công Nghệ & Sâu Lắng", ResourcePath = "Themes/CyberEmeraldTheme.xaml", PrimaryColorHex = "#0A1C16", AccentColorHex = "#10B981", IsDark = true },
        new() { Id = "AmethystViolet", Name = "🔮 Amethyst Violet", Description = "Tím Thạch Anh Cyberpunk Huyền Bí", Group = "🌙 Tone Tối Công Nghệ & Sâu Lắng", ResourcePath = "Themes/AmethystVioletTheme.xaml", PrimaryColorHex = "#1E0E2B", AccentColorHex = "#A855F7", IsDark = true },
        new() { Id = "SolarAmber", Name = "🌅 Solar Amber", Description = "Cam Than Ánh Kim Nổi Bật", Group = "🌙 Tone Tối Công Nghệ & Sâu Lắng", ResourcePath = "Themes/SolarAmberTheme.xaml", PrimaryColorHex = "#211406", AccentColorHex = "#F59E0B", IsDark = true },
        new() { Id = "DarkObsidian", Name = "🖤 Dark Obsidian", Description = "Đen Than Cổ Điển Tối Giản", Group = "🌙 Tone Tối Công Nghệ & Sâu Lắng", ResourcePath = "Themes/DarkTheme.xaml", PrimaryColorHex = "#141418", AccentColorHex = "#007ACC", IsDark = true }
    };

    private readonly GlobalAppSettingsService _settingsService;

    public ThemeService(GlobalAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string CurrentThemeId => _settingsService.Settings.ThemeId ?? "MidnightBlue";

    public void ApplyTheme()
    {
        var themeId = _settingsService.Settings.ThemeId;
        if (string.IsNullOrWhiteSpace(themeId))
        {
            themeId = _settingsService.Settings.IsDarkMode ? "DarkObsidian" : "TitaniumLight";
        }
        ApplyTheme(themeId);
    }

    public void ApplyTheme(bool isDarkMode)
    {
        ApplyTheme(isDarkMode ? "DarkObsidian" : "TitaniumLight");
    }

    public void ApplyTheme(string themeId)
    {
        var opt = AvailableThemes.FirstOrDefault(x => string.Equals(x.Id, themeId, StringComparison.OrdinalIgnoreCase))
                  ?? AvailableThemes[0];

        var dict = new ResourceDictionary { Source = new Uri(opt.ResourcePath, UriKind.Relative) };
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
        _settingsService.Settings.ThemeId = opt.Id;
        _settingsService.Settings.IsDarkMode = opt.IsDark;
        _settingsService.Save();
    }

    public void ToggleThemeNext()
    {
        var currentId = CurrentThemeId;
        int idx = 0;
        for (int i = 0; i < AvailableThemes.Count; i++)
        {
            if (string.Equals(AvailableThemes[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }
        var nextIdx = (idx + 1) % AvailableThemes.Count;
        ApplyTheme(AvailableThemes[nextIdx].Id);
    }
}

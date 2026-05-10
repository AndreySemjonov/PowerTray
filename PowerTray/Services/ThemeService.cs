using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PowerTray.Models;
using WpfApplication = System.Windows.Application;
using MediaColor = System.Windows.Media.Color;

namespace PowerTray.Services;

public static class ThemeService
{
    public static void Apply(AppTheme theme)
    {
        bool useLight = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => IsWindowsLightTheme()
        };

        Apply(useLight ? Palette.Light : Palette.Dark);
    }

    private static void Apply(Palette palette)
    {
        SetBrush("WindowBg", palette.WindowBg);
        SetBrush("TopBarBg", palette.TopBarBg);
        SetBrush("PanelBg", palette.PanelBg);
        SetBrush("PanelBorder", palette.PanelBorder);
        SetBrush("TextBrush", palette.Text);
        SetBrush("MutedTextBrush", palette.MutedText);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("BatteryAccentBrush", palette.BatteryAccent);
        SetBrush("ThermalAccentBrush", palette.ThermalAccent);
        SetBrush("GoodAccentBrush", palette.GoodAccent);
        SetBrush("ControlBg", palette.ControlBg);
        SetBrush("ControlHover", palette.ControlHover);
        SetBrush("ControlPressed", palette.ControlPressed);
        SetBrush("InputBg", palette.InputBg);
        SetBrush("CheckboxBg", palette.CheckboxBg);
        SetBrush("PopupBg", palette.PopupBg);
        SetBrush("SelectionBrush", palette.Selection);
        SetBrush("HighlightBg", palette.HighlightBg);
        SetBrush("HighlightBorder", palette.HighlightBorder);
        SetBrush("SelectedBg", palette.SelectedBg);
        SetBrush("ProgressTrack", palette.ProgressTrack);
        SetBrush("TooltipBg", palette.TooltipBg);
        SetBrush("ContextMenuBg", palette.ContextMenuBg);
        SetBrush("TitleButtonHover", palette.TitleButtonHover);
        SetBrush("TitleButtonPressed", palette.TitleButtonPressed);
        SetBrush("TitleCloseHover", palette.TitleCloseHover);
        SetBrush("TitleClosePressed", palette.TitleClosePressed);
        SetBrush("ChipBg", palette.ChipBg);
        SetBrush("DashboardChipBg", palette.DashboardChipBg);
        SetBrush("TabHoverBg", palette.TabHoverBg);
        SetBrush("MiniBarBg", palette.MiniBarBg);
        SetBrush("MiniBarFg", palette.MiniBarFg);
        SetBrush("MetricCardBg", palette.MetricCardBg);
        SetBrush("InsetPanelBg", palette.InsetPanelBg);
        SetBrush("ButtonPanelBg", palette.ButtonPanelBg);
        SetBrush("GpuAccentBrush", palette.GpuAccent);
        SetPanelGlow(palette.PanelGlowStart, palette.PanelGlowEnd);
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int intValue && intValue > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetBrush(string key, MediaColor color)
    {
        if (WpfApplication.Current.TryFindResource(key) is SolidColorBrush brush)
        {
            if (brush.IsFrozen)
            {
                brush = brush.CloneCurrentValue();
                ReplaceResource(key, brush);
            }

            brush.Color = color;
        }
    }

    private static void SetPanelGlow(MediaColor start, MediaColor end)
    {
        if (WpfApplication.Current.TryFindResource("PanelGlow") is not LinearGradientBrush brush || brush.GradientStops.Count < 2)
        {
            return;
        }

        if (brush.IsFrozen)
        {
            brush = brush.CloneCurrentValue();
            ReplaceResource("PanelGlow", brush);
        }

        brush.GradientStops[0].Color = start;
        brush.GradientStops[1].Color = end;
    }

    private static void ReplaceResource(string key, object value)
    {
        ResourceDictionary? dictionary = FindResourceDictionary(WpfApplication.Current.Resources, key);
        if (dictionary is not null)
        {
            dictionary[key] = value;
        }
    }

    private static ResourceDictionary? FindResourceDictionary(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
        {
            return dictionary;
        }

        foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
        {
            ResourceDictionary? match = FindResourceDictionary(mergedDictionary, key);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private sealed record Palette(
        MediaColor WindowBg,
        MediaColor TopBarBg,
        MediaColor PanelBg,
        MediaColor PanelBorder,
        MediaColor Text,
        MediaColor MutedText,
        MediaColor Accent,
        MediaColor BatteryAccent,
        MediaColor ThermalAccent,
        MediaColor GoodAccent,
        MediaColor ControlBg,
        MediaColor ControlHover,
        MediaColor ControlPressed,
        MediaColor InputBg,
        MediaColor CheckboxBg,
        MediaColor PopupBg,
        MediaColor Selection,
        MediaColor HighlightBg,
        MediaColor HighlightBorder,
        MediaColor SelectedBg,
        MediaColor ProgressTrack,
        MediaColor TooltipBg,
        MediaColor ContextMenuBg,
        MediaColor TitleButtonHover,
        MediaColor TitleButtonPressed,
        MediaColor TitleCloseHover,
        MediaColor TitleClosePressed,
        MediaColor ChipBg,
        MediaColor DashboardChipBg,
        MediaColor TabHoverBg,
        MediaColor MiniBarBg,
        MediaColor MiniBarFg,
        MediaColor MetricCardBg,
        MediaColor InsetPanelBg,
        MediaColor ButtonPanelBg,
        MediaColor GpuAccent,
        MediaColor PanelGlowStart,
        MediaColor PanelGlowEnd)
    {
        public static Palette Dark { get; } = new(
            MediaColor.FromRgb(16, 18, 20),
            MediaColor.FromRgb(24, 26, 29),
            MediaColor.FromRgb(27, 30, 33),
            MediaColor.FromRgb(52, 56, 61),
            MediaColor.FromRgb(242, 244, 247),
            MediaColor.FromRgb(184, 189, 196),
            MediaColor.FromRgb(74, 168, 255),
            MediaColor.FromRgb(245, 170, 45),
            MediaColor.FromRgb(255, 111, 135),
            MediaColor.FromRgb(110, 225, 109),
            MediaColor.FromRgb(42, 44, 49),
            MediaColor.FromRgb(54, 57, 64),
            MediaColor.FromRgb(32, 35, 40),
            MediaColor.FromRgb(17, 22, 27),
            MediaColor.FromRgb(18, 23, 28),
            MediaColor.FromRgb(29, 34, 40),
            MediaColor.FromArgb(51, 88, 166, 255),
            MediaColor.FromRgb(38, 49, 58),
            MediaColor.FromRgb(59, 107, 134),
            MediaColor.FromRgb(31, 66, 84),
            MediaColor.FromRgb(17, 19, 22),
            MediaColor.FromRgb(32, 37, 43),
            MediaColor.FromRgb(28, 30, 34),
            MediaColor.FromRgb(51, 54, 59),
            MediaColor.FromRgb(70, 74, 81),
            MediaColor.FromRgb(196, 43, 28),
            MediaColor.FromRgb(164, 38, 26),
            MediaColor.FromRgb(31, 35, 40),
            MediaColor.FromRgb(36, 40, 46),
            MediaColor.FromRgb(20, 28, 35),
            MediaColor.FromRgb(37, 42, 47),
            MediaColor.FromRgb(61, 123, 193),
            MediaColor.FromRgb(32, 37, 43),
            MediaColor.FromRgb(29, 34, 40),
            MediaColor.FromRgb(24, 32, 39),
            MediaColor.FromRgb(84, 214, 198),
            MediaColor.FromRgb(32, 38, 43),
            MediaColor.FromRgb(26, 28, 31));

        public static Palette Light { get; } = new(
            MediaColor.FromRgb(245, 247, 250),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromRgb(211, 218, 226),
            MediaColor.FromRgb(30, 37, 45),
            MediaColor.FromRgb(93, 103, 114),
            MediaColor.FromRgb(14, 113, 202),
            MediaColor.FromRgb(183, 101, 0),
            MediaColor.FromRgb(209, 55, 83),
            MediaColor.FromRgb(22, 132, 59),
            MediaColor.FromRgb(241, 244, 248),
            MediaColor.FromRgb(227, 234, 242),
            MediaColor.FromRgb(215, 224, 234),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromArgb(80, 102, 172, 232),
            MediaColor.FromRgb(232, 244, 255),
            MediaColor.FromRgb(151, 191, 229),
            MediaColor.FromRgb(213, 235, 255),
            MediaColor.FromRgb(230, 235, 241),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromRgb(232, 238, 245),
            MediaColor.FromRgb(219, 228, 238),
            MediaColor.FromRgb(196, 43, 28),
            MediaColor.FromRgb(164, 38, 26),
            MediaColor.FromRgb(238, 242, 247),
            MediaColor.FromRgb(238, 242, 247),
            MediaColor.FromRgb(235, 245, 255),
            MediaColor.FromRgb(226, 232, 239),
            MediaColor.FromRgb(38, 119, 199),
            MediaColor.FromRgb(247, 249, 252),
            MediaColor.FromRgb(247, 249, 252),
            MediaColor.FromRgb(239, 245, 251),
            MediaColor.FromRgb(22, 137, 125),
            MediaColor.FromRgb(255, 255, 255),
            MediaColor.FromRgb(246, 248, 251));
    }
}

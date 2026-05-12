using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerTray.Services;

public static class WindowsThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";
    private const string SystemUsesLightThemeValue = "SystemUsesLightTheme";
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;
    private static readonly nint HwndBroadcast = new(0xffff);

    public static bool IsLightMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
            object? value = key?.GetValue(AppsUseLightThemeValue);
            return value is int intValue && intValue > 0;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to read Windows theme mode.");
            return false;
        }
    }

    public static void ToggleLightDarkMode() => SetLightMode(!IsLightMode());

    public static void SetLightMode(bool useLightMode)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKeyPath);
            int value = useLightMode ? 1 : 0;
            key.SetValue(AppsUseLightThemeValue, value, RegistryValueKind.DWord);
            key.SetValue(SystemUsesLightThemeValue, value, RegistryValueKind.DWord);
            BroadcastThemeChanged();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to change Windows theme mode.");
        }
    }

    private static void BroadcastThemeChanged()
    {
        SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            nint.Zero,
            "ImmersiveColorSet",
            SmtoAbortIfHung,
            1000,
            out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint hWnd,
        int msg,
        nint wParam,
        string lParam,
        int flags,
        int timeout,
        out nint result);
}

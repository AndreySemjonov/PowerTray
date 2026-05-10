using Microsoft.Win32;
using System.IO;
using System.Reflection;

namespace PowerTray.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string AppName = "PowerTray";
    private const string AppExecutableName = "PowerTray.exe";
    private const string LegacyAppName = "XPSBatteryTray";
    private static readonly byte[] StartupApprovedEnabled = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public bool IsEnabled()
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        bool hasStartupCommand = runKey?.GetValue(AppName) is string value && value.Length > 0
            || runKey?.GetValue(LegacyAppName) is string legacyValue && legacyValue.Length > 0;

        return hasStartupCommand && !IsStartupApprovedDisabled();
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            key.SetValue(AppName, BuildStartupCommand());
            key.DeleteValue(LegacyAppName, false);
            SetStartupApprovedEnabled();
        }
        else
        {
            key.DeleteValue(AppName, false);
            key.DeleteValue(LegacyAppName, false);
            DeleteStartupApprovedValues();
        }
    }

    public void RefreshEnabledRegistration()
    {
        if (IsEnabled())
        {
            SetEnabled(enabled: true);
        }
    }

    private static bool IsStartupApprovedDisabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, false);
        return IsDisabledValue(key?.GetValue(AppName)) || IsDisabledValue(key?.GetValue(LegacyAppName));
    }

    private static bool IsDisabledValue(object? value) =>
        value is byte[] bytes && bytes.Length > 0 && bytes[0] == 3;

    private static void SetStartupApprovedEnabled()
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, true)
                                ?? Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath, true);

        key.SetValue(AppName, StartupApprovedEnabled, RegistryValueKind.Binary);
        key.DeleteValue(LegacyAppName, false);
    }

    private static void DeleteStartupApprovedValues()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, true);
        key?.DeleteValue(AppName, false);
        key?.DeleteValue(LegacyAppName, false);
    }

    private static string BuildStartupCommand()
    {
        string executable = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        string assemblyPath = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
        if (Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(assemblyPath))
        {
            string appHostPath = Path.Combine(Path.GetDirectoryName(assemblyPath) ?? string.Empty, AppExecutableName);
            if (File.Exists(appHostPath))
            {
                return $"\"{appHostPath}\" --minimized";
            }

            return $"\"{executable}\" \"{assemblyPath}\" --minimized";
        }

        return $"\"{executable}\" --minimized";
    }
}

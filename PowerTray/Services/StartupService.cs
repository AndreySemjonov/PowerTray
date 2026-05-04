using Microsoft.Win32;
using System.IO;
using System.Reflection;

namespace XPSBatteryTray.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PowerTray";
    private const string LegacyAppName = "XPSBatteryTray";

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(AppName) is string value && value.Length > 0
            || key?.GetValue(LegacyAppName) is string legacyValue && legacyValue.Length > 0;
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            key.SetValue(AppName, BuildStartupCommand());
            key.DeleteValue(LegacyAppName, false);
        }
        else
        {
            key.DeleteValue(AppName, false);
            key.DeleteValue(LegacyAppName, false);
        }
    }

    private static string BuildStartupCommand()
    {
        string executable = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        string assemblyPath = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
        if (Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(assemblyPath))
        {
            return $"\"{executable}\" \"{assemblyPath}\" --minimized";
        }

        return $"\"{executable}\" --minimized";
    }
}

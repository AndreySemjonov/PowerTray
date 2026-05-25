using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using PowerTray.Models;

namespace PowerTray.Services;

public sealed class WindowsThemeAutomationService : IDisposable
{
    private static readonly TimeSpan NetworkChangeDebounce = TimeSpan.FromSeconds(2);

    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _networkChangeDebounceTimer;
    private string _lastSsid = string.Empty;
    private string _manualOverrideSsid = string.Empty;
    private bool _isListeningForNetworkChanges;
    private bool _disposed;

    public WindowsThemeAutomationService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _networkChangeDebounceTimer = new DispatcherTimer
        {
            Interval = NetworkChangeDebounce
        };
        _networkChangeDebounceTimer.Tick += (_, _) =>
        {
            _networkChangeDebounceTimer.Stop();
            ApplyAutomation();
        };
    }

    public event EventHandler? StateChanged;
    public event EventHandler<WifiProfileAppliedEventArgs>? WifiProfileApplied;

    public string CurrentWifiSsid => GetCurrentWifiSsid();

    public void Start() => ApplySettings();

    public void ApplySettings()
    {
        if (_settingsService.Current.WindowsThemeAutomationMode == WindowsThemeAutomationMode.WifiNetwork)
        {
            StartNetworkChangeListener();
            ApplyAutomation();
            return;
        }

        StopNetworkChangeListener();
        _lastSsid = string.Empty;
        _manualOverrideSsid = string.Empty;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkManualOverrideUntilNetworkChanges()
    {
        _manualOverrideSsid = CurrentWifiSsid;
    }

    public bool SaveCurrentWifiRule(WindowsThemeMode theme) => SaveCurrentWifiThemeRule(theme);

    public bool SaveCurrentWifiThemeRule(WindowsThemeMode theme) =>
        SaveCurrentWifiRule(rule => rule.Theme = theme);

    public bool SaveCurrentWifiPowerModeRule(WindowsPowerMode powerMode, bool pluggedIn) =>
        SaveCurrentWifiRule(rule =>
        {
            if (pluggedIn)
            {
                rule.PluggedInPowerMode = powerMode;
            }
            else
            {
                rule.BatteryPowerMode = powerMode;
            }
        });

    public bool SaveCurrentWifiDellThermalRule(DellThermalProfile profile, bool pluggedIn) =>
        SaveCurrentWifiRule(rule =>
        {
            WifiDellThermalAction action = ToWifiDellThermalAction(profile);
            if (pluggedIn)
            {
                rule.PluggedInDellThermalAction = action;
            }
            else
            {
                rule.BatteryDellThermalAction = action;
            }
        });

    public bool SaveCurrentWifiProfile(
        WindowsThemeMode? theme,
        WindowsPowerMode? pluggedInPowerMode,
        WindowsPowerMode? batteryPowerMode,
        WifiDellThermalAction pluggedInDellThermalAction,
        WifiDellThermalAction batteryDellThermalAction) =>
        SaveCurrentWifiRule(rule =>
        {
            rule.Theme = theme;
            rule.PluggedInPowerMode = pluggedInPowerMode;
            rule.BatteryPowerMode = batteryPowerMode;
            rule.PluggedInDellThermalAction = pluggedInDellThermalAction;
            rule.BatteryDellThermalAction = batteryDellThermalAction;
        });

    private bool SaveCurrentWifiRule(Action<WindowsThemeWifiRule> updateRule)
    {
        string ssid = CurrentWifiSsid;
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return false;
        }

        WindowsThemeWifiRule? existing = _settingsService.Current.WindowsThemeWifiRules
            .FirstOrDefault(rule => rule.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new WindowsThemeWifiRule
            {
                Ssid = ssid,
                Theme = null
            };
            _settingsService.Current.WindowsThemeWifiRules.Add(existing);
        }

        updateRule(existing);
        _manualOverrideSsid = string.Empty;
        _settingsService.Save(_settingsService.Current);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopNetworkChangeListener();
    }

    private void StartNetworkChangeListener()
    {
        if (_isListeningForNetworkChanges)
        {
            return;
        }

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _isListeningForNetworkChanges = true;
    }

    private void StopNetworkChangeListener()
    {
        _networkChangeDebounceTimer.Stop();
        if (!_isListeningForNetworkChanges)
        {
            return;
        }

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _isListeningForNetworkChanges = false;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_disposed ||
            _settingsService.Current.WindowsThemeAutomationMode != WindowsThemeAutomationMode.WifiNetwork)
        {
            return;
        }

        _networkChangeDebounceTimer.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed ||
                _settingsService.Current.WindowsThemeAutomationMode != WindowsThemeAutomationMode.WifiNetwork)
            {
                return;
            }

            _networkChangeDebounceTimer.Stop();
            _networkChangeDebounceTimer.Start();
        });
    }

    private void ApplyAutomation()
    {
        if (_disposed || _settingsService.Current.WindowsThemeAutomationMode != WindowsThemeAutomationMode.WifiNetwork)
        {
            return;
        }

        string ssid = CurrentWifiSsid;
        if (!ssid.Equals(_lastSsid, StringComparison.OrdinalIgnoreCase))
        {
            _lastSsid = ssid;
            _manualOverrideSsid = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(ssid) ||
            ssid.Equals(_manualOverrideSsid, StringComparison.OrdinalIgnoreCase))
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        WindowsThemeWifiRule? rule = _settingsService.Current.WindowsThemeWifiRules
            .FirstOrDefault(item => item.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (rule.Theme is { } theme)
        {
            bool shouldUseLight = theme == WindowsThemeMode.Light;
            if (WindowsThemeService.IsLightMode() != shouldUseLight)
            {
                WindowsThemeService.SetLightMode(shouldUseLight);
                ThemeService.Apply(_settingsService.Current.Theme);
            }
        }

        WifiProfileApplied?.Invoke(this, new WifiProfileAppliedEventArgs(ssid, CloneRule(rule)));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static WindowsThemeWifiRule CloneRule(WindowsThemeWifiRule rule) => new()
    {
        Ssid = rule.Ssid,
        Theme = rule.Theme,
        PluggedInPowerMode = rule.PluggedInPowerMode,
        BatteryPowerMode = rule.BatteryPowerMode,
        PluggedInDellThermalAction = rule.PluggedInDellThermalAction,
        BatteryDellThermalAction = rule.BatteryDellThermalAction
    };

    private static WifiDellThermalAction ToWifiDellThermalAction(DellThermalProfile profile) => profile switch
    {
        DellThermalProfile.Optimized => WifiDellThermalAction.Optimized,
        DellThermalProfile.Cool => WifiDellThermalAction.Cool,
        DellThermalProfile.Quiet => WifiDellThermalAction.Quiet,
        DellThermalProfile.UltraPerformance => WifiDellThermalAction.UltraPerformance,
        _ => WifiDellThermalAction.DoNotChange
    };

    private static string GetCurrentWifiSsid()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "wlan show interfaces",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                return string.Empty;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf(':');
                if (separatorIndex >= 0)
                {
                    return line[(separatorIndex + 1)..].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to read current Wi-Fi SSID for Windows theme automation.");
        }

        return string.Empty;
    }
}

public sealed class WifiProfileAppliedEventArgs(string ssid, WindowsThemeWifiRule rule) : EventArgs
{
    public string Ssid { get; } = ssid;
    public WindowsThemeWifiRule Rule { get; } = rule;
}

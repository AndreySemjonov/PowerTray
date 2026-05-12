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

    public bool SaveCurrentWifiRule(WindowsThemeMode theme)
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
            _settingsService.Current.WindowsThemeWifiRules.Add(new WindowsThemeWifiRule
            {
                Ssid = ssid,
                Theme = theme
            });
        }
        else
        {
            existing.Theme = theme;
        }

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

        bool shouldUseLight = rule.Theme == WindowsThemeMode.Light;
        if (WindowsThemeService.IsLightMode() != shouldUseLight)
        {
            WindowsThemeService.SetLightMode(shouldUseLight);
            ThemeService.Apply(_settingsService.Current.Theme);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

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

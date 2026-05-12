using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerTray.Models;
using PowerTray.Services;

namespace PowerTray.ViewModels;

public sealed class WindowsThemeSettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly WindowsThemeAutomationService _automationService;
    private bool _showWindowsThemeToggle;
    private WindowsThemeAutomationMode _automationMode;
    private WindowsThemeMode _newRuleTheme = WindowsThemeMode.Dark;
    private string _currentWifiSsid = string.Empty;
    private string _status = string.Empty;

    public WindowsThemeSettingsViewModel(SettingsService settingsService, WindowsThemeAutomationService automationService)
    {
        _settingsService = settingsService;
        _automationService = automationService;
        _showWindowsThemeToggle = settingsService.Current.ShowWindowsThemeToggle;
        _automationMode = settingsService.Current.WindowsThemeAutomationMode;
        _currentWifiSsid = _automationService.CurrentWifiSsid;
        WifiRules = new ObservableCollection<WindowsThemeWifiRuleViewModel>(
            BuildRuleViewModels());

        RefreshWifiCommand = new RelayCommand(RefreshWifi);
        AddCurrentWifiRuleCommand = new RelayCommand(AddCurrentWifiRule, () => !string.IsNullOrWhiteSpace(_currentWifiSsid));
        RemoveRuleCommand = new RelayCommand(RemoveRule);
        SaveCommand = new RelayCommand(Save);
        _automationService.StateChanged += AutomationService_StateChanged;
    }

    public event EventHandler? Saved;

    public IEnumerable<WindowsThemeAutomationModeOption> AutomationModes { get; } =
    [
        new(WindowsThemeAutomationMode.Off, "Manual"),
        new(WindowsThemeAutomationMode.WifiNetwork, "By Wi-Fi network")
    ];

    public IEnumerable<WindowsThemeModeOption> ThemeModes { get; } =
    [
        new(WindowsThemeMode.Light, "Light"),
        new(WindowsThemeMode.Dark, "Dark")
    ];

    public ObservableCollection<WindowsThemeWifiRuleViewModel> WifiRules { get; }

    public bool ShowWindowsThemeToggle
    {
        get => _showWindowsThemeToggle;
        set => SetProperty(ref _showWindowsThemeToggle, value);
    }

    public WindowsThemeAutomationMode AutomationMode
    {
        get => _automationMode;
        set
        {
            if (SetProperty(ref _automationMode, value))
            {
                OnPropertyChanged(nameof(IsWifiAutomationEnabled));
            }
        }
    }

    public bool IsWifiAutomationEnabled => AutomationMode == WindowsThemeAutomationMode.WifiNetwork;

    public WindowsThemeMode NewRuleTheme
    {
        get => _newRuleTheme;
        set => SetProperty(ref _newRuleTheme, value);
    }

    public string CurrentWifiSsid
    {
        get => string.IsNullOrWhiteSpace(_currentWifiSsid) ? "No Wi-Fi connected" : _currentWifiSsid;
        private set
        {
            if (SetProperty(ref _currentWifiSsid, value))
            {
                OnPropertyChanged(nameof(CurrentWifiSsid));
                if (AddCurrentWifiRuleCommand is RelayCommand command)
                {
                    command.RaiseCanExecuteChanged();
                }
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand RefreshWifiCommand { get; }
    public ICommand AddCurrentWifiRuleCommand { get; }
    public ICommand RemoveRuleCommand { get; }
    public ICommand SaveCommand { get; }

    public void Detach() => _automationService.StateChanged -= AutomationService_StateChanged;

    private void AutomationService_StateChanged(object? sender, EventArgs e)
    {
        _showWindowsThemeToggle = _settingsService.Current.ShowWindowsThemeToggle;
        _automationMode = _settingsService.Current.WindowsThemeAutomationMode;
        _currentWifiSsid = _automationService.CurrentWifiSsid;
        WifiRules.Clear();
        foreach (WindowsThemeWifiRuleViewModel rule in BuildRuleViewModels())
        {
            WifiRules.Add(rule);
        }

        OnPropertyChanged(nameof(ShowWindowsThemeToggle));
        OnPropertyChanged(nameof(AutomationMode));
        OnPropertyChanged(nameof(IsWifiAutomationEnabled));
        OnPropertyChanged(nameof(CurrentWifiSsid));
        if (AddCurrentWifiRuleCommand is RelayCommand command)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private void RefreshWifi()
    {
        CurrentWifiSsid = _automationService.CurrentWifiSsid;
        Status = string.IsNullOrWhiteSpace(_currentWifiSsid)
            ? "No Wi-Fi network detected."
            : $"Current Wi-Fi: {_currentWifiSsid}";
    }

    private void AddCurrentWifiRule()
    {
        string ssid = _currentWifiSsid.Trim();
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return;
        }

        WindowsThemeWifiRuleViewModel? existing = WifiRules
            .FirstOrDefault(rule => rule.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Theme = NewRuleTheme;
            Status = $"Updated rule for {ssid}.";
            return;
        }

        WifiRules.Add(new WindowsThemeWifiRuleViewModel(ssid, NewRuleTheme));
        Status = $"Added rule for {ssid}.";
    }

    private void RemoveRule(object? parameter)
    {
        if (parameter is WindowsThemeWifiRuleViewModel rule)
        {
            WifiRules.Remove(rule);
        }
    }

    private void Save()
    {
        _settingsService.Current.ShowWindowsThemeToggle = ShowWindowsThemeToggle;
        _settingsService.Current.WindowsThemeAutomationMode = AutomationMode;
        _settingsService.Current.WindowsThemeWifiRules = WifiRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Ssid))
            .Select(rule => new WindowsThemeWifiRule
            {
                Ssid = rule.Ssid.Trim(),
                Theme = rule.Theme
            })
            .GroupBy(rule => rule.Ssid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        _settingsService.Save(_settingsService.Current);
        _automationService.ApplySettings();
        Status = "Saved.";
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<WindowsThemeWifiRuleViewModel> BuildRuleViewModels() =>
        _settingsService.Current.WindowsThemeWifiRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Ssid))
            .Select(rule => new WindowsThemeWifiRuleViewModel(rule.Ssid, rule.Theme));

    public sealed record WindowsThemeAutomationModeOption(WindowsThemeAutomationMode Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record WindowsThemeModeOption(WindowsThemeMode Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

public sealed class WindowsThemeWifiRuleViewModel : ObservableObject
{
    private WindowsThemeMode _theme;

    public WindowsThemeWifiRuleViewModel(string ssid, WindowsThemeMode theme)
    {
        Ssid = ssid;
        _theme = theme;
    }

    public string Ssid { get; }

    public WindowsThemeMode Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }
}

using System.Windows.Input;
using Microsoft.Win32;
using XPSBatteryTray.Models;
using XPSBatteryTray.Services;

namespace XPSBatteryTray.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private string _cctkPath;
    private bool _startWithWindows;
    private bool _startMinimized;
    private int _sensorSampleIntervalSeconds;
    private bool _enableHwinfoIntegration;
    private int _healthStart;
    private int _healthStop;
    private int _balancedStart;
    private int _balancedStop;
    private AppTheme _theme;
    private string _status = string.Empty;

    public SettingsViewModel(SettingsService settingsService, StartupService startupService)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        AppSettings settings = settingsService.Current;
        _cctkPath = settings.CctkPath;
        _startWithWindows = startupService.IsEnabled();
        _startMinimized = settings.StartMinimized;
        _sensorSampleIntervalSeconds = settings.SensorSampleIntervalSeconds;
        _enableHwinfoIntegration = settings.EnableHwinfoIntegration;
        _healthStart = settings.HealthStart;
        _healthStop = settings.HealthStop;
        _balancedStart = settings.BalancedStart;
        _balancedStop = settings.BalancedStop;
        _theme = settings.Theme;

        BrowseCommand = new RelayCommand(Browse);
        SaveCommand = new RelayCommand(Save);
    }

    public event EventHandler? Saved;

    public IEnumerable<AppTheme> Themes => Enum.GetValues<AppTheme>();

    public string CctkPath
    {
        get => _cctkPath;
        set => SetProperty(ref _cctkPath, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public int SensorSampleIntervalSeconds
    {
        get => _sensorSampleIntervalSeconds;
        set => SetProperty(ref _sensorSampleIntervalSeconds, value);
    }

    public bool EnableHwinfoIntegration
    {
        get => _enableHwinfoIntegration;
        set => SetProperty(ref _enableHwinfoIntegration, value);
    }

    public int HealthStart
    {
        get => _healthStart;
        set => SetProperty(ref _healthStart, value);
    }

    public int HealthStop
    {
        get => _healthStop;
        set => SetProperty(ref _healthStop, value);
    }

    public int BalancedStart
    {
        get => _balancedStart;
        set => SetProperty(ref _balancedStart, value);
    }

    public int BalancedStop
    {
        get => _balancedStop;
        set => SetProperty(ref _balancedStop, value);
    }

    public AppTheme Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand BrowseCommand { get; }
    public ICommand SaveCommand { get; }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Dell Command Configure (cctk.exe)|cctk.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select cctk.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            CctkPath = dialog.FileName;
        }
    }

    private void Save()
    {
        if (HealthStart < 0 || HealthStop > 100 || HealthStart >= HealthStop ||
            BalancedStart < 0 || BalancedStop > 100 || BalancedStart >= BalancedStop)
        {
            Status = "Preset ranges must be valid percentages with start below stop.";
            return;
        }

        var settings = new AppSettings
        {
            CctkPath = CctkPath,
            StartWithWindows = StartWithWindows,
            StartMinimized = StartMinimized,
            SensorSampleIntervalSeconds = Math.Clamp(SensorSampleIntervalSeconds, 1, 60),
            EnableHwinfoIntegration = EnableHwinfoIntegration,
            HealthStart = HealthStart,
            HealthStop = HealthStop,
            BalancedStart = BalancedStart,
            BalancedStop = BalancedStop,
            Theme = Theme
        };

        _settingsService.Save(settings);
        _startupService.SetEnabled(StartWithWindows);
        Status = "Saved.";
        Saved?.Invoke(this, EventArgs.Empty);
    }
}

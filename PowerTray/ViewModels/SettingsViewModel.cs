using System.Windows.Input;
using Microsoft.Win32;
using PowerTray.Models;
using PowerTray.Services;

namespace PowerTray.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private string _cctkPath;
    private bool _startWithWindows;
    private bool _startMinimized;
    private int _sensorSampleIntervalSeconds;
    private bool _enableHwinfoIntegration;
    private bool _enableHwinfoAutoRestart;
    private bool _enableHwinfoPersonalRecoveryScript;
    private string _hwinfoPersonalRecoveryScriptPath;
    private bool _enableLibreHardwareMonitorIntegration;
    private int _healthStart;
    private int _healthStop;
    private int _balancedStart;
    private int _balancedStop;
    private DellThermalControlMode _dellThermalControlMode;
    private DellThermalProfile _powerEfficiencyThermalProfile;
    private DellThermalProfile _balancedThermalProfile;
    private DellThermalProfile _performanceThermalProfile;
    private DellThermalProfile _manualPluggedInThermalProfile;
    private DellThermalProfile _manualBatteryThermalProfile;
    private bool _useBiosSetupPassword;
    private string _biosSetupPassword = string.Empty;
    private bool _hasSavedBiosSetupPassword;
    private bool _showBatteryWattsTile;
    private bool _showCpuGpuUsageTile;
    private bool _showBatteryUsageSection;
    private AppTheme _theme;
    private bool _showWindowsThemeToggle;
    private bool _enableScreenDimmer;
    private bool _extendBrightnessKeysWithDimmer;
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
        _enableHwinfoAutoRestart = settings.EnableHwinfoAutoRestart;
        _enableHwinfoPersonalRecoveryScript = settings.EnableHwinfoPersonalRecoveryScript;
        _hwinfoPersonalRecoveryScriptPath = settings.HwinfoPersonalRecoveryScriptPath;
        _enableLibreHardwareMonitorIntegration = settings.EnableLibreHardwareMonitorIntegration;
        _healthStart = settings.HealthStart;
        _healthStop = settings.HealthStop;
        _balancedStart = settings.BalancedStart;
        _balancedStop = settings.BalancedStop;
        _dellThermalControlMode = settings.DellThermalControlMode;
        _powerEfficiencyThermalProfile = settings.PowerEfficiencyThermalProfile;
        _balancedThermalProfile = settings.BalancedThermalProfile;
        _performanceThermalProfile = settings.PerformanceThermalProfile;
        _manualPluggedInThermalProfile = settings.ManualPluggedInThermalProfile;
        _manualBatteryThermalProfile = settings.ManualBatteryThermalProfile;
        _useBiosSetupPassword = settings.UseBiosSetupPassword;
        _hasSavedBiosSetupPassword = !string.IsNullOrWhiteSpace(settings.EncryptedBiosSetupPassword);
        _showBatteryWattsTile = settings.ShowBatteryWattsTile;
        _showCpuGpuUsageTile = settings.ShowCpuGpuUsageTile;
        _showBatteryUsageSection = settings.ShowBatteryUsageSection;
        _theme = settings.Theme;
        _showWindowsThemeToggle = settings.ShowWindowsThemeToggle;
        _enableScreenDimmer = settings.EnableScreenDimmer;
        _extendBrightnessKeysWithDimmer = settings.ExtendBrightnessKeysWithDimmer;

        BrowseCommand = new RelayCommand(Browse);
        BrowseHwinfoRecoveryScriptCommand = new RelayCommand(BrowseHwinfoRecoveryScript);
        ClearBiosSetupPasswordCommand = new RelayCommand(ClearBiosSetupPassword);
        SaveCommand = new RelayCommand(Save);
    }

    public event EventHandler? Saved;

    public IEnumerable<AppThemeOption> Themes { get; } =
    [
        new(AppTheme.FollowSystem, "Follow Windows"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.Dark, "Dark")
    ];
    public IEnumerable<DellThermalControlModeOption> DellThermalControlModes { get; } =
    [
        new(DellThermalControlMode.Off, "Off"),
        new(DellThermalControlMode.SyncWithWindowsPowerPlan, "Sync with Windows power plan"),
        new(DellThermalControlMode.Manual, "Separate Dell thermal profile")
    ];

    public IEnumerable<DellThermalProfileOption> DellThermalProfiles { get; } =
    [
        new(DellThermalProfile.Optimized, "Optimized"),
        new(DellThermalProfile.Cool, "Cool"),
        new(DellThermalProfile.Quiet, "Quiet"),
        new(DellThermalProfile.UltraPerformance, "Ultra Performance")
    ];

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

    public bool EnableHwinfoAutoRestart
    {
        get => _enableHwinfoAutoRestart;
        set => SetProperty(ref _enableHwinfoAutoRestart, value);
    }

    public bool EnableHwinfoPersonalRecoveryScript
    {
        get => _enableHwinfoPersonalRecoveryScript;
        set => SetProperty(ref _enableHwinfoPersonalRecoveryScript, value);
    }

    public string HwinfoPersonalRecoveryScriptPath
    {
        get => _hwinfoPersonalRecoveryScriptPath;
        set => SetProperty(ref _hwinfoPersonalRecoveryScriptPath, value);
    }

    public bool EnableLibreHardwareMonitorIntegration
    {
        get => _enableLibreHardwareMonitorIntegration;
        set => SetProperty(ref _enableLibreHardwareMonitorIntegration, value);
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

    public DellThermalControlMode DellThermalControlMode
    {
        get => _dellThermalControlMode;
        set => SetProperty(ref _dellThermalControlMode, value);
    }

    public DellThermalProfile PowerEfficiencyThermalProfile
    {
        get => _powerEfficiencyThermalProfile;
        set => SetProperty(ref _powerEfficiencyThermalProfile, value);
    }

    public DellThermalProfile BalancedThermalProfile
    {
        get => _balancedThermalProfile;
        set => SetProperty(ref _balancedThermalProfile, value);
    }

    public DellThermalProfile PerformanceThermalProfile
    {
        get => _performanceThermalProfile;
        set => SetProperty(ref _performanceThermalProfile, value);
    }

    public DellThermalProfile ManualPluggedInThermalProfile
    {
        get => _manualPluggedInThermalProfile;
        set => SetProperty(ref _manualPluggedInThermalProfile, value);
    }

    public DellThermalProfile ManualBatteryThermalProfile
    {
        get => _manualBatteryThermalProfile;
        set => SetProperty(ref _manualBatteryThermalProfile, value);
    }

    public bool UseBiosSetupPassword
    {
        get => _useBiosSetupPassword;
        set => SetProperty(ref _useBiosSetupPassword, value);
    }

    public string BiosSetupPassword
    {
        get => _biosSetupPassword;
        set
        {
            if (SetProperty(ref _biosSetupPassword, value))
            {
                OnPropertyChanged(nameof(BiosSetupPasswordStatusText));
            }
        }
    }

    public bool HasSavedBiosSetupPassword
    {
        get => _hasSavedBiosSetupPassword;
        private set
        {
            if (SetProperty(ref _hasSavedBiosSetupPassword, value))
            {
                OnPropertyChanged(nameof(BiosSetupPasswordStatusText));
            }
        }
    }

    public string BiosSetupPasswordStatusText => !string.IsNullOrEmpty(BiosSetupPassword)
        ? "New password will be encrypted and saved."
        : HasSavedBiosSetupPassword
            ? "Saved password is encrypted for this Windows user."
            : "No BIOS setup password saved.";

    public bool ShowBatteryWattsTile
    {
        get => _showBatteryWattsTile;
        set => SetProperty(ref _showBatteryWattsTile, value);
    }

    public bool ShowCpuGpuUsageTile
    {
        get => _showCpuGpuUsageTile;
        set => SetProperty(ref _showCpuGpuUsageTile, value);
    }

    public bool ShowBatteryUsageSection
    {
        get => _showBatteryUsageSection;
        set => SetProperty(ref _showBatteryUsageSection, value);
    }

    public AppTheme Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public bool ShowWindowsThemeToggle
    {
        get => _showWindowsThemeToggle;
        set => SetProperty(ref _showWindowsThemeToggle, value);
    }

    public bool EnableScreenDimmer
    {
        get => _enableScreenDimmer;
        set
        {
            if (SetProperty(ref _enableScreenDimmer, value))
            {
                OnPropertyChanged(nameof(CanExtendBrightnessKeysWithDimmer));
            }
        }
    }

    public bool ExtendBrightnessKeysWithDimmer
    {
        get => _extendBrightnessKeysWithDimmer;
        set => SetProperty(ref _extendBrightnessKeysWithDimmer, value);
    }

    public bool CanExtendBrightnessKeysWithDimmer => EnableScreenDimmer;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand BrowseCommand { get; }
    public ICommand BrowseHwinfoRecoveryScriptCommand { get; }
    public ICommand ClearBiosSetupPasswordCommand { get; }
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

    private void BrowseHwinfoRecoveryScript()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Scripts and executables (*.ps1;*.cmd;*.bat;*.exe)|*.ps1;*.cmd;*.bat;*.exe|PowerShell scripts (*.ps1)|*.ps1|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select personal HWiNFO recovery script",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            HwinfoPersonalRecoveryScriptPath = dialog.FileName;
        }
    }

    private void ClearBiosSetupPassword()
    {
        BiosSetupPassword = string.Empty;
        HasSavedBiosSetupPassword = false;
        Status = "Saved BIOS setup password will be removed when you save settings.";
    }

    private void Save()
    {
        if (HealthStart < 0 || HealthStop > 100 || HealthStart >= HealthStop ||
            BalancedStart < 0 || BalancedStop > 100 || BalancedStart >= BalancedStop)
        {
            Status = "Preset ranges must be valid percentages with start below stop.";
            return;
        }

        string encryptedBiosSetupPassword = _settingsService.Current.EncryptedBiosSetupPassword;
        if (!string.IsNullOrEmpty(BiosSetupPassword))
        {
            encryptedBiosSetupPassword = SecretProtectionService.Protect(BiosSetupPassword);
        }
        else if (!HasSavedBiosSetupPassword)
        {
            encryptedBiosSetupPassword = string.Empty;
        }

        var settings = new AppSettings
        {
            CctkPath = CctkPath,
            StartWithWindows = StartWithWindows,
            StartMinimized = StartMinimized,
            SensorSampleIntervalSeconds = Math.Clamp(SensorSampleIntervalSeconds, 1, 60),
            EnableHwinfoIntegration = EnableHwinfoIntegration,
            EnableHwinfoAutoRestart = EnableHwinfoAutoRestart,
            EnableHwinfoPersonalRecoveryScript = EnableHwinfoPersonalRecoveryScript,
            HwinfoPersonalRecoveryScriptPath = HwinfoPersonalRecoveryScriptPath,
            EnableLibreHardwareMonitorIntegration = EnableLibreHardwareMonitorIntegration,
            HealthStart = HealthStart,
            HealthStop = HealthStop,
            BalancedStart = BalancedStart,
            BalancedStop = BalancedStop,
            DellThermalControlMode = DellThermalControlMode,
            PowerEfficiencyThermalProfile = PowerEfficiencyThermalProfile,
            BalancedThermalProfile = BalancedThermalProfile,
            PerformanceThermalProfile = PerformanceThermalProfile,
            ManualPluggedInThermalProfile = ManualPluggedInThermalProfile,
            ManualBatteryThermalProfile = ManualBatteryThermalProfile,
            UseBiosSetupPassword = UseBiosSetupPassword,
            EncryptedBiosSetupPassword = encryptedBiosSetupPassword,
            ShowBatteryWattsTile = ShowBatteryWattsTile,
            ShowCpuGpuUsageTile = ShowCpuGpuUsageTile,
            ShowBatteryUsageSection = ShowBatteryUsageSection,
            DashboardWindowBehavior = _settingsService.Current.DashboardWindowBehavior,
            Theme = Theme,
            ShowWindowsThemeToggle = ShowWindowsThemeToggle,
            LastDellThermalSetting = _settingsService.Current.LastDellThermalSetting,
            EnableScreenDimmer = EnableScreenDimmer,
            ScreenDimmerLevel = _settingsService.Current.ScreenDimmerLevel,
            ExtendBrightnessKeysWithDimmer = EnableScreenDimmer && ExtendBrightnessKeysWithDimmer
        };

        _settingsService.Save(settings);
        _startupService.SetEnabled(StartWithWindows);
        StartWithWindows = _startupService.IsEnabled();
        BiosSetupPassword = string.Empty;
        HasSavedBiosSetupPassword = !string.IsNullOrWhiteSpace(settings.EncryptedBiosSetupPassword);
        Status = StartWithWindows == settings.StartWithWindows
            ? "Saved."
            : "Saved, but Windows startup registration did not persist.";
        Saved?.Invoke(this, EventArgs.Empty);
    }

    public sealed record DellThermalControlModeOption(DellThermalControlMode Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record DellThermalProfileOption(DellThermalProfile Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record AppThemeOption(AppTheme Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

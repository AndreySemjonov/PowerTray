namespace PowerTray.Models;

public sealed class AppSettings
{
    public string CctkPath { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public int SensorSampleIntervalSeconds { get; set; } = 2;
    public bool EnableHwinfoIntegration { get; set; } = true;
    public bool EnableHwinfoAutoRestart { get; set; } = true;
    public bool EnableHwinfoPersonalRecoveryScript { get; set; }
    public string HwinfoPersonalRecoveryScriptPath { get; set; } = string.Empty;
    public bool EnableLibreHardwareMonitorIntegration { get; set; } = true;
    public int HealthStart { get; set; } = 50;
    public int HealthStop { get; set; } = 80;
    public int BalancedStart { get; set; } = 70;
    public int BalancedStop { get; set; } = 90;
    public DellThermalControlMode DellThermalControlMode { get; set; } = DellThermalControlMode.Off;
    public DellThermalProfile PowerEfficiencyThermalProfile { get; set; } = DellThermalProfile.Quiet;
    public DellThermalProfile BalancedThermalProfile { get; set; } = DellThermalProfile.Optimized;
    public DellThermalProfile PerformanceThermalProfile { get; set; } = DellThermalProfile.UltraPerformance;
    public DellThermalProfile ManualPluggedInThermalProfile { get; set; } = DellThermalProfile.UltraPerformance;
    public DellThermalProfile ManualBatteryThermalProfile { get; set; } = DellThermalProfile.Quiet;
    public bool UseBiosSetupPassword { get; set; }
    public string EncryptedBiosSetupPassword { get; set; } = string.Empty;
    public bool ShowBatteryWattsTile { get; set; } = true;
    public bool ShowCpuGpuUsageTile { get; set; } = true;
    public bool ShowBatteryUsageSection { get; set; } = true;
    public DashboardWindowBehavior DashboardWindowBehavior { get; set; } = DashboardWindowBehavior.AutoHide;
    public AppTheme Theme { get; set; } = AppTheme.FollowSystem;
    public bool ShowWindowsThemeToggle { get; set; } = true;
    public WindowsThemeAutomationMode WindowsThemeAutomationMode { get; set; } = WindowsThemeAutomationMode.Off;
    public List<WindowsThemeWifiRule> WindowsThemeWifiRules { get; set; } = [];
    public string LastDellThermalSetting { get; set; } = string.Empty;
    public bool EnableScreenDimmer { get; set; }
    public double ScreenDimmerLevel { get; set; }
    public bool ExtendBrightnessKeysWithDimmer { get; set; }
}

public enum AppTheme
{
    FollowSystem,
    Light,
    Dark
}

public enum WindowsThemeAutomationMode
{
    Off,
    WifiNetwork
}

public enum WindowsThemeMode
{
    Light,
    Dark
}

public sealed class WindowsThemeWifiRule
{
    public string Ssid { get; set; } = string.Empty;
    public WindowsThemeMode? Theme { get; set; } = WindowsThemeMode.Dark;
    public WindowsPowerMode? PluggedInPowerMode { get; set; }
    public WindowsPowerMode? BatteryPowerMode { get; set; }
    public WifiDellThermalAction PluggedInDellThermalAction { get; set; } = WifiDellThermalAction.DoNotChange;
    public WifiDellThermalAction BatteryDellThermalAction { get; set; } = WifiDellThermalAction.DoNotChange;
}

public enum WifiDellThermalAction
{
    DoNotChange,
    SyncWithPowerPlan,
    Optimized,
    Cool,
    Quiet,
    UltraPerformance
}

public enum DellThermalControlMode
{
    Off,
    SyncWithWindowsPowerPlan,
    Manual
}

public enum DellThermalProfile
{
    Optimized,
    Cool,
    Quiet,
    UltraPerformance
}

public enum DashboardWindowBehavior
{
    AutoHide,
    ManualClose,
    StayOnTop
}

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
    public bool UseBiosSetupPassword { get; set; }
    public string EncryptedBiosSetupPassword { get; set; } = string.Empty;
    public bool ShowBatteryWattsTile { get; set; } = true;
    public bool ShowCpuGpuUsageTile { get; set; } = true;
    public bool ShowBatteryUsageSection { get; set; } = true;
    public DashboardWindowBehavior DashboardWindowBehavior { get; set; } = DashboardWindowBehavior.AutoHide;
    public AppTheme Theme { get; set; } = AppTheme.FollowSystem;
    public string LastDellThermalSetting { get; set; } = string.Empty;
}

public enum AppTheme
{
    FollowSystem,
    Light,
    Dark
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

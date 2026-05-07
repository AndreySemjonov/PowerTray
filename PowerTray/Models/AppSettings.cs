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
    public AppTheme Theme { get; set; } = AppTheme.FollowSystem;
}

public enum AppTheme
{
    FollowSystem,
    Light,
    Dark
}

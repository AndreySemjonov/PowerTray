namespace XPSBatteryTray.Models;

public sealed class BatteryStatus
{
    public int Percentage { get; init; }
    public bool IsPluggedIn { get; init; }
    public bool IsPowerSave { get; init; }
    public bool IsCritical { get; init; }
    public TimeSpan? EstimatedTimeRemaining { get; init; }
    public double? ChargeRateWatts { get; init; }
    public string? HealthStatus { get; init; }
    public BatteryHealthInfo BatteryHealth { get; init; } = new();
}

namespace PowerTray.Models;

public sealed class BatteryHealthInfo
{
    public int? DesignCapacityMilliWattHours { get; init; }
    public int? FullChargeCapacityMilliWattHours { get; init; }
    public int? CycleCount { get; init; }
    public string Source { get; init; } = "Unavailable";

    public double? HealthPercent =>
        DesignCapacityMilliWattHours is > 0 && FullChargeCapacityMilliWattHours is > 0
            ? Math.Clamp(FullChargeCapacityMilliWattHours.Value / (double)DesignCapacityMilliWattHours.Value * 100d, 0, 999)
            : null;
}

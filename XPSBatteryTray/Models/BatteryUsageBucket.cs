namespace XPSBatteryTray.Models;

public sealed class BatteryUsageBucket
{
    public DateTimeOffset Start { get; init; }
    public string Label { get; init; } = string.Empty;
    public int BatteryPercent { get; init; }
    public double DrainPercent { get; init; }
    public double ChargePercent { get; init; }
    public double AverageWatts { get; init; }
    public bool IsCharging { get; init; }
    public bool IsCurrent { get; init; }
    public bool HasData { get; init; }
}

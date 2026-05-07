namespace PowerTray.Models;

public enum BatteryUsageBucketKind
{
    Observed,
    Sleep,
    Missing,
    InferredCharge,
    ChargeHold,
    NoData
}

public sealed class BatteryUsageBucket
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public string Label { get; init; } = string.Empty;
    public int BatteryPercent { get; init; }
    public double DrainPercent { get; init; }
    public double ChargePercent { get; init; }
    public double AverageWatts { get; init; }
    public bool IsPluggedIn { get; init; }
    public bool IsCharging { get; init; }
    public bool IsPowerSave { get; init; }
    public bool IsCritical { get; init; }
    public bool IsMissingData { get; init; }
    public bool IsCurrent { get; init; }
    public bool HasData { get; init; }
    public BatteryUsageBucketKind Kind { get; init; } = BatteryUsageBucketKind.Observed;
    public WindowsPowerMode? PowerMode { get; init; }
}

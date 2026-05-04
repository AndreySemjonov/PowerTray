namespace XPSBatteryTray.Models;

public sealed class BatteryUsageSnapshot
{
    public string Title { get; init; } = "Battery usage (24h)";
    public DateTime Date { get; init; } = DateTime.Today;
    public string SessionText { get; init; } = "0m";
    public string ActiveText { get; init; } = "0m";
    public string IdleText { get; init; } = "0m";
    public string EstimatedDrainText { get; init; } = "0.0 mWh";
    public string SleepDrainText { get; init; } = "Sleep: none";
    public string ChargeBehaviorText { get; init; } = "Usage: collecting";
    public IReadOnlyList<BatteryUsageBucket> Buckets { get; init; } = [];
}

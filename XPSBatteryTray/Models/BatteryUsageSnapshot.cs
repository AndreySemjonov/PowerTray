namespace XPSBatteryTray.Models;

public sealed class BatteryUsageSnapshot
{
    public string Title { get; init; } = "Battery Usage Since Charge (24h)";
    public DateTime Date { get; init; } = DateTime.Today;
    public string SessionText { get; init; } = "0m";
    public string ActiveText { get; init; } = "0m";
    public string IdleText { get; init; } = "0m";
    public string EstimatedDrainText { get; init; } = "0.0 mWh";
    public IReadOnlyList<BatteryUsageBucket> Buckets { get; init; } = [];
}

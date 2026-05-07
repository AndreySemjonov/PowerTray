namespace XPSBatteryTray.Models;

public sealed class BatteryUsageSelection
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public string Title { get; init; } = "Battery Usage";
    public string Summary { get; init; } = string.Empty;
}

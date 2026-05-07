namespace PowerTray.Models;

public sealed class WindowsBatteryUsageRequest
{
    public DateTimeOffset? RangeStart { get; init; }
    public DateTimeOffset? RangeEnd { get; init; }
}

namespace XPSBatteryTray.Models;

public sealed class WindowsBatteryUsageSnapshot
{
    public IReadOnlyList<WindowsBatteryUsageInfo> Apps { get; init; } = [];
    public string StatusText { get; init; } = "Windows battery usage not loaded yet.";
    public DateTimeOffset? UpdatedAt { get; init; }
}

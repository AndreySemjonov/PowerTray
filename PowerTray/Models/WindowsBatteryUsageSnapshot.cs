namespace PowerTray.Models;

public sealed class WindowsBatteryUsageSnapshot
{
    public IReadOnlyList<WindowsBatteryUsageInfo> Apps { get; init; } = [];
    public string StatusText { get; init; } = "Windows battery usage not loaded yet.";
    public string AuditText { get; init; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; init; }
}

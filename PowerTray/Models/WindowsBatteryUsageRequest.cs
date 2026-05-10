namespace PowerTray.Models;

public sealed class WindowsBatteryUsageRequest
{
    public string RequestType { get; init; } = Services.WindowsBatteryUsageIpc.WindowsBatteryUsageRequestType;
    public DateTimeOffset? RangeStart { get; init; }
    public DateTimeOffset? RangeEnd { get; init; }
    public string CctkPath { get; init; } = string.Empty;
    public string CctkReadback { get; init; } = string.Empty;
}

using PowerTray.Models;

namespace PowerTray.Services;

public sealed class ElevatedWindowsBatteryUsageService : IWindowsBatteryUsageService
{
    private readonly WindowsBatteryUsagePipeClient _helperClient = new();
    private readonly WindowsBatteryUsageService _localFallback = new();

    public WindowsBatteryUsageSnapshot GetSnapshot() =>
        GetSnapshot(rangeStart: null, rangeEnd: null);

    public WindowsBatteryUsageSnapshot GetSnapshot(DateTimeOffset rangeStart, DateTimeOffset rangeEnd) =>
        GetSnapshot((DateTimeOffset?)rangeStart, rangeEnd);

    private WindowsBatteryUsageSnapshot GetSnapshot(DateTimeOffset? rangeStart, DateTimeOffset? rangeEnd)
    {
        WindowsBatteryUsageSnapshot? helperSnapshot = _helperClient
            .TryGetSnapshotAsync(rangeStart, rangeEnd)
            .GetAwaiter()
            .GetResult();
        if (helperSnapshot is not null)
        {
            return helperSnapshot;
        }

        WindowsBatteryUsageSnapshot fallback = rangeStart.HasValue && rangeEnd.HasValue
            ? _localFallback.GetSnapshot(rangeStart.Value, rangeEnd.Value)
            : _localFallback.GetSnapshot();

        if (fallback.Apps.Count > 0)
        {
            return CopyWithStatus(fallback, $"{fallback.StatusText} PowerTray helper service was not used.", fallback.AuditText);
        }

        return CopyWithStatus(
            fallback,
            "PowerTray helper service is not running. Install or restart the service to show Windows battery impact without running the app as Administrator.",
            string.Empty);
    }

    private static WindowsBatteryUsageSnapshot CopyWithStatus(WindowsBatteryUsageSnapshot snapshot, string statusText, string auditText) =>
        new()
        {
            Apps = snapshot.Apps,
            StatusText = statusText,
            AuditText = auditText,
            UpdatedAt = snapshot.UpdatedAt
        };
}

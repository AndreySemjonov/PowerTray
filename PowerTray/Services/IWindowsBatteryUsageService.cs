using PowerTray.Models;

namespace PowerTray.Services;

public interface IWindowsBatteryUsageService
{
    WindowsBatteryUsageSnapshot GetSnapshot();

    WindowsBatteryUsageSnapshot GetSnapshot(DateTimeOffset rangeStart, DateTimeOffset rangeEnd);
}

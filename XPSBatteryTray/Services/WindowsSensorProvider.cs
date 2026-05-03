using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class WindowsSensorProvider : ISensorProvider
{
    public string Name => "Windows";

    public SensorReadings Read() => new()
    {
        IsAvailable = false,
        Status = "HWiNFO disabled; Windows fallback stats active"
    };
}
